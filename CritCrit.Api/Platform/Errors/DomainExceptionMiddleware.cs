namespace CritCrit.Api.Platform.Errors;

public sealed class DomainExceptionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (DomainException ex)
        {
            context.Response.StatusCode = ex.StatusCode;
            context.Response.Headers[SupportId.HeaderName] = SupportId.Current;
            await context.Response.WriteAsJsonAsync(new
            {
                type = $"https://httpstatuses.com/{ex.StatusCode}",
                title = ex.Message,
                status = ex.StatusCode,
                detail = ex.Message,
                error = ex.Message,
                supportId = SupportId.Current
            }, context.RequestAborted);
        }
    }
}
