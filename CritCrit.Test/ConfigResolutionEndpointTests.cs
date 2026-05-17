using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Alba;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Api.Org.Features.Config;
using CritCrit.Test.Fixtures;

namespace CritCrit.Test;

public sealed class ConfigResolutionEndpointTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    /// <summary>Helper shape covering common test wiring across resolution scenarios.</summary>
    private sealed record Scenario(OrgNodeResponse Brand, OrgNodeResponse Country, OrgNodeResponse Store, string SchemaCode);

    private static ConfigSchemaDefinition Definition() =>
        new("POS Bridge", null,
        [
            new ConfigKeyDefinition("usetaxcalc", "Use tax calc", null, ConfigValueType.Boolean, null, null, new ConfigDefaultValue("false")),
            new ConfigKeyDefinition("menuname", "Menu name", null, ConfigValueType.String,
                new ConfigValueConstraints(MinLength: 1, MaxLength: 32), null, new ConfigDefaultValue("\"main\"")),
            new ConfigKeyDefinition("api-secret", "API secret", null, ConfigValueType.EncryptedString, null, null, null),
        ]);

    private async Task<Scenario> SetupAsync(string suffix)
    {
        var brand = await CreateBrand(Code("res-" + suffix));
        var country = await PostAsSuperAdmin<CreatePlainOrgNodeRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/countries",
            new CreatePlainOrgNodeRequest(brand.Id, "us", "United States"));
        var store = await PostAsSuperAdmin<CreateStoreRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/stores",
            new CreateStoreRequest(country.Id, "st-001", "Downtown", "UTC"));

        var code = "res-" + suffix;
        var createResult = await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new CreateConfigSchemaRequest(code, "Bridge", null, "v1", Definition()), JsonStyle.MinimalApi)
                .ToUrl("/api/platform/config-schemas");
            _.StatusCodeShouldBe(HttpStatusCode.Created);
        });
        using var doc = JsonDocument.Parse(await createResult.ReadAsTextAsync());
        var draftId = doc.RootElement.GetProperty("draftId").GetGuid();

        await PostAsSuperAdmin<PublishConfigDraftRequest, ConfigSchemaVersionResponse>(
            $"/api/platform/config-schemas/{code}/drafts/{draftId}/publish",
            new PublishConfigDraftRequest(1, null),
            HttpStatusCode.OK);

        await PostAsSuperAdmin<AssignConfigSchemaRequest, ConfigAssignmentResponse>(
            $"/api/brands/{brand.Id}/org-nodes/{brand.Id}/config-assignments",
            new AssignConfigSchemaRequest(code, 1, null),
            HttpStatusCode.Created);

        return new Scenario(brand, country, store, code);
    }

    [Fact]
    public async Task full_object_lookup_returns_defaults_when_no_local_values()
    {
        var s = await SetupAsync("def");
        var json = await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Get.Url($"/api/brands/{s.Brand.Id}/org-nodes/{s.Store.Id}/config/{s.SchemaCode}");
            _.StatusCodeShouldBe(HttpStatusCode.OK);
        });
        var body = await json.ReadAsTextAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.GetProperty("usetaxcalc").GetBoolean() == false);
        Assert.Equal("main", doc.RootElement.GetProperty("menuname").GetString());
        Assert.False(doc.RootElement.TryGetProperty("api-secret", out _)); // encrypted omitted
    }

    [Fact]
    public async Task nearest_set_overrides_ancestor_and_default()
    {
        var s = await SetupAsync("near");

        // Country-level set
        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Patch.Json(new PatchConfigValuesRequest(
                ExpectedVersion: 0,
                Operations: [new ConfigValuePatchOperationInput("menuname", ConfigValuePatchOperationKind.Set, "\"country-menu\"")],
                Reason: null), JsonStyle.MinimalApi)
                .ToUrl($"/api/brands/{s.Brand.Id}/org-nodes/{s.Country.Id}/config/{s.SchemaCode}");
            _.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });

        // Store-level set (more local)
        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Patch.Json(new PatchConfigValuesRequest(
                ExpectedVersion: 0,
                Operations: [new ConfigValuePatchOperationInput("menuname", ConfigValuePatchOperationKind.Set, "\"store-menu\"")],
                Reason: null), JsonStyle.MinimalApi)
                .ToUrl($"/api/brands/{s.Brand.Id}/org-nodes/{s.Store.Id}/config/{s.SchemaCode}");
            _.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });

        var single = await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Get.Url($"/api/brands/{s.Brand.Id}/org-nodes/{s.Store.Id}/config/{s.SchemaCode}.menuname");
            _.StatusCodeShouldBe(HttpStatusCode.OK);
        });
        Assert.Equal("\"store-menu\"", (await single.ReadAsTextAsync()).Trim());
    }

    [Fact]
    public async Task unset_at_descendant_suppresses_inherited_value_and_default()
    {
        var s = await SetupAsync("unset");

        // Brand-level set
        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Patch.Json(new PatchConfigValuesRequest(
                ExpectedVersion: 0,
                Operations: [new ConfigValuePatchOperationInput("menuname", ConfigValuePatchOperationKind.Set, "\"brand-menu\"")],
                Reason: null), JsonStyle.MinimalApi)
                .ToUrl($"/api/brands/{s.Brand.Id}/org-nodes/{s.Brand.Id}/config/{s.SchemaCode}");
            _.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });

        // Store-level unset
        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Patch.Json(new PatchConfigValuesRequest(
                ExpectedVersion: 0,
                Operations: [new ConfigValuePatchOperationInput("menuname", ConfigValuePatchOperationKind.Unset, null)],
                Reason: null), JsonStyle.MinimalApi)
                .ToUrl($"/api/brands/{s.Brand.Id}/org-nodes/{s.Store.Id}/config/{s.SchemaCode}");
            _.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });

        // Single-key pure lookup returns 404 (unset suppresses value).
        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Get.Url($"/api/brands/{s.Brand.Id}/org-nodes/{s.Store.Id}/config/{s.SchemaCode}.menuname");
            _.StatusCodeShouldBe(HttpStatusCode.NotFound);
        });
    }

    [Fact]
    public async Task encrypted_value_is_stored_as_ciphertext_and_not_returned_in_pure_lookup()
    {
        var s = await SetupAsync("enc");

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Patch.Json(new PatchConfigValuesRequest(
                ExpectedVersion: 0,
                Operations: [new ConfigValuePatchOperationInput("api-secret", ConfigValuePatchOperationKind.Set, "\"super-secret\"")],
                Reason: null), JsonStyle.MinimalApi)
                .ToUrl($"/api/brands/{s.Brand.Id}/org-nodes/{s.Brand.Id}/config/{s.SchemaCode}");
            _.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });

        // Full object pure lookup omits encrypted key.
        var full = await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Get.Url($"/api/brands/{s.Brand.Id}/org-nodes/{s.Store.Id}/config/{s.SchemaCode}");
            _.StatusCodeShouldBe(HttpStatusCode.OK);
        });
        using var fullDoc = JsonDocument.Parse(await full.ReadAsTextAsync());
        Assert.False(fullDoc.RootElement.TryGetProperty("api-secret", out _));

        // Single encrypted-key pure lookup → 404.
        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Get.Url($"/api/brands/{s.Brand.Id}/org-nodes/{s.Store.Id}/config/{s.SchemaCode}.api-secret");
            _.StatusCodeShouldBe(HttpStatusCode.NotFound);
        });

        // Metadata lookup exposes presence + masked value.
        var meta = await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Get.Url($"/api/brands/{s.Brand.Id}/org-nodes/{s.Store.Id}/config/{s.SchemaCode}?includeMetadata=true");
            _.StatusCodeShouldBe(HttpStatusCode.OK);
        });
        using var metaDoc = JsonDocument.Parse(await meta.ReadAsTextAsync());
        var secret = metaDoc.RootElement.GetProperty("values").GetProperty("api-secret");
        Assert.True(secret.GetProperty("encrypted").GetBoolean());
        Assert.True(secret.GetProperty("hasValue").GetBoolean());
        Assert.Equal("********", secret.GetProperty("maskedValue").GetString());
    }

    [Fact]
    public async Task unassigned_schema_returns_404()
    {
        var brand = await CreateBrand(Code("noassign"));
        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Get.Url($"/api/brands/{brand.Id}/org-nodes/{brand.Id}/config/nonexistent-schema");
            _.StatusCodeShouldBe(HttpStatusCode.NotFound);
        });
    }
}
