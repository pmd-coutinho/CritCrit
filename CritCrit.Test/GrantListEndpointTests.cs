using System.Net;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Test.Fixtures;

namespace CritCrit.Test;

public sealed class GrantListEndpointTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task admin_sees_joined_subject_and_node_rows()
    {
        var brand = await CreateBrand(Code("grants"));
        var country = await PostAsSuperAdmin<CreatePlainOrgNodeRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/countries",
            new CreatePlainOrgNodeRequest(brand.Id, "US", "United States"));

        var member = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects",
            new CreateSubjectRequest("member@example.com", "Mem", "test", "default", "member-ext"));
        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(country.Id, member.Id, OrgRole.Admin, null));

        var rows = await GetAsSuperAdmin<List<GrantListItem>>(
            $"/api/brands/{brand.Id}/access-grants");

        var row = Assert.Single(rows);
        Assert.Equal(member.Id, row.SubjectId);
        Assert.Equal("member@example.com", row.SubjectEmail);
        Assert.Equal(country.Id, row.OrgNodeId);
        Assert.Equal(country.Name, row.OrgNodeName);
        Assert.Equal(OrgNodeType.Country, row.OrgNodeType);
        Assert.Equal(OrgRole.Admin, row.Role);
    }

    [Fact]
    public async Task member_actor_is_forbidden_from_listing_grants()
    {
        var brand = await CreateBrand(Code("perm"));
        var member = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects",
            new CreateSubjectRequest("mem2@example.com", "Mem2", "test", "default", "mem2-ext"));

        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, member.Id, OrgRole.Member, null));

        await GetAsUserRaw(
            $"/api/brands/{brand.Id}/access-grants",
            "mem2-ext", "mem2@example.com",
            HttpStatusCode.Forbidden);
    }
}
