using System.Text.Json;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ThemeManager.Core;

namespace Jellyfin.Plugin.ThemeManager;

public sealed record ServerSettings(ThemeChoice Choice, string TimeZoneId, bool AllowPersonalSchedules = true);
public sealed record ScheduleTest(ThemeSchedule Schedule, DateOnly? From = null);

[ApiController]
[Authorize]
[ThemeManagerErrors]
[Route("ThemeManager")]
public sealed class ThemeController(ThemeCatalog catalog, UserThemeStore users, ThemePackInstaller installer, PreviewCache previews, ScheduleStore schedules, IAuthorizationContext authorization, ILogger<ThemeController> logger) : ControllerBase
{
    private JsonResult Data(object value) => new(value, ThemeJson.Options);
    private async Task<Guid?> CurrentUser()
    {
        var info = await authorization.GetAuthorizationInfo(HttpContext).ConfigureAwait(false);
        return !info.IsApiKey && info.UserId != Guid.Empty ? info.UserId : null;
    }

    private static object Describe(ResolvedTheme theme) => new
    {
        theme.Manifest.Id, theme.Manifest.Name, theme.Manifest.Version, theme.Manifest.Category,
        theme.Manifest.Extends, theme.Manifest.Calendar, theme.Palette,
        Assets = theme.Assets.ToDictionary(p => p.Key, p => new { p.Value.ContentType, p.Value.Revision }),
    };

    [HttpGet("Catalog")]
    public IActionResult GetCatalog() => Data(new { Themes = catalog.Snapshot.Themes.Values.OrderBy(t => t.Manifest.Name).Select(Describe), catalog.Snapshot.LoadedAt });

    [HttpGet("Me")]
    public async Task<IActionResult> GetPreference(CancellationToken cancellationToken)
    {
        if (await CurrentUser().ConfigureAwait(false) is not { } id) return Forbid();
        return Data(await users.GetAsync(id, cancellationToken).ConfigureAwait(false));
    }

    [HttpPut("Me")]
    public async Task<IActionResult> SetPreference([FromBody] ThemeChoice choice, CancellationToken cancellationToken)
    {
        if (await CurrentUser().ConfigureAwait(false) is not { } id) return Forbid();
        try { ThemeResolver.Validate(choice, catalog.Snapshot, allowFollowServer: true, await schedules.ForUserAsync(id, cancellationToken).ConfigureAwait(false)); }
        catch (InvalidDataException ex) { return BadRequest(new { Error = ex.Message }); }
        await users.SetAsync(id, choice, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    [HttpGet("Effective")]
    public async Task<IActionResult> Effective(CancellationToken cancellationToken)
    {
        if (await CurrentUser().ConfigureAwait(false) is not { } id) return Forbid();
        var choice = await users.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return ResolveChoice(choice, await schedules.ForUserAsync(id, cancellationToken).ConfigureAwait(false));
    }

    [HttpPost("Preview")]
    public async Task<IActionResult> Preview([FromBody] ThemeChoice choice, CancellationToken cancellationToken)
    {
        if (await CurrentUser().ConfigureAwait(false) is not { } id) return Forbid();
        var available = await schedules.ForUserAsync(id, cancellationToken).ConfigureAwait(false);
        try { ThemeResolver.Validate(choice, catalog.Snapshot, allowFollowServer: true, available); }
        catch (InvalidDataException ex) { return BadRequest(new { Error = ex.Message }); }
        return ResolveChoice(choice, available);
    }

    private static DateOnly? Today()
    {
        try { return SeasonalCalendar.LocalDate(DateTimeOffset.UtcNow, Plugin.Instance.Configuration.TimeZoneId); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { return null; }
    }

    private IActionResult ResolveChoice(ThemeChoice choice, IReadOnlyDictionary<string, ThemeSchedule> available)
    {
        if (Today() is not { } date) return Problem("The configured time zone is invalid. An administrator must correct it.");
        var snapshot = catalog.Snapshot;
        var result = ThemeResolver.Resolve(choice, Plugin.Instance.ServerChoice, snapshot, date, available);
        return Data(new { Resolution = result, LocalDate = date, Theme = result.ThemeId is { } themeId ? Describe(snapshot.Themes[themeId]) : null, Rendering = "OptInWebClient", RefreshAfterSeconds = 60 });
    }

    [HttpGet("Themes/{id}/Assets/{slot}")]
    public IActionResult Asset(string id, string slot)
    {
        if (!catalog.Snapshot.Themes.TryGetValue(id, out var theme) || !theme.Assets.TryGetValue(slot, out var asset)) return NotFound();
        // Open a catalog-approved path only, never a user-supplied filesystem path.
        Response.Headers.CacheControl = "private, max-age=0, must-revalidate";
        Response.Headers.XContentTypeOptions = "nosniff";
        try { return File(System.IO.File.OpenRead(asset.Path), asset.ContentType); }
        catch (IOException) { return NotFound(); }
    }

    [HttpGet("Themes/{id}/Assets/{slot}/Preview")]
    public async Task<IActionResult> AssetPreview(string id, string slot, [FromQuery] string? v, CancellationToken cancellationToken)
    {
        if (!catalog.Snapshot.Themes.TryGetValue(id, out var theme) || !theme.Assets.TryGetValue(slot, out var asset)) return NotFound();
        var path = await previews.GetAsync(asset, slot, cancellationToken).ConfigureAwait(false);
        // Revisioned URLs never change content, so the browser may keep them; unversioned ones revalidate.
        Response.Headers.CacheControl = v == asset.Revision ? "private, max-age=31536000, immutable" : "private, max-age=0, must-revalidate";
        Response.Headers.XContentTypeOptions = "nosniff";
        var type = path == asset.Path ? asset.ContentType : Path.GetExtension(path) == ".webp" ? "image/webp" : "image/jpeg";
        try { return File(System.IO.File.OpenRead(path), type); }
        catch (IOException) { return NotFound(); }
    }

    [HttpGet("Schedules")]
    public async Task<IActionResult> GetSchedules(CancellationToken cancellationToken)
    {
        if (await CurrentUser().ConfigureAwait(false) is not { } id) return Forbid();
        return Data(new
        {
            Shared = await schedules.GetSharedAsync(cancellationToken).ConfigureAwait(false),
            Personal = await schedules.GetPersonalAsync(id, cancellationToken).ConfigureAwait(false),
            Plugin.Instance.Configuration.AllowPersonalSchedules,
        });
    }

    /// <summary>Shows what an unsaved calendar would display, from today (or a test date) and one year ahead.</summary>
    [HttpPost("Schedules/Test")]
    public async Task<IActionResult> TestSchedule([FromBody] ScheduleTest test)
    {
        if (await CurrentUser().ConfigureAwait(false) is null) return Forbid();
        if (Today() is not { } today) return Problem("The configured time zone is invalid. An administrator must correct it.");
        ThemeSchedule schedule;
        try { schedule = ThemeResolver.ValidateSchedule(test?.Schedule, catalog.Snapshot, personal: true); }
        catch (InvalidDataException ex) { return BadRequest(new { Error = ex.Message }); }
        var from = test!.From ?? today;
        return Data(new { From = from, Today = today, Timeline = ThemeResolver.Timeline(schedule, catalog.Snapshot, from) });
    }

    [HttpPut("Me/Schedule")]
    public async Task<IActionResult> SetPersonalSchedule([FromBody] ThemeSchedule schedule, CancellationToken cancellationToken)
    {
        if (await CurrentUser().ConfigureAwait(false) is not { } id) return Forbid();
        if (!Plugin.Instance.Configuration.AllowPersonalSchedules) return Problem("An administrator has turned off personal calendars.", statusCode: StatusCodes.Status403Forbidden);
        try { schedule = ThemeResolver.ValidateSchedule(schedule, catalog.Snapshot, personal: true); }
        catch (InvalidDataException ex) { return BadRequest(new { Error = ex.Message }); }
        await schedules.SavePersonalAsync(id, schedule, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    [HttpDelete("Me/Schedule")]
    public async Task<IActionResult> DeletePersonalSchedule(CancellationToken cancellationToken)
    {
        if (await CurrentUser().ConfigureAwait(false) is not { } id) return Forbid();
        await schedules.DeletePersonalAsync(id, cancellationToken).ConfigureAwait(false);
        // A choice that pointed at the deleted calendar goes back to the server default instead of warning forever.
        var choice = await users.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (choice is { Mode: ThemeMode.Schedule, ScheduleId: ThemeSchedule.PersonalId }) await users.SetAsync(id, new(), cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    [HttpPut("Admin/Schedules/{id}")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<IActionResult> SetSharedSchedule(string id, [FromBody] ThemeSchedule schedule, CancellationToken cancellationToken)
    {
        try
        {
            schedule = ThemeResolver.ValidateSchedule(schedule with { Id = id }, catalog.Snapshot, personal: false);
            await schedules.SaveSharedAsync(schedule, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException ex) { return BadRequest(new { Error = ex.Message }); }
        return NoContent();
    }

    [HttpDelete("Admin/Schedules/{id}")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<IActionResult> DeleteSharedSchedule(string id, CancellationToken cancellationToken)
    {
        if (Plugin.Instance.ServerChoice is { Mode: ThemeMode.Schedule } server && server.ScheduleId == id)
            return Conflict(new { Error = "This calendar is the server default. Choose another server default first." });
        // Users who chose it get Automatic Seasonal with a warning until they choose again.
        return await schedules.DeleteSharedAsync(id, cancellationToken).ConfigureAwait(false) ? NoContent() : NotFound();
    }

    [HttpGet("Admin/Settings")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public IActionResult GetSettings() => Data(new ServerSettings(Plugin.Instance.ServerChoice, Plugin.Instance.Configuration.TimeZoneId, Plugin.Instance.Configuration.AllowPersonalSchedules));

    [HttpPut("Admin/Settings")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public async Task<IActionResult> SetSettings([FromBody] ServerSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            if (settings.Choice is null || string.IsNullOrWhiteSpace(settings.TimeZoneId)) return BadRequest();
            // The server default may use shared calendars only, never one user's personal calendar.
            var shared = (await schedules.GetSharedAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(s => s.Id, StringComparer.Ordinal);
            ThemeResolver.Validate(settings.Choice, catalog.Snapshot, allowFollowServer: false, shared);
            TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
        }
        catch (Exception ex) when (ex is InvalidDataException or TimeZoneNotFoundException or InvalidTimeZoneException) { return BadRequest(new { Error = ex.Message }); }
        // Use Jellyfin's native plugin configuration persistence, not a separate server settings file.
        var configuration = Plugin.Instance.Configuration;
        configuration.ServerMode = settings.Choice.Mode.ToString();
        configuration.ServerThemeId = settings.Choice.Mode == ThemeMode.Fixed ? settings.Choice.ThemeId : null;
        configuration.ServerScheduleId = settings.Choice.Mode == ThemeMode.Schedule ? settings.Choice.ScheduleId : null;
        configuration.TimeZoneId = settings.TimeZoneId;
        configuration.AllowPersonalSchedules = settings.AllowPersonalSchedules;
        Plugin.Instance.SaveConfiguration();
        return NoContent();
    }

    [HttpPost("Admin/Reload")]
    [Authorize(Policy = Policies.RequiresElevation)]
    public IActionResult Reload()
    {
        var snapshot = catalog.Reload();
        CatalogLog.Report(logger, snapshot, "reloaded by an administrator");
        return Data(new { Count = snapshot.Themes.Count, snapshot.Problems, snapshot.LoadedAt });
    }

    [HttpPost("Admin/Import")]
    [Authorize(Policy = Policies.RequiresElevation)]
    [RequestSizeLimit(32 * 1024 * 1024)]
    [Consumes("application/zip")]
    public async Task<IActionResult> Import(CancellationToken cancellationToken)
    {
        // ZipArchive requires a seekable source; bound both compressed and expanded data.
        await using var buffer = new MemoryStream();
        var chunk = new byte[65536];
        int read;
        while ((read = await Request.Body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > 32 * 1024 * 1024) return StatusCode(StatusCodes.Status413PayloadTooLarge);
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        buffer.Position = 0;
        try { return Data(new { Id = await installer.InstallAsync(buffer, cancellationToken).ConfigureAwait(false) }); }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or ArgumentException)
        {
            logger.LogWarning("Theme Manager rejected an uploaded theme pack: {Reason}", ex.Message);
            return BadRequest(new { Error = ex.Message });
        }
    }
}
