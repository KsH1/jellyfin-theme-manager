using System.Text.Json;

namespace ThemeManager.Core;

/// <summary>Shared schedules in one file, each user's personal schedule in its own file. Writes are atomic.</summary>
/// <param name="onUnreadable">Told about a file that cannot be read; it is then treated as empty.</param>
public sealed class ScheduleStore(string root, Action<string, Exception>? onUnreadable = null)
{
    public const int MaxEntries = 100;
    public const int MaxShared = 50;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<ThemeSchedule>? _shared;
    private string SharedPath => Path.Combine(root, "schedules.json");
    private string PersonalPath(Guid userId)
    {
        if (userId == Guid.Empty) throw new ArgumentException("A real user session is required.", nameof(userId));
        return Path.Combine(root, "users", userId.ToString("N") + ".schedule.json");
    }

    public async Task<IReadOnlyList<ThemeSchedule>> GetSharedAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _shared) is { } cached) return cached;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return _shared ??= (await ReadAsync<List<ThemeSchedule>>(SharedPath, cancellationToken).ConfigureAwait(false) ?? []).AsReadOnly(); }
        finally { _gate.Release(); }
    }

    /// <summary>Adds or replaces the shared schedule with the same id.</summary>
    public async Task SaveSharedAsync(ThemeSchedule schedule, CancellationToken cancellationToken = default)
    {
        if (schedule.Id == ThemeSchedule.PersonalId) throw new InvalidDataException("\"personal\" is reserved for each user's own calendar.");
        var current = await GetSharedAsync(cancellationToken).ConfigureAwait(false);
        var next = current.Where(s => s.Id != schedule.Id).Append(schedule).OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (next.Count > MaxShared) throw new InvalidDataException("At most " + MaxShared + " shared calendars are supported.");
        await WriteSharedAsync(next, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteSharedAsync(string id, CancellationToken cancellationToken = default)
    {
        var current = await GetSharedAsync(cancellationToken).ConfigureAwait(false);
        var next = current.Where(s => s.Id != id).ToList();
        if (next.Count == current.Count) return false;
        await WriteSharedAsync(next, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<ThemeSchedule?> GetPersonalAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var path = PersonalPath(userId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await ReadAsync<ThemeSchedule>(path, cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async Task SavePersonalAsync(Guid userId, ThemeSchedule schedule, CancellationToken cancellationToken = default)
    {
        var path = PersonalPath(userId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await WriteAsync(path, schedule with { Id = ThemeSchedule.PersonalId }, cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async Task DeletePersonalAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var path = PersonalPath(userId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { if (File.Exists(path)) File.Delete(path); }
        finally { _gate.Release(); }
    }

    /// <summary>The schedules one user may resolve: every shared one, plus their own under <see cref="ThemeSchedule.PersonalId"/>.</summary>
    public async Task<IReadOnlyDictionary<string, ThemeSchedule>> ForUserAsync(Guid? userId, CancellationToken cancellationToken = default)
    {
        var result = (await GetSharedAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(s => s.Id, StringComparer.Ordinal);
        if (userId is { } id && await GetPersonalAsync(id, cancellationToken).ConfigureAwait(false) is { } personal) result[ThemeSchedule.PersonalId] = personal;
        return result;
    }

    private async Task WriteSharedAsync(List<ThemeSchedule> schedules, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteAsync(SharedPath, schedules, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _shared, schedules.AsReadOnly());
        }
        finally { _gate.Release(); }
    }

    private async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false), ThemeJson.Options); }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // The file is left untouched for inspection; the next save replaces it.
            onUnreadable?.Invoke(path, ex);
            return null;
        }
    }

    private static async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, ThemeJson.Options), cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
