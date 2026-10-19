using System.Security.Cryptography;
using System.Text;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ThemeManager.Core;

namespace Jellyfin.Plugin.ThemeManager;

/// <summary>Result of one run, shown on the admin page.</summary>
public sealed record ServerImagesRun(DateTimeOffset At, string? ThemeId, IReadOnlyList<string> Changed, IReadOnlyList<string> Restored, string? Error);

/// <summary>
/// Sets library Primary images from the server default theme, for TV and mobile apps that do not run Jellyfin Web.
/// Off by default. Before a library's image is first replaced it is copied to a backup; "Original Jellyfin", switching
/// the feature off or Restore puts every backup back (or removes the image if the library had none).
/// </summary>
public sealed class ServerImagesService(ILibraryManager libraries, IProviderManager providers, IImageEncoder encoder, ThemeCatalog catalog, ScheduleStore schedules, ServerImageState state, ILogger<ServerImagesService> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public ServerImagesRun? LastRun { get; private set; }

    public async Task<ServerImagesRun> ApplyAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var plugin = Plugin.Instance;
            if (!plugin.Configuration.ServerWideImages) return LastRun = new(DateTimeOffset.UtcNow, null, [], [], null);
            var date = SeasonalCalendar.LocalDate(DateTimeOffset.UtcNow, plugin.Configuration.TimeZoneId);
            var snapshot = catalog.Snapshot;
            var shared = (await schedules.GetSharedAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(s => s.Id, StringComparer.Ordinal);
            // Apps cannot show a personal theme, so this is always what a user who follows the server default would see.
            var themeId = ThemeResolver.Resolve(new(), plugin.ServerChoice, snapshot, date, shared, plugin.AutomaticExcluded).ThemeId;
            var theme = themeId is null ? null : snapshot.Themes.GetValueOrDefault(themeId);
            var folders = libraries.GetVirtualFolders().Where(f => Guid.TryParse(f.ItemId, out _)).ToList();
            var slots = Slots(folders);
            var records = state.Load();
            List<string> changed = [], restored = [];
            foreach (var folder in folders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                slots.TryGetValue(folder.ItemId, out var slot);
                if (slot == LibraryBanners.None)
                {
                    // The administrator keeps this library's own image.
                    if (await RestoreAsync(folder.ItemId, records, cancellationToken).ConfigureAwait(false)) restored.Add(folder.Name);
                    continue;
                }
                var themed = theme is not null && slot is not null ? theme.Assets.GetValueOrDefault(slot) : null;
                // Libraries without a themed banner (e.g. Music) get a generic one: the theme background with the library name.
                var asset = themed ?? (theme?.Assets.GetValueOrDefault("background") is { } background ? GenericBanner(folder.ItemId, folder.Name, background) : null);
                if (asset is null)
                {
                    // No themed banner for this library today: give back what it had.
                    if (await RestoreAsync(folder.ItemId, records, cancellationToken).ConfigureAwait(false)) restored.Add(folder.Name);
                    continue;
                }
                if (libraries.GetItemById(Guid.Parse(folder.ItemId)) is not { } item) continue;
                records.TryGetValue(folder.ItemId, out var record);
                if (record?.Applied == asset.Revision && item.HasImage(ImageType.Primary, 0)) continue;
                if (record is null)
                {
                    // First change to this library: keep its own image (or the fact that it had none).
                    var current = item.HasImage(ImageType.Primary, 0) ? item.GetImagePath(ImageType.Primary, 0) : null;
                    record = new() { Backup = current is not null && File.Exists(current) ? state.Backup(folder.ItemId, current) : null };
                    records[folder.ItemId] = record;
                    state.Save(records);
                }
                await using (var stream = File.OpenRead(asset.Path))
                    await providers.SaveImage(item, stream, asset.ContentType, ImageType.Primary, null, cancellationToken).ConfigureAwait(false);
                await item.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, cancellationToken).ConfigureAwait(false);
                records[folder.ItemId] = record with { Applied = asset.Revision };
                state.Save(records);
                changed.Add(folder.Name);
            }
            if (changed.Count + restored.Count > 0)
                logger.LogInformation("Theme Manager set library images for apps from theme {Theme}: changed {Changed}; restored {Restored}", themeId ?? "(original)", string.Join(", ", changed), string.Join(", ", restored));
            return LastRun = new(DateTimeOffset.UtcNow, themeId, changed, restored, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Theme Manager could not set library images for apps; library images are left as they were");
            return LastRun = new(DateTimeOffset.UtcNow, null, [], [], ex.Message);
        }
        finally { _gate.Release(); }
    }

    /// <summary>Banner slot per library (id as Jellyfin gives it), with the administrator's choices applied.</summary>
    public IReadOnlyDictionary<string, string> Slots(IEnumerable<MediaBrowser.Model.Entities.VirtualFolderInfo> folders)
    {
        var chosen = Plugin.Instance.LibraryBannerChoices;
        var list = folders.Where(f => Guid.TryParse(f.ItemId, out _)).ToList();
        var byKey = list.ToDictionary(f => Guid.Parse(f.ItemId).ToString("N"), f => f.ItemId, StringComparer.Ordinal);
        return LibraryBanners.Assign(
            list.Select(f => new LibraryInfo(f.ItemId, f.CollectionType?.ToString()?.ToLowerInvariant(), f.Name)),
            chosen.Where(c => byKey.ContainsKey(c.Key)).ToDictionary(c => byKey[c.Key], c => c.Value, StringComparer.Ordinal));
    }

    /// <summary>Puts every library back as it was before the plugin changed it.</summary>
    public async Task<ServerImagesRun> RestoreAllAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var records = state.Load();
            var names = libraries.GetVirtualFolders().ToDictionary(f => f.ItemId, f => f.Name, StringComparer.OrdinalIgnoreCase);
            List<string> restored = [];
            foreach (var id in records.Keys.ToList())
                if (await RestoreAsync(id, records, cancellationToken).ConfigureAwait(false)) restored.Add(names.GetValueOrDefault(id, id));
            logger.LogInformation("Theme Manager restored the original library images: {Restored}", string.Join(", ", restored));
            return LastRun = new(DateTimeOffset.UtcNow, null, [], restored, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Theme Manager could not restore all original library images; the backups are kept in {Folder}", state.Root);
            return LastRun = new(DateTimeOffset.UtcNow, null, [], [], ex.Message);
        }
        finally { _gate.Release(); }
    }

    private async Task<bool> RestoreAsync(string libraryId, Dictionary<string, LibraryImageRecord> records, CancellationToken cancellationToken)
    {
        if (!records.TryGetValue(libraryId, out var record)) return false;
        if (libraries.GetItemById(Guid.Parse(libraryId)) is { } item)
        {
            if (record.Backup is { } backup && File.Exists(backup))
            {
                await using (var stream = File.OpenRead(backup))
                    await providers.SaveImage(item, stream, MimeFor(backup), ImageType.Primary, null, cancellationToken).ConfigureAwait(false);
            }
            else if (item.HasImage(ImageType.Primary, 0))
            {
                // The library had no image of its own before the plugin set one.
                await item.DeleteImageAsync(ImageType.Primary, 0).ConfigureAwait(false);
            }
            await item.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, cancellationToken).ConfigureAwait(false);
        }
        records.Remove(libraryId);
        state.Save(records);
        if (record.Backup is { } used && File.Exists(used)) File.Delete(used);
        return true;
    }

    /// <summary>
    /// Jellyfin's own library-image builder (the one that makes its default library tiles) draws the name on the
    /// background. Made once per library, background revision and name; null when the encoder cannot make collages.
    /// </summary>
    private ResolvedAsset? GenericBanner(string libraryId, string name, ResolvedAsset background)
    {
        if (!encoder.SupportsImageCollageCreation) return null;
        var revision = "generic-" + background.Revision + "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..12].ToLowerInvariant();
        var folder = Path.Combine(state.Root, "generated");
        var prefix = Guid.Parse(libraryId).ToString("N") + "-";
        // Short name: Skia writes through native file APIs that fail silently on paths over 260 characters.
        var path = Path.Combine(folder, prefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(revision)))[..16].ToLowerInvariant() + ".png");
        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(folder);
                encoder.CreateImageCollage(new ImageCollageOptions { InputPaths = [background.Path], OutputPath = path, Width = 960, Height = 540 }, name);
                if (!File.Exists(path)) return null;
                // Only the newest generic banner per library is kept.
                foreach (var old in Directory.EnumerateFiles(folder, prefix + "*.png").Where(f => f != path)) File.Delete(old);
            }
            return new ResolvedAsset(path, "image/png", revision);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Theme Manager could not make a generic banner for library {Library}; its image is left as it was", name);
            return null;
        }
    }

    private static string MimeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", ".gif" => "image/gif", _ => "image/png",
    };
}

public sealed class ApplyServerImagesTask(ServerImagesService images) : IScheduledTask
{
    public string Name => "Set library images for apps";
    public string Key => "ThemeManagerServerImages";
    public string Description => "When switched on in Theme Manager, sets each library's image from the server default theme for TV and mobile apps. Does nothing while switched off.";
    public string Category => "Theme Manager";
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() =>
    [
        new() { Type = TaskTriggerInfoType.StartupTrigger },
        // Shortly after midnight, when the calendar may move to a new theme.
        new() { Type = TaskTriggerInfoType.DailyTrigger, TimeOfDayTicks = TimeSpan.FromMinutes(5).Ticks },
    ];
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        await images.ApplyAsync(cancellationToken).ConfigureAwait(false);
        progress.Report(100);
    }
}

[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("ThemeManager/Admin/ServerImages")]
[ThemeManagerErrors]
public sealed class ServerImagesController(ServerImagesService images) : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => new JsonResult(new { Enabled = Plugin.Instance.Configuration.ServerWideImages, images.LastRun }, ThemeJson.Options);

    /// <summary>Switching on applies the theme now; switching off restores the original images.</summary>
    [HttpPut]
    public async Task<IActionResult> Set([FromBody] ServerImagesSetting setting, CancellationToken cancellationToken)
    {
        if (setting is null) return BadRequest();
        Plugin.Instance.Configuration.ServerWideImages = setting.Enabled;
        Plugin.Instance.SaveConfiguration();
        var run = setting.Enabled ? await images.ApplyAsync(cancellationToken).ConfigureAwait(false) : await images.RestoreAllAsync(cancellationToken).ConfigureAwait(false);
        return new JsonResult(run, ThemeJson.Options);
    }

    [HttpPost("Apply")]
    public async Task<IActionResult> Apply(CancellationToken cancellationToken) => new JsonResult(await images.ApplyAsync(cancellationToken).ConfigureAwait(false), ThemeJson.Options);

    [HttpPost("Restore")]
    public async Task<IActionResult> Restore(CancellationToken cancellationToken) => new JsonResult(await images.RestoreAllAsync(cancellationToken).ConfigureAwait(false), ThemeJson.Options);
}

/// <summary>Which banner each library gets, in Jellyfin Web and (when switched on) in apps.</summary>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("ThemeManager/Admin/Libraries")]
[ThemeManagerErrors]
public sealed class LibraryBannersController(ILibraryManager libraries, ServerImagesService images) : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        var folders = libraries.GetVirtualFolders().Where(f => Guid.TryParse(f.ItemId, out _)).ToList();
        var chosen = Plugin.Instance.LibraryBannerChoices;
        // Banner: what the library gets now, with the choices applied.
        var current = images.Slots(folders);
        return new JsonResult(new
        {
            Choices = LibraryBanners.Choices,
            Libraries = folders.Select(f => new
            {
                Id = Guid.Parse(f.ItemId).ToString("N"), f.Name, CollectionType = f.CollectionType?.ToString(),
                Banner = current.GetValueOrDefault(f.ItemId, LibraryBanners.Generic),
                Choice = chosen.GetValueOrDefault(Guid.Parse(f.ItemId).ToString("N")),
            }),
        }, ThemeJson.Options);
    }

    /// <summary>Sets one library's banner; null or "auto" goes back to automatic. App images follow at once when switched on.</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Set(string id, [FromBody] LibraryBannerSetting setting, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(id, out var guid) || setting is null) return BadRequest();
        var key = guid.ToString("N");
        if (!libraries.GetVirtualFolders().Any(f => Guid.TryParse(f.ItemId, out var folder) && folder == guid)) return NotFound();
        var choice = setting.Banner is null or "auto" ? null : setting.Banner;
        if (choice is not null && !LibraryBanners.Choices.Contains(choice)) return BadRequest(new { Error = "Choose one of: auto, " + string.Join(", ", LibraryBanners.Choices) + "." });
        var configuration = Plugin.Instance.Configuration;
        var rest = Plugin.Instance.LibraryBannerChoices.Where(c => c.Key != key).Select(c => c.Key + "=" + c.Value);
        configuration.LibraryBanners = [.. choice is null ? rest : rest.Append(key + "=" + choice)];
        Plugin.Instance.SaveConfiguration();
        if (configuration.ServerWideImages) await images.ApplyAsync(cancellationToken).ConfigureAwait(false);
        return NoContent();
    }
}

public sealed record ServerImagesSetting(bool Enabled);
public sealed record LibraryBannerSetting(string? Banner);
