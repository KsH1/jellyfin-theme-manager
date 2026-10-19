using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ThemeManager.Core;

namespace Jellyfin.Plugin.ThemeManager;

public sealed class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost applicationHost)
    {
        services.AddSingleton(provider =>
        {
            var paths = provider.GetRequiredService<IApplicationPaths>();
            var catalog = new ThemeCatalog(Path.Combine(paths.DataPath, "theme-manager", "packs"));
            CatalogLog.Report(provider.GetRequiredService<ILogger<ThemeCatalog>>(), catalog.Reload(), "loaded");
            return catalog;
        });
        services.AddSingleton(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<UserThemeStore>>();
            return new UserThemeStore(Path.Combine(provider.GetRequiredService<IApplicationPaths>().DataPath, "theme-manager", "users"),
                (user, ex) => logger.LogWarning(ex, "Theme Manager could not read the saved theme of user {UserId}; that user follows the server default until they save again", user));
        });
        services.AddSingleton(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<ScheduleStore>>();
            return new ScheduleStore(Path.Combine(provider.GetRequiredService<IApplicationPaths>().DataPath, "theme-manager"),
                (path, ex) => logger.LogWarning(ex, "Theme Manager could not read calendar file {Path}; it is treated as empty until saved again", path));
        });
        services.AddSingleton<ThemePackInstaller>();
        services.AddSingleton(provider =>
        {
            // Jellyfin's own Skia encoder; no extra image library ships with the plugin.
            var logger = provider.GetRequiredService<ILogger<PreviewCache>>();
            var root = Path.Combine(provider.GetRequiredService<IApplicationPaths>().DataPath, "theme-manager", "previews");
            var encoder = provider.GetService<IImageEncoder>();
            if (encoder is null || !encoder.SupportsImageEncoding)
            {
                logger.LogWarning("Theme Manager: Jellyfin has no image encoder ({Encoder}); theme previews use the full-size originals", encoder?.Name ?? "none");
                return new PreviewCache(root, (source, _, _) => source);
            }
            var format = encoder.SupportedOutputFormats.Contains(ImageFormat.Webp) ? ImageFormat.Webp : ImageFormat.Jpg;
            return new PreviewCache(root, (source, output, width) =>
            {
                try
                {
                    return encoder.EncodeImage(source, File.GetLastWriteTimeUtc(source), output, false, null, 80,
                        new ImageProcessingOptions { MaxWidth = width, Quality = 80, SupportedOutputFormats = [format] }, format);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Theme Manager could not make a preview of {Source}; the original is served", source);
                    throw;
                }
            }, format == ImageFormat.Webp ? ".webp" : ".jpg");
        });
    }
}

public sealed class RefreshCatalogTask(ThemeCatalog catalog, PreviewCache previews, ILogger<RefreshCatalogTask> logger) : IScheduledTask
{
    public string Name => "Refresh theme packs";
    public string Key => "ThemeManagerRefreshCatalog";
    public string Description => "Validate theme packs and refresh the in-memory catalog. Does not change branding or media files.";
    public string Category => "Theme Manager";
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() =>
    [
        new() { Type = TaskTriggerInfoType.StartupTrigger },
        new() { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = TimeSpan.FromHours(6).Ticks },
    ];
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = catalog.Reload();
        CatalogLog.Report(logger, snapshot, "refreshed");
        logger.LogInformation("Theme Manager removed {Pruned} stale previews", previews.Prune(snapshot));
        progress.Report(100);
        return Task.CompletedTask;
    }
}

public static class CatalogLog
{
    /// <summary>Logs why each rejected pack was rejected. Rejected packs are hidden; all other packs keep working.</summary>
    public static void Report(ILogger logger, CatalogSnapshot snapshot, string action)
    {
        foreach (var problem in snapshot.Problems)
            logger.LogWarning("Theme Manager rejected theme pack {Pack}: {Reason}", problem.Pack, problem.Message);
        logger.LogInformation("Theme Manager catalog {Action}: {Count} valid packs, {InvalidCount} rejected packs", action, snapshot.Themes.Count, snapshot.Problems.Count);
    }
}
