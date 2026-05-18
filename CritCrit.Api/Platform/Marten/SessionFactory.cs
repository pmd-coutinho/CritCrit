using CritCrit.Api.Platform.Tenancy;
using Marten;

namespace CritCrit.Api.Platform.Marten;

/// <summary>
/// Thin session-opening helpers. Tenant and actor are injected directly into
/// handlers as scoped DI services — see Program.cs registrations.
/// </summary>
public static class SessionFactory
{
    public static IDocumentSession TenantSession(IDocumentStore store, BrandTenantContext tenant) =>
        store.LightweightSession(tenant.TenantId.Value.ToString());

    public static IDocumentSession PlatformSession(IDocumentStore store) =>
        store.LightweightSession(PlatformTenant.Id);

    public static IQuerySession PlatformQuerySession(IDocumentStore store) =>
        store.QuerySession(PlatformTenant.Id);
}
