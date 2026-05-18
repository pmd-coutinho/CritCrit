using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Api.Org.Infrastructure;
using Marten;
using Wolverine.Http;

namespace CritCrit.Api.Org.Features.AccessGrants;

// Remaining endpoints that do not need their own `Validate` convention method.
// GrantRoleEndpoint and SetGrantExpirationEndpoint live in separate classes so
// Wolverine.Http's per-class convention-method resolution can dispatch their
// validations correctly. See .scratch/deterministic-stream-ids/PRD.md "Prereq 4".
public static class AccessGrantHandlers
{
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

        if (grant.Role == OrgRole.Owner)
            throw new DomainException("Use /owners/{subjectId}/revoke to revoke an Owner grant.", 400);

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
}
