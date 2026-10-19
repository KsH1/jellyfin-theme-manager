using ThemeManager.Core;
using Xunit;

namespace ThemeManager.Tests;

public sealed class WebInjectionTests
{
    private const string Page = "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Jellyfin</title></head><body></body></html>";

    [Fact]
    public void AddsTheLoaderOnceBeforeHeadEnd()
    {
        var result = IndexInjection.Inject(Page);
        Assert.Equal(Page.Replace("</head>", IndexInjection.ScriptTag + "</head>"), result);
        Assert.Equal(result, IndexInjection.Inject(result));
    }

    [Fact]
    public void UsesARelativePathSoBaseUrlIsKept() => Assert.StartsWith("../", IndexInjection.ScriptPath);

    [Theory]
    [InlineData("")] [InlineData(null)] [InlineData("<html><body>no head end</body></html>")]
    public void UnrecognisedPagesAreReturnedUnchanged(string? html) => Assert.Equal(html ?? "", IndexInjection.Inject(html));

    [Fact]
    public void UsesTheLastHeadEndAndIgnoresCase()
    {
        const string page = "<html><HEAD><script>var s = '</head>';</script></HEAD><body></body></html>";
        Assert.Equal("<html><HEAD><script>var s = '</head>';</script>" + IndexInjection.ScriptTag + "</HEAD><body></body></html>", IndexInjection.Inject(page));
    }
}
