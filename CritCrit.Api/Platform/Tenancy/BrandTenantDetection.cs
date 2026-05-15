using Wolverine.Http.Runtime.MultiTenancy;

namespace CritCrit.Api.Platform.Tenancy;

public sealed class BrandTenantDetection : ITenantDetection
{
    public ValueTask<string?> DetectTenant(HttpContext context)
    {
        var tenant = context.Items[BrandTenantContext.ItemKey] as BrandTenantContext;
        return ValueTask.FromResult(tenant?.TenantId.Value.ToString());
    }
}
