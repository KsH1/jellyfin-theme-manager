using ThemeManager.Core;
using Xunit;

namespace ThemeManager.Tests;

public sealed class ServerImagesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "theme-manager-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public void BannerGoesToTheLibraryNamedForItsType()
    {
        var assigned = LibraryBanners.Assign([
            new("a", "movies", "Børnefilm"), new("b", "movies", "Film"), new("c", "tvshows", "Anime"),
            new("d", "tvshows", "TV-serier"), new("e", "boxsets", "Mine samlinger"), new("f", "music", "Musik"), new("g", null, "Blandet"),
        ]);
        Assert.Equal(new Dictionary<string, string> { ["b"] = "movies", ["d"] = "tvShows", ["e"] = "collections" }, assigned);
    }

    [Fact]
    public void FirstLibraryOfATypeWinsWithoutANamedOne()
        => Assert.Equal("movies", LibraryBanners.Assign([new("a", "movies", "Kids"), new("b", "movies", "Classics")])["a"]);

    [Fact]
    public void StateRoundTripsAndBackupCopiesTheImage()
    {
        var state = new ServerImageState(_root);
        Assert.Empty(state.Load());
        var source = Path.Combine(_root, "current.JPG"); Directory.CreateDirectory(_root); File.WriteAllText(source, "original");
        var id = Guid.NewGuid().ToString();
        var backup = state.Backup(id, source);
        Assert.Equal("original", File.ReadAllText(backup));
        Assert.EndsWith(".jpg", backup);
        state.Save(new() { [id] = new() { Backup = backup, Applied = "rev1" }, ["other"] = new() { Applied = "rev2" } });
        var loaded = state.Load();
        Assert.Equal(backup, loaded[id].Backup); Assert.Equal("rev1", loaded[id].Applied); Assert.Null(loaded["other"].Backup);
    }

    [Fact]
    public void BackupRejectsNonGuidIds() => Assert.Throws<ArgumentException>(() => new ServerImageState(_root).Backup("../x", "y.png"));

    [Fact]
    public void UnreadableStateIsEmptyNotAnError()
    {
        Directory.CreateDirectory(_root); File.WriteAllText(Path.Combine(_root, "state.json"), "{ broken");
        Assert.Empty(new ServerImageState(_root).Load());
    }
}
