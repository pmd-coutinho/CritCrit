using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Api.Org.Features.Invitations;
using CritCrit.Api.Org.Infrastructure;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;

namespace CritCrit.Api.Org.Features.AccessGrants;

public static class SetGrantExpirationEndpoint
{
    public static ProblemDetails Validate(SetGrantExpirationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OrgNodeId))
            return new ProblemDetails { Title = "orgNodeId", Detail = "orgNodeId is required.", Status = 400 };
        if (string.IsNullOrWhiteSpace(request.SubjectId))
            return new ProblemDetails { Title = "subjectId", Detail = "subjectId is required.", Status = 400 };
        if (request.ExpiresAt is { } at && at <= DateTimeOffset.UtcNow)
            return new ProblemDetails { Title = "expiresAt", Detail = "Expiration must be in the future.", Status = 400 };
        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/brands/{brandId}/access-grants/expiration")]
    public static async Task<GrantResponse> Handle(
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
