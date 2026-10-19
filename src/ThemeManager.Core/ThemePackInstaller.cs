using System.IO.Compression;
using System.Text.Json;

namespace ThemeManager.Core;

public sealed class ThemePackInstaller(ThemeCatalog catalog)
{
    public const long MaxExpandedBytes = 128 * 1024 * 1024;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<string> InstallAsync(Stream input, CancellationToken cancellationToken)
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
            if (Directory.Exists(target)) throw new InvalidDataException("This theme already exists. Existing packs are never overwritten by import.");
            if (manifest.Extends is { } parent && !catalog.Snapshot.Themes.ContainsKey(parent)) throw new InvalidDataException("Install the parent theme first.");
            Directory.CreateDirectory(catalog.Root);
            Directory.Move(staging, target);
            var snapshot = catalog.Reload();
            if (!snapshot.Themes.ContainsKey(manifest.Id))
            {
                // Roll back only the exact new directory moved above; existing packs are untouched.
                Directory.Move(target, staging);
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
}
