using CritCrit.Api.Org.Auth;
using Marten;

namespace CritCrit.Api.Org.Infrastructure;

/// <summary>
/// Resolves the authenticated principal into an <see cref="ActorContext"/> once
/// per request and stamps it on <see cref="HttpContext.Items"/> so downstream
/// scoped DI factories can hand it back without re-querying the document store.
/// Runs after authentication middleware.
/// </summary>
public sealed class RequestActorMiddleware(RequestDelegate next)
{
    public const string ItemKey = "ActorContext";

    public async Task InvokeAsync(HttpContext context, IDocumentStore store)
    {
        await using var query = store.QuerySession();
        var actor = await ActorContextResolver.ResolveAsync(query, context.User, context.RequestAborted);
        context.Items[ItemKey] = actor;
        await next(context);
    }
}
