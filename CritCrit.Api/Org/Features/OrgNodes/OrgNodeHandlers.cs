using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Api.Org.Features.Brands;
using CritCrit.Api.Org.Infrastructure;
using Marten;
using Wolverine.Http;

namespace CritCrit.Api.Org.Features.OrgNodes;

public static class OrgNodeHandlers
{
    [WolverinePost("/api/brands/{brandId}/countries")]
    public static async Task<IResult> CreateCountry(
        CreatePlainOrgNodeRequest request,
        string brandId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var session = SessionFactory.TenantSession(store, tenant);
        SessionMetadata.StampActor(session, actor);

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
        var publicId = OrgPublicId.Format(OrgNodeType.Country, id);
        return Results.Created($"/api/brands/{brandId}/org-nodes/{publicId}", BrandHandlers.ToResponse(node));
    }

    [WolverinePost("/api/brands/{brandId}/franchises")]
    public static async Task<IResult> CreateFranchise(
        CreatePlainOrgNodeRequest request,
        string brandId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var session = SessionFactory.TenantSession(store, tenant);
        SessionMetadata.StampActor(session, actor);

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
        var publicId = OrgPublicId.Format(OrgNodeType.Franchise, id);
        return Results.Created($"/api/brands/{brandId}/org-nodes/{publicId}", BrandHandlers.ToResponse(node));
    }

    [WolverinePost("/api/brands/{brandId}/stores")]
    public static async Task<IResult> CreateStore(
        CreateStoreRequest request,
        string brandId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var session = SessionFactory.TenantSession(store, tenant);
        SessionMetadata.StampActor(session, actor);

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
        var publicId = OrgPublicId.Format(OrgNodeType.Store, id);
        return Results.Created($"/api/brands/{brandId}/org-nodes/{publicId}", BrandHandlers.ToResponse(node));
    }

    [WolverinePost("/api/brands/{brandId}/devices")]
    public static async Task<IResult> CreateDevice(
        CreateDeviceRequest request,
        string brandId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var session = SessionFactory.TenantSession(store, tenant);
        SessionMetadata.StampActor(session, actor);

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
        var publicId = OrgPublicId.Format(OrgNodeType.Device, id);
        return Results.Created($"/api/brands/{brandId}/org-nodes/{publicId}", BrandHandlers.ToResponse(node));
    }

    // ── Lifecycle ──

    [WolverinePost("/api/brands/{brandId}/org-nodes/{nodeId}/archive")]
    public static async Task<OrgNodeResponse> ArchiveOrgNode(
        string nodeId,
        ArchiveOrgNodeRequest request,
        string brandId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var session = SessionFactory.TenantSession(store, tenant);
        SessionMetadata.StampActor(session, actor);

        if (!OrgPublicId.TryParseOrgNode(nodeId, out var id, out _))
            throw new DomainException("Invalid org node ID.");

        var target = await OrgValidation.LoadNodeAsync(session, id, ct);
        if (target.TenantId != tenant.TenantId.Value)
            throw new DomainException("Org node does not belong to the requested brand tenant.");
        if (target.HardDeleted)
            throw new DomainException("Cannot archive a hard-deleted org node.");
        if (target.Archived)
            throw new DomainException("Org node is already archived.");

        if (target.Type == OrgNodeType.Brand)
        {
            if (!request.Force)
                throw new DomainException("Archiving a brand requires force=true.");
            if (string.IsNullOrWhiteSpace(request.Reason))
                throw new DomainException("A reason is required when archiving a brand.");

            await authorization.EnforceRootOwnerOrSuperAdminAsync(session, actor, target, ct);
        }
        else
        {
            await authorization.EnforceRoleAsync(session, actor, target, OrgRole.Admin, ct);
        }

        if (!request.Force)
        {
            var hasChildren = await OrgValidation.HasActiveChildrenAsync(session, id, tenant.TenantId.Value, ct);
            if (hasChildren)
                throw new DomainException("Org node has active children. Use force=true to cascade archive.");
        }
        else if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new DomainException("A reason is required when force-archiving an org node.");
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

            AuditLog.Write(
                session,
                target.Type == OrgNodeType.Brand ? OrgAuditActions.BrandArchive : OrgAuditActions.CascadeArchive,
                actor,
                tenant.TenantId.Value,
                id.Value,
                request.Reason,
                new
                {
                    TargetId = target.PublicId,
                    DescendantCount = descendants.Count
                });
        }

        await session.SaveChangesAsync(ct);
        var archived = await session.LoadAsync<OrgNodeReadModel>(id.Value, ct)
            ?? throw new InvalidOperationException("Projection failed to update OrgNodeReadModel.");
        return BrandHandlers.ToResponse(archived);
    }

    [WolverinePost("/api/brands/{brandId}/org-nodes/{nodeId}/restore")]
    public static async Task<OrgNodeResponse> RestoreOrgNode(
        string nodeId,
        string brandId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var session = SessionFactory.TenantSession(store, tenant);
        SessionMetadata.StampActor(session, actor);

        if (!OrgPublicId.TryParseOrgNode(nodeId, out var id, out _))
            throw new DomainException("Invalid org node ID.");

        var target = await OrgValidation.LoadNodeAsync(session, id, ct);
        if (target.TenantId != tenant.TenantId.Value)
            throw new DomainException("Org node does not belong to the requested brand tenant.");
        if (target.HardDeleted)
            throw new DomainException("Cannot restore a hard-deleted org node.");
        if (!target.Archived)
            throw new DomainException("Org node is not archived.");

        if (target.Type == OrgNodeType.Brand)
            await authorization.EnforceRootOwnerOrSuperAdminAsync(session, actor, target, ct);
        else
            await authorization.EnforceRoleAsync(session, actor, target, OrgRole.Admin, ct);

        session.Events.Append(id.Value, new OrgNodeRestored(id));

        var descendants = await OrgValidation.LoadDescendantsAsync(session, id, tenant.TenantId.Value, ct);
        if (descendants.Count != 0)
        {
            var ancestorIds = descendants
                .SelectMany(x => x.AncestorIds)
                .Where(x => x != id.Value)
                .Distinct()
                .ToArray();

            var archivedAncestorIds = ancestorIds.Length == 0
                ? []
                : (await session.Query<OrgNodeReadModel>()
                    .Where(x => x.TenantId == tenant.TenantId.Value && ancestorIds.Contains(x.Id) && x.Archived)
                    .Select(x => x.Id)
                    .ToListAsync(ct))
                .ToHashSet();

            foreach (var descendant in descendants)
            {
                descendant.EffectiveArchived = descendant.Archived ||
                                               descendant.AncestorIds
                                                   .Where(x => x != id.Value)
                                                   .Any(archivedAncestorIds.Contains);
                session.Store(descendant);
            }
        }

        if (target.Type == OrgNodeType.Brand)
        {
            AuditLog.Write(
                session,
                OrgAuditActions.BrandRestore,
                actor,
                tenant.TenantId.Value,
                id.Value,
                null,
                new
                {
                    TargetId = target.PublicId,
                    DescendantCount = descendants.Count
                });
        }

        await session.SaveChangesAsync(ct);
        var restored = await session.LoadAsync<OrgNodeReadModel>(id.Value, ct)
            ?? throw new InvalidOperationException("Projection failed to update OrgNodeReadModel.");
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
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var session = SessionFactory.TenantSession(store, tenant);
        SessionMetadata.StampActor(session, actor);

        if (!OrgPublicId.TryParseOrgNode(nodeId, out var id, out _))
            throw new DomainException("Invalid org node ID.");

        var target = await OrgValidation.LoadNodeAsync(session, id, ct);
        if (target.TenantId != tenant.TenantId.Value)
            throw new DomainException("Org node does not belong to the requested brand tenant.");
        if (target.HardDeleted)
            throw new DomainException("Org node is already hard-deleted.");

        await authorization.EnforceRootOwnerOrSuperAdminAsync(session, actor, target, ct);

        var descendants = await OrgValidation.LoadDescendantsAsync(session, id, tenant.TenantId.Value, ct);
        var subtree = new[] { target }.Concat(descendants).ToArray();

        foreach (var node in subtree)
        {
            var subtreeNodeId = new OrgNodeId(node.Id);
            session.Events.Append(node.Id, new OrgNodeHardDeleted(subtreeNodeId, request.Reason));

            if (node.Type == OrgNodeType.Store)
                session.Events.Append(node.Id, new StoreProfileHardDeleted(subtreeNodeId));
            else if (node.Type == OrgNodeType.Device)
                session.Events.Append(node.Id, new DeviceProfileHardDeleted(subtreeNodeId));
        }

        var subtreeIds = subtree.Select(x => x.Id).ToArray();
        var activeGrants = await session.Query<OrgAccessGrantReadModel>()
            .Where(x => x.TenantId == tenant.TenantId.Value &&
                        subtreeIds.Contains(x.OrgNodeId) &&
                        x.Status == OrgAccessGrantStatus.Active)
            .ToListAsync(ct);

        var now = TimeProvider.System.GetUtcNow();
        foreach (var grant in activeGrants.Where(x => x.ExpiresAt is null || x.ExpiresAt > now))
        {
            session.Events.Append(
                grant.StreamId,
                new OrgAccessRevoked(
                    tenant.TenantId,
                    new OrgNodeId(grant.OrgNodeId),
                    new SubjectId(grant.SubjectId),
                    OrgAccessRevokedReason.TargetHardDeleted));
        }

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

        AuditLog.Write(
            session,
            OrgAuditActions.HardDeleteSubtree,
            actor,
            tenant.TenantId.Value,
            id.Value,
            request.Reason,
            new
            {
                TargetId = target.PublicId,
                DeletedNodes = subtree.Select(DescribeNode).ToArray(),
                RevokedGrantCount = activeGrants.Count(x => x.ExpiresAt is null || x.ExpiresAt > now)
            });

        await session.SaveChangesAsync(ct);
    }

    [WolverinePost("/api/brands/{brandId}/org-nodes/{nodeId}/move")]
    public static async Task<OrgNodeResponse> MoveOrgNode(
        string nodeId,
        MoveOrgNodeRequest request,
        string brandId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var session = SessionFactory.TenantSession(store, tenant);
        SessionMetadata.StampActor(session, actor);

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
        AuditLog.Write(
            session,
            OrgAuditActions.OrgNodeMove,
            actor,
            tenant.TenantId.Value,
            id.Value,
            request.Reason,
            new
            {
                TargetId = target.PublicId,
                OldParentId = target.ParentPublicId ?? tenant.BrandPublicId,
                NewParentId = newParent.PublicId
            });

        await session.SaveChangesAsync(ct);

        // Trigger cleanup for redundant grants in the moved subtree
        // Inline because the projection has updated ancestry by now
        await using var cleanupSession = store.LightweightSession(tenant.TenantId.Value.ToString());
        var movedNodeAfter = await cleanupSession.LoadAsync<OrgNodeReadModel>(id.Value, ct)
            ?? throw new InvalidOperationException("Moved node projection missing after save.");

        var subtreeNodes = await cleanupSession.Query<OrgNodeReadModel>()
            .Where(x => x.TenantId == tenant.TenantId.Value &&
                        x.AncestorIds.Contains(id.Value) &&
                        !x.HardDeleted)
            .ToListAsync(ct);

        var allNodes = subtreeNodes.Prepend(movedNodeAfter).ToList();
        var subtreeNodeIds = allNodes.Select(x => x.Id).ToHashSet();
        var now = TimeProvider.System.GetUtcNow();

        var subtreeGrants = await cleanupSession.Query<OrgAccessGrantReadModel>()
            .Where(x => x.TenantId == tenant.TenantId.Value &&
                        subtreeNodeIds.Contains(x.OrgNodeId) &&
                        x.Status == OrgAccessGrantStatus.Active)
            .ToListAsync(ct);

        var changed = false;
        foreach (var grant in subtreeGrants.Where(g => g.ExpiresAt is null || g.ExpiresAt > now))
        {
            var grantNode = allNodes.FirstOrDefault(x => x.Id == grant.OrgNodeId);
            if (grantNode is null)
                continue;

            var externalAncestors = grantNode.AncestorIds.Where(a => !subtreeNodeIds.Contains(a)).ToArray();
            if (externalAncestors.Length == 0)
                continue;

            var ancestorGrants = await cleanupSession.Query<OrgAccessGrantReadModel>()
                .Where(x => x.TenantId == tenant.TenantId.Value &&
                            x.SubjectId == grant.SubjectId &&
                            externalAncestors.Contains(x.OrgNodeId) &&
                            x.Status == OrgAccessGrantStatus.Active)
                .ToListAsync(ct);

            var strongestAncestor = ancestorGrants
                .Where(g => g.ExpiresAt is null || g.ExpiresAt > now)
                .OrderByDescending(g => g.Role)
                .FirstOrDefault();

            if (strongestAncestor is not null && strongestAncestor.Role >= grant.Role)
            {
                cleanupSession.Events.Append(grant.StreamId,
                    new OrgAccessRevoked(
                        tenant.TenantId,
                        new OrgNodeId(grant.OrgNodeId),
                        new SubjectId(grant.SubjectId),
                        OrgAccessRevokedReason.RedundantByAncestorGrant));
                changed = true;
            }
        }

        if (changed)
            await cleanupSession.SaveChangesAsync(ct);
        var moved = await session.LoadAsync<OrgNodeReadModel>(id.Value, ct)
            ?? throw new InvalidOperationException("Projection failed to update OrgNodeReadModel.");
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

    private static object DescribeNode(OrgNodeReadModel node) => new
    {
        node.PublicId,
        node.Type,
        node.Code,
        node.Name,
        node.ParentPublicId
    };
}
