using CritCrit.Api.Org.Domain;
using CritCrit.Api.Platform.Tenancy;
using Microsoft.AspNetCore.Http;

namespace CritCrit.UnitTests;

public sealed class BrandTenantDetectionTests
{
    [Fact]
    public async Task DetectTenant_returns_brand_tenant_from_http_context_items()
    {
        var tenantId = OrgNodeId.New();
        var context = new DefaultHttpContext();
        context.Items[BrandTenantContext.ItemKey] = new BrandTenantContext(tenantId, "br_test");

        var detected = await new BrandTenantDetection().DetectTenant(context);

        Assert.Equal(tenantId.Value.ToString(), detected);
    }

    [Fact]
    public async Task DetectTenant_returns_null_when_brand_tenant_is_missing()
    {
        var detected = await new BrandTenantDetection().DetectTenant(new DefaultHttpContext());

        Assert.Null(detected);
    }
}
