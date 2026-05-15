namespace CritCrit.Api.Observability.Support;

public sealed class SupportIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey(SupportId.HeaderName))
                context.Response.Headers[SupportId.HeaderName] = SupportId.Current;

            return Task.CompletedTask;
        });

        await next(context);
    }
}
