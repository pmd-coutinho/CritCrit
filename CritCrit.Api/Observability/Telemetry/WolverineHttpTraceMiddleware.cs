using System.Diagnostics;

namespace CritCrit.Api.Observability.Telemetry;

public sealed class WolverineHttpTraceMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        using var activity = CritCritTelemetry.StartActivity(
            $"wolverine.http {context.Request.Method} {context.Request.Path}");

        activity?.SetTag("wolverine.http.endpoint", true);
        activity?.SetTag("http.request.method", context.Request.Method);
        activity?.SetTag("url.path", context.Request.Path.Value);

        try
        {
            await next(context);
            TagCompletedRequest(context, activity);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            throw;
        }
    }

    private static void TagCompletedRequest(HttpContext context, Activity? activity)
    {
        if (activity is null)
            return;

        var endpointName = context.GetEndpoint()?.DisplayName;
        if (!string.IsNullOrWhiteSpace(endpointName))
            activity.SetTag("aspnetcore.endpoint", endpointName);

        activity.SetTag("http.response.status_code", context.Response.StatusCode);
        if (context.Response.StatusCode >= StatusCodes.Status500InternalServerError)
            activity.SetStatus(ActivityStatusCode.Error);
    }
}
