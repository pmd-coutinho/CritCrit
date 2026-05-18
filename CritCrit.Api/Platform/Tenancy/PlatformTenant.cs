namespace CritCrit.Api.Platform.Tenancy;

/// <summary>
/// Sentinel tenant id for platform-scoped Marten streams and documents under
/// the conjoined event-store tenancy regime (see
/// <c>.scratch/event-store-tenancy/PRD.md</c>). Used by SessionFactory when
/// opening sessions for entities that have no per-brand isolation (subjects,
/// invitations, config-schemas, external identities, audit, etc.).
///
/// Anything that crosses the brand/platform boundary must open a parallel
/// platform-tenanted query session — the brand-tenanted session cannot see
/// PLATFORM-scoped rows.
/// </summary>
public static class PlatformTenant
{
    public const string Id = "PLATFORM";
}
