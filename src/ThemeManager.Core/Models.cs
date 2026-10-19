using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThemeManager.Core;

[JsonConverter(typeof(JsonStringEnumConverter<ThemeMode>))]
public enum ThemeMode { FollowServer, Automatic, Fixed, Original, Schedule }

/// <param name="ScheduleId">For <see cref="ThemeMode.Schedule"/>: a shared schedule id, or <see cref="ThemeSchedule.PersonalId"/> for the user's own.</param>
public sealed record ThemeChoice(ThemeMode Mode = ThemeMode.FollowServer, string? ThemeId = null, string? ScheduleId = null);

/// <summary>One line of a custom calendar: show <see cref="ThemeId"/> when <see cref="Rule"/> matches the date.</summary>
public sealed record ScheduleEntry
{
    public string ThemeId { get; init; } = "";
    public CalendarRule Rule { get; init; } = new();
}

/// <summary>A user-made "Automatic Seasonal": dated entries plus a theme for the remaining days.</summary>
public sealed record ThemeSchedule
{
    public const string PersonalId = "personal";
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string? FallbackThemeId { get; init; }
    public List<ScheduleEntry> Entries { get; init; } = [];
}

public sealed record CalendarRule
{
    public string Kind { get; init; } = "Month";
    public int Month { get; init; }
    public string? Start { get; init; }
    public string? End { get; init; }
    public int StartOffset { get; init; }
    public int EndOffset { get; init; }
    public int Priority { get; init; }
}

public sealed record ThemeManifest
{
    public int SchemaVersion { get; init; } = 1;
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Version { get; init; } = "1.0.0";
    public string Author { get; init; } = "Local";
    public string Category { get; init; } = "Custom";
    public string? Extends { get; init; }
    public CalendarRule? Calendar { get; init; }
    public Dictionary<string, string> Palette { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Assets { get; init; } = new(StringComparer.Ordinal);
}

public sealed record ResolvedAsset(string Path, string ContentType, string Revision);
public sealed record ResolvedTheme(ThemeManifest Manifest, IReadOnlyDictionary<string, string> Palette, IReadOnlyDictionary<string, ResolvedAsset> Assets);
public sealed record CatalogProblem(string Pack, string Message);
public sealed record CatalogSnapshot(IReadOnlyDictionary<string, ResolvedTheme> Themes, IReadOnlyList<CatalogProblem> Problems, DateTimeOffset LoadedAt);
public sealed record ThemeResolution(string? ThemeId, string Source, string? Warning = null);

public static class ThemeJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16,
    };
}
