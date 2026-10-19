using System.Text.Json;

namespace ThemeManager.Core;

/// <summary>
/// Copies the theme packs that ship with the plugin into the server's pack folder, so a new install has themes.
/// A pack is copied once: an existing folder is never overwritten, and a default pack an administrator deleted
/// does not come back on the next start.
/// </summary>
public static class DefaultPacks
{
    public const string FolderName = "default-packs";

    /// <summary>Returns the ids that were copied this time.</summary>
    public static IReadOnlyList<string> Seed(string bundledRoot, string packsRoot, string recordPath)
    {
        if (!Directory.Exists(bundledRoot)) return [];
        var seeded = ReadRecord(recordPath);
        var copied = new List<string>();
        Directory.CreateDirectory(packsRoot);
        foreach (var source in Directory.GetDirectories(bundledRoot).Order(StringComparer.Ordinal))
        {
            var id = Path.GetFileName(source);
            if (!SafePaths.IsId(id) || seeded.Contains(id)) continue;
            var target = Path.Combine(packsRoot, id);
            if (!Directory.Exists(target))
            {
                // Copy next to the target first, then move: a crash never leaves a half-copied pack in the catalog.
                var staging = Path.Combine(packsRoot, "." + id + "." + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    Directory.CreateDirectory(staging);
                    foreach (var file in Directory.GetFiles(source))
                        File.Copy(file, Path.Combine(staging, Path.GetFileName(file)));
                    Directory.Move(staging, target);
                    copied.Add(id);
                }
                finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            }
            // An existing pack with this id (the admin's own or an earlier copy) also counts as done.
            seeded.Add(id);
        }
        WriteRecord(recordPath, seeded);
        return copied;
    }

    private static HashSet<string> ReadRecord(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(path)) ?? [] : []; }
        catch (JsonException) { return []; }
    }

    private static void WriteRecord(string path, HashSet<string> ids)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(ids.Order(StringComparer.Ordinal)));
        File.Move(temporary, path, overwrite: true);
    }
}
