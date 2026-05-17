using System.Net;
using Alba;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Features.Config;
using CritCrit.Test.Fixtures;

namespace CritCrit.Test;

public sealed class ConfigSchemaLifecycleTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private static ConfigSchemaDefinition SimpleDefinition(string keyCode = "usetaxcalc") =>
        new("POS Bridge Settings",
            "Settings for the POS bridge",
            [
                new ConfigKeyDefinition(
                    keyCode,
                    "Use tax calc",
                    null,
                    ConfigValueType.Boolean,
                    null,
                    null,
                    new ConfigDefaultValue("true")),
            ]);

    [Fact]
    public async Task superadmin_creates_schema_with_initial_draft_no_published_version()
    {
        var code = "pos-bridge-" + Guid.NewGuid().ToString("N")[..8];
        var result = await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new CreateConfigSchemaRequest(code, "POS Bridge", null, "v1 draft", SimpleDefinition()), JsonStyle.MinimalApi)
                .ToUrl("/api/platform/config-schemas");
            _.StatusCodeShouldBe(HttpStatusCode.Created);
        });
        var raw = await result.ReadAsTextAsync();
        Assert.Contains("\"draftId\"", raw, StringComparison.OrdinalIgnoreCase);

        var schema = await GetAsSuperAdmin<ConfigSchemaResponse>($"/api/platform/config-schemas/{code}");
        Assert.Equal(code, schema.Code);
        Assert.Null(schema.LatestPublishedVersion);
        Assert.False(schema.Archived);
    }

    [Fact]
    public async Task duplicate_schema_code_is_rejected()
    {
        var code = "dup-" + Guid.NewGuid().ToString("N")[..8];
        await PostAsSuperAdmin<CreateConfigSchemaRequest, object>(
            "/api/platform/config-schemas",
            new CreateConfigSchemaRequest(code, "A", null, "d1", SimpleDefinition()),
            HttpStatusCode.Created);

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new CreateConfigSchemaRequest(code, "B", null, "d1", SimpleDefinition()), JsonStyle.MinimalApi)
                .ToUrl("/api/platform/config-schemas");
            _.StatusCodeShouldBe(HttpStatusCode.Conflict);
        });
    }

    [Fact]
    public async Task non_superadmin_cannot_create_schema()
    {
        await Host.Scenario(_ =>
        {
            AsUser(_, "regular", "regular@example.com");
            _.Post.Json(new CreateConfigSchemaRequest("nope", "Nope", null, "d1", SimpleDefinition()), JsonStyle.MinimalApi)
                .ToUrl("/api/platform/config-schemas");
            _.StatusCodeShouldBe(HttpStatusCode.Forbidden);
        });
    }

    [Fact]
    public async Task publishing_first_draft_creates_version_1_then_subsequent_drafts_publish_as_version_2()
    {
        var code = "publish-" + Guid.NewGuid().ToString("N")[..8];

        var createResult = await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new CreateConfigSchemaRequest(code, "Bridge", null, "v1", SimpleDefinition()), JsonStyle.MinimalApi)
                .ToUrl("/api/platform/config-schemas");
            _.StatusCodeShouldBe(HttpStatusCode.Created);
        });
        var firstDraftId = ExtractDraftId(await createResult.ReadAsTextAsync());

        // Publish v1.
        var v1 = await PostAsSuperAdmin<PublishConfigDraftRequest, ConfigSchemaVersionResponse>(
            $"/api/platform/config-schemas/{code}/drafts/{firstDraftId}/publish",
            new PublishConfigDraftRequest(ExpectedVersion: 1, Reason: null),
            HttpStatusCode.OK);
        Assert.Equal(1, v1.Version);

        // Create v2 draft based on v1 with an extra key.
        var v2Definition = SimpleDefinition() with
        {
            Keys = SimpleDefinition().Keys
                .Append(new ConfigKeyDefinition(
                    "menuname",
                    "Menu name",
                    null,
                    ConfigValueType.String,
                    new ConfigValueConstraints(MinLength: 1),
                    null,
                    new ConfigDefaultValue("\"main\"")))
                .ToArray(),
        };
        var draftResult = await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new CreateConfigDraftRequest("v2 draft", BaseVersion: 1, v2Definition), JsonStyle.MinimalApi)
                .ToUrl($"/api/platform/config-schemas/{code}/drafts");
            _.StatusCodeShouldBe(HttpStatusCode.Created);
        });
        var secondDraftId = ExtractDraftId(await draftResult.ReadAsTextAsync());

        // Publish v2.
        var v2 = await PostAsSuperAdmin<PublishConfigDraftRequest, ConfigSchemaVersionResponse>(
            $"/api/platform/config-schemas/{code}/drafts/{secondDraftId}/publish",
            new PublishConfigDraftRequest(ExpectedVersion: 1, Reason: "added menu name"),
            HttpStatusCode.OK);
        Assert.Equal(2, v2.Version);

        // Versions listing returns both, ordered ascending.
        var versions = await GetAsSuperAdmin<List<ConfigSchemaVersionResponse>>($"/api/platform/config-schemas/{code}/versions");
        Assert.Equal([1, 2], versions.Select(v => v.Version).ToArray());
    }

    [Fact]
    public async Task stale_base_version_cannot_publish()
    {
        var code = "stale-" + Guid.NewGuid().ToString("N")[..8];
        var createResult = await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new CreateConfigSchemaRequest(code, "S", null, "v1", SimpleDefinition()), JsonStyle.MinimalApi)
                .ToUrl("/api/platform/config-schemas");
            _.StatusCodeShouldBe(HttpStatusCode.Created);
        });
        var d1 = ExtractDraftId(await createResult.ReadAsTextAsync());

        // Publish v1.
        await PostAsSuperAdmin<PublishConfigDraftRequest, ConfigSchemaVersionResponse>(
            $"/api/platform/config-schemas/{code}/drafts/{d1}/publish",
            new PublishConfigDraftRequest(ExpectedVersion: 1, Reason: null),
            HttpStatusCode.OK);

        // Two parallel drafts, both based on v1.
        var draftAResult = await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new CreateConfigDraftRequest("draft A", BaseVersion: 1, SimpleDefinition("alpha")), JsonStyle.MinimalApi)
                .ToUrl($"/api/platform/config-schemas/{code}/drafts");
            _.StatusCodeShouldBe(HttpStatusCode.Created);
        });
        var draftA = ExtractDraftId(await draftAResult.ReadAsTextAsync());

        var draftBResult = await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new CreateConfigDraftRequest("draft B", BaseVersion: 1, SimpleDefinition("beta")), JsonStyle.MinimalApi)
                .ToUrl($"/api/platform/config-schemas/{code}/drafts");
            _.StatusCodeShouldBe(HttpStatusCode.Created);
        });
        var draftB = ExtractDraftId(await draftBResult.ReadAsTextAsync());

        // Publish A → version becomes 2. Latest published shifts to 2.
        await PostAsSuperAdmin<PublishConfigDraftRequest, ConfigSchemaVersionResponse>(
            $"/api/platform/config-schemas/{code}/drafts/{draftA}/publish",
            new PublishConfigDraftRequest(ExpectedVersion: 1, Reason: null),
            HttpStatusCode.OK);

        // Publishing B now must 409 — its BaseVersion (1) is no longer latest (2).
        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new PublishConfigDraftRequest(ExpectedVersion: 1, Reason: null), JsonStyle.MinimalApi)
                .ToUrl($"/api/platform/config-schemas/{code}/drafts/{draftB}/publish");
            _.StatusCodeShouldBe(HttpStatusCode.Conflict);
        });
    }

    [Fact]
    public async Task archived_schema_rejects_new_drafts()
    {
        var code = "arch-" + Guid.NewGuid().ToString("N")[..8];
        await PostAsSuperAdmin<CreateConfigSchemaRequest, object>(
            "/api/platform/config-schemas",
            new CreateConfigSchemaRequest(code, "A", null, "v1", SimpleDefinition()),
            HttpStatusCode.Created);

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new ArchiveConfigSchemaRequest("deprecated"), JsonStyle.MinimalApi)
                .ToUrl($"/api/platform/config-schemas/{code}/archive");
            _.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new CreateConfigDraftRequest("after archive", null, SimpleDefinition()), JsonStyle.MinimalApi)
                .ToUrl($"/api/platform/config-schemas/{code}/drafts");
            _.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }

    private static Guid ExtractDraftId(string json)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        // CreateSchema returns { schema, draftId } as anonymous object; CreateDraft returns { draftId }.
        if (doc.RootElement.TryGetProperty("draftId", out var direct)) return direct.GetGuid();
        return doc.RootElement.GetProperty("draftId").GetGuid();
    }
}
