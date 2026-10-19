using System.Text.Json;
using ThemeManager.Core;
using Xunit;

namespace ThemeManager.Tests;

public sealed class ScheduleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "theme-manager-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private CatalogSnapshot Catalog(params string[] ids)
    {
        foreach (var id in ids)
        {
            var path = Path.Combine(_root, "packs", id); Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "theme.json"), JsonSerializer.Serialize(new ThemeManifest { Id = id, Name = id, Category = "Permanent" }, ThemeJson.Options));
        }
        return new ThemeCatalog(Path.Combine(_root, "packs")).Reload();
    }
    private static ScheduleEntry Entry(string theme, CalendarRule rule) => new() { ThemeId = theme, Rule = rule };
    private static ThemeSchedule Kids => new()
    {
        Id = "kids", Name = "Kids", FallbackThemeId = "ocean",
        Entries =
        [
            Entry("space", new() { Kind = "Month", Month = 7 }),
            Entry("candy", new() { Kind = "Range", Start = "07-10", End = "07-20", Priority = 10 }),
            Entry("neon", new() { Kind = "Range", Start = "12-30", End = "01-02" }),
            Entry("bunny", new() { Kind = "EasterOffset", StartOffset = -3, EndOffset = 1 }),
            Entry("gone", new() { Kind = "Month", Month = 8, Priority = 100 }),
        ],
    };

    [Theory]
    [InlineData(2026, 7, 1, "space")] [InlineData(2026, 7, 15, "candy")] [InlineData(2026, 7, 21, "space")]
    [InlineData(2026, 12, 31, "neon")] [InlineData(2027, 1, 2, "neon")] [InlineData(2026, 4, 5, "bunny")]
    [InlineData(2026, 8, 10, "ocean")] [InlineData(2026, 3, 1, "ocean")]
    public void ScheduleUsesRulesPriorityAndFallback(int year, int month, int day, string expected)
    {
        // "gone" is not installed, so its higher-priority August rule is skipped and the fallback shows.
        var catalog = Catalog("space", "candy", "neon", "bunny", "ocean");
        Assert.Equal(expected, ThemeResolver.ResolveSchedule(Kids, catalog, new DateOnly(year, month, day)));
    }

    [Fact]
    public void EqualPriorityGoesToEarlierEntryAndNoFallbackMeansOriginal()
    {
        var catalog = Catalog("a", "b");
        var schedule = new ThemeSchedule { Id = "x", Name = "X", Entries = [Entry("b", new() { Kind = "Month", Month = 5 }), Entry("a", new() { Kind = "Month", Month = 5 })] };
        Assert.Equal("b", ThemeResolver.ResolveSchedule(schedule, catalog, new DateOnly(2026, 5, 3)));
        Assert.Null(ThemeResolver.ResolveSchedule(schedule, catalog, new DateOnly(2026, 6, 3)));
    }

    [Fact]
    public void TimelineListsOnlyChanges()
    {
        var catalog = Catalog("space", "candy", "neon", "bunny", "ocean");
        var timeline = ThemeResolver.Timeline(Kids, catalog, new DateOnly(2026, 6, 29), days: 30);
        Assert.Equal(
            [(new DateOnly(2026, 6, 29), "ocean"), (new DateOnly(2026, 7, 1), "space"), (new DateOnly(2026, 7, 10), "candy"), (new DateOnly(2026, 7, 21), "space")],
            timeline.Select(c => (c.From, c.ThemeId)));
    }

    [Fact]
    public void ChoiceResolvesSharedPersonalAndMissingSchedules()
    {
        var catalog = Catalog("space", "candy", "neon", "bunny", "ocean");
        var date = new DateOnly(2026, 3, 1);
        var mine = new ThemeSchedule { Id = ThemeSchedule.PersonalId, Name = "Mine", FallbackThemeId = "neon" };
        var schedules = new Dictionary<string, ThemeSchedule> { ["kids"] = Kids, [ThemeSchedule.PersonalId] = mine };
        Assert.Equal("ocean", ThemeResolver.Resolve(new(ThemeMode.Schedule, ScheduleId: "kids"), new(ThemeMode.Original), catalog, date, schedules).ThemeId);
        Assert.Equal("neon", ThemeResolver.Resolve(new(ThemeMode.Schedule, ScheduleId: "personal"), new(ThemeMode.Original), catalog, date, schedules).ThemeId);
        var server = ThemeResolver.Resolve(new(), new(ThemeMode.Schedule, ScheduleId: "kids"), catalog, date, schedules);
        Assert.Equal(("ocean", "Server"), (server.ThemeId, server.Source));
        var missing = ThemeResolver.Resolve(new(ThemeMode.Schedule, ScheduleId: "deleted"), new(ThemeMode.Original), catalog, date, schedules);
        Assert.NotNull(missing.Warning);
    }

    [Fact]
    public void ChoiceValidationRequiresAnAvailableSchedule()
    {
        var catalog = Catalog("ocean");
        var shared = new Dictionary<string, ThemeSchedule> { ["kids"] = Kids };
        ThemeResolver.Validate(new(ThemeMode.Schedule, ScheduleId: "kids"), catalog, false, shared);
        Assert.Throws<InvalidDataException>(() => ThemeResolver.Validate(new(ThemeMode.Schedule, ScheduleId: "personal"), catalog, false, shared));
        Assert.Throws<InvalidDataException>(() => ThemeResolver.Validate(new(ThemeMode.Schedule), catalog, true, shared));
    }

    [Fact]
    public void ScheduleValidationRejectsBadInput()
    {
        var catalog = Catalog("space", "candy", "neon", "bunny", "ocean");
        Assert.Throws<InvalidDataException>(() => ThemeResolver.ValidateSchedule(Kids, catalog, personal: false)); // "gone" is not installed
        var valid = Kids with { Entries = Kids.Entries.Where(e => e.ThemeId != "gone").ToList(), Name = "  Kids  " };
        Assert.Equal("Kids", ThemeResolver.ValidateSchedule(valid, catalog, personal: false).Name);
        Assert.Equal("personal", ThemeResolver.ValidateSchedule(valid with { Id = "anything" }, catalog, personal: true).Id);
        foreach (var bad in new[]
        {
            valid with { Id = "personal" }, valid with { Id = "Bad Id" }, valid with { Name = " " }, valid with { Name = new string('x', 61) },
            valid with { FallbackThemeId = "missing" }, valid with { Entries = null! },
            valid with { Entries = [Entry("ocean", new() { Kind = "Range", Start = "13-01", End = "01-01" })] },
            valid with { Entries = [Entry("ocean", new() { Kind = "Month", Month = 1, Priority = 5000 })] },
            valid with { Entries = Enumerable.Repeat(Entry("ocean", new() { Kind = "Month", Month = 1 }), ScheduleStore.MaxEntries + 1).ToList() },
        })
            Assert.Throws<InvalidDataException>(() => ThemeResolver.ValidateSchedule(bad, catalog, personal: false));
    }

    [Fact]
    public async Task StoreKeepsSharedAndPersonalSeparateAndSurvivesRestart()
    {
        var store = new ScheduleStore(_root);
        var user = Guid.NewGuid(); var other = Guid.NewGuid();
        await store.SaveSharedAsync(Kids);
        await store.SaveSharedAsync(Kids with { Name = "Kids v2" });
        await store.SavePersonalAsync(user, new() { Id = "ignored", Name = "Mine", FallbackThemeId = "ocean" });
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveSharedAsync(Kids with { Id = "personal" }));

        var reopened = new ScheduleStore(_root);
        Assert.Equal(["Kids v2"], (await reopened.GetSharedAsync()).Select(s => s.Name));
        var forUser = await reopened.ForUserAsync(user);
        Assert.Equal("Mine", forUser["personal"].Name);
        Assert.Equal("personal", forUser["personal"].Id);
        Assert.False((await reopened.ForUserAsync(other)).ContainsKey("personal"));

        Assert.True(await reopened.DeleteSharedAsync("kids"));
        Assert.False(await reopened.DeleteSharedAsync("kids"));
        await reopened.DeletePersonalAsync(user);
        Assert.Empty(await reopened.ForUserAsync(user));
        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task UnreadableFileIsReportedAndTreatedAsEmpty()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "schedules.json"), "{ not json");
        var reported = new List<string>();
        var store = new ScheduleStore(_root, (path, _) => reported.Add(Path.GetFileName(path)));
        Assert.Empty(await store.GetSharedAsync());
        Assert.Equal(["schedules.json"], reported);
    }
}
