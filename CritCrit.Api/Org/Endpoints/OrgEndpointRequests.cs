using CritCrit.Api.Org.Domain;

namespace CritCrit.Api.Org.Endpoints;

public sealed record CreateBrandRequest(string Code, string Name);

public sealed record CreatePlainOrgNodeRequest(string ParentId, string Code, string Name);

public sealed record CreateStoreRequest(string ParentId, string Code, string Name, string? TimeZone);

public sealed record CreateDeviceRequest(string ParentStoreId, string SerialNumber, string Name, DeviceType DeviceType);

public sealed record CreateSubjectRequest(string Email, string? DisplayName, string Provider, string ProviderTenant, string ExternalId);

public sealed record GrantRoleRequest(string OrgNodeId, string SubjectId, OrgRole Role, DateTimeOffset? ExpiresAt);

public sealed record SetGrantExpirationRequest(string OrgNodeId, string SubjectId, DateTimeOffset? ExpiresAt);

public sealed record GrantOwnerRequest(string SubjectId);

public sealed record DowngradeOwnerRequest(OrgRole NewRole, string Reason);

public sealed record RevokeOwnerRequest(string Reason);

public sealed record RevokeGrantRequest(string OrgNodeId, string SubjectId, string? Reason);

public sealed record DeactivateSubjectRequest(string? Reason);

public sealed record ReactivateSubjectRequest(string? Reason);

public sealed record RelinkSubjectIdentityRequest(
    string Provider,
    string ProviderTenant,
    string OldExternalId,
    string NewExternalId,
    string? Reason);

public sealed record CreateInvitationRequest(string OrgNodeId, string Email, OrgRole Role);

public sealed record CancelInvitationRequest(string? Reason);

public sealed record OrgNodeResponse(
    string Id,
    string TenantId,
    string? ParentId,
    OrgNodeType Type,
    string Code,
    string Name,
    string Path,
    bool Archived,
    bool EffectiveArchived,
    bool HardDeleted);

public sealed record BrandListItem(
    string Id,
    string Code,
    string Name,
    bool Archived,
    OrgRole? HighestRole,
    BrandAccessSource Source);

public enum BrandAccessSource
{
    Grant,
    Platform
}

public sealed record OrgTreeNodeResponse(
    string Id,
    string? ParentId,
    OrgNodeType Type,
    string Code,
    string Name,
    string Path,
    bool Archived,
    bool EffectiveArchived,
    IReadOnlyList<OrgTreeNodeResponse> Children);

public sealed record SubjectResponse(string Id, string Email, string? DisplayName);

public sealed record SubjectListItem(
    string Id,
    string Email,
    string? DisplayName,
    SubjectKind Kind,
    bool Active,
    DateTimeOffset? OnboardedAt);

public sealed record GrantResponse(string Id, string OrgNodeId, string SubjectId, OrgRole Role, DateTimeOffset? ExpiresAt);

public sealed record GrantListItem(
    string Id,
    string OrgNodeId,
    string OrgNodeName,
    OrgNodeType OrgNodeType,
    string SubjectId,
    string SubjectEmail,
    string? SubjectDisplayName,
    OrgRole Role,
    DateTimeOffset? ExpiresAt,
    OrgAccessGrantSource Source);

public sealed record InvitationResponse(
    string Id,
    string BrandId,
    string OrgNodeId,
    string Email,
    string? SubjectId,
    OrgRole Role,
    InvitationStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? LastSentAt,
    string? Failure);

public sealed record AcceptInvitationResponse(
    string InvitationId,
    InvitationStatus Status,
    bool GrantCreated,
    bool SubjectOnboarded,
    int AutoAppliedInvitations);

public sealed record AuditEventResponse(
    Guid Id,
    string Action,
    DateTimeOffset OccurredAt,
    string? Reason,
    string ActorExternalId,
    Guid? ActorSubjectId,
    Guid? TenantId,
    Guid? TargetOrgNodeId,
    object? Details);

public sealed record ArchiveOrgNodeRequest(bool Force, string? Reason);

public sealed record HardDeleteOrgNodeRequest(string Reason);

public sealed record MoveOrgNodeRequest(string NewParentId, string Reason);
