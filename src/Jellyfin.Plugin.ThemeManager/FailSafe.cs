using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ThemeManager;

/// <summary>Contains an unexpected plugin error to the one request: logged with its cause, answered with a short message.</summary>
public sealed class ThemeManagerErrorsAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        // A closed browser tab is not an error.
        if (context.Exception is OperationCanceledException && context.HttpContext.RequestAborted.IsCancellationRequested) return;
        var request = context.HttpContext.Request;
        context.HttpContext.RequestServices.GetRequiredService<ILogger<ThemeManagerErrorsAttribute>>()
            .LogError(context.Exception, "Theme Manager request {Method} {Path} failed. Jellyfin is unaffected; only this plugin request was rejected", request.Method, request.Path);
        context.Result = new ObjectResult(new { Error = "Theme Manager had an internal error. Jellyfin works as normal; the reason is in the server log." })
        {
            StatusCode = StatusCodes.Status500InternalServerError,
        };
        context.ExceptionHandled = true;
    }
}
