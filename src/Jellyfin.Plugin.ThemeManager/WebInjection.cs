using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using MediaBrowser.Common.Api;
using MediaBrowser.Model.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ThemeManager.Core;

namespace Jellyfin.Plugin.ThemeManager;

/// <summary>
/// Shows the theme in the normal Jellyfin Web address through the optional File Transformation plugin, which
/// rewrites index.html as it is served. Nothing on disk is changed; without File Transformation the plugin keeps
/// working through /web/theme-manager.html.
/// </summary>
public static class WebInjection
{
    public static readonly Guid TransformationId = new("3c0f6d1e-6b8a-4f5e-9d2a-7a41b9c3e215");
    private const string FileTransformationAssembly = "Jellyfin.Plugin.FileTransformation";

    public static string State { get; private set; } = "NotChecked";
    public static string? Detail { get; private set; }
    public static DateTimeOffset? LastServed { get; private set; }

    internal static void MarkServed() => LastServed = DateTimeOffset.UtcNow;

    public static void Register(ILogger logger)
    {
        try
        {
            var assembly = AssemblyLoadContext.All.SelectMany(context => context.Assemblies).FirstOrDefault(a => a.GetName().Name == FileTransformationAssembly);
            if (assembly is null)
            {
                (State, Detail) = ("FileTransformationMissing", null);
                logger.LogInformation("Theme Manager: File Transformation is not installed; themes are available at /web/theme-manager.html only");
                return;
            }
            var method = assembly.GetType(FileTransformationAssembly + ".PluginInterface")?.GetMethod("RegisterTransformation");
            var parameter = method?.GetParameters() is [var only] ? only.ParameterType : null;
            // The payload type (Newtonsoft JObject) is taken from File Transformation itself: plugins load in separate contexts.
            var parse = parameter?.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, [typeof(string)]);
            if (method is null || parse is null)
            {
                (State, Detail) = ("Failed", "Unsupported File Transformation version " + assembly.GetName().Version);
                logger.LogWarning("Theme Manager: File Transformation {Version} has an unexpected interface; themes are available at /web/theme-manager.html only", assembly.GetName().Version);
                return;
            }
            var json = JsonSerializer.Serialize(new Dictionary<string, string?>
            {
                // "index.html" exactly: File Transformation runs one pipeline per key, shared with other plugins using the same key.
                ["id"] = TransformationId.ToString(), ["fileNamePattern"] = "index.html",
                ["callbackAssembly"] = typeof(IndexHtmlTransform).Assembly.FullName,
                ["callbackClass"] = typeof(IndexHtmlTransform).FullName, ["callbackMethod"] = nameof(IndexHtmlTransform.Transform),
            });
            method.Invoke(null, [parse.Invoke(null, [json])]);
            (State, Detail) = ("Registered", "File Transformation " + assembly.GetName().Version);
            logger.LogInformation("Theme Manager: registered with File Transformation {Version}; the theme is shown in the normal Jellyfin Web address", assembly.GetName().Version);
        }
        catch (Exception ex)
        {
            (State, Detail) = ("Failed", (ex as TargetInvocationException)?.InnerException?.Message ?? ex.Message);
            logger.LogWarning(ex, "Theme Manager could not register with File Transformation; themes are available at /web/theme-manager.html only");
        }
    }
}

/// <summary>The shape File Transformation fills from its JSON payload.</summary>
public sealed class IndexHtmlPayload
{
    public string? Contents { get; set; }
}

public static class IndexHtmlTransform
{
    /// <summary>Called by File Transformation for every index.html response. Never throws: a failure serves Jellyfin unchanged.</summary>
    public static string Transform(IndexHtmlPayload payload)
    {
        var html = payload?.Contents ?? "";
        try
        {
            // Read live, so switching the setting off takes effect without a restart.
            if (Plugin.Instance?.Configuration.ShowInWebClient != true) return html;
            var result = IndexInjection.Inject(html);
            if (!ReferenceEquals(result, html)) WebInjection.MarkServed();
            return result;
        }
        catch (Exception) { return html; }
    }
}

public sealed class RegisterWebInjectionTask(ILogger<RegisterWebInjectionTask> logger) : IScheduledTask
{
    public string Name => "Show theme in Jellyfin Web";
    public string Key => "ThemeManagerRegisterWebInjection";
    public string Description => "Connects Theme Manager to the File Transformation plugin, if installed. Run it again after installing File Transformation.";
    public string Category => "Theme Manager";
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [new() { Type = TaskTriggerInfoType.StartupTrigger }];
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        WebInjection.Register(logger);
        progress.Report(100);
        return Task.CompletedTask;
    }
}

[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("ThemeManager/Admin/WebClient")]
[ThemeManagerErrors]
public sealed class WebInjectionController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => new JsonResult(new
    {
        WebInjection.State, WebInjection.Detail, WebInjection.LastServed,
        Enabled = Plugin.Instance.Configuration.ShowInWebClient,
    }, ThemeJson.Options);

    [HttpPut]
    public IActionResult Set([FromBody] WebClientSetting setting)
    {
        if (setting is null) return BadRequest();
        Plugin.Instance.Configuration.ShowInWebClient = setting.Enabled;
        Plugin.Instance.SaveConfiguration();
        return NoContent();
    }
}

public sealed record WebClientSetting(bool Enabled);
