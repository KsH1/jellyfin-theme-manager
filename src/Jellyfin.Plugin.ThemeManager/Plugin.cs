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
    public string TimeZoneId { get; set; } = "Europe/Copenhagen";
    public bool AllowPersonalSchedules { get; set; } = true;
    /// <summary>Show the theme in the normal Jellyfin Web address when File Transformation is installed.</summary>
    public bool ShowInWebClient { get; set; } = true;
}

public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static Plugin Instance { get; private set; } = null!;
    public Plugin(IApplicationPaths paths, IXmlSerializer serializer) : base(paths, serializer) => Instance = this;
    public override string Name => "Theme Manager";
    public override string Description => "Theme packs, seasonal calendar and personal theme preferences. Preview alpha; does not modify global branding.";
    public override Guid Id => new("8fa53208-c9f3-4e99-9858-2963ed658aae");
    public IEnumerable<PluginPageInfo> GetPages() =>
    [
        new() { Name = "theme-manager", DisplayName = Name, EmbeddedResourcePath = GetType().Namespace + ".Web.admin.html" },
    ];

    public ThemeChoice ServerChoice => new(Enum.TryParse<ThemeMode>(Configuration.ServerMode, out var mode) && Enum.IsDefined(mode) && mode != ThemeMode.FollowServer ? mode : ThemeMode.Automatic, Configuration.ServerThemeId, Configuration.ServerScheduleId);
}
