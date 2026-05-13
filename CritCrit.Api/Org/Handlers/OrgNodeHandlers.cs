using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Api.Org.Infrastructure;
using Marten;
using Wolverine.Http;

namespace CritCrit.Api.Org.Handlers;

public static class OrgNodeHandlers
{
    [WolverineGet("/api/brands/{brandId}/org-nodes/{nodeId}")]
    public static async Task<IResult> GetNode(
        string nodeId,
        IDocumentStore store,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenant = HandlerContext.GetTenant(httpContext);
        await using var session = HandlerContext.TenantSession(store, tenant);

        if (!OrgPublicId.TryParseOrgNode(nodeId, out var id, out _))
            return Results.NotFound();

        var node = await session.LoadAsync<OrgNodeReadModel>(id.Value, ct);
        if (node is null || node.TenantId != tenant.TenantId.Value)
            return Results.NotFound();

        return Results.Ok(BrandHandlers.ToResponse(node));
    }

    [WolverinePost("/api/brands/{brandId}/countries")]
    public static async Task<OrgNodeResponse> CreateCountry(
        CreatePlainOrgNodeRequest request,
        IDocumentStore store,
        OrgCommandService commands,
        OrgAuthorizationService authorization,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenant = HandlerContext.GetTenant(httpContext);
        await using var session = HandlerContext.TenantSession(store, tenant);
        var actor = httpContext.GetActor();

        if (!OrgPublicId.TryParseOrgNode(request.ParentId, out var parentId, out _))
            throw new DomainException("Invalid parent ID.");

        var parent = await session.LoadAsync<OrgNodeReadModel>(parentId.Value, ct)
            ?? throw new DomainException("Parent org node not found.");
        await authorization.EnforceRoleAsync(session, actor, parent, OrgRole.Admin, ct);

        var node = await commands.CreatePlainNodeAsync(session, actor, tenant.TenantId, parentId,
            OrgNodeType.Country, request.Code, request.Name, ct);

        return BrandHandlers.ToResponse(node);
    }

    [WolverinePost("/api/brands/{brandId}/franchises")]
    public static async Task<OrgNodeResponse> CreateFranchise(
        CreatePlainOrgNodeRequest request,
        IDocumentStore store,
        OrgCommandService commands,
        OrgAuthorizationService authorization,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenant = HandlerContext.GetTenant(httpContext);
        await using var session = HandlerContext.TenantSession(store, tenant);
        var actor = httpContext.GetActor();

        if (!OrgPublicId.TryParseOrgNode(request.ParentId, out var parentId, out _))
            throw new DomainException("Invalid parent ID.");

        var parent = await session.LoadAsync<OrgNodeReadModel>(parentId.Value, ct)
            ?? throw new DomainException("Parent org node not found.");
        await authorization.EnforceRoleAsync(session, actor, parent, OrgRole.Admin, ct);

        var node = await commands.CreatePlainNodeAsync(session, actor, tenant.TenantId, parentId,
            OrgNodeType.Franchise, request.Code, request.Name, ct);

        return BrandHandlers.ToResponse(node);
    }

    [WolverinePost("/api/brands/{brandId}/stores")]
    public static async Task<OrgNodeResponse> CreateStore(
        CreateStoreRequest request,
        IDocumentStore store,
        OrgCommandService commands,
        OrgAuthorizationService authorization,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenant = HandlerContext.GetTenant(httpContext);
        await using var session = HandlerContext.TenantSession(store, tenant);
        var actor = httpContext.GetActor();

        if (!OrgPublicId.TryParseOrgNode(request.ParentId, out var parentId, out _))
            throw new DomainException("Invalid parent ID.");

        var parent = await session.LoadAsync<OrgNodeReadModel>(parentId.Value, ct)
            ?? throw new DomainException("Parent org node not found.");
        await authorization.EnforceRoleAsync(session, actor, parent, OrgRole.Admin, ct);

        var node = await commands.CreateStoreAsync(session, actor, tenant.TenantId, parentId,
            request.Code, request.Name, request.TimeZone ?? "UTC", ct);

        return BrandHandlers.ToResponse(node);
    }

    [WolverinePost("/api/brands/{brandId}/devices")]
    public static async Task<OrgNodeResponse> CreateDevice(
        CreateDeviceRequest request,
        IDocumentStore store,
        OrgCommandService commands,
        OrgAuthorizationService authorization,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenant = HandlerContext.GetTenant(httpContext);
        await using var session = HandlerContext.TenantSession(store, tenant);
        var actor = httpContext.GetActor();

        if (!OrgPublicId.TryParseOrgNode(request.ParentStoreId, OrgNodeType.Store, out var parentStoreId))
            throw new DomainException("Invalid parent store ID.");

        var parent = await session.LoadAsync<OrgNodeReadModel>(parentStoreId.Value, ct)
            ?? throw new DomainException("Parent store not found.");
        await authorization.EnforceRoleAsync(session, actor, parent, OrgRole.Admin, ct);

        var node = await commands.CreateDeviceAsync(session, actor, tenant.TenantId, parentStoreId,
            request.SerialNumber, request.Name, request.DeviceType, ct);

        return BrandHandlers.ToResponse(node);
    }
}
