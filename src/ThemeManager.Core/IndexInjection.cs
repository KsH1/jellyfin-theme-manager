namespace ThemeManager.Core;

/// <summary>Adds the Theme Manager loader to Jellyfin Web's index.html as it is served. The file on disk is never changed.</summary>
public static class IndexInjection
{
    // Relative to /web/index.html, so a reverse proxy's BaseUrl is preserved.
    public const string ScriptPath = "../ThemeManager/Client/bootstrap.js";
    public const string ScriptTag = "<script defer src=\"" + ScriptPath + "\"></script>";

    /// <summary>Returns the page with the loader added once before &lt;/head&gt;; an unrecognised or already-injected page is returned unchanged.</summary>
    public static string Inject(string? html)
    {
        if (string.IsNullOrEmpty(html) || html.Contains(ScriptPath, StringComparison.Ordinal)) return html ?? "";
        var end = html.LastIndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        return end < 0 ? html : html.Insert(end, ScriptTag);
    }
}
