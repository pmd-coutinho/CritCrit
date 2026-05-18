using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Api.Org.Features.Invitations;
using CritCrit.Api.Org.Infrastructure;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;
using Wolverine.Marten;

namespace CritCrit.Api.Org.Features.Owners;

// Refactor wave 1 (per .scratch/aggregate-handler-workflow/issues/01-owner-pilot.md):
// each Owner endpoint lives in its own static class so Wolverine.Http can dispatch
// the convention methods `Validate` and `LoadAsync` per endpoint without overload
// collisions. Pure invariants are in `OwnerRules`. Session-opening and event
// emission remain inline pending the deterministic-stream-ids prerequisite that
// unlocks `[AggregateHandler]`.

public sealed record GrantOwnerContext(
    SubjectId SubjectId,
    OrgNodeReadModel Root,
    SubjectReadModel? Subject,
    OrgAccessGrantReadModel? Grant);

public sealed record OwnerTransitionContext(
    SubjectId SubjectId,
    OrgAccessGrantReadModel? Grant,
    SubjectReadModel? Subject);

public static class GrantOwnerEndpoint
{
    public static ProblemDetails Validate(GrantOwnerRequest request) =>
        string.IsNullOrWhiteSpace(request.SubjectId)
            ? new ProblemDetails { Title = "subjectId", Detail = "SubjectId is required.", Status = 400 }
            : WolverineContinue.NoProblems;

    public static async Task<GrantOwnerContext> LoadAsync(
        GrantOwnerRequest request,
        IDocumentStore store,
        BrandTenantContext tenant,
        CancellationToken ct)
    {
        if (!OrgPublicId.TryParseSubject(request.SubjectId, out var subjectId))
            throw new DomainException("Invalid subject ID.");

        await using var query = store.QuerySession(tenant.TenantId.Value.ToString());
        var root = await OrgValidation.LoadActiveNodeAsync(query, tenant.TenantId, ct);
        var subject = await query.LoadAsync<SubjectReadModel>(subjectId.Value, ct);
        var grantId = OrgAccessGrantReadModel.BuildId(tenant.TenantId, tenant.TenantId, subjectId);
        var grant = await query.LoadAsync<OrgAccessGrantReadModel>(grantId, ct);
        return new GrantOwnerContext(subjectId, root, subject, grant);
    }

    [WolverinePost("/api/brands/{brandId}/owners")]
    public static async Task<IResult> Handle(
        GrantOwnerRequest request,
        string brandId,
        GrantOwnerContext loaded,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IMartenOutbox outbox,
        IAuditWriter audit,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        authorization.EnforceSuperAdmin(actor);
        OwnerRules.RequireBrandRoot(loaded.Root);
        OwnerRules.RequireActiveSubject(loaded.Subject);

        var now = TimeProvider.System.GetUtcNow();
        if (await authorization.WouldBeRedundantAsync(store.QuerySession(), loaded.Root, loaded.SubjectId, OrgRole.Owner, now, ct))
            throw new DomainException("Grant would be redundant with inherited access.");

        await using var session = SessionFactory.TenantSession(store, tenant);
        SessionMetadata.StampActor(session, actor);
        outbox.Enroll(session);

        if (loaded.Grant is { Status: OrgAccessGrantStatus.Active })
        {
            OwnerRules.RequireNotAlreadyOwner(loaded.Grant);

            session.Events.Append(loaded.Grant.StreamId,
                new OrgAccessRoleChanged(tenant.TenantId, tenant.TenantId, loaded.SubjectId, loaded.Grant.Role, OrgRole.Owner));

            audit.Record(session, OrgAudit.Record(
                OrgAuditActions.OwnerGranted,
                AuditCategories.Access,
                AuditSeverities.Critical,
                actor,
                tenant.TenantId.Value,
                tenant.TenantId.Value,
                details: new { SubjectId = loaded.Subject!.PublicId, SubjectEmailMasked = AuditIdentity.MaskEmail(loaded.Subject.Email) },
                subjectId: loaded.Subject.Id,
                changes: [new AuditFieldChange("role", loaded.Grant.Role.ToString(), OrgRole.Owner.ToString())],
                targetPublicId: brandId,
                targetType: "brand"));

            await session.SaveChangesAsync(ct);
            var updated = await session.LoadAsync<OrgAccessGrantReadModel>(loaded.Grant.Id, ct)
                ?? throw new InvalidOperationException("Projection failed to update OrgAccessGrantReadModel.");
            return Results.Ok(new GrantResponse(updated.Id, brandId, request.SubjectId, updated.Role, updated.ExpiresAt));
        }

        // Deterministic stream id per .scratch/deterministic-stream-ids/PRD.md.
        // Existing grants from before this migration still carry random ids on
        // their docs; that branch above (loaded.Grant is { Status: Active })
        // honours them via loaded.Grant.StreamId.
        var streamId = DeterministicGuid.From(tenant.TenantId.Value, tenant.TenantId.Value, loaded.SubjectId.Value);
        session.Events.StartStream<OrgAccessGrantReadModel>(streamId,
            new OrgAccessGranted(tenant.TenantId, tenant.TenantId, loaded.SubjectId, OrgRole.Owner, null, OrgAccessGrantSource.DirectGrant, null));

        audit.Record(session, OrgAudit.Record(
            OrgAuditActions.OwnerGranted,
            AuditCategories.Access,
            AuditSeverities.Critical,
            actor,
            tenant.TenantId.Value,
            tenant.TenantId.Value,
            details: new { SubjectId = loaded.Subject!.PublicId, SubjectEmailMasked = AuditIdentity.MaskEmail(loaded.Subject.Email) },
            subjectId: loaded.Subject.Id,
            changes: [new AuditFieldChange("role", null, OrgRole.Owner.ToString())],
            targetPublicId: brandId,
            targetType: "brand"));

        await outbox.PublishAsync(new CleanupRedundantGrants(tenant.TenantId, tenant.TenantId, loaded.SubjectId, OrgRole.Owner));

        await session.SaveChangesAsync(ct);
        var grantDocId = OrgAccessGrantReadModel.BuildId(tenant.TenantId, tenant.TenantId, loaded.SubjectId);
        var created = await session.LoadAsync<OrgAccessGrantReadModel>(grantDocId, ct)
            ?? throw new InvalidOperationException("Projection failed to create OrgAccessGrantReadModel.");

        return Results.Created($"/api/brands/{brandId}/access-grants/{grantDocId}",
            new GrantResponse(created.Id, brandId, request.SubjectId, created.Role, created.ExpiresAt));
    }
}

public static class DowngradeOwnerEndpoint
{
    public static ProblemDetails Validate(DowngradeOwnerRequest request)
    {
        if (request.NewRole == OrgRole.Owner)
            return new ProblemDetails { Title = "newRole", Detail = "Downgrade target role cannot be Owner.", Status = 400 };
        if (string.IsNullOrWhiteSpace(request.Reason))
            return new ProblemDetails { Title = "reason", Detail = "Reason is required.", Status = 400 };
        if (request.Reason.Length > 500)
            return new ProblemDetails { Title = "reason", Detail = "Reason must be 500 characters or fewer.", Status = 400 };
        return WolverineContinue.NoProblems;
    }

    public static async Task<OwnerTransitionContext> LoadAsync(
        DowngradeOwnerRequest request,
        SubjectId subjectId,
        IDocumentStore store,
        BrandTenantContext tenant,
        CancellationToken ct)
    {
        await using var query = store.QuerySession(tenant.TenantId.Value.ToString());
        var grantId = OrgAccessGrantReadModel.BuildId(tenant.TenantId, tenant.TenantId, subjectId);
        var grant = await query.LoadAsync<OrgAccessGrantReadModel>(grantId, ct);
        var subject = await query.LoadAsync<SubjectReadModel>(subjectId.Value, ct);
        return new OwnerTransitionContext(subjectId, grant, subject);
    }

    [WolverinePost("/api/brands/{brandId}/owners/{subjectId}/downgrade")]
    public static async Task<GrantResponse> Handle(
        DowngradeOwnerRequest request,
        string brandId,
        SubjectId subjectId,
        OwnerTransitionContext loaded,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IMartenOutbox outbox,
        IAuditWriter audit,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        authorization.EnforceSuperAdmin(actor);
        OwnerRules.RequireDowngradeTarget(request.NewRole);
        OwnerRules.RequireActiveOwnerGrant(loaded.Grant);

        await using var session = SessionFactory.TenantSession(store, tenant);
        SessionMetadata.StampActor(session, actor);
        outbox.Enroll(session);

        session.Events.Append(loaded.Grant!.StreamId,
            new OrgAccessRoleChanged(tenant.TenantId, tenant.TenantId, loaded.SubjectId, OrgRole.Owner, request.NewRole));

        audit.Record(session, OrgAudit.Record(
            OrgAuditActions.OwnerDowngraded,
            AuditCategories.Access,
            AuditSeverities.Critical,
            actor,
            tenant.TenantId.Value,
            tenant.TenantId.Value,
            request.Reason,
            new { SubjectId = loaded.Subject?.PublicId, SubjectEmailMasked = AuditIdentity.MaskEmail(loaded.Subject?.Email) },
            subjectId: loaded.Subject?.Id,
            changes: [new AuditFieldChange("role", OrgRole.Owner.ToString(), request.NewRole.ToString())],
            targetPublicId: brandId,
            targetType: "brand"));

        await outbox.PublishAsync(new CleanupRedundantGrants(tenant.TenantId, tenant.TenantId, loaded.SubjectId, request.NewRole));

        await session.SaveChangesAsync(ct);
        var updated = await session.LoadAsync<OrgAccessGrantReadModel>(loaded.Grant.Id, ct)
            ?? throw new InvalidOperationException("Projection failed to update OrgAccessGrantReadModel.");

        return new GrantResponse(updated.Id, brandId, OrgPublicId.FormatSubject(subjectId), updated.Role, updated.ExpiresAt);
    }
}

public static class RevokeOwnerEndpoint
{
    public static ProblemDetails Validate(RevokeOwnerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return new ProblemDetails { Title = "reason", Detail = "Reason is required.", Status = 400 };
        if (request.Reason.Length > 500)
            return new ProblemDetails { Title = "reason", Detail = "Reason must be 500 characters or fewer.", Status = 400 };
        return WolverineContinue.NoProblems;
    }

    public static async Task<OwnerTransitionContext> LoadAsync(
        RevokeOwnerRequest request,
        SubjectId subjectId,
        IDocumentStore store,
        BrandTenantContext tenant,
        CancellationToken ct)
    {
        await using var query = store.QuerySession(tenant.TenantId.Value.ToString());
        var grantId = OrgAccessGrantReadModel.BuildId(tenant.TenantId, tenant.TenantId, subjectId);
        var grant = await query.LoadAsync<OrgAccessGrantReadModel>(grantId, ct);
        var subject = await query.LoadAsync<SubjectReadModel>(subjectId.Value, ct);
        return new OwnerTransitionContext(subjectId, grant, subject);
    }

    [WolverinePost("/api/brands/{brandId}/owners/{subjectId}/revoke")]
    public static async Task<IResult> Handle(
        RevokeOwnerRequest request,
        string brandId,
        SubjectId subjectId,
        OwnerTransitionContext loaded,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IAuditWriter audit,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        authorization.EnforceSuperAdmin(actor);
        OwnerRules.RequireActiveOwnerGrant(loaded.Grant);

        await using var session = SessionFactory.TenantSession(store, tenant);
        SessionMetadata.StampActor(session, actor);

        session.Events.Append(loaded.Grant!.StreamId,
            new OrgAccessRevoked(tenant.TenantId, tenant.TenantId, loaded.SubjectId, OrgAccessRevokedReason.UserRequested));

        audit.Record(session, OrgAudit.Record(
            OrgAuditActions.OwnerRevoked,
            AuditCategories.Access,
            AuditSeverities.Critical,
            actor,
            tenant.TenantId.Value,
            tenant.TenantId.Value,
            request.Reason,
            new { SubjectId = loaded.Subject?.PublicId, SubjectEmailMasked = AuditIdentity.MaskEmail(loaded.Subject?.Email) },
            subjectId: loaded.Subject?.Id,
            targetPublicId: brandId,
            targetType: "brand"));

        await session.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
