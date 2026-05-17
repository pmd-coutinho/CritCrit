using CritCrit.Api.Org.Domain;

namespace CritCrit.Api.Org.Features.Config;

/// <summary>
/// Internal invalidation signal. Published transactionally via Marten's
/// outbox alongside config events. V1 consumers are no-op/logging; v2 will
/// drive cache invalidation on cache-equipped services.
/// </summary>
public sealed record ConfigInvalidationRequested(
    ConfigChangeKind Kind,
    Guid? TenantId,
    Guid? ScopeOrgNodeId,
    string SchemaCode,
    int? SchemaVersion,
    IReadOnlyList<string> AffectedKeys);
