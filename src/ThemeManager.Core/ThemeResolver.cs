namespace ThemeManager.Core;

public static class ThemeResolver
{
    /// <param name="schedules">Schedules the user may resolve: shared ones plus, under <see cref="ThemeSchedule.PersonalId"/>, their own.</param>
    /// <param name="automaticExcluded">Theme ids left out of Automatic Seasonal, e.g. holidays not celebrated where the server is.</param>
    public static ThemeResolution Resolve(ThemeChoice user, ThemeChoice server, CatalogSnapshot catalog, DateOnly date, IReadOnlyDictionary<string, ThemeSchedule>? schedules = null, IReadOnlyCollection<string>? automaticExcluded = null)
    {
        var choice = user.Mode == ThemeMode.FollowServer ? server : user;
        var source = user.Mode == ThemeMode.FollowServer ? "Server" : "User";
        if (choice.Mode == ThemeMode.Original) return new(null, source);
        if (choice.Mode == ThemeMode.Fixed && choice.ThemeId is { } id && catalog.Themes.ContainsKey(id)) return new(id, source);
        if (choice.Mode == ThemeMode.Schedule && choice.ScheduleId is { } scheduleId && schedules?.TryGetValue(scheduleId, out var schedule) == true)
            return new(ResolveSchedule(schedule, catalog, date), source);
        var automatic = SeasonalCalendar.Resolve(catalog.Themes.Values.Where(t => automaticExcluded?.Contains(t.Manifest.Id) != true), date);
        var warning = choice.Mode switch
        {
            ThemeMode.Fixed => "The selected pack is unavailable; using the calendar or original Jellyfin.",
            ThemeMode.Schedule => "The selected calendar no longer exists; using Automatic Seasonal.",
            _ => null,
        };
        return new(automatic, source, warning);
    }

    /// <summary>The matching entry with the highest priority wins; equal priority goes to the earlier entry. No match gives the fallback.</summary>
    public static string? ResolveSchedule(ThemeSchedule schedule, CatalogSnapshot catalog, DateOnly date)
        => schedule.Entries.Select((entry, index) => (Entry: entry, Index: index))
               .Where(p => catalog.Themes.ContainsKey(p.Entry.ThemeId) && Matches(p.Entry.Rule, date))
               .OrderByDescending(p => p.Entry.Rule.Priority).ThenBy(p => p.Index)
               .Select(p => p.Entry.ThemeId).FirstOrDefault()
           ?? (schedule.FallbackThemeId is { } fallback && catalog.Themes.ContainsKey(fallback) ? fallback : null);

    public sealed record TimelineChange(DateOnly From, string? ThemeId);

    /// <summary>Each day in the period where the shown theme changes, starting with <paramref name="from"/>.</summary>
    public static IReadOnlyList<TimelineChange> Timeline(ThemeSchedule schedule, CatalogSnapshot catalog, DateOnly from, int days = 366)
    {
        var changes = new List<TimelineChange>();
        for (var day = 0; day < days; day++)
        {
            var date = from.AddDays(day);
            var theme = ResolveSchedule(schedule, catalog, date);
            if (changes.Count == 0 || changes[^1].ThemeId != theme) changes.Add(new(date, theme));
        }
        return changes;
    }

    // A rule that no longer validates (for example a hand-edited file) is skipped, not fatal.
    private static bool Matches(CalendarRule? rule, DateOnly date)
    {
        if (rule is null) return false;
        try { return SeasonalCalendar.Matches(rule, date); }
        catch (InvalidDataException) { return false; }
    }

    public static void Validate(ThemeChoice choice, CatalogSnapshot catalog, bool allowFollowServer, IReadOnlyDictionary<string, ThemeSchedule>? schedules = null)
    {
        if (!Enum.IsDefined(choice.Mode) || (!allowFollowServer && choice.Mode == ThemeMode.FollowServer)) throw new InvalidDataException("Invalid theme mode.");
        if (choice.Mode == ThemeMode.Fixed && (choice.ThemeId is null || !catalog.Themes.ContainsKey(choice.ThemeId))) throw new InvalidDataException("Select an installed, valid theme.");
        if (choice.Mode == ThemeMode.Schedule && (choice.ScheduleId is null || schedules?.ContainsKey(choice.ScheduleId) != true)) throw new InvalidDataException("Select an existing calendar.");
    }

    /// <summary>Checks a schedule before it is saved and returns it with a trimmed name.</summary>
    public static ThemeSchedule ValidateSchedule(ThemeSchedule? schedule, CatalogSnapshot catalog, bool personal)
    {
        if (schedule is null) throw new InvalidDataException("A calendar is required.");
        var id = personal ? ThemeSchedule.PersonalId : schedule.Id;
        if (!personal && (!SafePaths.IsId(id) || id == ThemeSchedule.PersonalId)) throw new InvalidDataException("Calendar id must use lowercase letters, digits and single hyphens, and cannot be \"personal\".");
        var name = schedule.Name?.Trim() ?? "";
        if (name.Length is 0 or > 60) throw new InvalidDataException("Calendar name must be 1–60 characters.");
        if (schedule.Entries is null || schedule.Entries.Count > ScheduleStore.MaxEntries) throw new InvalidDataException("A calendar can have at most " + ScheduleStore.MaxEntries + " dates.");
        foreach (var entry in schedule.Entries)
        {
            if (entry?.ThemeId is null || !catalog.Themes.ContainsKey(entry.ThemeId)) throw new InvalidDataException("Every date must use an installed theme.");
            if (entry.Rule is null) throw new InvalidDataException("Every date needs a rule.");
            SeasonalCalendar.Validate(entry.Rule);
        }
        if (schedule.FallbackThemeId is { } fallback && !catalog.Themes.ContainsKey(fallback)) throw new InvalidDataException("The theme for other days must be installed.");
        return schedule with { Id = id, Name = name };
    }
}
