using CritCrit.Api.Org.Domain;

namespace CritCrit.UnitTests;

public class OrgRulesTests
{
    [Theory]
    [InlineData(OrgNodeType.Brand, OrgNodeType.Country, true)]
    [InlineData(OrgNodeType.Brand, OrgNodeType.Franchise, true)]
    [InlineData(OrgNodeType.Brand, OrgNodeType.Store, true)]
    [InlineData(OrgNodeType.Brand, OrgNodeType.Device, false)]
    [InlineData(OrgNodeType.Country, OrgNodeType.Franchise, true)]
    [InlineData(OrgNodeType.Country, OrgNodeType.Store, true)]
    [InlineData(OrgNodeType.Country, OrgNodeType.Device, false)]
    [InlineData(OrgNodeType.Franchise, OrgNodeType.Store, true)]
    [InlineData(OrgNodeType.Franchise, OrgNodeType.Country, false)]
    [InlineData(OrgNodeType.Store, OrgNodeType.Device, true)]
    [InlineData(OrgNodeType.Store, OrgNodeType.Store, false)]
    [InlineData(OrgNodeType.Device, OrgNodeType.Device, false)]
    [InlineData(OrgNodeType.Brand, OrgNodeType.Brand, false)]
    public void CanContain_matrix(OrgNodeType parent, OrgNodeType child, bool expected)
    {
        Assert.Equal(expected, OrgRules.CanContain(parent, child));
    }

    [Theory]
    [InlineData(OrgRole.Viewer, OrgNodeType.Brand, true)]
    [InlineData(OrgRole.Viewer, OrgNodeType.Store, true)]
    [InlineData(OrgRole.Member, OrgNodeType.Store, true)]
    [InlineData(OrgRole.Admin, OrgNodeType.Store, true)]
    [InlineData(OrgRole.Owner, OrgNodeType.Brand, true)]
    [InlineData(OrgRole.Owner, OrgNodeType.Country, false)]
    [InlineData(OrgRole.Owner, OrgNodeType.Store, false)]
    [InlineData(OrgRole.Owner, OrgNodeType.Device, false)]
    public void CanGrantRoleAt_only_allows_Owner_at_Brand(OrgRole role, OrgNodeType type, bool expected)
    {
        Assert.Equal(expected, OrgRules.CanGrantRoleAt(role, type));
    }

    [Theory]
    [InlineData(OrgRole.Viewer, OrgRole.Viewer, true)]
    [InlineData(OrgRole.Member, OrgRole.Viewer, true)]
    [InlineData(OrgRole.Admin, OrgRole.Member, true)]
    [InlineData(OrgRole.Owner, OrgRole.Admin, true)]
    [InlineData(OrgRole.Viewer, OrgRole.Member, false)]
    [InlineData(OrgRole.Member, OrgRole.Admin, false)]
    [InlineData(OrgRole.Admin, OrgRole.Owner, false)]
    public void IsAtLeast_compares_by_role_strength(OrgRole have, OrgRole required, bool expected)
    {
        Assert.Equal(expected, have.IsAtLeast(required));
    }

    [Fact]
    public void PermissionsFor_is_strictly_growing_with_role()
    {
        var viewer = OrgRules.PermissionsFor(OrgRole.Viewer);
        var member = OrgRules.PermissionsFor(OrgRole.Member);
        var admin = OrgRules.PermissionsFor(OrgRole.Admin);
        var owner = OrgRules.PermissionsFor(OrgRole.Owner);

        Assert.True(viewer.IsSubsetOf(member));
        Assert.True(member.IsSubsetOf(admin));
        Assert.True(admin.IsSubsetOf(owner));
        Assert.Contains(OrgPermissions.OrgNodeRead, viewer);
        Assert.Contains(OrgPermissions.DeviceOperate, member);
        Assert.Contains(OrgPermissions.OrgNodeCreate, admin);
        Assert.Contains(OrgPermissions.OrgNodeHardDelete, owner);
        Assert.DoesNotContain(OrgPermissions.OrgNodeCreate, viewer);
        Assert.DoesNotContain(OrgPermissions.OrgNodeHardDelete, admin);
    }
}
