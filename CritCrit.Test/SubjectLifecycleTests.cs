using System.Net;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Test.Fixtures;
using Marten;

namespace CritCrit.Test;

public sealed class SubjectLifecycleTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task deactivate_marks_subject_inactive_and_cascades_revoke()
    {
        var brandA = await CreateBrand(Code("dact-a"));
        var brandB = await CreateBrand(Code("dact-b"));
        var subject = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects",
            new CreateSubjectRequest("multi@example.com", null, "test", "default", "multi-ext"));

        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brandA.Id}/access-grants",
            new GrantRoleRequest(brandA.Id, subject.Id, OrgRole.Member, null));
        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brandB.Id}/access-grants",
            new GrantRoleRequest(brandB.Id, subject.Id, OrgRole.Admin, null));

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new DeactivateSubjectRequest("offboarding"), Alba.JsonStyle.MinimalApi)
                .ToUrl($"/api/platform/subjects/{subject.Id}/deactivate");
            _.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });

        await using var platform = DocumentStore.QuerySession("PLATFORM");
        var loaded = await platform.LoadAsync<SubjectReadModel>(ParseSubjectGuid(subject.Id));
        Assert.NotNull(loaded);
        Assert.False(loaded!.Active);

        foreach (var brandId in new[] { brandA.Id, brandB.Id })
        {
            await using var tenant = DocumentStore.QuerySession(ParseGuid(brandId).ToString());
            var grants = await tenant.Query<OrgAccessGrantReadModel>()
                .Where(x => x.SubjectId == ParseSubjectGuid(subject.Id))
                .ToListAsync();
            Assert.All(grants, g => Assert.Equal(OrgAccessGrantStatus.Revoked, g.Status));
        }
    }

    [Fact]
    public async Task reactivate_flips_active_back_without_restoring_grants()
    {
        var brand = await CreateBrand(Code("react"));
        var subject = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects",
            new CreateSubjectRequest("returner@example.com", null, "test", "default", "returner-ext"));
        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, subject.Id, OrgRole.Member, null));

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new DeactivateSubjectRequest(null), Alba.JsonStyle.MinimalApi)
                .ToUrl($"/api/platform/subjects/{subject.Id}/deactivate");
            _.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new ReactivateSubjectRequest("returning employee"), Alba.JsonStyle.MinimalApi)
                .ToUrl($"/api/platform/subjects/{subject.Id}/reactivate");
            _.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });

        await using var platform = DocumentStore.QuerySession("PLATFORM");
        var loaded = await platform.LoadAsync<SubjectReadModel>(ParseSubjectGuid(subject.Id));
        Assert.True(loaded!.Active);

        // Grants stay revoked — operator must re-grant explicitly.
        await using var tenant = DocumentStore.QuerySession(ParseGuid(brand.Id).ToString());
        var grants = await tenant.Query<OrgAccessGrantReadModel>()
            .Where(x => x.SubjectId == ParseSubjectGuid(subject.Id))
            .ToListAsync();
        Assert.All(grants, g => Assert.Equal(OrgAccessGrantStatus.Revoked, g.Status));
    }

    [Fact]
    public async Task relink_swaps_external_identity_to_new_keycloak_user()
    {
        var subject = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects",
            new CreateSubjectRequest("relink@example.com", null, "keycloak", "api", "old-kc-sub"));

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(
                new RelinkSubjectIdentityRequest("keycloak", "api", "old-kc-sub", "new-kc-sub", "KC user re-created"),
                Alba.JsonStyle.MinimalApi)
                .ToUrl($"/api/platform/subjects/{subject.Id}/relink");
            _.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });

        await using var session = DocumentStore.QuerySession();
        var oldLink = await session.LoadAsync<ExternalIdentityReadModel>(
            ExternalIdentityReadModel.BuildId("keycloak", "api", "old-kc-sub"));
        var newLink = await session.LoadAsync<ExternalIdentityReadModel>(
            ExternalIdentityReadModel.BuildId("keycloak", "api", "new-kc-sub"));

        Assert.Null(oldLink);
        Assert.NotNull(newLink);
        Assert.Equal(ParseSubjectGuid(subject.Id), newLink!.SubjectId);
    }

    private static Guid ParseGuid(string publicId) =>
        OrgPublicId.TryParseOrgNode(publicId, out var id, out _) ? id.Value : Guid.Empty;

    private static Guid ParseSubjectGuid(string publicId) =>
        OrgPublicId.TryParseSubject(publicId, out var id) ? id.Value : Guid.Empty;
}
