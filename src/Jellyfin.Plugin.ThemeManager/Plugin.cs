using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using ThemeManager.Core;

namespace Jellyfin.Plugin.ThemeManager;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public string ServerMode { get; set; } = "Automatic";
    public string? ServerThemeId { get; set; }
    public string? ServerScheduleId { get; set; }
    // A new install uses the server's own time zone; a saved value (older installs) is kept.
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;
    /// <summary>Theme ids left out of Automatic Seasonal. Null: not chosen yet, see <see cref="Plugin.AutomaticExcluded"/>.</summary>
    public string[]? AutomaticExcluded { get; set; }
    public bool AllowPersonalSchedules { get; set; } = true;
    /// <summary>Show the theme in the normal Jellyfin Web address when File Transformation is installed.</summary>
    public bool ShowInWebClient { get; set; } = true;
    /// <summary>Set library images from the server default theme, for TV and mobile apps. Off until an administrator switches it on.</summary>
    public bool ServerWideImages { get; set; }
}

public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static Plugin Instance { get; private set; } = null!;
    public Plugin(IApplicationPaths paths, IXmlSerializer serializer) : base(paths, serializer) => Instance = this;
    public override string Name => "Theme Manager";
    public override string Description => "Seasonal and permanent themes, a personal theme per user and library banners for Jellyfin Web. Server branding is not changed.";
    public override Guid Id => new("8fa53208-c9f3-4e99-9858-2963ed658aae");
    public IEnumerable<PluginPageInfo> GetPages() =>
    [
        new() { Name = "theme-manager", DisplayName = Name, EmbeddedResourcePath = GetType().Namespace + ".Web.admin.html" },
    ];

    /// <summary>The administrator's choice, or by default the Danish holidays on servers outside Denmark.</summary>
    public IReadOnlyList<string> AutomaticExcluded => Configuration.AutomaticExcluded
        ?? RegionalDefaults.Excluded(Configuration.TimeZoneId, System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName);

    public ThemeChoice ServerChoice => new(Enum.TryParse<ThemeMode>(Configuration.ServerMode, out var mode) && Enum.IsDefined(mode) && mode != ThemeMode.FollowServer ? mode : ThemeMode.Automatic, Configuration.ServerThemeId, Configuration.ServerScheduleId);
}
