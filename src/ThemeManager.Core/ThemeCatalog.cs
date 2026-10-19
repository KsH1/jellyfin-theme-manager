using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ThemeManager.Core;

public sealed partial class ThemeCatalog(string root)
{
    private readonly object _sync = new();
    private CatalogSnapshot _snapshot = new(new ReadOnlyDictionary<string, ResolvedTheme>(new Dictionary<string, ResolvedTheme>()), [], DateTimeOffset.MinValue);
    public string Root { get; } = Path.GetFullPath(root);
    public CatalogSnapshot Snapshot => Volatile.Read(ref _snapshot);
    private static readonly HashSet<string> AssetKeys = ["background", "splash", "thumbnail", "collections", "movies", "tvShows"];
    private static readonly HashSet<string> ColorKeys = ["accent", "secondary", "surface", "text", "background"];

    [GeneratedRegex("^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex HexColor();

    /// <summary>Never throws for bad packs or an unreadable folder: problems are reported in the snapshot instead.</summary>
    public CatalogSnapshot Reload()
    {
        lock (_sync)
        {
            try { return Load(); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Keep serving the last good catalog so users are not affected by a temporary disk problem.
                var previous = Snapshot;
                var snapshot = new CatalogSnapshot(previous.Themes, [new("(catalog)", "Theme packs folder could not be read: " + ex.Message)], DateTimeOffset.UtcNow);
                Volatile.Write(ref _snapshot, snapshot);
                return snapshot;
            }
        }
    }

    private CatalogSnapshot Load()
    {
        {
            Directory.CreateDirectory(Root);
            var manifests = new Dictionary<string, (ThemeManifest Manifest, string Directory)>(StringComparer.Ordinal);
            var problems = new List<CatalogProblem>();
            foreach (var directory in Directory.GetDirectories(Root).Order(StringComparer.Ordinal))
            {
                var folder = Path.GetFileName(directory);
                try
                {
                    if (!SafePaths.IsId(folder)) throw new InvalidDataException("Directory name must be a theme id.");
                    var path = SafePaths.Within(Root, folder + "/theme.json");
                    if (!File.Exists(path) || new FileInfo(path).Length > 65536) throw new InvalidDataException("A theme.json smaller than 64 KiB is required.");
                    var manifest = JsonSerializer.Deserialize<ThemeManifest>(File.ReadAllText(path), ThemeJson.Options) ?? throw new InvalidDataException("Empty manifest.");
                    ValidateManifest(manifest);
                    if (manifest.Id != folder) throw new InvalidDataException("Theme id must match the directory name.");
                    manifests.Add(manifest.Id, (manifest, directory));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    problems.Add(new(folder, ex.Message));
                }
            }

            var resolved = new Dictionary<string, ResolvedTheme>(StringComparer.Ordinal);
            ResolvedTheme Resolve(string id, HashSet<string> visited)
            {
                if (resolved.TryGetValue(id, out var cached)) return cached;
                if (visited.Count >= 16 || !visited.Add(id)) throw new InvalidDataException("Cyclic or excessive theme inheritance.");
                if (!manifests.TryGetValue(id, out var entry)) throw new InvalidDataException("Missing parent theme: " + id);
                var palette = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["accent"] = "#00a4dc", ["secondary"] = "#aa5cc3", ["surface"] = "#101722", ["text"] = "#ffffff", ["background"] = "#080b12",
                };
                var assets = new Dictionary<string, ResolvedAsset>(StringComparer.Ordinal);
                if (entry.Manifest.Extends is { } parent)
                {
                    var inherited = Resolve(parent, visited);
                    foreach (var pair in inherited.Palette) palette[pair.Key] = pair.Value;
                    foreach (var pair in inherited.Assets) assets[pair.Key] = pair.Value;
                }
                foreach (var pair in entry.Manifest.Palette) palette[pair.Key] = pair.Value;
                foreach (var pair in entry.Manifest.Assets)
                {
                    var path = SafePaths.Within(entry.Directory, pair.Value);
                    if (!File.Exists(path)) throw new InvalidDataException("Declared asset does not exist: " + pair.Key);
                    var size = new FileInfo(path).Length;
                    if (size is < 16 or > 20 * 1024 * 1024) throw new InvalidDataException("Asset size must be between 16 bytes and 20 MiB.");
                    using var stream = File.OpenRead(path);
                    var type = ValidateImage(stream, path);
                    stream.Position = 0;
                    var revision = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                    assets[pair.Key] = new(path, type, revision);
                }
                visited.Remove(id);
                var theme = new ResolvedTheme(entry.Manifest, new ReadOnlyDictionary<string, string>(palette), new ReadOnlyDictionary<string, ResolvedAsset>(assets));
                resolved[id] = theme;
                return theme;
            }

            foreach (var id in manifests.Keys)
            {
                try { Resolve(id, new HashSet<string>(StringComparer.Ordinal)); }
                catch (Exception ex) when (ex is not OutOfMemoryException) { problems.Add(new(id, ex.Message)); }
            }
            var snapshot = new CatalogSnapshot(new ReadOnlyDictionary<string, ResolvedTheme>(resolved), problems.AsReadOnly(), DateTimeOffset.UtcNow);
            Volatile.Write(ref _snapshot, snapshot);
            return snapshot;
        }
    }

    private static string ValidateImage(Stream stream, string path)
    {
        Span<byte> header = stackalloc byte[16];
        stream.ReadExactly(header);
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" when header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) => "image/png",
            ".jpg" or ".jpeg" when header[0] == 255 && header[1] == 216 && header[2] == 255 => "image/jpeg",
            ".webp" when header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8) => "image/webp",
            _ => throw new InvalidDataException("Only PNG, JPEG and WebP images with matching headers are supported."),
        };
    }

    private static void ValidateManifest(ThemeManifest manifest)
    {
        if (manifest.SchemaVersion != 1 || !SafePaths.IsId(manifest.Id) || string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Length > 100)
            throw new InvalidDataException("Invalid theme schema, id or name.");
        if (!Version.TryParse(manifest.Version, out _)) throw new InvalidDataException("Invalid version.");
        if (manifest.Category is not ("Calendar" or "Permanent" or "Custom")) throw new InvalidDataException("Invalid category.");
        if (manifest.Extends is { } parent && !SafePaths.IsId(parent)) throw new InvalidDataException("Invalid parent id.");
        if (manifest.Palette is null || manifest.Assets is null) throw new InvalidDataException("Palette and assets cannot be null.");
        if (manifest.Palette.Any(p => !ColorKeys.Contains(p.Key) || p.Value is null || !HexColor().IsMatch(p.Value))) throw new InvalidDataException("Palette accepts known keys and #RRGGBB colors only.");
        if (manifest.Assets.Keys.Any(k => !AssetKeys.Contains(k))) throw new InvalidDataException("Unknown asset. Executable code and arbitrary CSS are not accepted in schema 1.");
        if (manifest.Calendar is { } rule) SeasonalCalendar.Validate(rule);
    }
}
