using System.Collections.Concurrent;

namespace ThemeManager.Core;

/// <summary>Encodes reduced copies once per asset revision. Encoding failures fall back to the original file.</summary>
/// <param name="encode">Writes a copy no wider than the given width to the output path and returns the path to serve.</param>
public sealed class PreviewCache(string root, Func<string, string, int, string> encode, string extension = ".webp")
{
    public const int BackgroundWidth = 1280;
    public const int BannerWidth = 640;
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _pending = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _encoders = new(2, 2);
    public string Root { get; } = Path.GetFullPath(root);

    public static int WidthFor(string slot) => slot is "background" or "splash" or "thumbnail" ? BackgroundWidth : BannerWidth;

    public async Task<string> GetAsync(ResolvedAsset asset, string slot, CancellationToken cancellationToken = default)
    {
        var width = WidthFor(slot);
        var path = Path.Combine(Root, asset.Revision + "-" + width + extension);
        if (File.Exists(path)) return path;
        // Concurrent requests for one revision share a single encode; the shared task is not cancelled by one caller.
        var job = _pending.GetOrAdd(path, key => new Lazy<Task<string>>(() => Task.Run(() => EncodeAsync(asset.Path, key, width))));
        var result = await job.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        // A failed encode stays memoised (serving the original) until the next prune, so it is not retried per request.
        if (result == path) _pending.TryRemove(new(path, job));
        return result;
    }

    private async Task<string> EncodeAsync(string source, string path, int width)
    {
        await _encoders.WaitAsync().ConfigureAwait(false);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (File.Exists(path)) return path;
            Directory.CreateDirectory(Root);
            var written = encode(source, temporary, width);
            // The encoder returns the source unchanged when it is already small enough.
            if (!string.Equals(written, temporary, StringComparison.Ordinal) || !File.Exists(temporary)) return source;
            File.Move(temporary, path, overwrite: true);
            return path;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException) { return source; }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            finally { _encoders.Release(); }
        }
    }

    /// <summary>Deletes cached copies whose revision is no longer in the catalog. Returns the number removed.</summary>
    public int Prune(CatalogSnapshot snapshot)
    {
        _pending.Clear();
        if (!Directory.Exists(Root)) return 0;
        var live = snapshot.Themes.Values.SelectMany(t => t.Assets.Values).Select(a => a.Revision).ToHashSet(StringComparer.Ordinal);
        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(Root))
        {
            var name = Path.GetFileName(file);
            var dash = name.IndexOf('-', StringComparison.Ordinal);
            // Temporary files belong to an encode in progress; it deletes them itself.
            if (name.EndsWith(".tmp", StringComparison.Ordinal) || (dash > 0 && live.Contains(name[..dash]))) continue;
            try { File.Delete(file); removed++; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return removed;
    }
}
