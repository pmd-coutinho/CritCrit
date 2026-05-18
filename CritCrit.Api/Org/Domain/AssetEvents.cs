namespace CritCrit.Api.Org.Domain;

public sealed record AssetNodeValueSetInitialized(
    Guid StreamId,
    OrgNodeId TenantId,
    OrgNodeId OrgNodeId);

public sealed record AssetNodeValuesPatched(
    OrgNodeId TenantId,
    OrgNodeId OrgNodeId,
    IReadOnlyList<AssetPatchApplied> Operations,
    DateTimeOffset AppliedAt,
    string AppliedByExternalId);
