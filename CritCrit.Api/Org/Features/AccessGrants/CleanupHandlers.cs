using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Features.Invitations;
using CritCrit.Api.Org.Infrastructure;
using CritCrit.Api.Observability.Logging;
using Marten;
using Wolverine.Attributes;

namespace CritCrit.Api.Org.Features.AccessGrants;

[WolverineHandler]
public sealed class CleanupHandlers(IDocumentStore store, IAuditWriter audit, ILogger<CleanupHandlers> logger)
{
    public async Task Handle(ExpireGrant command, CancellationToken ct)
    {
        await using var session = store.LightweightSession(command.TenantId.Value.ToString());
        SessionMetadata.StampSystem(session, AuditActor.BackgroundSystem(), command.Audit?.CausationId);
        var id = OrgAccessGrantReadModel.BuildId(command.TenantId, command.OrgNodeId, command.SubjectId);
        var grant = await session.LoadAsync<OrgAccessGrantReadModel>(id, ct);
        if (grant is null || grant.Status != OrgAccessGrantStatus.Active)
            return;
        if (grant.ExpiresAt is null || grant.ExpiresAt.Value != command.ExpiresAt)
            return;
        if (grant.ExpiresAt > TimeProvider.System.GetUtcNow())
            return;

        session.Events.Append(grant.StreamId,
            new OrgAccessExpired(command.TenantId, command.OrgNodeId, command.SubjectId));
        audit.Record(session, OrgAudit.SystemRecord(
            OrgAuditActions.GrantExpired,
            AuditCategories.Access,
            AuditSeverities.Warn,
            AuditActor.BackgroundSystem(),
            command.TenantId.Value,
            command.OrgNodeId.Value,
            details: new { command.ExpiresAt },
            subjectId: command.SubjectId.Value,
            changes: [new AuditFieldChange("status", OrgAccessGrantStatus.Active.ToString(), OrgAccessGrantStatus.Expired.ToString())],
            causationId: command.Audit?.CausationId));
        await session.SaveChangesAsync(ct);
        logger.AccessGrantExpired(command.TenantId.Value, command.OrgNodeId.Value, command.SubjectId.Value, SupportId.Current);
    }

    public async Task Handle(CleanupRedundantGrants command, CancellationToken ct)
    {
        await using var session = store.LightweightSession(command.TenantId.Value.ToString());
        SessionMetadata.StampSystem(session, AuditActor.BackgroundSystem(), command.Audit?.CausationId);
        var now = TimeProvider.System.GetUtcNow();

        var descendants = await session.Query<OrgNodeReadModel>()
            .Where(x => x.TenantId == command.TenantId.Value &&
                        x.AncestorIds.Contains(command.OrgNodeId.Value) &&
                        !x.HardDeleted)
            .ToListAsync(ct);

        var descendantIds = descendants.Select(x => x.Id).ToArray();
        if (descendantIds.Length == 0)
            return;

        var redundantGrants = await session.Query<OrgAccessGrantReadModel>()
            .Where(x => x.TenantId == command.TenantId.Value &&
                        x.SubjectId == command.SubjectId.Value &&
                        descendantIds.Contains(x.OrgNodeId) &&
                        x.Status == OrgAccessGrantStatus.Active)
            .ToListAsync(ct);

        var changed = 0;
        foreach (var grant in redundantGrants.Where(g => g.ExpiresAt is null || g.ExpiresAt > now))
        {
            if (grant.Role <= command.NewRole)
            {
                session.Events.Append(grant.StreamId,
                    new OrgAccessRevoked(
                        command.TenantId,
                        new OrgNodeId(grant.OrgNodeId),
                        command.SubjectId,
                        OrgAccessRevokedReason.RedundantByAncestorGrant));
                changed++;
            }
        }

        if (changed > 0)
        {
            audit.Record(session, OrgAudit.SystemRecord(
                OrgAuditActions.GrantRedundantRevoked,
                AuditCategories.Access,
                AuditSeverities.Warn,
                AuditActor.BackgroundSystem(),
                command.TenantId.Value,
                command.OrgNodeId.Value,
                details: new { RevokedGrantCount = changed, NewRole = command.NewRole.ToString() },
                subjectId: command.SubjectId.Value,
                causationId: command.Audit?.CausationId));
            await session.SaveChangesAsync(ct);
            logger.RedundantAccessGrantsCleaned(command.TenantId.Value, command.OrgNodeId.Value, command.SubjectId.Value, changed, SupportId.Current);
        }
    }

    public async Task Handle(CleanupMovedSubtreeGrants command, CancellationToken ct)
    {
        await using var session = store.LightweightSession(command.TenantId.Value.ToString());
        SessionMetadata.StampSystem(session, AuditActor.BackgroundSystem(), command.Audit?.CausationId);
        var now = TimeProvider.System.GetUtcNow();

        // Load the moved node to get its new ancestry
        var movedNode = await session.LoadAsync<OrgNodeReadModel>(command.MovedNodeId.Value, ct);
        if (movedNode is null || movedNode.HardDeleted)
            return;

        // Build the full subtree (moved node + all descendants)
        var subtreeNodes = await session.Query<OrgNodeReadModel>()
            .Where(x => x.TenantId == command.TenantId.Value &&
                        x.AncestorIds.Contains(command.MovedNodeId.Value) &&
                        !x.HardDeleted)
            .ToListAsync(ct);

        var allNodes = subtreeNodes.Prepend(movedNode).ToList();
        var subtreeNodeIds = allNodes.Select(x => x.Id).ToHashSet();

        // Get all active grants in the subtree
        var subtreeGrants = await session.Query<OrgAccessGrantReadModel>()
            .Where(x => x.TenantId == command.TenantId.Value &&
                        subtreeNodeIds.Contains(x.OrgNodeId) &&
                        x.Status == OrgAccessGrantStatus.Active)
            .ToListAsync(ct);

        if (subtreeGrants.Count == 0)
            return;

        // Build new ancestry for the moved node
        var newAncestorIds = movedNode.AncestorIds.ToHashSet();
        newAncestorIds.Add(movedNode.Id);

        var changed = 0;
        foreach (var grant in subtreeGrants.Where(g => g.ExpiresAt is null || g.ExpiresAt > now))
        {
            var grantNode = allNodes.FirstOrDefault(x => x.Id == grant.OrgNodeId);
            if (grantNode is null)
                continue;

            // Build full ancestry for this grant node in the new position
            var grantNodeAncestors = grantNode.AncestorIds.ToHashSet();
            grantNodeAncestors.Add(grantNode.Id);

            // Check if any ancestor (outside the moved subtree) has a stronger or equal grant for this subject
            var externalAncestors = grantNodeAncestors.Where(a => !subtreeNodeIds.Contains(a)).ToArray();
            if (externalAncestors.Length == 0)
                continue;

            var ancestorGrants = await session.Query<OrgAccessGrantReadModel>()
                .Where(x => x.TenantId == command.TenantId.Value &&
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
                session.Events.Append(grant.StreamId,
                    new OrgAccessRevoked(
                        command.TenantId,
                        new OrgNodeId(grant.OrgNodeId),
                        new SubjectId(grant.SubjectId),
                        OrgAccessRevokedReason.RedundantByAncestorGrant));
                changed++;
            }
        }

        if (changed > 0)
        {
            audit.Record(session, OrgAudit.SystemRecord(
                OrgAuditActions.GrantRedundantRevoked,
                AuditCategories.Access,
                AuditSeverities.Warn,
                AuditActor.BackgroundSystem(),
                command.TenantId.Value,
                command.MovedNodeId.Value,
                details: new { RevokedGrantCount = changed, Reason = "moved_subtree" },
                causationId: command.Audit?.CausationId));
            await session.SaveChangesAsync(ct);
        }
    }
}
