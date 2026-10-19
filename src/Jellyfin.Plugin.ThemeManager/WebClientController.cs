using MediaBrowser.Common.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThemeManager;

/// <summary>Opt-in web entry point. Reads the installed shell but never writes to Jellyfin's web directory.</summary>
[ApiController]
[AllowAnonymous]
[ThemeManagerErrors]
public sealed class WebClientController(IApplicationPaths paths, ILogger<WebClientController> logger) : ControllerBase
{
    private static readonly IReadOnlyDictionary<string, string> Files = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["bootstrap.js"] = "text/javascript", ["client.mjs"] = "text/javascript", ["client-core.mjs"] = "text/javascript",
        ["theme-picker.mjs"] = "text/javascript", ["effects.mjs"] = "text/javascript", ["schedule-editor.mjs"] = "text/javascript", ["client.css"] = "text/css",
    };

    [HttpGet("web/theme-manager.html")]
    public async Task<IActionResult> Entry(CancellationToken cancellationToken)
    {
        // Whatever goes wrong, the themed link still opens normal Jellyfin; the reason goes to the server log.
        IActionResult Fallback(string reason, Exception? ex = null)
        {
            logger.LogWarning(ex, "Theme Manager web entry fell back to normal Jellyfin Web: {Reason}", reason);
            Response.Headers.CacheControl = "no-store";
            return Redirect("index.html");
        }
        var path = Path.Combine(paths.WebPath, "index.html");
        string html;
        try
        {
            if (!System.IO.File.Exists(path) || new FileInfo(path).Length > 2 * 1024 * 1024) return Fallback("No local Jellyfin Web index.html at " + paths.WebPath);
            html = await System.IO.File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Fallback("index.html could not be read", ex); }
        var end = html.LastIndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (end < 0) return Fallback("Unrecognized Jellyfin Web index.html (no </head>)");
        // Relative URLs preserve a reverse proxy's BaseUrl. The route lives alongside index.html.
        const string loader = "<script defer src=\"../ThemeManager/Client/bootstrap.js\"></script>";
        Response.Headers.CacheControl = "no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        return Content(html.Insert(end, loader), "text/html; charset=utf-8");
    }

    [HttpGet("ThemeManager/Client/{file}")]
    public IActionResult Asset(string file)
    {
        if (!Files.TryGetValue(file, out var type)) return NotFound();
        var stream = GetType().Assembly.GetManifestResourceStream(GetType().Namespace + ".Web." + file);
        if (stream is null) return NotFound();
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(stream, type + "; charset=utf-8");
    }
}
