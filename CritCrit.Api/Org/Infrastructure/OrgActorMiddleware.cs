using CritCrit.Api.Org.Auth;
using Marten;

namespace CritCrit.Api.Org.Infrastructure;

public static class OrgActorMiddleware
{
    public const string ItemKey = "ResolvedActor";

    public static async Task Apply(HttpContext httpContext, IDocumentStore store, CancellationToken ct)
    {
        await using var query = store.QuerySession(HandlerContext.DefaultTenant);
        var actor = await ActorContextResolver.ResolveAsync(query, httpContext.User, ct);
        if (!actor.IsAuthenticated)
            throw new DomainException("Authentication required.", 401);

        httpContext.Items[ItemKey] = actor;
    }
}
