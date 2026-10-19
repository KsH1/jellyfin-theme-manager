using System.IO.Compression;
using System.Text.Json;
using ThemeManager.Core;
using Xunit;

namespace ThemeManager.Tests;

public sealed class CoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "theme-manager-tests", Guid.NewGuid().ToString("N"));
    private ThemeCatalog Catalog => new(Path.Combine(_root, "packs"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private void Pack(ThemeManifest manifest)
    {
        var path = Path.Combine(_root, "packs", manifest.Id); Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "theme.json"), JsonSerializer.Serialize(manifest, ThemeJson.Options));
    }
    private static MemoryStream Zip(params (string Path, string Content)[] files)
    {
        var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
            foreach (var (path, content) in files) { using var writer = new StreamWriter(zip.CreateEntry(path).Open()); writer.Write(content); }
        buffer.Position = 0; return buffer;
    }

    [Theory]
    [InlineData(2024, 3, 31)] [InlineData(2025, 4, 20)] [InlineData(2026, 4, 5)] [InlineData(2027, 3, 28)] [InlineData(2038, 4, 25)]
    public void EasterIsComputed(int year, int month, int day) => Assert.Equal(new DateOnly(year, month, day), SeasonalCalendar.Easter(year));

    [Theory]
    [InlineData(12,29,false)] [InlineData(12,30,true)] [InlineData(12,31,true)] [InlineData(1,1,true)] [InlineData(1,2,true)] [InlineData(1,3,false)]
    public void NewYearWrapsAndIncludesBoundaries(int month, int day, bool expected) => Assert.Equal(expected, SeasonalCalendar.Matches(new() { Kind = "Range", Start = "12-30", End = "01-02" }, new DateOnly(2026, month, day)));

    [Theory]
    [InlineData(-4,false)] [InlineData(-3,true)] [InlineData(0,true)] [InlineData(1,true)] [InlineData(2,false)]
    public void EasterOffsetBoundaries(int offset, bool expected) => Assert.Equal(expected, SeasonalCalendar.Matches(new() { Kind = "EasterOffset", StartOffset = -3, EndOffset = 1 }, SeasonalCalendar.Easter(2026).AddDays(offset)));

    [Fact]
    public void UsesCopenhagenDateNotUtc() => Assert.Equal(new DateOnly(2027, 1, 1), SeasonalCalendar.LocalDate(new DateTimeOffset(2026,12,31,23,30,0,TimeSpan.Zero), "Europe/Copenhagen"));

    [Fact]
    public void HolidayWinsAndUserOverridesServer()
    {
        Pack(new() { Id = "october", Name = "October", Calendar = new() { Month = 10 } });
        Pack(new() { Id = "halloween", Name = "Halloween", Calendar = new() { Kind = "Range", Start = "10-15", End = "10-31", Priority = 100 } });
        var snapshot = Catalog.Reload(); var date = new DateOnly(2026,10,18);
        Assert.Equal("halloween", ThemeResolver.Resolve(new(), new(ThemeMode.Automatic), snapshot, date).ThemeId);
        Assert.Equal("october", ThemeResolver.Resolve(new(ThemeMode.Fixed, "october"), new(ThemeMode.Automatic), snapshot, date).ThemeId);
        Assert.Null(ThemeResolver.Resolve(new(ThemeMode.Original), new(ThemeMode.Fixed, "halloween"), snapshot, date).ThemeId);
        Assert.Equal("halloween", ThemeResolver.Resolve(new(ThemeMode.Automatic), new(ThemeMode.Original), snapshot, date).ThemeId);
        Assert.NotNull(ThemeResolver.Resolve(new(ThemeMode.Fixed, "missing"), new(ThemeMode.Original), snapshot, date).Warning);
    }

    [Theory]
    [InlineData("snow", true)] [InlineData("none", true)] [InlineData("fireworks", false)] [InlineData("Snow", false)]
    public void OnlyKnownEffectsAreAccepted(string effect, bool valid)
    {
        Pack(new() { Id = "fx", Name = "Effects", Effect = effect });
        var snapshot = Catalog.Reload();
        Assert.Equal(valid, snapshot.Themes.ContainsKey("fx"));
        if (valid) Assert.Equal(effect, snapshot.Themes["fx"].Manifest.Effect);
    }

    [Fact]
    public void ExcludedHolidayGivesWayToTheMonth()
    {
        Pack(new() { Id = "june", Name = "June", Calendar = new() { Month = 6 } });
        Pack(new() { Id = "sankthans", Name = "Midsummer", Calendar = new() { Kind = "Range", Start = "06-23", End = "06-24", Priority = 80 } });
        var snapshot = Catalog.Reload(); var date = new DateOnly(2026, 6, 23);
        Assert.Equal("sankthans", ThemeResolver.Resolve(new(), new(ThemeMode.Automatic), snapshot, date).ThemeId);
        Assert.Equal("june", ThemeResolver.Resolve(new(), new(ThemeMode.Automatic), snapshot, date, automaticExcluded: ["sankthans"]).ThemeId);
        // A fixed choice of an excluded holiday still works: exclusion only affects Automatic Seasonal.
        Assert.Equal("sankthans", ThemeResolver.Resolve(new(ThemeMode.Fixed, "sankthans"), new(ThemeMode.Automatic), snapshot, date, automaticExcluded: ["sankthans"]).ThemeId);
    }

    [Theory]
    [InlineData("Europe/Copenhagen", "US", false)] [InlineData("America/New_York", "DK", false)]
    [InlineData("America/New_York", "US", true)] [InlineData("Romance Standard Time", "FR", true)] [InlineData(null, null, true)]
    public void DanishHolidaysAreOnlyDefaultInDenmark(string? zone, string? region, bool excluded)
        => Assert.Equal(excluded ? RegionalDefaults.DanishHolidays : [], RegionalDefaults.Excluded(zone, region));

    [Theory]
    [InlineData("../escape.png")] [InlineData("/escape.png")] [InlineData("C:/escape.png")]
    [InlineData("a\\b.png")] [InlineData("a/../b.png")] [InlineData("a//b.png")]
    [InlineData("a./b.png")] [InlineData("a /b.png")] [InlineData("image.png:evil")]
    [InlineData("CON.png")] [InlineData("nested/nul.jpg")] [InlineData("LPT1.webp")] [InlineData("wild*.png")]
    public void RejectsUnsafePaths(string path) => Assert.Throws<InvalidDataException>(() => SafePaths.Within(_root, path));

    [Fact]
    public void InheritsPaletteAndRejectsCyclesWithoutLosingValidPacks()
    {
        Pack(new() { Id="base", Name="Base", Palette=new() { ["accent"]="#abcdef" } });
        Pack(new() { Id="child", Name="Child", Extends="base", Palette=new() { ["text"]="#112233" } });
        Pack(new() { Id="cycle-a", Name="Cycle A", Extends="cycle-b" });
        Pack(new() { Id="cycle-b", Name="Cycle B", Extends="cycle-a" });
        var result = Catalog.Reload();
        Assert.Equal(2,result.Themes.Count); Assert.Equal(2,result.Problems.Count);
        Assert.Equal("#abcdef",result.Themes["child"].Palette["accent"]);
        Assert.Equal("#112233",result.Themes["child"].Palette["text"]);
    }

    [Theory]
    [InlineData("{\"id\":null,\"name\":\"Bad\"}")]
    [InlineData("{\"id\":\"bad\",\"name\":\"Bad\",\"palette\":null}")]
    [InlineData("{\"id\":\"bad\",\"name\":\"Bad\",\"palette\":{\"accent\":\"url(https://example.com)\"}}")]
    [InlineData("{\"id\":\"bad\",\"name\":\"Bad\",\"script\":\"evil\"}")]
    [InlineData("{\"id\":\"bad\",\"name\":\"Bad\",\"assets\":{\"background\":null}}")]
    public void InvalidManifestIsQuarantined(string json)
    {
        var path=Path.Combine(_root,"packs","bad"); Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path,"theme.json"),json);
        var result=Catalog.Reload(); Assert.Empty(result.Themes); Assert.Single(result.Problems);
    }

    [Fact]
    public async Task ZipImportIsAtomicAndRefusesOverwrite()
    {
        var catalog=Catalog; catalog.Reload(); var installer=new ThemePackInstaller(catalog);
        var json=JsonSerializer.Serialize(new ThemeManifest { Id="custom",Name="Custom" },ThemeJson.Options);
        using var first=Zip(("theme.json",json)); Assert.Equal("custom",await installer.InstallAsync(first,default));
        using var second=Zip(("theme.json",json)); await Assert.ThrowsAsync<InvalidDataException>(()=>installer.InstallAsync(second,default));
        Assert.Single(catalog.Snapshot.Themes);
    }

    [Theory]
    [InlineData("../outside.txt")] [InlineData("evil.js")] [InlineData("image.svg")]
    public async Task ZipRejectsTraversalAndCode(string name)
    {
        var catalog=Catalog; catalog.Reload(); using var input=Zip((name,"test"));
        await Assert.ThrowsAsync<InvalidDataException>(()=>new ThemePackInstaller(catalog).InstallAsync(input,default));
        Assert.Empty(catalog.Snapshot.Themes); Assert.False(File.Exists(Path.Combine(_root,"outside.txt")));
    }

    [Fact]
    public async Task InvalidPackRollsBack()
    {
        var catalog=Catalog; catalog.Reload(); using var input=Zip(("theme.json","{\"id\":\"bad\",\"name\":\"Bad\",\"assets\":{\"background\":\"missing.png\"}}"));
        await Assert.ThrowsAsync<InvalidDataException>(()=>new ThemePackInstaller(catalog).InstallAsync(input,default));
        Assert.False(Directory.Exists(Path.Combine(catalog.Root,"bad"))); Assert.Empty(catalog.Snapshot.Themes);
    }

    [Fact]
    public async Task ReplaceKeepsBackupAndRestoresOnFailure()
    {
        var catalog=Catalog; catalog.Reload(); var installer=new ThemePackInstaller(catalog);
        using var first=Zip(("theme.json",JsonSerializer.Serialize(new ThemeManifest { Id="custom",Name="Old" },ThemeJson.Options)));
        await installer.InstallAsync(first,default);
        using var bad=Zip(("theme.json","{\"id\":\"custom\",\"name\":\"Bad\",\"assets\":{\"background\":\"missing.png\"}}"));
        await Assert.ThrowsAsync<InvalidDataException>(()=>installer.InstallAsync(bad,default,replace:true));
        Assert.Equal("Old",catalog.Snapshot.Themes["custom"].Manifest.Name);
        Assert.False(Directory.Exists(installer.BackupRoot) && Directory.EnumerateDirectories(installer.BackupRoot).Any());
        using var good=Zip(("theme.json",JsonSerializer.Serialize(new ThemeManifest { Id="custom",Name="New" },ThemeJson.Options)));
        await installer.InstallAsync(good,default,replace:true);
        Assert.Equal("New",catalog.Snapshot.Themes["custom"].Manifest.Name);
        Assert.Contains("\"Old\"",File.ReadAllText(Path.Combine(Assert.Single(Directory.GetDirectories(installer.BackupRoot)),"theme.json")));
    }

    [Fact]
    public async Task ExportImportsAgainAndDeleteKeepsBackup()
    {
        Pack(new ThemeManifest { Id="base",Name="Base" }); Pack(new ThemeManifest { Id="child",Name="Child",Extends="base" });
        var catalog=Catalog; catalog.Reload(); var installer=new ThemePackInstaller(catalog);
        await Assert.ThrowsAsync<InvalidDataException>(()=>installer.DeleteAsync("base",default));
        using var exported=new MemoryStream(); await installer.ExportAsync("child",exported,default);
        var backup=await installer.DeleteAsync("child",default);
        Assert.False(catalog.Snapshot.Themes.ContainsKey("child")); Assert.True(File.Exists(Path.Combine(backup,"theme.json")));
        exported.Position=0; Assert.Equal("child",await installer.InstallAsync(exported,default));
        await Assert.ThrowsAsync<FileNotFoundException>(()=>installer.DeleteAsync("missing",default));
        await Assert.ThrowsAsync<InvalidDataException>(()=>installer.DeleteAsync("../packs",default));
    }

    [Fact]
    public async Task UserPreferencesPersistAndStayIsolated()
    {
        var path=Path.Combine(_root,"users"); var store=new UserThemeStore(path); var alice=Guid.NewGuid(); var bob=Guid.NewGuid();
        await Task.WhenAll(store.SetAsync(alice,new(ThemeMode.Fixed,"halloween")),store.SetAsync(bob,new(ThemeMode.Original)));
        var reopened=new UserThemeStore(path);
        Assert.Equal(new(ThemeMode.Fixed,"halloween"),await reopened.GetAsync(alice));
        Assert.Equal(new(ThemeMode.Original),await reopened.GetAsync(bob));
        Assert.Equal(new(),await reopened.GetAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<ArgumentException>(()=>store.GetAsync(Guid.Empty));
    }
}
