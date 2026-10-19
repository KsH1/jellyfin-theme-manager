using System.Text.Json;

namespace ThemeManager.Core;

/// <summary>A Jellyfin library as the server sees it.</summary>
public sealed record LibraryInfo(string Id, string? CollectionType, string Name);

/// <summary>
/// Which library gets which theme banner. The same rule as the web client (client-core.mjs, librarySlots): one library
/// per banner type uses the theme banner, the one whose name matches the type (e.g. "Film" rather than "Børnefilm"),
/// otherwise the first. Libraries without a matching type get no banner.
/// </summary>
public static class LibraryBanners
{
    private static readonly Dictionary<string, string> BannerTypes = new(StringComparer.Ordinal)
    {
        ["movies"] = "movies", ["tvshows"] = "tvShows", ["boxsets"] = "collections",
    };
    private static readonly Dictionary<string, string[]> TypeNames = new(StringComparer.Ordinal)
    {
        ["movies"] = ["movies", "movie", "film", "films", "filmer", "spillefilm"],
        ["tvShows"] = ["tvshows", "tv", "series", "serier", "shows", "tvserier"],
        ["collections"] = ["collections", "samlinger", "boxsets"],
    };

    private static string Plain(string? name) => new((name ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>An administrator's choice: the theme's background with the library name.</summary>
    public const string Generic = "generic";
    /// <summary>An administrator's choice: leave the library's own image.</summary>
    public const string None = "none";
    /// <summary>What an administrator may choose for one library; no choice means automatic.</summary>
    public static readonly IReadOnlyList<string> Choices = ["movies", "tvShows", "collections", Generic, None];

    /// <summary>
    /// Library id to banner slot, for the libraries that get one. A library the administrator chose for gets that choice
    /// (<see cref="None"/> included; <see cref="Generic"/> means no entry) and takes no part in the automatic rule.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Assign(IEnumerable<LibraryInfo> libraries, IReadOnlyDictionary<string, string>? chosen = null)
    {
        var owner = new Dictionary<string, (string Id, bool Named)>(StringComparer.Ordinal);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var library in libraries)
        {
            if (chosen?.GetValueOrDefault(library.Id) is { } choice && Choices.Contains(choice))
            {
                if (choice != Generic) result[library.Id] = choice;
                continue;
            }
            if (library.CollectionType is null || !BannerTypes.TryGetValue(library.CollectionType, out var slot)) continue;
            var named = TypeNames[slot].Contains(Plain(library.Name));
            if (!owner.TryGetValue(slot, out var current) || (named && !current.Named)) owner[slot] = (library.Id, named);
        }
        foreach (var (slot, library) in owner) result[library.Id] = slot;
        return result;
    }
}

/// <summary>What the plugin did to one library's image, so it can be undone.</summary>
public sealed record LibraryImageRecord
{
    /// <summary>Backup of the image the library had before the plugin first changed it; null if it had none.</summary>
    public string? Backup { get; init; }
    /// <summary>Revision of the theme banner now set, so an unchanged banner is not written again.</summary>
    public string? Applied { get; init; }
}

/// <summary>Per-library records in one JSON file, written atomically.</summary>
public sealed class ServerImageState(string root)
{
    public string Root { get; } = Path.GetFullPath(root);
    private string FilePath => Path.Combine(Root, "state.json");

    public Dictionary<string, LibraryImageRecord> Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<Dictionary<string, LibraryImageRecord>>(File.ReadAllText(FilePath), ThemeJson.Options) ?? [] : []; }
        // An unreadable state must not lose the backups: the backup files stay in Root and are listed in the log.
        catch (JsonException) { return []; }
    }

    public void Save(Dictionary<string, LibraryImageRecord> state)
    {
        Directory.CreateDirectory(Root);
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, ThemeJson.Options));
        File.Move(temporary, FilePath, overwrite: true);
    }

    /// <summary>Copies the library's current image into the backup folder and returns the copy's path.</summary>
    public string Backup(string libraryId, string imagePath)
    {
        Directory.CreateDirectory(Root);
        if (!Guid.TryParse(libraryId, out var id)) throw new ArgumentException("A library id is a GUID.", nameof(libraryId));
        var target = Path.Combine(Root, "original-" + id.ToString("N") + Path.GetExtension(imagePath).ToLowerInvariant());
        File.Copy(imagePath, target, overwrite: true);
        return target;
    }
}
