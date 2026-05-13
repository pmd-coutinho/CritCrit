namespace CritCrit.Api.Org.Domain;

public static class OrgRules
{
    private static readonly HashSet<(OrgNodeType Parent, OrgNodeType Child)> AllowedChildren =
    [
        (OrgNodeType.Brand, OrgNodeType.Country),
        (OrgNodeType.Brand, OrgNodeType.Franchise),
        (OrgNodeType.Brand, OrgNodeType.Store),
        (OrgNodeType.Country, OrgNodeType.Franchise),
        (OrgNodeType.Country, OrgNodeType.Store),
        (OrgNodeType.Franchise, OrgNodeType.Store),
        (OrgNodeType.Store, OrgNodeType.Device)
    ];

    public static bool CanContain(OrgNodeType parent, OrgNodeType child) => AllowedChildren.Contains((parent, child));

    public static bool CanGrantRoleAt(OrgRole role, OrgNodeType nodeType) =>
        role != OrgRole.Owner || nodeType == OrgNodeType.Brand;

    public static bool IsAtLeast(this OrgRole role, OrgRole required) => role >= required;

    public static IReadOnlySet<string> PermissionsFor(OrgRole role)
    {
        var permissions = new HashSet<string>(StringComparer.Ordinal);

        if (role >= OrgRole.Viewer)
        {
            permissions.Add(OrgPermissions.OrgNodeRead);
            permissions.Add(OrgPermissions.StoreProfileRead);
            permissions.Add(OrgPermissions.DeviceProfileRead);
        }

        if (role >= OrgRole.Member)
        {
            permissions.Add(OrgPermissions.DeviceOperate);
        }

        if (role >= OrgRole.Admin)
        {
            permissions.Add(OrgPermissions.OrgNodeCreate);
            permissions.Add(OrgPermissions.OrgNodeMove);
            permissions.Add(OrgPermissions.OrgNodeArchive);
            permissions.Add(OrgPermissions.OrgNodeRestore);
            permissions.Add(OrgPermissions.OrgAccessManage);
            permissions.Add(OrgPermissions.StoreProfileUpdate);
            permissions.Add(OrgPermissions.DeviceProfileUpdate);
        }

        if (role >= OrgRole.Owner)
        {
            permissions.Add(OrgPermissions.OrgNodeHardDelete);
            permissions.Add(OrgPermissions.OwnerManage);
        }

        return permissions;
    }
}

public static class OrgPermissions
{
    public const string OrgNodeRead = "org.node.read";
    public const string OrgNodeCreate = "org.node.create";
    public const string OrgNodeMove = "org.node.move";
    public const string OrgNodeArchive = "org.node.archive";
    public const string OrgNodeRestore = "org.node.restore";
    public const string OrgNodeHardDelete = "org.node.hard-delete";
    public const string OrgAccessManage = "org.access.manage";
    public const string OwnerManage = "org.owner.manage";
    public const string StoreProfileRead = "store.profile.read";
    public const string StoreProfileUpdate = "store.profile.update";
    public const string DeviceProfileRead = "device.profile.read";
    public const string DeviceProfileUpdate = "device.profile.update";
    public const string DeviceOperate = "device.operate";
}
