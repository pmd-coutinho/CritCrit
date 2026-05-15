namespace CritCrit.Api.Org.Infrastructure;

/// <summary>
/// String constants for Org-slice audit-event action names. Kept Org-namespaced
/// because other slices will define their own action constants.
/// </summary>
public static class OrgAuditActions
{
    public const string HardDeleteSubtree = "org.hard-delete-subtree";
    public const string CascadeArchive = "org.archive-subtree";
    public const string BrandArchive = "brand.archive";
    public const string BrandRestore = "brand.restore";
    public const string OrgNodeMove = "org.move";
    public const string OrgNodeCreated = "org.created";
    public const string OrgNodeUpdated = "org.updated";
    public const string StoreProfileUpdated = "store.profile.updated";
    public const string DeviceProfileUpdated = "device.profile.updated";
    public const string GrantCreated = "org.grant.created";
    public const string GrantRoleChanged = "org.grant.role-changed";
    public const string GrantExpirationChanged = "org.grant.expiration-changed";
    public const string GrantExpired = "org.grant.expired";
    public const string GrantRedundantRevoked = "org.grant.redundant-revoked";
    public const string OwnerGranted = "org.owner.granted";
    public const string OwnerRevoked = "org.owner.revoked";
    public const string OwnerDowngraded = "org.owner.downgraded";
    public const string InvitationRequested = "invitation.requested";
    public const string InvitationCancelled = "invitation.cancelled";
    public const string InvitationResent = "invitation.resent";
    public const string InvitationEmailDispatched = "invitation.email-dispatched";
    public const string InvitationSecurityFailed = "invitation.security-failed";
    public const string InvitationAccepted = "invitation.accepted";
    public const string InvitationAutoApplied = "invitation.auto-applied";
    public const string InvitationObsoleted = "invitation.obsoleted";
    public const string InvitationFailed = "invitation.failed";
    public const string InvitationExpired = "invitation.expired";
    public const string GrantRevoked = "org.grant.revoked";
    public const string SubjectDeactivated = "subject.deactivated";
    public const string SubjectCreated = "subject.created";
    public const string SubjectReactivated = "subject.reactivated";
    public const string SubjectRelinked = "subject.relinked";
    public const string PrivilegedActionDenied = "security.privileged-action-denied";
}
