using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Api.Org.Infrastructure;
using Marten;
using Wolverine.Http;

namespace CritCrit.Api.Org.Features.Audit;

public static class AuditHandlers
{
    [WolverineGet("/api/platform/audit")]
    public static async Task<IResult> GetPlatformAudit(
        string? action,
        string? category,
        string? severity,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? targetOrgNodeId,
        string? subjectId,
        string? actorExternalId,
        string? tenantId,
        string? supportId,
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
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(x => x.Category == category);
        if (!string.IsNullOrWhiteSpace(severity))
            query = query.Where(x => x.Severity == severity);
        if (from is not null)
            query = query.Where(x => x.OccurredAt >= from.Value);
        if (to is not null)
            query = query.Where(x => x.OccurredAt <= to.Value);
        if (!string.IsNullOrWhiteSpace(targetOrgNodeId))
        {
            var parsed = ParseOrgNodeFilter(targetOrgNodeId).Value;
            query = query.Where(x => x.TargetOrgNodeId == parsed);
        }
        if (!string.IsNullOrWhiteSpace(subjectId))
        {
            var parsed = ParseSubjectFilter(subjectId).Value;
            query = query.Where(x => x.SubjectId == parsed);
        }
        if (!string.IsNullOrWhiteSpace(actorExternalId))
            query = query.Where(x => x.ActorExternalId == actorExternalId);
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            var parsed = ParseOrgNodeFilter(tenantId).Value;
            query = query.Where(x => x.TenantId == parsed);
        }
        if (!string.IsNullOrWhiteSpace(supportId))
            query = query.Where(x => x.SupportId == supportId);

        var take = Math.Min(limit ?? 50, 200);
        var skip = offset ?? 0;

        var items = await query
            .OrderByDescending(x => x.OccurredAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        var responses = items.Select(ToResponse).ToList();

        return Results.Ok(responses);
    }

    [WolverineGet("/api/brands/{brandId}/audit")]
    public static async Task<IResult> GetBrandAudit(
        string brandId,
        string? action,
        string? category,
        string? severity,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? targetOrgNodeId,
        string? subjectId,
        string? actorExternalId,
        string? supportId,
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
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(x => x.Category == category);
        if (!string.IsNullOrWhiteSpace(severity))
            query = query.Where(x => x.Severity == severity);
        if (from is not null)
            query = query.Where(x => x.OccurredAt >= from.Value);
        if (to is not null)
            query = query.Where(x => x.OccurredAt <= to.Value);
        if (!string.IsNullOrWhiteSpace(targetOrgNodeId))
        {
            var parsed = ParseOrgNodeFilter(targetOrgNodeId).Value;
            query = query.Where(x => x.TargetOrgNodeId == parsed);
        }
        if (!string.IsNullOrWhiteSpace(subjectId))
        {
            var parsed = ParseSubjectFilter(subjectId).Value;
            query = query.Where(x => x.SubjectId == parsed);
        }
        if (!string.IsNullOrWhiteSpace(actorExternalId))
            query = query.Where(x => x.ActorExternalId == actorExternalId);
        if (!string.IsNullOrWhiteSpace(supportId))
            query = query.Where(x => x.SupportId == supportId);

        var take = Math.Min(limit ?? 50, 200);
        var skip = offset ?? 0;

        var items = await query
            .OrderByDescending(x => x.OccurredAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        var responses = items.Select(ToResponse).ToList();

        return Results.Ok(responses);
    }

    private static AuditEventResponse ToResponse(ImmutableAuditEvent x) => new(
        x.Id,
        x.Action,
        x.Category,
        x.Severity,
        x.OccurredAt,
        x.Reason,
        x.ActorExternalId,
        x.ActorSubjectId,
        x.ActorSubjectPublicId,
        x.ActorKind,
        x.TenantId,
        x.TenantPublicId,
        x.TargetOrgNodeId,
        x.Target?.PublicId,
        x.Target?.Type,
        x.Target?.Label,
        x.SubjectId,
        x.SubjectPublicId,
        x.SupportId,
        x.CorrelationId,
        x.CausationId,
        x.RelatedResources,
        x.Changes,
        x.Request,
        x.Details);

    private static OrgNodeId ParseOrgNodeFilter(string value)
    {
        if (OrgPublicId.TryParseOrgNode(value, out var publicId, out _))
            return publicId;
        if (Guid.TryParse(value, out var guid))
            return new OrgNodeId(guid);
        throw new DomainException("Invalid org node ID filter.");
    }

    private static SubjectId ParseSubjectFilter(string value)
    {
        if (OrgPublicId.TryParseSubject(value, out var publicId))
            return publicId;
        if (Guid.TryParse(value, out var guid))
            return new SubjectId(guid);
        throw new DomainException("Invalid subject ID filter.");
    }
}
