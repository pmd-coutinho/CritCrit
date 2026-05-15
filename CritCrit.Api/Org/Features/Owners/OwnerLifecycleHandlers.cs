using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Api.Org.Features.Invitations;
using CritCrit.Api.Org.Infrastructure;
using Marten;
using Wolverine.Http;
using Wolverine.Marten;

namespace CritCrit.Api.Org.Features.Owners;

public static class OwnerLifecycleHandlers
{
    [WolverinePost("/api/brands/{brandId}/owners")]
    public static async Task<IResult> GrantOwner(
        GrantOwnerRequest request,
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
        authorization.EnforceSuperAdmin(actor);

        if (!OrgPublicId.TryParseSubject(request.SubjectId, out var subjectId))
            throw new DomainException("Invalid subject ID.");

        var root = await OrgValidation.LoadActiveNodeAsync(session, tenant.TenantId, ct);
        if (root.Type != OrgNodeType.Brand)
            throw new DomainException("Owner can only be granted at the brand root.");

        var subject = await session.LoadAsync<SubjectReadModel>(subjectId.Value, ct);
        if (subject is null || !subject.Active)
            throw new DomainException("Subject does not exist or is inactive.");

        var now = TimeProvider.System.GetUtcNow();
        if (await authorization.WouldBeRedundantAsync(session, root, subjectId, OrgRole.Owner, now, ct))
            throw new DomainException("Grant would be redundant with inherited access.");

        var id = OrgAccessGrantReadModel.BuildId(tenant.TenantId, tenant.TenantId, subjectId);
        var grant = await session.LoadAsync<OrgAccessGrantReadModel>(id, ct);

        if (grant is { Status: OrgAccessGrantStatus.Active })
        {
            if (grant.Role == OrgRole.Owner)
                throw new DomainException("Equivalent direct owner grant already exists.");

            session.Events.Append(grant.StreamId,
                new OrgAccessRoleChanged(tenant.TenantId, tenant.TenantId, subjectId, grant.Role, OrgRole.Owner));

            audit.Record(session, OrgAudit.Record(
                OrgAuditActions.OwnerGranted,
                AuditCategories.Access,
                AuditSeverities.Critical,
                actor,
                tenant.TenantId.Value,
                tenant.TenantId.Value,
                details: new { SubjectId = subject.PublicId, SubjectEmailMasked = AuditIdentity.MaskEmail(subject.Email) },
                subjectId: subject.Id,
                changes: [new AuditFieldChange("role", grant.Role.ToString(), OrgRole.Owner.ToString())],
                targetPublicId: brandId,
                targetType: "brand"));

            await session.SaveChangesAsync(ct);
            var updated = await session.LoadAsync<OrgAccessGrantReadModel>(id, ct);
            return Results.Ok(new GrantResponse(updated!.Id, brandId, request.SubjectId, updated.Role, updated.ExpiresAt));
        }

        var streamId = Guid.CreateVersion7();
        session.Events.StartStream<OrgAccessGrantReadModel>(streamId,
            new OrgAccessGranted(tenant.TenantId, tenant.TenantId, subjectId, OrgRole.Owner, null, OrgAccessGrantSource.DirectGrant, null));

        audit.Record(session, OrgAudit.Record(
            OrgAuditActions.OwnerGranted,
            AuditCategories.Access,
            AuditSeverities.Critical,
            actor,
            tenant.TenantId.Value,
            tenant.TenantId.Value,
            details: new { SubjectId = subject.PublicId, SubjectEmailMasked = AuditIdentity.MaskEmail(subject.Email) },
            subjectId: subject.Id,
            changes: [new AuditFieldChange("role", null, OrgRole.Owner.ToString())],
            targetPublicId: brandId,
            targetType: "brand"));

        // Trigger redundant cleanup for the newly granted owner role
        await outbox.PublishAsync(new CleanupRedundantGrants(tenant.TenantId, tenant.TenantId, subjectId, OrgRole.Owner));

        await session.SaveChangesAsync(ct);
        var created = await session.LoadAsync<OrgAccessGrantReadModel>(id, ct)
            ?? throw new InvalidOperationException("Projection failed to create OrgAccessGrantReadModel.");

        return Results.Created($"/api/brands/{brandId}/access-grants/{id}",
            new GrantResponse(created.Id, brandId, request.SubjectId, created.Role, created.ExpiresAt));
    }

    [WolverinePost("/api/brands/{brandId}/owners/{subjectId}/downgrade")]
    public static async Task<GrantResponse> DowngradeOwner(
        DowngradeOwnerRequest request,
        string brandId,
        string subjectId,
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
        authorization.EnforceSuperAdmin(actor);

        if (!OrgPublicId.TryParseSubject(subjectId, out var parsedSubjectId))
            throw new DomainException("Invalid subject ID.");

        if (request.NewRole == OrgRole.Owner)
            throw new DomainException("Downgrade cannot target Owner role.");
        if (!OrgRules.CanGrantRoleAt(request.NewRole, OrgNodeType.Brand))
            throw new DomainException($"{request.NewRole} can only be granted at the Brand root.");

        var id = OrgAccessGrantReadModel.BuildId(tenant.TenantId, tenant.TenantId, parsedSubjectId);
        var grant = await session.LoadAsync<OrgAccessGrantReadModel>(id, ct);
        if (grant is not { Status: OrgAccessGrantStatus.Active, Role: OrgRole.Owner })
            throw new DomainException("Active owner grant not found.");

        var subject = await session.LoadAsync<SubjectReadModel>(parsedSubjectId.Value, ct);

        session.Events.Append(grant.StreamId,
            new OrgAccessRoleChanged(tenant.TenantId, tenant.TenantId, parsedSubjectId, OrgRole.Owner, request.NewRole));

        audit.Record(session, OrgAudit.Record(
            OrgAuditActions.OwnerDowngraded,
            AuditCategories.Access,
            AuditSeverities.Critical,
            actor,
            tenant.TenantId.Value,
            tenant.TenantId.Value,
            request.Reason,
            new { SubjectId = subject?.PublicId, SubjectEmailMasked = AuditIdentity.MaskEmail(subject?.Email) },
            subjectId: subject?.Id,
            changes: [new AuditFieldChange("role", OrgRole.Owner.ToString(), request.NewRole.ToString())],
            targetPublicId: brandId,
            targetType: "brand"));

        // After downgrade, descendant grants that were redundant may no longer be redundant.
        // We do not auto-restore them; explicit re-grant is required.
        // But if the new role is stronger than some descendants, we should clean up those descendants.
        await outbox.PublishAsync(new CleanupRedundantGrants(tenant.TenantId, tenant.TenantId, parsedSubjectId, request.NewRole));

        await session.SaveChangesAsync(ct);
        var updated = await session.LoadAsync<OrgAccessGrantReadModel>(id, ct)
            ?? throw new InvalidOperationException("Projection failed to update OrgAccessGrantReadModel.");

        return new GrantResponse(updated.Id, brandId, subjectId, updated.Role, updated.ExpiresAt);
    }

    [WolverinePost("/api/brands/{brandId}/owners/{subjectId}/revoke")]
    public static async Task<IResult> RevokeOwner(
        RevokeOwnerRequest request,
        string brandId,
        string subjectId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IAuditWriter audit,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var session = SessionFactory.TenantSession(store, tenant);
        SessionMetadata.StampActor(session, actor);
        authorization.EnforceSuperAdmin(actor);

        if (!OrgPublicId.TryParseSubject(subjectId, out var parsedSubjectId))
            throw new DomainException("Invalid subject ID.");

        var id = OrgAccessGrantReadModel.BuildId(tenant.TenantId, tenant.TenantId, parsedSubjectId);
        var grant = await session.LoadAsync<OrgAccessGrantReadModel>(id, ct);
        if (grant is not { Status: OrgAccessGrantStatus.Active, Role: OrgRole.Owner })
            throw new DomainException("Active owner grant not found.");

        var subject = await session.LoadAsync<SubjectReadModel>(parsedSubjectId.Value, ct);

        session.Events.Append(grant.StreamId,
            new OrgAccessRevoked(tenant.TenantId, tenant.TenantId, parsedSubjectId, OrgAccessRevokedReason.UserRequested));

        audit.Record(session, OrgAudit.Record(
            OrgAuditActions.OwnerRevoked,
            AuditCategories.Access,
            AuditSeverities.Critical,
            actor,
            tenant.TenantId.Value,
            tenant.TenantId.Value,
            request.Reason,
            new { SubjectId = subject?.PublicId, SubjectEmailMasked = AuditIdentity.MaskEmail(subject?.Email) },
            subjectId: subject?.Id,
            targetPublicId: brandId,
            targetType: "brand"));

        await session.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
