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

public static class GrantRoleEndpoint
{
    public static ProblemDetails Validate(GrantRoleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OrgNodeId))
            return new ProblemDetails { Title = "orgNodeId", Detail = "orgNodeId is required.", Status = 400 };
        if (string.IsNullOrWhiteSpace(request.SubjectId))
            return new ProblemDetails { Title = "subjectId", Detail = "subjectId is required.", Status = 400 };
        if (!Enum.IsDefined(request.Role))
            return new ProblemDetails { Title = "role", Detail = "role is not a recognised value.", Status = 400 };
        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/brands/{brandId}/access-grants")]
    public static async Task<IResult> Handle(
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

        await using var platformQuery = store.QuerySession(PlatformTenant.Id);
        var subject = await platformQuery.LoadAsync<SubjectReadModel>(subjectId.Value, ct);
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

            if (request.Role > grant.Role)
                await outbox.PublishAsync(new CleanupRedundantGrants(tenant.TenantId, nodeId, subjectId, request.Role));

            await session.SaveChangesAsync(ct);

            var updated = await session.LoadAsync<OrgAccessGrantReadModel>(id, ct);
            return Results.Created($"/api/brands/{brandId}/access-grants/{id}",
                new GrantResponse(updated!.Id, request.OrgNodeId, request.SubjectId, updated.Role, updated.ExpiresAt));
        }

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

        if (request.ExpiresAt is not null)
        {
            await outbox.ScheduleAsync(
                new ExpireGrant(tenant.TenantId, nodeId, subjectId, request.ExpiresAt.Value),
                request.ExpiresAt.Value.UtcDateTime);
        }

        await outbox.PublishAsync(new CleanupRedundantGrants(tenant.TenantId, nodeId, subjectId, request.Role));

        await session.SaveChangesAsync(ct);
        var created = await session.LoadAsync<OrgAccessGrantReadModel>(id, ct)
            ?? throw new InvalidOperationException("Projection failed to create OrgAccessGrantReadModel.");

        return Results.Created($"/api/brands/{brandId}/access-grants/{id}",
            new GrantResponse(created.Id, request.OrgNodeId, request.SubjectId, created.Role, created.ExpiresAt));
    }
}
