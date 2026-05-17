using System.Net;
using Alba;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Api.Org.Features.Config;
using CritCrit.Test.Fixtures;

namespace CritCrit.Test;

public sealed class ConfigAssignmentTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private static ConfigSchemaDefinition Definition(string keyCode = "usetaxcalc") =>
        new("Bridge", null,
        [
            new ConfigKeyDefinition(keyCode, keyCode, null, ConfigValueType.Boolean, null, null, new ConfigDefaultValue("true")),
        ]);

    private async Task<(OrgNodeResponse Brand, string SchemaCode, int Version)> CreateBrandAndPublishedSchema(string suffix)
    {
        var brand = await CreateBrand(Code("assign-" + suffix));
        var code = "as-" + suffix;
        var createResult = await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new CreateConfigSchemaRequest(code, "Bridge", null, "v1", Definition()), JsonStyle.MinimalApi)
                .ToUrl("/api/platform/config-schemas");
            _.StatusCodeShouldBe(HttpStatusCode.Created);
        });
        using var doc = System.Text.Json.JsonDocument.Parse(await createResult.ReadAsTextAsync());
        var draftId = doc.RootElement.GetProperty("draftId").GetGuid();

        await PostAsSuperAdmin<PublishConfigDraftRequest, ConfigSchemaVersionResponse>(
            $"/api/platform/config-schemas/{code}/drafts/{draftId}/publish",
            new PublishConfigDraftRequest(ExpectedVersion: 1, Reason: null),
            HttpStatusCode.OK);

        return (brand, code, 1);
    }

    [Fact]
    public async Task superadmin_creates_assignment_against_brand_root()
    {
        var (brand, code, version) = await CreateBrandAndPublishedSchema("root");

        var assignment = await PostAsSuperAdmin<AssignConfigSchemaRequest, ConfigAssignmentResponse>(
            $"/api/brands/{brand.Id}/org-nodes/{brand.Id}/config-assignments",
            new AssignConfigSchemaRequest(code, version, "rollout"),
            HttpStatusCode.Created);

        Assert.Equal(code, assignment.SchemaCode);
        Assert.Equal(version, assignment.SchemaVersion);
        Assert.False(assignment.Archived);
    }

    [Fact]
    public async Task duplicate_active_assignment_same_root_and_schema_is_rejected()
    {
        var (brand, code, version) = await CreateBrandAndPublishedSchema("dup");
        await PostAsSuperAdmin<AssignConfigSchemaRequest, ConfigAssignmentResponse>(
            $"/api/brands/{brand.Id}/org-nodes/{brand.Id}/config-assignments",
            new AssignConfigSchemaRequest(code, version, null),
            HttpStatusCode.Created);

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new AssignConfigSchemaRequest(code, version, null), JsonStyle.MinimalApi)
                .ToUrl($"/api/brands/{brand.Id}/org-nodes/{brand.Id}/config-assignments");
            _.StatusCodeShouldBe(HttpStatusCode.Conflict);
        });
    }

    [Fact]
    public async Task non_superadmin_cannot_create_assignment()
    {
        var (brand, code, version) = await CreateBrandAndPublishedSchema("perm");
        await Host.Scenario(_ =>
        {
            AsUser(_, "regular", "regular@example.com");
            _.Post.Json(new AssignConfigSchemaRequest(code, version, null), JsonStyle.MinimalApi)
                .ToUrl($"/api/brands/{brand.Id}/org-nodes/{brand.Id}/config-assignments");
            _.StatusCodeShouldBe(HttpStatusCode.Forbidden);
        });
    }

    [Fact]
    public async Task archived_then_restored_assignment_cycle_works()
    {
        var (brand, code, version) = await CreateBrandAndPublishedSchema("life");
        var assignment = await PostAsSuperAdmin<AssignConfigSchemaRequest, ConfigAssignmentResponse>(
            $"/api/brands/{brand.Id}/org-nodes/{brand.Id}/config-assignments",
            new AssignConfigSchemaRequest(code, version, null),
            HttpStatusCode.Created);

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new ArchiveConfigAssignmentRequest(ExpectedVersion: 1, Reason: "rollback"), JsonStyle.MinimalApi)
                .ToUrl($"/api/brands/{brand.Id}/org-nodes/{brand.Id}/config-assignments/{assignment.Id}/archive");
            _.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new RestoreConfigAssignmentRequest(ExpectedVersion: 2, Reason: null), JsonStyle.MinimalApi)
                .ToUrl($"/api/brands/{brand.Id}/org-nodes/{brand.Id}/config-assignments/{assignment.Id}/restore");
            _.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });

        var rows = await GetAsSuperAdmin<List<ConfigAssignmentResponse>>(
            $"/api/brands/{brand.Id}/org-nodes/{brand.Id}/config-assignments");
        Assert.Single(rows);
        Assert.False(rows[0].Archived);
    }
}
