using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Api.Org.Features.Brands;
using CritCrit.Api.Org.Infrastructure;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;

namespace CritCrit.Api.Org.Features.OrgNodes;

public static class ArchiveOrgNodeEndpoint
{
    public static ProblemDetails Validate(ArchiveOrgNodeRequest request)
    {
        if (request.Reason is { Length: > 500 })
            return new ProblemDetails { Title = "reason", Detail = "Reason must be 500 characters or fewer.", Status = 400 };
        if (request.Force && string.IsNullOrWhiteSpace(request.Reason))
            return new ProblemDetails { Title = "reason", Detail = "Reason is required when force=true.", Status = 400 };
        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/brands/{brandId}/org-nodes/{nodeId}/archive")]
    public static async Task<OrgNodeResponse> Handle(
        OrgNodeId nodeId,
        ArchiveOrgNodeRequest request,
        string brandId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IAuditWriter audit,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var session = SessionFactory.TenantSession(store, tenant);
        SessionMetadata.StampActor(session, actor);

        var id = nodeId;
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

            audit.Record(session, OrgAudit.Record(
                target.Type == OrgNodeType.Brand ? OrgAuditActions.BrandArchive : OrgAuditActions.CascadeArchive,
                AuditCategories.Org,
                target.Type == OrgNodeType.Brand ? AuditSeverities.Critical : AuditSeverities.Warn,
                actor,
                tenant.TenantId.Value,
                id.Value,
                request.Reason,
                new { TargetId = target.PublicId, DescendantCount = descendants.Count },
                changes: [new AuditFieldChange("archived", false, true)],
                targetPublicId: target.PublicId,
                targetType: target.Type.ToString().ToLowerInvariant(),
                targetLabel: target.Name));
        }

        await session.SaveChangesAsync(ct);
        var archived = await session.LoadAsync<OrgNodeReadModel>(id.Value, ct)
            ?? throw new InvalidOperationException("Projection failed to update OrgNodeReadModel.");
        return BrandHandlers.ToResponse(archived);
    }
}

public static class RestoreOrgNodeEndpoint
{
    // No Validate method — Restore has no request body to shape-check.

    [WolverinePost("/api/brands/{brandId}/org-nodes/{nodeId}/restore")]
    public static async Task<OrgNodeResponse> Handle(
        OrgNodeId nodeId,
        string brandId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IAuditWriter audit,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var session = SessionFactory.TenantSession(store, tenant);
        SessionMetadata.StampActor(session, actor);

        var id = nodeId;
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
            audit.Record(session, OrgAudit.Record(
                OrgAuditActions.BrandRestore,
                AuditCategories.Org,
                AuditSeverities.Critical,
                actor,
                tenant.TenantId.Value,
                id.Value,
                details: new { TargetId = target.PublicId, DescendantCount = descendants.Count },
                changes: [new AuditFieldChange("archived", true, false)],
                targetPublicId: target.PublicId,
                targetType: target.Type.ToString().ToLowerInvariant(),
                targetLabel: target.Name));
        }

        await session.SaveChangesAsync(ct);
        var restored = await session.LoadAsync<OrgNodeReadModel>(id.Value, ct)
            ?? throw new InvalidOperationException("Projection failed to update OrgNodeReadModel.");
        return BrandHandlers.ToResponse(restored);
    }
}

public static class HardDeleteOrgNodeEndpoint
{
    public static ProblemDetails Validate(HardDeleteOrgNodeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return new ProblemDetails { Title = "reason", Detail = "Reason is required.", Status = 400 };
        if (request.Reason.Length > 500)
            return new ProblemDetails { Title = "reason", Detail = "Reason must be 500 characters or fewer.", Status = 400 };
        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/brands/{brandId}/org-nodes/{nodeId}/hard-delete")]
    [EmptyResponse]
    public static async Task Handle(
        OrgNodeId nodeId,
        HardDeleteOrgNodeRequest request,
        string brandId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IAuditWriter audit,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var session = SessionFactory.TenantSession(store, tenant);
        SessionMetadata.StampActor(session, actor);

        var id = nodeId;
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

        audit.Record(session, OrgAudit.Record(
            OrgAuditActions.HardDeleteSubtree,
            AuditCategories.Org,
            AuditSeverities.Critical,
            actor,
            tenant.TenantId.Value,
            id.Value,
            request.Reason,
            new
            {
                TargetId = target.PublicId,
                DeletedNodes = subtree.Select(OrgNodeParentValidation.DescribeNode).ToArray(),
                RevokedGrantCount = activeGrants.Count(x => x.ExpiresAt is null || x.ExpiresAt > now)
            },
            changes: [new AuditFieldChange("hardDeleted", false, true)],
            targetPublicId: target.PublicId,
            targetType: target.Type.ToString().ToLowerInvariant(),
            targetLabel: target.Name));

        await session.SaveChangesAsync(ct);
    }
}

public static class MoveOrgNodeEndpoint
{
    public static ProblemDetails Validate(MoveOrgNodeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NewParentId))
            return new ProblemDetails { Title = "newParentId", Detail = "newParentId is required.", Status = 400 };
        if (string.IsNullOrWhiteSpace(request.Reason))
            return new ProblemDetails { Title = "reason", Detail = "Reason is required.", Status = 400 };
        if (request.Reason.Length > 500)
            return new ProblemDetails { Title = "reason", Detail = "Reason must be 500 characters or fewer.", Status = 400 };
        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/brands/{brandId}/org-nodes/{nodeId}/move")]
    public static async Task<OrgNodeResponse> Handle(
        OrgNodeId nodeId,
        MoveOrgNodeRequest request,
        string brandId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IAuditWriter audit,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var session = SessionFactory.TenantSession(store, tenant);
        SessionMetadata.StampActor(session, actor);

        var id = nodeId;
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
        audit.Record(session, OrgAudit.Record(
            OrgAuditActions.OrgNodeMove,
            AuditCategories.Org,
            AuditSeverities.Info,
            actor,
            tenant.TenantId.Value,
            id.Value,
            request.Reason,
            new
            {
                TargetId = target.PublicId,
                OldParentId = target.ParentPublicId ?? tenant.BrandPublicId,
                NewParentId = newParent.PublicId
            },
            changes: [new AuditFieldChange("parentId", target.ParentPublicId ?? tenant.BrandPublicId, newParent.PublicId)],
            targetPublicId: target.PublicId,
            targetType: target.Type.ToString().ToLowerInvariant(),
            targetLabel: target.Name));

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
}
