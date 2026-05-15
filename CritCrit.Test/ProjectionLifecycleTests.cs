using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Test.Fixtures;
using Marten;

namespace CritCrit.Test;

public sealed class ProjectionLifecycleTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task brand_index_tracks_create_archive_restore_hard_delete()
    {
        var brand = await CreateBrand(Code("idx"));
        var brandGuid = ParseOrgNodeGuid(brand.Id);

        var initial = await LoadIndexAsync(brandGuid);
        Assert.NotNull(initial);
        Assert.Equal(brand.Code, initial!.Code);
        Assert.False(initial.Archived);

        await PostAsSuperAdmin<ArchiveOrgNodeRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/org-nodes/{brand.Id}/archive",
            new ArchiveOrgNodeRequest(true, "test"),
            System.Net.HttpStatusCode.OK);
        Assert.True((await LoadIndexAsync(brandGuid))!.Archived);

        await PostAsSuperAdmin<object, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/org-nodes/{brand.Id}/restore",
            new { },
            System.Net.HttpStatusCode.OK);
        Assert.False((await LoadIndexAsync(brandGuid))!.Archived);

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new HardDeleteOrgNodeRequest("teardown"), Alba.JsonStyle.MinimalApi)
                .ToUrl($"/api/brands/{brand.Id}/org-nodes/{brand.Id}/hard-delete");
            _.StatusCodeShouldBe(System.Net.HttpStatusCode.NoContent);
        });
        Assert.Null(await LoadIndexAsync(brandGuid));
    }

    [Fact]
    public async Task subject_brand_access_upserts_grant_and_removes_when_empty()
    {
        var brand = await CreateBrand(Code("sba"));
        var country = await PostAsSuperAdmin<CreatePlainOrgNodeRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/countries",
            new CreatePlainOrgNodeRequest(brand.Id, "US", "United States"));

        var subject = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects",
            new CreateSubjectRequest("sba@example.com", "S", "test", "default", "sba-ext"));

        var tenantGuid = ParseOrgNodeGuid(brand.Id);
        var subjectGuid = ParseSubjectGuid(subject.Id);
        var accessId = $"{subjectGuid:N}:{tenantGuid:N}";

        // Grant Member at brand root → HighestRole=Member, one entry
        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, subject.Id, OrgRole.Member, null));

        var first = await LoadAccessAsync(accessId);
        Assert.NotNull(first);
        Assert.Equal(OrgRole.Member, first!.HighestRole);
        Assert.Single(first.Grants);

        // Grant Admin at country → HighestRole rises to Admin, two entries
        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(country.Id, subject.Id, OrgRole.Admin, null));

        var second = await LoadAccessAsync(accessId);
        Assert.NotNull(second);
        Assert.Equal(OrgRole.Admin, second!.HighestRole);
        Assert.Equal(2, second.Grants.Count);
    }

    private async Task<BrandIndexReadModel?> LoadIndexAsync(Guid brandId)
    {
        await using var s = DocumentStore.QuerySession();
        return await s.LoadAsync<BrandIndexReadModel>(brandId);
    }

    private async Task<SubjectBrandAccessReadModel?> LoadAccessAsync(string id)
    {
        await using var s = DocumentStore.QuerySession();
        return await s.LoadAsync<SubjectBrandAccessReadModel>(id);
    }

    private static Guid ParseOrgNodeGuid(string publicId) => OrgPublicId.TryParseOrgNode(publicId, out var id, out _) ? id.Value : Guid.Empty;
    private static Guid ParseSubjectGuid(string publicId) => OrgPublicId.TryParseSubject(publicId, out var id) ? id.Value : Guid.Empty;
}
