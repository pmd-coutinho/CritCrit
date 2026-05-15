namespace CritCrit.Api.Org.Domain;

public enum OrgNodeType
{
    Brand,
    Country,
    Franchise,
    Store,
    Device
}

public enum DeviceType
{
    Kiosk,
    DriveThru
}

public enum OrgRole
{
    Viewer = 10,
    Member = 20,
    Admin = 30,
    Owner = 40
}

public enum SubjectKind
{
    User,
    Group,
    ServiceAccount
}

public enum OrgAccessGrantStatus
{
    None,
    Active,
    Revoked,
    Expired
}

public enum OrgAccessGrantSource
{
    DirectGrant,
    Invitation
}

public enum OrgAccessRevokedReason
{
    UserRequested,
    RedundantByAncestorGrant,
    TargetHardDeleted,
    SubjectDisabled,
    SubjectDeactivated
}

public enum InvitationStatus
{
    Requested,
    Provisioning,
    Pending,
    Accepted,
    AutoApplied,
    Cancelled,
    Superseded,
    Expired,
    Obsolete,
    Failed
}
