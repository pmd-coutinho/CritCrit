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

    // ── Lifecycle ──

    [WolverinePost("/api/brands/{brandId}/org-nodes/{nodeId}/archive")]
    public static async Task<OrgNodeResponse> ArchiveOrgNode(
        string nodeId,
        ArchiveOrgNodeRequest request,
        string brandId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenant = HandlerContext.GetTenant(httpContext);
        await using var session = HandlerContext.TenantSession(store, tenant);
        var actor = await HandlerContext.ResolveActorAsync(httpContext, store, ct);

        if (!OrgPublicId.TryParseOrgNode(nodeId, out var id, out _))
            throw new DomainException("Invalid org node ID.");

        var target = await OrgValidation.LoadNodeAsync(session, id, ct);
        if (target.TenantId != tenant.TenantId.Value)
            throw new DomainException("Org node does not belong to the requested brand tenant.");
        if (target.HardDeleted)
            throw new DomainException("Cannot archive a hard-deleted org node.");
        if (target.Archived)
            throw new DomainException("Org node is already archived.");

        await authorization.EnforceRoleAsync(session, actor, target, OrgRole.Admin, ct);

        if (!request.Force)
        {
            var hasChildren = await OrgValidation.HasActiveChildrenAsync(session, id, tenant.TenantId.Value, ct);
            if (hasChildren)
                throw new DomainException("Org node has active children. Use force=true to cascade archive.");
        }

        session.Events.Append(id.Value, new OrgNodeArchived(id, request.Force, request.Reason));

        if (request.Force)
        {
            var descendants = await OrgValidation.LoadDescendantsAsync(session, id, tenant.TenantId.Value, ct);
            foreach (var descendant in descendants)
            {
                descendant.EffectiveArchived = true;
                session.Store(descendant);
            }
        }

        await session.SaveChangesAsync(ct);
        var archived = await session.LoadAsync<OrgNodeReadModel>(id.Value, ct)!;
        return BrandHandlers.ToResponse(archived);
    }

    [WolverinePost("/api/brands/{brandId}/org-nodes/{nodeId}/restore")]
    public static async Task<OrgNodeResponse> RestoreOrgNode(
        string nodeId,
        string brandId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenant = HandlerContext.GetTenant(httpContext);
        await using var session = HandlerContext.TenantSession(store, tenant);
        var actor = await HandlerContext.ResolveActorAsync(httpContext, store, ct);

        if (!OrgPublicId.TryParseOrgNode(nodeId, out var id, out _))
            throw new DomainException("Invalid org node ID.");

        var target = await OrgValidation.LoadNodeAsync(session, id, ct);
        if (target.TenantId != tenant.TenantId.Value)
            throw new DomainException("Org node does not belong to the requested brand tenant.");
        if (target.HardDeleted)
            throw new DomainException("Cannot restore a hard-deleted org node.");
        if (!target.Archived)
            throw new DomainException("Org node is not archived.");

        await authorization.EnforceRoleAsync(session, actor, target, OrgRole.Admin, ct);

        session.Events.Append(id.Value, new OrgNodeRestored(id));

        await session.SaveChangesAsync(ct);
        var restored = await session.LoadAsync<OrgNodeReadModel>(id.Value, ct)!;
        return BrandHandlers.ToResponse(restored);
    }

    [WolverinePost("/api/brands/{brandId}/org-nodes/{nodeId}/hard-delete")]
    [EmptyResponse]
    public static async Task HardDeleteOrgNode(
        string nodeId,
        HardDeleteOrgNodeRequest request,
        string brandId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenant = HandlerContext.GetTenant(httpContext);
        await using var session = HandlerContext.TenantSession(store, tenant);
        var actor = await HandlerContext.ResolveActorAsync(httpContext, store, ct);

        if (!OrgPublicId.TryParseOrgNode(nodeId, out var id, out _))
            throw new DomainException("Invalid org node ID.");

        var target = await OrgValidation.LoadNodeAsync(session, id, ct);
        if (target.TenantId != tenant.TenantId.Value)
            throw new DomainException("Org node does not belong to the requested brand tenant.");
        if (target.HardDeleted)
            throw new DomainException("Org node is already hard-deleted.");

        authorization.EnforceSuperAdmin(actor);

        var descendants = await OrgValidation.LoadDescendantsAsync(session, id, tenant.TenantId.Value, ct);
        session.Events.Append(id.Value, new OrgNodeHardDeleted(id, request.Reason));
        foreach (var descendant in descendants)
            session.Events.Append(descendant.Id, new OrgNodeHardDeleted(new OrgNodeId(descendant.Id), request.Reason));

        if (target.Type == OrgNodeType.Brand)
        {
            session.Store(new BrandTombstone
            {
                Id = target.Id,
                PublicId = target.PublicId,
                Code = target.Code,
                Name = target.Name,
                DeletedAt = TimeProvider.System.GetUtcNow()
            });
        }

        await session.SaveChangesAsync(ct);
    }

    [WolverinePost("/api/brands/{brandId}/org-nodes/{nodeId}/move")]
    public static async Task<OrgNodeResponse> MoveOrgNode(
        string nodeId,
        MoveOrgNodeRequest request,
        string brandId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tenant = HandlerContext.GetTenant(httpContext);
        await using var session = HandlerContext.TenantSession(store, tenant);
        var actor = await HandlerContext.ResolveActorAsync(httpContext, store, ct);

        if (!OrgPublicId.TryParseOrgNode(nodeId, out var id, out _))
            throw new DomainException("Invalid org node ID.");
        if (!OrgPublicId.TryParseOrgNode(request.NewParentId, out var newParentId, out _))
            throw new DomainException("Invalid new parent ID.");

        var target = await OrgValidation.LoadNodeAsync(session, id, ct);
        if (target.TenantId != tenant.TenantId.Value)
            throw new DomainException("Org node does not belong to the requested brand tenant.");
        if (target.HardDeleted || target.EffectiveArchived)
            throw new DomainException("Cannot move an inactive org node.");
        if (target.ParentId == newParentId.Value)
            throw new DomainException("Org node is already under the specified parent.");

        var newParent = await OrgValidation.LoadActiveNodeAsync(session, newParentId, ct);
        if (newParent.TenantId != tenant.TenantId.Value)
            throw new DomainException("New parent does not belong to the requested brand tenant.");
        if (!OrgRules.CanContain(newParent.Type, target.Type))
            throw new DomainException($"{newParent.Type} cannot contain {target.Type}.");

        if (newParent.AncestorIds.Contains(target.Id))
            throw new DomainException("Cannot move an org node under its own descendant.");

        await authorization.EnforceRoleAsync(session, actor, target, OrgRole.Admin, ct);
        await authorization.EnforceRoleAsync(session, actor, newParent, OrgRole.Admin, ct);

        var oldParentId = target.ParentId is not null ? new OrgNodeId(target.ParentId.Value) : tenant.TenantId;
        session.Events.Append(id.Value, new OrgNodeMoved(id, oldParentId, newParentId, request.Reason));

        await session.SaveChangesAsync(ct);
        var moved = await session.LoadAsync<OrgNodeReadModel>(id.Value, ct)!;
        return BrandHandlers.ToResponse(moved);
    }

    // ── Helpers ──

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
