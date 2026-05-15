using CritCrit.Api.Org.Domain;

namespace CritCrit.Api.Platform.Tenancy;

/// <summary>
/// Per-request tenant context for brand-scoped endpoints. Populated by
/// <see cref="CritCrit.Api.Org.Infrastructure.BrandTenantMiddleware"/> from the
/// route's <c>{brandId}</c> segment and resolved to handlers via scoped DI in
/// <c>Program.cs</c>. Slice-agnostic so future slices (Config, …) can take it as
/// a parameter without dragging Org/Infrastructure imports.
/// </summary>
public sealed record BrandTenantContext(OrgNodeId TenantId, string BrandPublicId)
{
    public const string ItemKey = "BrandTenantContext";
}
