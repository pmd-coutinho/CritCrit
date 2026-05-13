using CritCrit.Api.Org.Domain;
using Marten;

namespace CritCrit.Api.Org.Infrastructure;

public sealed class BrandTenantMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IDocumentStore store)
    {
        if (TryFindBrandId(context.Request.Path, out var brandId))
        {
            if (!OrgPublicId.TryParseOrgNode(brandId, OrgNodeType.Brand, out var tenantId))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            await using var session = store.QuerySession(tenantId.Value.ToString());
            var root = await session.LoadAsync<OrgNodeReadModel>(tenantId.Value, context.RequestAborted);
            if (root is null || root.Type != OrgNodeType.Brand || root.TenantId != tenantId.Value || root.HardDeleted)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (root.EffectiveArchived && !IsAllowedForArchivedBrand(context))
            {
                context.Response.StatusCode = StatusCodes.Status410Gone;
                return;
            }

            context.Items[BrandTenantContext.ItemKey] = new BrandTenantContext(tenantId, brandId);
        }

        await next(context);
    }

    private static bool IsAllowedForArchivedBrand(HttpContext context) =>
        context.Request.Path.Value?.Contains("/restore", StringComparison.OrdinalIgnoreCase) == true ||
        context.Request.Path.Value?.Contains("/audit", StringComparison.OrdinalIgnoreCase) == true;

    private static bool TryFindBrandId(PathString path, out string brandId)
    {
        brandId = "";
        var parts = path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (!string.Equals(parts[i], "brands", StringComparison.OrdinalIgnoreCase))
                continue;

            brandId = parts[i + 1];
            return true;
        }

        return false;
    }
}

public sealed record BrandTenantContext(OrgNodeId TenantId, string BrandPublicId)
{
    public const string ItemKey = "BrandTenantContext";
}
