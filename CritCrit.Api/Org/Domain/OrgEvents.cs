namespace CritCrit.Api.Org.Domain;

public sealed record OrgNodeCreated(
    OrgNodeId Id,
    OrgNodeId TenantId,
    OrgNodeId? ParentId,
    OrgNodeType Type,
    string Code,
    string CodeNormalized,
    string Name);

public sealed record OrgNodeRenamed(OrgNodeId Id, string OldName, string NewName);

public sealed record OrgNodeMoved(OrgNodeId Id, OrgNodeId OldParentId, OrgNodeId NewParentId, string Reason);

public sealed record OrgNodeArchived(OrgNodeId Id, bool Force, string? Reason);

public sealed record OrgNodeRestored(OrgNodeId Id);

public sealed record OrgNodeHardDeleted(OrgNodeId Id, string Reason);

public sealed record StoreProfileCreated(OrgNodeId StoreId, string TimeZone);

public sealed record StoreProfileUpdated(OrgNodeId StoreId, string TimeZone);

public sealed record StoreProfileHardDeleted(OrgNodeId StoreId);

public sealed record DeviceProfileCreated(OrgNodeId DeviceId, string SerialNumber, DeviceType DeviceType);

public sealed record DeviceProfileHardDeleted(OrgNodeId DeviceId);

public sealed record SubjectCreated(
    SubjectId Id,
    SubjectKind Kind,
    string Email,
    string? DisplayName);

public sealed record ExternalIdentityLinked(
    SubjectId SubjectId,
    string Provider,
    string ProviderTenant,
    string ExternalId);

public sealed record OrgAccessGranted(
    OrgNodeId TenantId,
    OrgNodeId OrgNodeId,
    SubjectId SubjectId,
    OrgRole Role,
    DateTimeOffset? ExpiresAt,
    OrgAccessGrantSource Source,
    InvitationId? InvitationId);

public sealed record OrgAccessRoleChanged(
    OrgNodeId TenantId,
    OrgNodeId OrgNodeId,
    SubjectId SubjectId,
    OrgRole OldRole,
    OrgRole NewRole);

public sealed record OrgAccessRevoked(
    OrgNodeId TenantId,
    OrgNodeId OrgNodeId,
    SubjectId SubjectId,
    OrgAccessRevokedReason Reason);

public sealed record OrgAccessExpired(
    OrgNodeId TenantId,
    OrgNodeId OrgNodeId,
    SubjectId SubjectId);
