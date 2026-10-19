using MediaBrowser.Common.Api;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ThemeManager.Core;

namespace Jellyfin.Plugin.ThemeManager;

/// <summary>Everything an administrator needs at a glance, including a newer version that waits for a restart.</summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("ThemeManager/Admin/Status")]
[ThemeManagerErrors]
public sealed class StatusController(IPluginManager plugins, IServerApplicationHost host, ThemeCatalog catalog, ServerImagesService images) : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        var plugin = Plugin.Instance;
        var loaded = typeof(Plugin).Assembly.GetName().Version ?? new Version(0, 0);
        // A newer version installed from the catalog is loaded only after Jellyfin is stopped and started again.
        var pending = plugins.Plugins.Where(p => p.Id == plugin.Id && p.Version > loaded).OrderByDescending(p => p.Version).FirstOrDefault();
        var snapshot = catalog.Snapshot;
        string? today = null;
        try { today = SeasonalCalendar.LocalDate(DateTimeOffset.UtcNow, plugin.Configuration.TimeZoneId).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { }
        return new JsonResult(new
        {
            Jellyfin = host.ApplicationVersion.ToString(),
            Loaded = loaded.ToString(),
            Pending = pending?.Version.ToString(),
            Themes = snapshot.Themes.Count,
            snapshot.Problems,
            snapshot.LoadedAt,
            Web = WebInjection.State,
            WebEnabled = plugin.Configuration.ShowInWebClient,
            Apps = plugin.Configuration.ServerWideImages,
            AppsLastRun = images.LastRun,
            plugin.Configuration.TimeZoneId,
            Today = today,
        }, ThemeJson.Options);
    }
}
