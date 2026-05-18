using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Alba;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Api.Org.Features.Assets;
using CritCrit.Test.Fixtures;

namespace CritCrit.Test;

public sealed class AssetEndpointTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private sealed record Scenario(OrgNodeResponse Brand, OrgNodeResponse Country, OrgNodeResponse Store);

    private async Task<Scenario> SetupAsync(string suffix)
    {
        var brand = await CreateBrand(Code("asset-" + suffix));
        var country = await PostAsSuperAdmin<CreatePlainOrgNodeRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/countries",
            new CreatePlainOrgNodeRequest(brand.Id, "us", "United States"));
        var store = await PostAsSuperAdmin<CreateStoreRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/stores",
            new CreateStoreRequest(country.Id, "st-001", "Downtown", "UTC"));
        return new Scenario(brand, country, store);
    }

    [Fact]
    public async Task upload_resolve_and_stream_inherited_asset()
    {
        var s = await SetupAsync("inherit");
        await UploadAsSuperAdmin(
            s.Brand.Id,
            s.Brand.Id,
            "kiosk.background-video",
            "loop.mp4",
            "video/mp4",
            "brand-video",
            expectedVersion: 0);

        var asset = await GetAsSuperAdmin<AssetLookupResponse>(
            $"/api/brands/{s.Brand.Id}/org-nodes/{s.Store.Id}/assets/kiosk.background-video");

        Assert.Equal("kiosk.background-video", asset.Key);
        Assert.Equal("kiosk", asset.Group);
        Assert.Equal("set", asset.State);
        Assert.Equal("Inherited", asset.Source);
        Assert.Equal("loop.mp4", asset.File?.FileName);
        Assert.NotNull(asset.ContentUrl);

        var content = await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Get.Url(asset.ContentUrl!);
            _.StatusCodeShouldBe(HttpStatusCode.OK);
            _.ContentShouldBe("brand-video");
        });
        Assert.Equal("video/mp4", content.Context.Response.ContentType);
    }

    [Fact]
    public async Task unset_blocks_inherited_asset_and_inherit_restores_it()
    {
        var s = await SetupAsync("unset");
        await UploadAsSuperAdmin(
            s.Brand.Id,
            s.Brand.Id,
            "kiosk.background-video",
            "loop.mp4",
            "video/mp4",
            "brand-video",
            expectedVersion: 0);

        await PatchAssetAsSuperAdmin(
            $"/api/brands/{s.Brand.Id}/org-nodes/{s.Store.Id}/assets/kiosk.background-video/unset",
            new PatchAssetRequest(0, null));

        var unset = await GetAsSuperAdmin<AssetLookupResponse>(
            $"/api/brands/{s.Brand.Id}/org-nodes/{s.Store.Id}/assets/kiosk.background-video");
        Assert.Equal("unset", unset.State);
        Assert.Null(unset.File);
        Assert.Null(unset.ContentUrl);

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Get.Url($"/api/brands/{s.Brand.Id}/org-nodes/{s.Store.Id}/assets/kiosk.background-video/content");
            _.StatusCodeShouldBe(HttpStatusCode.NotFound);
        });

        await PatchAssetAsSuperAdmin(
            $"/api/brands/{s.Brand.Id}/org-nodes/{s.Store.Id}/assets/kiosk.background-video/inherit",
            new PatchAssetRequest(unset.ValueSetVersion, null));

        var restored = await GetAsSuperAdmin<AssetLookupResponse>(
            $"/api/brands/{s.Brand.Id}/org-nodes/{s.Store.Id}/assets/kiosk.background-video");
        Assert.Equal("set", restored.State);
        Assert.Equal("Inherited", restored.Source);
    }

    [Fact]
    public async Task replacement_uses_latest_immutable_blob()
    {
        var s = await SetupAsync("replace");
        await UploadAsSuperAdmin(s.Brand.Id, s.Brand.Id, "menu.board", "menu.md", "text/markdown", "# old", 0);

        var first = await GetAsSuperAdmin<AssetLookupResponse>(
            $"/api/brands/{s.Brand.Id}/org-nodes/{s.Brand.Id}/assets/menu.board");

        await UploadAsSuperAdmin(s.Brand.Id, s.Brand.Id, "menu.board", "menu.md", "text/markdown", "# new", first.ValueSetVersion);

        var second = await GetAsSuperAdmin<AssetLookupResponse>(
            $"/api/brands/{s.Brand.Id}/org-nodes/{s.Brand.Id}/assets/menu.board");
        Assert.NotEqual(first.File?.Sha256, second.File?.Sha256);

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Get.Url(second.ContentUrl!);
            _.StatusCodeShouldBe(HttpStatusCode.OK);
            _.ContentShouldBe("# new");
        });
    }

    private async Task UploadAsSuperAdmin(
        string brandId,
        string nodeId,
        string key,
        string fileName,
        string contentType,
        string content,
        long expectedVersion)
    {
        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(expectedVersion.ToString()), "expectedVersion");
            form.Add(new StringContent(""), "reason");
            var file = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(content));
            file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(file, "file", fileName);
            _.Put.MultipartFormData(form).ToUrl($"/api/brands/{brandId}/org-nodes/{nodeId}/assets/{key}");
            _.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });
    }

    private async Task PatchAssetAsSuperAdmin(string url, PatchAssetRequest request)
    {
        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(request, JsonStyle.MinimalApi).ToUrl(url);
            _.StatusCodeShouldBe(HttpStatusCode.NoContent);
        });
    }
}
