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

public sealed record SubjectEmailUpdated(
    SubjectId Id,
    string Email);

public sealed record SubjectOnboarded(
    SubjectId Id,
    DateTimeOffset OnboardedAt);

public sealed record ExternalIdentityLinked(
    SubjectId SubjectId,
    string Provider,
    string ProviderTenant,
    string ExternalId);

public sealed record ExternalIdentityRelinked(
    SubjectId SubjectId,
    string Provider,
    string ProviderTenant,
    string OldExternalId,
    string NewExternalId,
    string? Reason);

public sealed record SubjectDeactivated(
    SubjectId Id,
    string? Reason,
    DateTimeOffset DeactivatedAt);

public sealed record SubjectReactivated(
    SubjectId Id,
    string? Reason,
    DateTimeOffset ReactivatedAt);

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

public sealed record OrgAccessExpirationChanged(
    OrgNodeId TenantId,
    OrgNodeId OrgNodeId,
    SubjectId SubjectId,
    DateTimeOffset? OldExpiresAt,
    DateTimeOffset? NewExpiresAt);

public sealed record InvitationRequested(
    InvitationId Id,
    OrgNodeId TenantId,
    OrgNodeId TargetOrgNodeId,
    string TargetOrgNodePublicId,
    string Email,
    OrgRole Role,
    SubjectId? InviterSubjectId,
    string InviterExternalId,
    DateTimeOffset CreatedAt);

public sealed record InvitationProvisioningStarted(
    InvitationId Id,
    DateTimeOffset StartedAt);

public sealed record InvitationSubjectBound(
    InvitationId Id,
    SubjectId SubjectId,
    string Email);

public sealed record InvitationTokenIssued(
    InvitationId Id,
    string TokenHash,
    DateTimeOffset ExpiresAt);

public sealed record InvitationMarkedPending(
    InvitationId Id,
    DateTimeOffset MarkedAt);

public sealed record InvitationAccepted(
    InvitationId Id,
    DateTimeOffset AcceptedAt,
    bool GrantCreated,
    bool SubjectOnboarded);

public sealed record InvitationAutoApplied(
    InvitationId Id,
    DateTimeOffset AppliedAt,
    bool GrantCreated);

public sealed record InvitationCancelled(
    InvitationId Id,
    DateTimeOffset CancelledAt,
    string? Reason);

public sealed record InvitationSuperseded(
    InvitationId Id,
    InvitationId ReplacedByInvitationId,
    DateTimeOffset SupersededAt);

public sealed record InvitationExpired(
    InvitationId Id,
    DateTimeOffset ExpiredAt);

public sealed record InvitationObsoleted(
    InvitationId Id,
    DateTimeOffset ObsoletedAt,
    string Reason);

public sealed record InvitationFailed(
    InvitationId Id,
    DateTimeOffset FailedAt,
    string FailureCode,
    string FailureSummary);

public sealed record InvitationEmailDispatched(
    InvitationId Id,
    DateTimeOffset SentAt);
