using System.Text.Json;

namespace ThemeManager.Core;

/// <param name="onUnreadable">Told about a user file that cannot be read; that user then follows the server default.</param>
public sealed class UserThemeStore(string root, Action<Guid, Exception>? onUnreadable = null)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string UserPath(Guid userId)
    {
        if (userId == Guid.Empty) throw new ArgumentException("A real user session is required.", nameof(userId));
        return Path.Combine(root, userId.ToString("N") + ".json");
    }

    public async Task<ThemeChoice> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var path = UserPath(userId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path)) return new();
            try { return JsonSerializer.Deserialize<ThemeChoice>(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false), ThemeJson.Options) ?? new(); }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // The file is left untouched for inspection; the next save replaces it.
                onUnreadable?.Invoke(userId, ex);
                return new();
            }
        }
        finally { _gate.Release(); }
    }

    public async Task SetAsync(Guid userId, ThemeChoice choice, CancellationToken cancellationToken = default)
    {
        var path = UserPath(userId);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(choice, ThemeJson.Options), cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            finally { _gate.Release(); }
        }
    }
}
