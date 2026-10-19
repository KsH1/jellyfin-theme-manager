using System.IO.Compression;
using System.Text.Json;

namespace ThemeManager.Core;

public sealed class ThemePackInstaller(ThemeCatalog catalog)
{
    public const long MaxExpandedBytes = 128 * 1024 * 1024;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Where deleted and replaced packs are kept, one folder each, so nothing is lost by a mistake.</summary>
    public string BackupRoot => Path.Combine(Path.GetDirectoryName(catalog.Root)!, "deleted-packs");

    /// <param name="replace">Replace an installed pack with the same id; the old one is moved to <see cref="BackupRoot"/> and put back if the new one is invalid.</param>
    public async Task<string> InstallAsync(Stream input, CancellationToken cancellationToken, bool replace = false)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var staging = Path.Combine(Path.GetDirectoryName(catalog.Root)!, "ThemePackStaging", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(staging);
            using var zip = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
            if (zip.Entries.Count is 0 or > 64) throw new InvalidDataException("A theme pack must contain 1–64 files.");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (var entry in zip.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.FullName.EndsWith('/')) continue;
                var destination = SafePaths.Within(staging, entry.FullName);
                if (!names.Add(entry.FullName) || entry.Length > 20 * 1024 * 1024 || (total += entry.Length) > MaxExpandedBytes)
                    throw new InvalidDataException("Duplicate file or size limit exceeded.");
                if (entry.FullName != "theme.json" && Path.GetExtension(entry.Name).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg" or ".webp"))
                    throw new InvalidDataException("ZIP may contain theme.json and supported images only.");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using var source = entry.Open();
                await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
                var buffer = new byte[65536];
                long copied = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
                {
                    copied += read;
                    if (copied > entry.Length) throw new InvalidDataException("Invalid expanded ZIP size.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }

            var manifestPath = Path.Combine(staging, "theme.json");
            if (!File.Exists(manifestPath) || new FileInfo(manifestPath).Length > 65536) throw new InvalidDataException("theme.json must be at the archive root and smaller than 64 KiB.");
            var manifest = JsonSerializer.Deserialize<ThemeManifest>(await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false), ThemeJson.Options)
                ?? throw new InvalidDataException("Empty theme manifest.");
            if (!SafePaths.IsId(manifest.Id)) throw new InvalidDataException("Invalid pack id.");
            var target = SafePaths.Within(catalog.Root, manifest.Id);
            if (Directory.Exists(target) && !replace) throw new InvalidDataException("This theme already exists. Tick \"Replace\" to update it; the old version is kept as a backup.");
            if (manifest.Extends is { } parent && !catalog.Snapshot.Themes.ContainsKey(parent)) throw new InvalidDataException("Install the parent theme first.");
            Directory.CreateDirectory(catalog.Root);
            var backup = Directory.Exists(target) ? MoveToBackup(target, manifest.Id) : null;
            Directory.Move(staging, target);
            var snapshot = catalog.Reload();
            if (!snapshot.Themes.ContainsKey(manifest.Id))
            {
                // Roll back exactly what was moved above: the new pack out, the replaced one back.
                Directory.Move(target, staging);
                if (backup is not null) Directory.Move(backup, target);
                catalog.Reload();
                throw new InvalidDataException("Pack validation failed: " + string.Join("; ", snapshot.Problems.Where(p => p.Pack == manifest.Id).Select(p => p.Message)));
            }
            return manifest.Id;
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            finally { _gate.Release(); }
        }
    }

    /// <summary>Removes a pack from the catalog by moving it to <see cref="BackupRoot"/>; returns the backup folder.</summary>
    public async Task<string> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var folder = PackFolder(id);
            var children = catalog.Snapshot.Themes.Values.Where(t => t.Manifest.Extends == id).Select(t => t.Manifest.Name).ToList();
            if (children.Count > 0) throw new InvalidDataException("Other themes build on this one: " + string.Join(", ", children) + ". Delete those first.");
            var backup = MoveToBackup(folder, id);
            catalog.Reload();
            return backup;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Writes the pack as a ZIP that can be imported again.</summary>
    public async Task ExportAsync(string id, Stream output, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var folder = PackFolder(id);
            using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(folder, file).Replace(Path.DirectorySeparatorChar, '/');
                // Same path rules as import, so an export always imports again.
                SafePaths.Within(folder, relative);
                await using var source = File.OpenRead(file);
                await using var entry = zip.CreateEntry(relative, CompressionLevel.Optimal).Open();
                await source.CopyToAsync(entry, cancellationToken).ConfigureAwait(false);
            }
        }
        finally { _gate.Release(); }
    }

    private string PackFolder(string id)
    {
        if (!SafePaths.IsId(id)) throw new InvalidDataException("Invalid theme id.");
        var folder = SafePaths.Within(catalog.Root, id);
        if (!Directory.Exists(folder)) throw new FileNotFoundException("No installed theme with this id.", id);
        return folder;
    }

    private string MoveToBackup(string folder, string id)
    {
        Directory.CreateDirectory(BackupRoot);
        var backup = Path.Combine(BackupRoot, id + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.Move(folder, backup);
        return backup;
    }
}
