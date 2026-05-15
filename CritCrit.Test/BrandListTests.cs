using System.Net;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Test.Fixtures;

namespace CritCrit.Test;

public sealed class BrandListTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task superadmin_sees_every_brand_with_platform_source()
    {
        var a = await CreateBrand(Code("alpha"));
        var b = await CreateBrand(Code("bravo"));

        var brands = await GetAsSuperAdmin<List<BrandListItem>>("/api/brands");

        Assert.Contains(brands, x => x.Id == a.Id);
        Assert.Contains(brands, x => x.Id == b.Id);
        Assert.All(brands, x =>
        {
            Assert.Null(x.HighestRole);
            Assert.Equal(BrandAccessSource.Platform, x.Source);
        });
    }

    [Fact]
    public async Task member_sees_only_brands_where_they_have_a_grant()
    {
        var ownBrand = await CreateBrand(Code("own"));
        var otherBrand = await CreateBrand(Code("other"));

        var subject = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects",
            new CreateSubjectRequest("member@example.com", "Mem", "test", "default", "member-ext"));

        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{ownBrand.Id}/access-grants",
            new GrantRoleRequest(ownBrand.Id, subject.Id, OrgRole.Member, null));

        var brands = await GetAsUser<List<BrandListItem>>(
            "/api/brands", "member-ext", "member@example.com");

        var entry = Assert.Single(brands);
        Assert.Equal(ownBrand.Id, entry.Id);
        Assert.Equal(OrgRole.Member, entry.HighestRole);
        Assert.Equal(BrandAccessSource.Grant, entry.Source);
        Assert.DoesNotContain(brands, x => x.Id == otherBrand.Id);
    }
}
