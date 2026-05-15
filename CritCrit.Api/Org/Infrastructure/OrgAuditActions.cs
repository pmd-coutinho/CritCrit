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
    public const string OwnerGranted = "org.owner.granted";
    public const string OwnerRevoked = "org.owner.revoked";
    public const string OwnerDowngraded = "org.owner.downgraded";
    public const string InvitationRequested = "invitation.requested";
    public const string InvitationCancelled = "invitation.cancelled";
    public const string InvitationAccepted = "invitation.accepted";
    public const string InvitationAutoApplied = "invitation.auto-applied";
    public const string InvitationObsoleted = "invitation.obsoleted";
    public const string InvitationFailed = "invitation.failed";
    public const string InvitationExpired = "invitation.expired";
}
