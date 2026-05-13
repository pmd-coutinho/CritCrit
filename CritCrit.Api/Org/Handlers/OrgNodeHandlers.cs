using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Api.Org.Infrastructure;
using Marten;
using Wolverine.Http;

namespace CritCrit.Api.Org.Handlers;

public static class OrgNodeHandlers
{
    [WolverinePost("/api/brands/{brandId}/countries")]
    public static async Task<OrgNodeResponse> CreateCountry(
        CreatePlainOrgNodeRequest request,
        string brandId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenant = HandlerContext.GetTenant(httpContext);
        await using var session = HandlerContext.TenantSession(store, tenant);
        var actor = await HandlerContext.ResolveActorAsync(httpContext, store, ct);

        if (!OrgPublicId.TryParseOrgNode(request.ParentId, out var parentId, out _))
            throw new DomainException("Invalid parent ID.");

        var parent = await ValidateParent(session, actor, authorization, tenant.TenantId.Value, parentId, OrgNodeType.Country, ct);

        var normalized = OrgValidation.ValidateAndNormalizeCode(OrgNodeType.Country, request.Code);
        await OrgValidation.EnsureCodeAvailableAsync(session, tenant.TenantId, normalized, ct);

        var id = OrgNodeId.New();
        session.Events.StartStream<OrgNodeReadModel>(id.Value,
            new OrgNodeCreated(id, tenant.TenantId, parentId, OrgNodeType.Country, request.Code.Trim(), normalized, request.Name.Trim()));

        await session.SaveChangesAsync(ct);
        var node = await session.LoadAsync<OrgNodeReadModel>(id.Value, ct)
            ?? throw new InvalidOperationException("Projection failed to create OrgNodeReadModel.");
        return BrandHandlers.ToResponse(node);
    }

    [WolverinePost("/api/brands/{brandId}/franchises")]
    public static async Task<OrgNodeResponse> CreateFranchise(
        CreatePlainOrgNodeRequest request,
        string brandId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenant = HandlerContext.GetTenant(httpContext);
        await using var session = HandlerContext.TenantSession(store, tenant);
        var actor = await HandlerContext.ResolveActorAsync(httpContext, store, ct);

        if (!OrgPublicId.TryParseOrgNode(request.ParentId, out var parentId, out _))
            throw new DomainException("Invalid parent ID.");

        var parent = await ValidateParent(session, actor, authorization, tenant.TenantId.Value, parentId, OrgNodeType.Franchise, ct);

        var normalized = OrgValidation.ValidateAndNormalizeCode(OrgNodeType.Franchise, request.Code);
        await OrgValidation.EnsureCodeAvailableAsync(session, tenant.TenantId, normalized, ct);

        var id = OrgNodeId.New();
        session.Events.StartStream<OrgNodeReadModel>(id.Value,
            new OrgNodeCreated(id, tenant.TenantId, parentId, OrgNodeType.Franchise, request.Code.Trim(), normalized, request.Name.Trim()));

        await session.SaveChangesAsync(ct);
        var node = await session.LoadAsync<OrgNodeReadModel>(id.Value, ct)
            ?? throw new InvalidOperationException("Projection failed to create OrgNodeReadModel.");
        return BrandHandlers.ToResponse(node);
    }

    [WolverinePost("/api/brands/{brandId}/stores")]
    public static async Task<OrgNodeResponse> CreateStore(
        CreateStoreRequest request,
        string brandId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenant = HandlerContext.GetTenant(httpContext);
        await using var session = HandlerContext.TenantSession(store, tenant);
        var actor = await HandlerContext.ResolveActorAsync(httpContext, store, ct);

        if (!OrgPublicId.TryParseOrgNode(request.ParentId, out var parentId, out _))
            throw new DomainException("Invalid parent ID.");

        var parent = await ValidateParent(session, actor, authorization, tenant.TenantId.Value, parentId, OrgNodeType.Store, ct);

        var normalized = OrgValidation.ValidateAndNormalizeCode(OrgNodeType.Store, request.Code);
        await OrgValidation.EnsureCodeAvailableAsync(session, tenant.TenantId, normalized, ct);

        var id = OrgNodeId.New();
        var tz = string.IsNullOrWhiteSpace(request.TimeZone) ? "UTC" : request.TimeZone.Trim();
        session.Events.StartStream<OrgNodeReadModel>(id.Value,
            new OrgNodeCreated(id, tenant.TenantId, parentId, OrgNodeType.Store, request.Code.Trim(), normalized, request.Name.Trim()));
        session.Events.Append(id.Value,
            new StoreProfileCreated(id, tz));

        await session.SaveChangesAsync(ct);
        var node = await session.LoadAsync<OrgNodeReadModel>(id.Value, ct)
            ?? throw new InvalidOperationException("Projection failed to create OrgNodeReadModel.");
        return BrandHandlers.ToResponse(node);
    }

    [WolverinePost("/api/brands/{brandId}/devices")]
    public static async Task<OrgNodeResponse> CreateDevice(
        CreateDeviceRequest request,
        string brandId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenant = HandlerContext.GetTenant(httpContext);
        await using var session = HandlerContext.TenantSession(store, tenant);
        var actor = await HandlerContext.ResolveActorAsync(httpContext, store, ct);

        if (!OrgPublicId.TryParseOrgNode(request.ParentStoreId, OrgNodeType.Store, out var parentStoreId))
            throw new DomainException("Invalid parent store ID.");

        var parent = await ValidateParent(session, actor, authorization, tenant.TenantId.Value, parentStoreId, OrgNodeType.Device, ct);

        var normalized = OrgValidation.ValidateAndNormalizeCode(OrgNodeType.Device, request.SerialNumber);
        await OrgValidation.EnsureCodeAvailableAsync(session, tenant.TenantId, normalized, ct);

        var id = OrgNodeId.New();
        session.Events.StartStream<OrgNodeReadModel>(id.Value,
            new OrgNodeCreated(id, tenant.TenantId, parentStoreId, OrgNodeType.Device, request.SerialNumber.Trim(), normalized, request.Name.Trim()));
        session.Events.Append(id.Value,
            new DeviceProfileCreated(id, request.SerialNumber.Trim(), request.DeviceType));

        await session.SaveChangesAsync(ct);
        var node = await session.LoadAsync<OrgNodeReadModel>(id.Value, ct)
            ?? throw new InvalidOperationException("Projection failed to create OrgNodeReadModel.");
        return BrandHandlers.ToResponse(node);
    }

    private static async Task<OrgNodeReadModel> ValidateParent(
        IDocumentSession session,
        ActorContext actor,
        OrgAuthorizationService authorization,
        Guid tenantId,
        OrgNodeId parentId,
        OrgNodeType childType,
        CancellationToken ct)
    {
        var parent = await OrgValidation.LoadActiveNodeAsync(session, parentId, ct);

        if (parent.TenantId != tenantId)
            throw new DomainException("Org node does not belong to the requested brand tenant.");

        if (!OrgRules.CanContain(parent.Type, childType))
            throw new DomainException($"{parent.Type} cannot contain {childType}.");

        await authorization.EnforceRoleAsync(session, actor, parent, OrgRole.Admin, ct);
        return parent;
    }
}
