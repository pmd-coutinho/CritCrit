using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Api.Org.Features.Invitations;
using CritCrit.Api.Org.Infrastructure;
using Marten;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace CritCrit.Api.Org.Features.AccessGrants;

public static class AccessGrantHandlers
{
    [WolverinePost("/api/brands/{brandId}/access-grants")]
    public static async Task<IResult> GrantRole(
        GrantRoleRequest request,
        string brandId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IMartenOutbox outbox,
        IAuditWriter audit,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var session = SessionFactory.TenantSession(store, tenant);
        SessionMetadata.StampActor(session, actor);
        outbox.Enroll(session);

        if (!OrgPublicId.TryParseOrgNode(request.OrgNodeId, out var nodeId, out _))
            throw new DomainException("Invalid org node ID.");
        if (!OrgPublicId.TryParseSubject(request.SubjectId, out var subjectId))
            throw new DomainException("Invalid subject ID.");

        var target = await OrgValidation.LoadNodeAsync(session, nodeId, ct);
        if (target.TenantId != tenant.TenantId.Value)
            throw new DomainException("Org node does not belong to the requested brand tenant.");
        if (target.HardDeleted || target.EffectiveArchived)
            throw new DomainException("Cannot grant access to inactive org nodes.");
        if (!OrgRules.CanGrantRoleAt(request.Role, target.Type))
            throw new DomainException($"{request.Role} can only be granted at the Brand root.");

        if (request.Role == OrgRole.Owner)
            authorization.EnforceSuperAdmin(actor);
        else
            await authorization.EnforceRoleAsync(session, actor, target, OrgRole.Admin, ct);

        var subject = await session.LoadAsync<SubjectReadModel>(subjectId.Value, ct);
        if (subject is null || !subject.Active)
            throw new DomainException("Subject does not exist or is inactive.");

        var now = TimeProvider.System.GetUtcNow();
        if (await authorization.WouldBeRedundantAsync(session, target, subjectId, request.Role, now, ct))
            throw new DomainException("Grant would be redundant with inherited access.");

        var id = OrgAccessGrantReadModel.BuildId(tenant.TenantId, nodeId, subjectId);
        var grant = await session.LoadAsync<OrgAccessGrantReadModel>(id, ct);
        var ownerTransition = request.Role == OrgRole.Owner || grant is { Status: OrgAccessGrantStatus.Active, Role: OrgRole.Owner };

        if (ownerTransition)
            authorization.EnforceSuperAdmin(actor);

        if (grant is { Status: OrgAccessGrantStatus.Active })
        {
            if (grant.Role == request.Role && grant.ExpiresAt == request.ExpiresAt)
                throw new DomainException("Equivalent direct grant already exists.");

            if (request.ExpiresAt != grant.ExpiresAt)
                throw new DomainException("Changing expiration on an active grant is not allowed through this endpoint. Use /api/brands/{brandId}/access-grants/expiration instead.");

            session.Events.Append(grant.StreamId, new OrgAccessRoleChanged(tenant.TenantId, nodeId, subjectId, grant.Role, request.Role));

            if (request.Role == OrgRole.Owner)
            {
                audit.Record(session, OrgAudit.Record(
                    OrgAuditActions.OwnerGranted,
                    AuditCategories.Access,
                    AuditSeverities.Critical,
                    actor,
                    tenant.TenantId.Value,
                    nodeId.Value,
                    details: new { SubjectId = subject.PublicId, SubjectEmailMasked = AuditIdentity.MaskEmail(subject.Email) },
                    subjectId: subject.Id,
                    changes: [new AuditFieldChange("role", grant.Role.ToString(), request.Role.ToString())],
                    targetPublicId: target.PublicId,
                    targetType: target.Type.ToString().ToLowerInvariant(),
                    targetLabel: target.Name));
            }
            else if (grant.Role == OrgRole.Owner)
            {
                audit.Record(session, OrgAudit.Record(
                    OrgAuditActions.OwnerRevoked,
                    AuditCategories.Access,
                    AuditSeverities.Critical,
                    actor,
                    tenant.TenantId.Value,
                    nodeId.Value,
                    details: new { SubjectId = subject.PublicId, SubjectEmailMasked = AuditIdentity.MaskEmail(subject.Email) },
                    subjectId: subject.Id,
                    changes: [new AuditFieldChange("role", grant.Role.ToString(), request.Role.ToString())],
                    targetPublicId: target.PublicId,
                    targetType: target.Type.ToString().ToLowerInvariant(),
                    targetLabel: target.Name));
            }
            else
            {
                audit.Record(session, OrgAudit.Record(
                    OrgAuditActions.GrantRoleChanged,
                    AuditCategories.Access,
                    AuditSeverities.Info,
                    actor,
                    tenant.TenantId.Value,
                    nodeId.Value,
                    details: new { SubjectId = subject.PublicId, SubjectEmailMasked = AuditIdentity.MaskEmail(subject.Email) },
                    subjectId: subject.Id,
                    changes: [new AuditFieldChange("role", grant.Role.ToString(), request.Role.ToString())],
                    targetPublicId: target.PublicId,
                    targetType: target.Type.ToString().ToLowerInvariant(),
                    targetLabel: target.Name));
            }

            // Trigger redundant cleanup if upgrading to a stronger role
            if (request.Role > grant.Role)
            {
                await outbox.PublishAsync(new CleanupRedundantGrants(tenant.TenantId, nodeId, subjectId, request.Role));
            }

            await session.SaveChangesAsync(ct);

            var updated = await session.LoadAsync<OrgAccessGrantReadModel>(id, ct);
            return Results.Created($"/api/brands/{brandId}/access-grants/{id}",
                new GrantResponse(updated!.Id, request.OrgNodeId, request.SubjectId, updated.Role, updated.ExpiresAt));
        }

        // Deterministic stream id per .scratch/deterministic-stream-ids/PRD.md.
        // Pre-existing grants from before this migration carry random ids; the
        // branch above honours them via existingGrant.StreamId.
        var streamId = DeterministicGuid.From(tenant.TenantId.Value, nodeId.Value, subjectId.Value);
        session.Events.StartStream<OrgAccessGrantReadModel>(streamId,
            new OrgAccessGranted(tenant.TenantId, nodeId, subjectId, request.Role, request.ExpiresAt, OrgAccessGrantSource.DirectGrant, null));

        if (request.Role == OrgRole.Owner)
        {
            audit.Record(session, OrgAudit.Record(
                OrgAuditActions.OwnerGranted,
                AuditCategories.Access,
                AuditSeverities.Critical,
                actor,
                tenant.TenantId.Value,
                nodeId.Value,
                details: new { SubjectId = subject.PublicId, SubjectEmailMasked = AuditIdentity.MaskEmail(subject.Email) },
                subjectId: subject.Id,
                changes: [new AuditFieldChange("role", null, request.Role.ToString())],
                targetPublicId: target.PublicId,
                targetType: target.Type.ToString().ToLowerInvariant(),
                targetLabel: target.Name));
        }
        else
        {
            audit.Record(session, OrgAudit.Record(
                OrgAuditActions.GrantCreated,
                AuditCategories.Access,
                AuditSeverities.Info,
                actor,
                tenant.TenantId.Value,
                nodeId.Value,
                details: new { SubjectId = subject.PublicId, SubjectEmailMasked = AuditIdentity.MaskEmail(subject.Email), ExpiresAt = request.ExpiresAt },
                subjectId: subject.Id,
                changes: [new AuditFieldChange("role", null, request.Role.ToString())],
                targetPublicId: target.PublicId,
                targetType: target.Type.ToString().ToLowerInvariant(),
                targetLabel: target.Name));
        }

        // Schedule expiration if applicable
        if (request.ExpiresAt is not null)
        {
            await outbox.ScheduleAsync(
                new ExpireGrant(tenant.TenantId, nodeId, subjectId, request.ExpiresAt.Value),
                request.ExpiresAt.Value.UtcDateTime);
        }

        // Trigger redundant cleanup for ancestor grants
        await outbox.PublishAsync(new CleanupRedundantGrants(tenant.TenantId, nodeId, subjectId, request.Role));

        await session.SaveChangesAsync(ct);
        var created = await session.LoadAsync<OrgAccessGrantReadModel>(id, ct)
            ?? throw new InvalidOperationException("Projection failed to create OrgAccessGrantReadModel.");

        return Results.Created($"/api/brands/{brandId}/access-grants/{id}",
            new GrantResponse(created.Id, request.OrgNodeId, request.SubjectId, created.Role, created.ExpiresAt));
    }

    [WolverinePost("/api/brands/{brandId}/access-grants/revoke")]
    [EmptyResponse]
    public static async Task RevokeGrant(
        RevokeGrantRequest request,
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

        if (!OrgPublicId.TryParseOrgNode(request.OrgNodeId, out var nodeId, out _))
            throw new DomainException("Invalid org node ID.");
        if (!OrgPublicId.TryParseSubject(request.SubjectId, out var subjectId))
            throw new DomainException("Invalid subject ID.");

        var target = await OrgValidation.LoadNodeAsync(session, nodeId, ct);
        if (target.TenantId != tenant.TenantId.Value)
            throw new DomainException("Org node does not belong to the requested brand tenant.");

        var grantId = OrgAccessGrantReadModel.BuildId(tenant.TenantId, nodeId, subjectId);
        var grant = await session.LoadAsync<OrgAccessGrantReadModel>(grantId, ct);
        if (grant is not { Status: OrgAccessGrantStatus.Active })
            throw new DomainException("Active grant not found.", 404);

        // Owner grants flow through /owners/{subjectId}/revoke so audit + downgrade
        // semantics stay together. Refuse to use this endpoint for owners.
        if (grant.Role == OrgRole.Owner)
            throw new DomainException("Use /owners/{subjectId}/revoke to revoke an Owner grant.", 400);

        // Role gate: actor must hold a role >= the grant's role at this node
        // (SuperAdmin bypasses). Prevents a Member-at-node from removing an
        // Admin-at-node grant even if they technically have any Admin in scope.
        if (!actor.IsSuperAdmin)
        {
            await authorization.EnforceRoleAsync(session, actor, target, OrgRole.Admin, ct);

            if (actor.SubjectId is null)
                throw new DomainException("Authenticated actor is not provisioned in CritCrit.", 403);

            var effective = await authorization.GetEffectiveRoleAsync(
                session, target, actor.SubjectId.Value, TimeProvider.System.GetUtcNow(), ct);
            if (effective is null || effective.Value < grant.Role)
                throw new DomainException(
                    $"Your role at this node ({effective?.ToString() ?? "none"}) is below the grant being revoked ({grant.Role}).",
                    403);
        }

        var subject = await session.LoadAsync<SubjectReadModel>(subjectId.Value, ct);

        session.Events.Append(grant.StreamId,
            new OrgAccessRevoked(tenant.TenantId, nodeId, subjectId, OrgAccessRevokedReason.UserRequested));

        audit.Record(session, OrgAudit.Record(
            OrgAuditActions.GrantRevoked,
            AuditCategories.Access,
            AuditSeverities.Warn,
            actor,
            tenant.TenantId.Value,
            nodeId.Value,
            request.Reason,
            new { SubjectId = subject?.PublicId, SubjectEmailMasked = AuditIdentity.MaskEmail(subject?.Email), Role = grant.Role.ToString() },
            subjectId: subject?.Id,
            targetPublicId: target.PublicId,
            targetType: target.Type.ToString().ToLowerInvariant(),
            targetLabel: target.Name));

        await session.SaveChangesAsync(ct);
    }

    [WolverineGet("/api/brands/{brandId}/access-grants")]
    public static async Task<IReadOnlyList<GrantListItem>> ListGrants(
        string brandId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var tenantSession = SessionFactory.TenantSession(store, tenant);

        var brandNode = await OrgValidation.LoadNodeAsync(tenantSession, tenant.TenantId, ct);
        if (brandNode.HardDeleted)
            throw new DomainException("Brand not found.", 404);

        // Visibility: SuperAdmin or Admin+ at brand root.
        if (!actor.IsSuperAdmin)
            await authorization.EnforceRoleAsync(tenantSession, actor, brandNode, OrgRole.Admin, ct);

        var grants = await tenantSession.Query<OrgAccessGrantReadModel>()
            .Where(x => x.TenantId == tenant.TenantId.Value && x.Status == OrgAccessGrantStatus.Active)
            .ToListAsync(ct);

        if (grants.Count == 0)
            return [];

        var nodeIds = grants.Select(g => g.OrgNodeId).Distinct().ToArray();
        var subjectIds = grants.Select(g => g.SubjectId).Distinct().ToArray();

        var nodes = (await tenantSession.Query<OrgNodeReadModel>()
                .Where(n => nodeIds.Contains(n.Id))
                .ToListAsync(ct))
            .ToDictionary(n => n.Id);

        await using var platform = store.QuerySession();
        var subjects = (await platform.Query<SubjectReadModel>()
                .Where(s => subjectIds.Contains(s.Id))
                .ToListAsync(ct))
            .ToDictionary(s => s.Id);

        return grants
            .Select(g =>
            {
                if (!nodes.TryGetValue(g.OrgNodeId, out var node)) return null;
                if (!subjects.TryGetValue(g.SubjectId, out var subject)) return null;
                return new GrantListItem(
                    g.Id,
                    node.PublicId,
                    node.Name,
                    node.Type,
                    subject.PublicId,
                    subject.Email,
                    subject.DisplayName,
                    g.Role,
                    g.ExpiresAt,
                    g.Source);
            })
            .Where(x => x is not null)
            .Select(x => x!)
            .OrderByDescending(x => x.Role)
            .ThenBy(x => x.OrgNodeName)
            .ToArray();
    }

    [WolverinePost("/api/brands/{brandId}/access-grants/expiration")]
    public static async Task<GrantResponse> SetGrantExpiration(
        SetGrantExpirationRequest request,
        string brandId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IMartenOutbox outbox,
        IAuditWriter audit,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var session = SessionFactory.TenantSession(store, tenant);
        SessionMetadata.StampActor(session, actor);
        outbox.Enroll(session);

        if (!OrgPublicId.TryParseOrgNode(request.OrgNodeId, out var nodeId, out _))
            throw new DomainException("Invalid org node ID.");
        if (!OrgPublicId.TryParseSubject(request.SubjectId, out var subjectId))
            throw new DomainException("Invalid subject ID.");

        var target = await OrgValidation.LoadNodeAsync(session, nodeId, ct);
        if (target.TenantId != tenant.TenantId.Value)
            throw new DomainException("Org node does not belong to the requested brand tenant.");
        if (target.HardDeleted || target.EffectiveArchived)
            throw new DomainException("Cannot modify access for inactive org nodes.");

        await authorization.EnforceRoleAsync(session, actor, target, OrgRole.Admin, ct);

        var id = OrgAccessGrantReadModel.BuildId(tenant.TenantId, nodeId, subjectId);
        var grant = await session.LoadAsync<OrgAccessGrantReadModel>(id, ct);
        if (grant is not { Status: OrgAccessGrantStatus.Active })
            throw new DomainException("Active grant not found.");

        var oldExpiresAt = grant.ExpiresAt;
        session.Events.Append(grant.StreamId,
            new OrgAccessExpirationChanged(tenant.TenantId, nodeId, subjectId, oldExpiresAt, request.ExpiresAt));
        var subject = await session.LoadAsync<SubjectReadModel>(subjectId.Value, ct);

        audit.Record(session, OrgAudit.Record(
            OrgAuditActions.GrantExpirationChanged,
            AuditCategories.Access,
            AuditSeverities.Info,
            actor,
            tenant.TenantId.Value,
            nodeId.Value,
            details: new { SubjectId = subject?.PublicId, SubjectEmailMasked = AuditIdentity.MaskEmail(subject?.Email) },
            subjectId: subject?.Id,
            changes: [new AuditFieldChange("expiresAt", oldExpiresAt, request.ExpiresAt)],
            targetPublicId: target.PublicId,
            targetType: target.Type.ToString().ToLowerInvariant(),
            targetLabel: target.Name));

        // Schedule or cancel expiration
        if (request.ExpiresAt is not null)
        {
            await outbox.ScheduleAsync(
                new ExpireGrant(tenant.TenantId, nodeId, subjectId, request.ExpiresAt.Value),
                request.ExpiresAt.Value.UtcDateTime);
        }

        await session.SaveChangesAsync(ct);

        var updated = await session.LoadAsync<OrgAccessGrantReadModel>(id, ct)
            ?? throw new InvalidOperationException("Projection failed to update OrgAccessGrantReadModel.");
        return new GrantResponse(updated.Id, request.OrgNodeId, request.SubjectId, updated.Role, updated.ExpiresAt);
    }
}
