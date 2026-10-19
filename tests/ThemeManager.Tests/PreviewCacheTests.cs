using System.Collections.ObjectModel;
using ThemeManager.Core;
using Xunit;

namespace ThemeManager.Tests;

public sealed class PreviewCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "theme-manager-tests", Guid.NewGuid().ToString("N"));
    private string Previews => Path.Combine(_root, "previews");
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private ResolvedAsset Asset(string revision)
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, revision + ".png"); File.WriteAllText(path, "original");
        return new(path, "image/png", revision);
    }
    private static CatalogSnapshot Snapshot(params ResolvedAsset[] assets) => new(
        new ReadOnlyDictionary<string, ResolvedTheme>(new Dictionary<string, ResolvedTheme>
        {
            ["t"] = new(new ThemeManifest { Id = "t", Name = "T" }, new Dictionary<string, string>(), assets.Select((a, i) => (a, i)).ToDictionary(p => "slot" + p.i, p => p.a)),
        }), [], DateTimeOffset.UtcNow);

    [Theory]
    [InlineData("background", PreviewCache.BackgroundWidth)] [InlineData("thumbnail", PreviewCache.BackgroundWidth)]
    [InlineData("movies", PreviewCache.BannerWidth)] [InlineData("tvShows", PreviewCache.BannerWidth)] [InlineData("collections", PreviewCache.BannerWidth)]
    public void WidthDependsOnSlot(string slot, int width) => Assert.Equal(width, PreviewCache.WidthFor(slot));

    [Fact]
    public async Task ConcurrentRequestsEncodeOnceAndReuseTheFile()
    {
        var calls = 0; var widths = new List<int>();
        var cache = new PreviewCache(Previews, (source, output, width) => { Interlocked.Increment(ref calls); lock (widths) widths.Add(width); Thread.Sleep(30); File.WriteAllText(output, "small"); return output; });
        var asset = Asset("aa11");
        var paths = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => cache.GetAsync(asset, "movies")));
        Assert.Equal(1, calls); Assert.Equal([PreviewCache.BannerWidth], widths);
        Assert.All(paths, p => Assert.Equal(Path.Combine(Previews, "aa11-640.webp"), p));
        Assert.Equal("small", File.ReadAllText(paths[0]));
        Assert.Equal(paths[0], await cache.GetAsync(asset, "movies"));
        Assert.Equal(1, calls);
        Assert.Empty(Directory.GetFiles(Previews, "*.tmp"));
    }

    [Fact]
    public async Task ChangedRevisionGetsANewCopy()
    {
        var calls = 0;
        var cache = new PreviewCache(Previews, (_, output, _) => { calls++; File.WriteAllText(output, "small"); return output; });
        var first = await cache.GetAsync(Asset("aa11"), "background");
        var second = await cache.GetAsync(Asset("bb22"), "background");
        Assert.NotEqual(first, second); Assert.Equal(2, calls);
    }

    [Fact]
    public async Task SmallSourceIsServedAsIs()
    {
        var cache = new PreviewCache(Previews, (source, _, _) => source);
        var asset = Asset("aa11");
        Assert.Equal(asset.Path, await cache.GetAsync(asset, "movies"));
        Assert.False(Directory.Exists(Previews) && Directory.EnumerateFiles(Previews).Any());
    }

    [Fact]
    public async Task EncoderFailureFallsBackToOriginalWithoutRetryingUntilPrune()
    {
        var calls = 0;
        var cache = new PreviewCache(Previews, (_, output, _) => { calls++; File.WriteAllText(output, "partial"); throw new InvalidOperationException("no encoder"); });
        var asset = Asset("aa11");
        Assert.Equal(asset.Path, await cache.GetAsync(asset, "movies"));
        Assert.Equal(asset.Path, await cache.GetAsync(asset, "movies"));
        Assert.Equal(1, calls);
        Assert.Empty(Directory.GetFiles(Previews));
        cache.Prune(Snapshot(asset));
        await cache.GetAsync(asset, "movies");
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task PruneRemovesOnlyStaleRevisions()
    {
        var cache = new PreviewCache(Previews, (_, output, _) => { File.WriteAllText(output, "small"); return output; });
        var live = Asset("aa11"); var stale = Asset("bb22");
        var kept = await cache.GetAsync(live, "movies");
        var removed = await cache.GetAsync(stale, "movies");
        var inProgress = Path.Combine(Previews, "cc33-640.webp.0123.tmp"); File.WriteAllText(inProgress, "busy");
        File.WriteAllText(Path.Combine(Previews, "stray.txt"), "x");
        Assert.Equal(2, cache.Prune(Snapshot(live)));
        Assert.True(File.Exists(kept)); Assert.False(File.Exists(removed)); Assert.True(File.Exists(inProgress));
    }
}
