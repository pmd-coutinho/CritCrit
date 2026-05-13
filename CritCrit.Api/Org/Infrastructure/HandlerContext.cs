using CritCrit.Api.Org.Domain;
using Marten;

namespace CritCrit.Api.Org.Infrastructure;

public static class HandlerContext
{
    public const string DefaultTenant = "global";

    public static BrandTenantContext GetTenant(HttpContext httpContext) =>
        httpContext.Items[BrandTenantContext.ItemKey] as BrandTenantContext
        ?? throw new DomainException("Brand tenant not resolved.");

    public static IDocumentSession TenantSession(IDocumentStore store, BrandTenantContext tenant) =>
        store.LightweightSession(tenant.TenantId.Value.ToString());

    public static IDocumentSession PlatformSession(IDocumentStore store) =>
        store.LightweightSession();
}
