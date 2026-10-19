using ThemeManager.Core;
using Xunit;

namespace ThemeManager.Tests;

public sealed class DefaultPacksTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "theme-manager-tests", Guid.NewGuid().ToString("N"));
    private string Bundled => Path.Combine(_root, "bundled");
    private string Packs => Path.Combine(_root, "packs");
    private string Record => Path.Combine(_root, "default-packs.json");
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private void Bundle(string id, string content = "bundled")
    {
        Directory.CreateDirectory(Path.Combine(Bundled, id));
        File.WriteAllText(Path.Combine(Bundled, id, "theme.json"), content);
    }

    [Fact]
    public void NewInstallGetsEveryBundledPack()
    {
        Bundle("halloween"); Bundle("christmas");
        Assert.Equal(["christmas", "halloween"], DefaultPacks.Seed(Bundled, Packs, Record));
        Assert.Equal("bundled", File.ReadAllText(Path.Combine(Packs, "halloween", "theme.json")));
        Assert.Empty(Directory.GetDirectories(Packs, ".*"));
    }

    [Fact]
    public void ExistingPackIsNeverOverwritten()
    {
        Bundle("halloween");
        Directory.CreateDirectory(Path.Combine(Packs, "halloween"));
        File.WriteAllText(Path.Combine(Packs, "halloween", "theme.json"), "admin's own");
        Assert.Empty(DefaultPacks.Seed(Bundled, Packs, Record));
        Assert.Equal("admin's own", File.ReadAllText(Path.Combine(Packs, "halloween", "theme.json")));
    }

    [Fact]
    public void DeletedDefaultPackDoesNotComeBack()
    {
        Bundle("halloween");
        DefaultPacks.Seed(Bundled, Packs, Record);
        Directory.Delete(Path.Combine(Packs, "halloween"), true);
        Assert.Empty(DefaultPacks.Seed(Bundled, Packs, Record));
        Assert.False(Directory.Exists(Path.Combine(Packs, "halloween")));
    }

    [Fact]
    public void NewPackInALaterPluginVersionIsAdded()
    {
        Bundle("halloween");
        DefaultPacks.Seed(Bundled, Packs, Record);
        Bundle("easter");
        Assert.Equal(["easter"], DefaultPacks.Seed(Bundled, Packs, Record));
    }

    [Fact]
    public void MissingBundleOrInvalidNamesAreIgnored()
    {
        Assert.Empty(DefaultPacks.Seed(Bundled, Packs, Record));
        Bundle("Not A Valid Id");
        Assert.Empty(DefaultPacks.Seed(Bundled, Packs, Record));
        Assert.False(Directory.Exists(Path.Combine(Packs, "Not A Valid Id")));
    }

    [Fact]
    public void UnreadableRecordStartsOverWithoutOverwriting()
    {
        Bundle("halloween");
        Directory.CreateDirectory(_root); File.WriteAllText(Record, "{ not json");
        Assert.Equal(["halloween"], DefaultPacks.Seed(Bundled, Packs, Record));
    }
}
