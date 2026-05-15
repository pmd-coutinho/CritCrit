using System.Net;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Test.Fixtures;
using Marten;

namespace CritCrit.Test;

public sealed class GrantRevokeTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task superadmin_can_revoke_any_active_member_grant()
    {
        var brand = await CreateBrand(Code("rev"));
        var subject = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects",
            new CreateSubjectRequest("mem@example.com", null, "test", "default", "mem-ext"));
        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, subject.Id, OrgRole.Member, null));

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new RevokeGrantRequest(brand.Id, subject.Id, "no longer needed"), Alba.JsonStyle.MinimalApi)
                .ToUrl($"/api/brands/{brand.Id}/access-grants/revoke");
            _.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });

        await using var session = DocumentStore.QuerySession(ParseGuid(brand.Id).ToString());
        var grants = await session.Query<OrgAccessGrantReadModel>()
            .Where(x => x.SubjectId == ParseSubjectGuid(subject.Id))
            .ToListAsync();
        Assert.Single(grants);
        Assert.Equal(OrgAccessGrantStatus.Revoked, grants[0].Status);
    }

    [Fact]
    public async Task revoke_refuses_owner_grants()
    {
        var brand = await CreateBrand(Code("rev-own"));
        var subject = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects",
            new CreateSubjectRequest("own@example.com", null, "test", "default", "own-ext"));
        await PostAsSuperAdmin<GrantOwnerRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/owners",
            new GrantOwnerRequest(subject.Id));

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new RevokeGrantRequest(brand.Id, subject.Id, null), Alba.JsonStyle.MinimalApi)
                .ToUrl($"/api/brands/{brand.Id}/access-grants/revoke");
            _.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }

    [Fact]
    public async Task non_superadmin_actor_with_lower_role_cannot_revoke_higher_role_grant()
    {
        // Brand owner = "actor-ext" subject with Admin (not Owner) at the brand.
        // Target grant = Admin role on the same node. Actor.role >= grant.role
        // because both are Admin → should allow.
        var brand = await CreateBrand(Code("rev-eq"));
        var actorSubject = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects",
            new CreateSubjectRequest("actor@example.com", null, "test", "default", "actor-ext"));
        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, actorSubject.Id, OrgRole.Admin, null));

        var target = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects",
            new CreateSubjectRequest("target@example.com", null, "test", "default", "target-ext"));
        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, target.Id, OrgRole.Member, null));

        // Admin revoking Member → role gate passes (Admin >= Member).
        await Host.Scenario(_ =>
        {
            AsUser(_, "actor-ext", "actor@example.com");
            _.Post.Json(new RevokeGrantRequest(brand.Id, target.Id, null), Alba.JsonStyle.MinimalApi)
                .ToUrl($"/api/brands/{brand.Id}/access-grants/revoke");
            _.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });

        // Now have Admin try to revoke an Owner-equivalent: grant another Admin
        // and try as a Member actor. Member < Admin → 403.
        var member = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects",
            new CreateSubjectRequest("member@example.com", null, "test", "default", "member-ext"));
        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, member.Id, OrgRole.Member, null));

        var other = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects",
            new CreateSubjectRequest("other-admin@example.com", null, "test", "default", "other-admin-ext"));
        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, other.Id, OrgRole.Admin, null));

        await Host.Scenario(_ =>
        {
            AsUser(_, "member-ext", "member@example.com");
            _.Post.Json(new RevokeGrantRequest(brand.Id, other.Id, null), Alba.JsonStyle.MinimalApi)
                .ToUrl($"/api/brands/{brand.Id}/access-grants/revoke");
            _.StatusCodeShouldBe(HttpStatusCode.Forbidden);
        });
    }

    private static Guid ParseGuid(string publicId) =>
        OrgPublicId.TryParseOrgNode(publicId, out var id, out _) ? id.Value : Guid.Empty;

    private static Guid ParseSubjectGuid(string publicId) =>
        OrgPublicId.TryParseSubject(publicId, out var id) ? id.Value : Guid.Empty;
}
