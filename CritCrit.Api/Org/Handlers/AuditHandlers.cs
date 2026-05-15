using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Api.Org.Infrastructure;
using Marten;
using Wolverine.Http;

namespace CritCrit.Api.Org.Handlers;

public static class AuditHandlers
{
    [WolverineGet("/api/platform/audit")]
    public static async Task<IResult> GetPlatformAudit(
        string? action,
        DateTimeOffset? from,
        DateTimeOffset? to,
        Guid? targetOrgNodeId,
        string? actorExternalId,
        Guid? tenantId,
        int? limit,
        int? offset,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        ActorContext actor,
        CancellationToken ct)
    {
        authorization.EnforceSuperAdmin(actor);

        await using var session = store.QuerySession();
        var query = session.Query<ImmutableAuditEvent>().AsQueryable();

        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(x => x.Action == action);
        if (from is not null)
            query = query.Where(x => x.OccurredAt >= from.Value);
        if (to is not null)
            query = query.Where(x => x.OccurredAt <= to.Value);
        if (targetOrgNodeId is not null)
            query = query.Where(x => x.TargetOrgNodeId == targetOrgNodeId.Value);
        if (!string.IsNullOrWhiteSpace(actorExternalId))
            query = query.Where(x => x.ActorExternalId == actorExternalId);
        if (tenantId is not null)
            query = query.Where(x => x.TenantId == tenantId.Value);

        var take = Math.Min(limit ?? 50, 200);
        var skip = offset ?? 0;

        var items = await query
            .OrderByDescending(x => x.OccurredAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        var responses = items.Select(x => new AuditEventResponse(
            x.Id,
            x.Action,
            x.OccurredAt,
            x.Reason,
            x.ActorExternalId,
            x.ActorSubjectId,
            x.TenantId,
            x.TargetOrgNodeId,
            x.Details)).ToList();

        return Results.Ok(responses);
    }

    [WolverineGet("/api/brands/{brandId}/audit")]
    public static async Task<IResult> GetBrandAudit(
        string brandId,
        string? action,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? targetOrgNodeId,
        string? actorExternalId,
        int? limit,
        int? offset,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var session = SessionFactory.TenantSession(store, tenant);
        var root = await session.LoadAsync<OrgNodeReadModel>(tenant.TenantId.Value, ct)
            ?? throw new DomainException("Brand not found.", 404);

        // Brand audit is Owner or SuperAdmin only; Admin does not get audit read access in v1
        if (!actor.IsSuperAdmin)
            await authorization.EnforceRoleAsync(session, actor, root, OrgRole.Owner, ct);

        await using var platformSession = store.QuerySession();
        var query = platformSession.Query<ImmutableAuditEvent>()
            .Where(x => x.TenantId == tenant.TenantId.Value);

        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(x => x.Action == action);
        if (from is not null)
            query = query.Where(x => x.OccurredAt >= from.Value);
        if (to is not null)
            query = query.Where(x => x.OccurredAt <= to.Value);
        if (!string.IsNullOrWhiteSpace(targetOrgNodeId) && OrgPublicId.TryParseOrgNode(targetOrgNodeId, out var nodeId, out _))
            query = query.Where(x => x.TargetOrgNodeId == nodeId.Value);
        if (!string.IsNullOrWhiteSpace(actorExternalId))
            query = query.Where(x => x.ActorExternalId == actorExternalId);

        var take = Math.Min(limit ?? 50, 200);
        var skip = offset ?? 0;

        var items = await query
            .OrderByDescending(x => x.OccurredAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        var responses = items.Select(x => new AuditEventResponse(
            x.Id,
            x.Action,
            x.OccurredAt,
            x.Reason,
            x.ActorExternalId,
            x.ActorSubjectId,
            x.TenantId,
            x.TargetOrgNodeId,
            x.Details)).ToList();

        return Results.Ok(responses);
    }
}
