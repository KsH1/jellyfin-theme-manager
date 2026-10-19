using System.Text.Json;
using ThemeManager.Core;
using Xunit;

namespace ThemeManager.Tests;

public sealed class FailSafeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "theme-manager-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private void Pack(string packs, string id)
    {
        var path = Path.Combine(packs, id); Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "theme.json"), JsonSerializer.Serialize(new ThemeManifest { Id = id, Name = id, Category = "Permanent" }, ThemeJson.Options));
    }

    [Fact]
    public void UnreadablePacksFolderIsReportedNotThrown()
    {
        Directory.CreateDirectory(_root);
        var packs = Path.Combine(_root, "packs");
        File.WriteAllText(packs, "a file where the folder should be");
        var snapshot = new ThemeCatalog(packs).Reload();
        Assert.Empty(snapshot.Themes);
        Assert.Contains(snapshot.Problems, p => p.Pack == "(catalog)" && p.Message.Contains("could not be read"));
    }

    [Fact]
    public void LastGoodCatalogSurvivesALaterFolderFailure()
    {
        var packs = Path.Combine(_root, "packs");
        Pack(packs, "minimal-dark");
        var catalog = new ThemeCatalog(packs);
        Assert.Single(catalog.Reload().Themes);
        Directory.Delete(packs, true); File.WriteAllText(packs, "broken");
        var snapshot = catalog.Reload();
        Assert.True(snapshot.Themes.ContainsKey("minimal-dark"));
        Assert.Contains(snapshot.Problems, p => p.Pack == "(catalog)");
    }

    [Fact]
    public void OneBrokenPackDoesNotHideTheOthers()
    {
        var packs = Path.Combine(_root, "packs");
        Pack(packs, "minimal-dark");
        Directory.CreateDirectory(Path.Combine(packs, "broken"));
        File.WriteAllText(Path.Combine(packs, "broken", "theme.json"), "{ not json");
        var snapshot = new ThemeCatalog(packs).Reload();
        Assert.True(snapshot.Themes.ContainsKey("minimal-dark"));
        Assert.Contains(snapshot.Problems, p => p.Pack == "broken");
    }

    [Fact]
    public async Task CorruptUserFileFallsBackToServerDefaultAndIsReported()
    {
        var users = Path.Combine(_root, "users"); Directory.CreateDirectory(users);
        var user = Guid.NewGuid();
        var file = Path.Combine(users, user.ToString("N") + ".json");
        File.WriteAllText(file, "{ broken");
        var reported = new List<Guid>();
        var store = new UserThemeStore(users, (id, _) => reported.Add(id));
        Assert.Equal(new ThemeChoice(), await store.GetAsync(user));
        Assert.Equal([user], reported);
        Assert.Equal("{ broken", File.ReadAllText(file));
        await store.SetAsync(user, new(ThemeMode.Original));
        Assert.Equal(ThemeMode.Original, (await store.GetAsync(user)).Mode);
    }
}
