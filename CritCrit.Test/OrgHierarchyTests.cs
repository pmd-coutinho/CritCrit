using System.Net;
using Alba;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Test.Fixtures;

namespace CritCrit.Test;

public class OrgHierarchyTests(ApiFixture fixture) : ContractTestWithAlba(fixture)
{
    [Fact]
    public async Task superadmin_can_create_brand_store_and_device()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Burger Palace"));

        Assert.Equal(OrgNodeType.Brand, brand.Type);
        Assert.StartsWith("brand_", brand.Id, StringComparison.Ordinal);

        var store = await PostAsSuperAdmin<CreateStoreRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/stores",
            new CreateStoreRequest(brand.Id, Code("store"), "Main Street", "Europe/Lisbon"));

        Assert.Equal(OrgNodeType.Store, store.Type);
        Assert.Equal(brand.Id, store.ParentId);
        Assert.Contains("/store/", store.Path, StringComparison.Ordinal);

        var device = await PostAsSuperAdmin<CreateDeviceRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/devices",
            new CreateDeviceRequest(store.Id, "SN-AbC-123", "Front Kiosk", DeviceType.Kiosk));

        Assert.Equal(OrgNodeType.Device, device.Type);
        Assert.Equal("SN-AbC-123", device.Code);
        Assert.Equal(store.Id, device.ParentId);
    }

    [Fact]
    public async Task hierarchy_rules_reject_invalid_children()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Brand"));

        var store = await PostAsSuperAdmin<CreateStoreRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/stores",
            new CreateStoreRequest(brand.Id, Code("store"), "Store", "UTC"));

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new CreateStoreRequest(store.Id, Code("store"), "Nested Store", "UTC"), JsonStyle.MinimalApi).ToUrl($"/api/brands/{brand.Id}/stores");
            _.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }

    [Fact]
    public async Task grants_reject_redundant_descendant_access()
    {
        var brand = await PostAsSuperAdmin<CreateBrandRequest, OrgNodeResponse>(
            "/api/platform/brands",
            new CreateBrandRequest(Code(), "Brand"));

        var store = await PostAsSuperAdmin<CreateStoreRequest, OrgNodeResponse>(
            $"/api/brands/{brand.Id}/stores",
            new CreateStoreRequest(brand.Id, Code("store"), "Store", "UTC"));

        var subject = await PostAsSuperAdmin<CreateSubjectRequest, SubjectResponse>(
            "/api/platform/subjects",
            new CreateSubjectRequest("critic@example.com", "Critic", "test", "default", "critic-idp"));

        await PostAsSuperAdmin<GrantRoleRequest, GrantResponse>(
            $"/api/brands/{brand.Id}/access-grants",
            new GrantRoleRequest(brand.Id, subject.Id, OrgRole.Admin, null));

        await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(new GrantRoleRequest(store.Id, subject.Id, OrgRole.Member, null), JsonStyle.MinimalApi).ToUrl($"/api/brands/{brand.Id}/access-grants");
            _.StatusCodeShouldBe(HttpStatusCode.BadRequest);
        });
    }

    private async Task<TResponse> PostAsSuperAdmin<TRequest, TResponse>(string url, TRequest request)
    {
        var result = await Host.Scenario(_ =>
        {
            AsSuperAdmin(_);
            _.Post.Json(request!, JsonStyle.MinimalApi).ToUrl(url);
        });
        if (result.Context.Response.StatusCode != 200)
        {
            using var reader = new StreamReader(result.Context.Response.Body);
            var body = await reader.ReadToEndAsync();
            throw new Xunit.Sdk.XunitException($"Expected 200 but got {result.Context.Response.StatusCode}: {body}");
        }

        return (await result.ReadAsJsonAsync<TResponse>())!;
    }

    private static void AsSuperAdmin(Scenario scenario)
    {
        scenario.WithRequestHeader("X-Test-User", "superadmin-idp");
        scenario.WithRequestHeader("X-Test-Email", "superadmin@example.com");
        scenario.WithRequestHeader("X-Test-Roles", "critcrit.superadmin");
    }

    private static string Code(string prefix = "brand") => $"{prefix}-{Guid.NewGuid():N}"[..32];
}
