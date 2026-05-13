using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using Marten;

namespace CritCrit.Api.Org.Infrastructure;

public static class HandlerContext
{
    public static BrandTenantContext GetTenant(HttpContext httpContext) =>
        httpContext.Items[BrandTenantContext.ItemKey] as BrandTenantContext
        ?? throw new DomainException("Brand tenant not resolved.");

    public static IDocumentSession TenantSession(IDocumentStore store, BrandTenantContext tenant) =>
        store.LightweightSession(tenant.TenantId.Value.ToString());

    public static IDocumentSession PlatformSession(IDocumentStore store) =>
        store.LightweightSession();

    public static async Task<ActorContext> ResolveActorAsync(
        HttpContext httpContext, IDocumentStore store, CancellationToken ct)
    {
        await using var query = store.QuerySession();
        var actor = await ActorContextResolver.ResolveAsync(query, httpContext.User, ct);
        if (!actor.IsAuthenticated)
            throw new DomainException("Authentication required.", 401);
        return actor;
    }
}
