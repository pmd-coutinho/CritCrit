using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Api.Org.Infrastructure;
using CritCrit.Api.Org.Invitations;
using Marten;
using Wolverine;
using Wolverine.Http;

namespace CritCrit.Api.Org.Features.Invitations;

public static class InvitationHandlers
{
    // CreateInvitation moved to CreateInvitationEndpoint.
    // CancelInvitation moved to CancelInvitationEndpoint.

    [WolverineGet("/api/brands/{brandId}/invitations/{invitationId}")]
    public static async Task<InvitationResponse> GetInvitation(
        string brandId,
        InvitationId invitationId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IAuditWriter audit,
        ActorContext actor,
        CancellationToken ct)
    {
        var invitation = await LoadInvitationAsync(invitationId, store, ct);
        await EnsureInvitationVisibleAsync(store, authorization, actor, invitation, ct);
        return ToResponse(invitation);
    }

    [WolverineGet("/api/brands/{brandId}/invitations")]
    public static async Task<IReadOnlyList<InvitationResponse>> ListInvitations(
        string brandId,
        string? status,
        string? orgNodeId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var platform = store.QuerySession();
        var query = platform.Query<InvitationReadModel>()
            .Where(x => x.TenantId == tenant.TenantId.Value);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<InvitationStatus>(status, true, out var parsedStatus))
            query = query.Where(x => x.Status == parsedStatus);

        if (!string.IsNullOrWhiteSpace(orgNodeId))
        {
            if (!OrgPublicId.TryParseOrgNode(orgNodeId, out var nodeId, out _))
                throw new DomainException("Invalid org node ID.");
            query = query.Where(x => x.TargetOrgNodeId == nodeId.Value);
        }

        var items = await query.OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        if (actor.IsSuperAdmin)
            return items.Select(ToResponse).ToArray();

        var visible = new List<InvitationResponse>();
        foreach (var item in items)
        {
            if (await CanManageInvitationAsync(store, authorization, actor, item, ct))
                visible.Add(ToResponse(item));
        }

        return visible;
    }

    [WolverinePost("/api/brands/{brandId}/invitations/{invitationId}/resend")]
    public static async Task<InvitationResponse> ResendInvitation(
        string brandId,
        InvitationId invitationId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IMessageBus bus,
        IAuditWriter audit,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var platformSession = SessionFactory.PlatformSession(store);
        SessionMetadata.StampActor(platformSession, actor);
        var invitation = await LoadInvitationAsync(invitationId, platformSession, ct);

        if (invitation.Status != InvitationStatus.Pending)
            throw new DomainException("Only pending invitations can be resent.");

        await EnsureInvitationManageableAsync(store, authorization, actor, invitation, ct);

        var expiresAt = TimeProvider.System.GetUtcNow().AddDays(1);
        audit.Record(platformSession, OrgAudit.Record(
            OrgAuditActions.InvitationResent,
            AuditCategories.Invitation,
            AuditSeverities.Info,
            actor,
            invitation.TenantId,
            invitation.TargetOrgNodeId,
            details: OrgAudit.InviteDetails(invitation)));
        await platformSession.SaveChangesAsync(ct);

        await bus.InvokeAsync(new SendInvitationEmail(new InvitationId(invitation.Id), RequiresPasswordSetup: false, 1, new MessageAuditContext(SupportId.Current)));
        await bus.ScheduleAsync(new ExpireInvitation(new InvitationId(invitation.Id), expiresAt, new MessageAuditContext(SupportId.Current)), expiresAt.UtcDateTime);

        var updated = await platformSession.LoadAsync<InvitationReadModel>(invitation.Id, ct)
            ?? throw new InvalidOperationException("Invitation projection missing after resend.");
        return ToResponse(updated);
    }

    [WolverineGet("/api/invitations/accept")]
    public static async Task<IResult> AcceptInvitation(
        string token,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IAuditWriter audit,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var tokenService = httpContext.RequestServices.GetRequiredService<InvitationTokenService>();
        await using var platformSession = SessionFactory.PlatformSession(store);
        var tokenHash = tokenService.Hash(token);
        var invitation = await platformSession.Query<InvitationReadModel>()
            .Where(x => x.TokenHash == tokenHash)
            .SingleOrDefaultAsync(ct);

        if (invitation is null)
        {
            await audit.RecordDeniedAsync(OrgAudit.SystemRecord(
                OrgAuditActions.InvitationSecurityFailed,
                AuditCategories.Security,
                AuditSeverities.Warn,
                AuditActor.UnauthenticatedSystem(),
                null,
                null,
                details: new { FailureCode = "invalid_or_expired_token" }), ct);
            throw new DomainException("Invitation token is invalid or has expired.", 404);
        }

        if (httpContext.User.Identity?.IsAuthenticated != true)
            return Results.Challenge();

        await using var resolveSession = store.QuerySession();
        var actor = await ActorContextResolver.ResolveAsync(resolveSession, httpContext.User, ct);
        if (!actor.IsAuthenticated)
            throw new DomainException("Authentication required.", 401);
        SessionMetadata.StampActor(platformSession, actor);

        if (actor.SubjectId is null || invitation.SubjectId != actor.SubjectId.Value.Value)
        {
            await audit.RecordDeniedAsync(OrgAudit.Record(
                OrgAuditActions.InvitationSecurityFailed,
                AuditCategories.Security,
                AuditSeverities.Warn,
                actor,
                invitation.TenantId,
                invitation.TargetOrgNodeId,
                details: OrgAudit.InviteDetails(invitation, new { FailureCode = "actor_mismatch" }),
                subjectId: invitation.SubjectId), ct);
            throw new DomainException("The authenticated user does not match this invitation.", 403);
        }

        var acceptedId = new InvitationId(invitation.Id);
        var acceptedAt = TimeProvider.System.GetUtcNow();

        if (invitation.Status != InvitationStatus.Pending)
        {
            await audit.RecordDeniedAsync(OrgAudit.Record(
                OrgAuditActions.InvitationSecurityFailed,
                AuditCategories.Security,
                AuditSeverities.Warn,
                actor,
                invitation.TenantId,
                invitation.TargetOrgNodeId,
                details: OrgAudit.InviteDetails(invitation, new { FailureCode = "not_pending" }),
                subjectId: invitation.SubjectId), ct);
            throw new DomainException("Invitation is no longer pending.");
        }
        if (invitation.ExpiresAt is not null && invitation.ExpiresAt <= acceptedAt)
        {
            await audit.RecordDeniedAsync(OrgAudit.Record(
                OrgAuditActions.InvitationSecurityFailed,
                AuditCategories.Security,
                AuditSeverities.Warn,
                actor,
                invitation.TenantId,
                invitation.TargetOrgNodeId,
                details: OrgAudit.InviteDetails(invitation, new { FailureCode = "expired" }),
                subjectId: invitation.SubjectId), ct);
            throw new DomainException("Invitation has expired.");
        }

        var subject = await platformSession.LoadAsync<SubjectReadModel>(invitation.SubjectId.Value, ct)
            ?? throw new DomainException("Invitation subject is missing.");

        var currentTarget = await ExecuteInvitationAgainstTargetAsync(
            store,
            platformSession,
            authorization,
            actor,
            invitation,
            acceptedId,
            acceptedAt,
            accepted: true,
            audit,
            ct);

        if (currentTarget.Outcome == InvitationApplyOutcome.Obsolete)
        {
            await platformSession.SaveChangesAsync(ct);
            var obsolete = await platformSession.LoadAsync<InvitationReadModel>(invitation.Id, ct)
                ?? throw new InvalidOperationException("Invitation projection missing after obsoletion.");
            return Results.Ok(new AcceptInvitationResponse(obsolete.PublicId, obsolete.Status, false, false, 0));
        }

        var subjectOnboarded = subject.OnboardedAt is null;
        if (subjectOnboarded)
        {
            platformSession.Events.Append(subject.Id, new SubjectOnboarded(new SubjectId(subject.Id), acceptedAt));
        }

        platformSession.Events.Append(invitation.Id,
            new InvitationAccepted(acceptedId, acceptedAt, currentTarget.GrantCreated, subjectOnboarded));
        audit.Record(platformSession, OrgAudit.Record(
            OrgAuditActions.InvitationAccepted,
            AuditCategories.Invitation,
            AuditSeverities.Info,
            actor,
            invitation.TenantId,
            invitation.TargetOrgNodeId,
            details: OrgAudit.InviteDetails(invitation, new { currentTarget.GrantCreated }),
            subjectId: invitation.SubjectId));

        var autoApplied = 0;
        if (subjectOnboarded)
        {
            var pending = await platformSession.Query<InvitationReadModel>()
                .Where(x =>
                    x.Id != invitation.Id &&
                    x.SubjectId == invitation.SubjectId &&
                    x.Status == InvitationStatus.Pending)
                .ToListAsync(ct);

            foreach (var pendingInvitation in await OrderInvitationsForAutoApplyAsync(store, pending, ct))
            {
                var result = await ExecuteInvitationAgainstTargetAsync(
                    store,
                    platformSession,
                    authorization,
                    actor,
                    pendingInvitation,
                    new InvitationId(pendingInvitation.Id),
                    acceptedAt,
                    accepted: false,
                    audit,
                    ct);

                if (result.Outcome != InvitationApplyOutcome.Obsolete)
                {
                    platformSession.Events.Append(pendingInvitation.Id,
                        new InvitationAutoApplied(new InvitationId(pendingInvitation.Id), acceptedAt, result.GrantCreated));
                    audit.Record(platformSession, OrgAudit.Record(
                        OrgAuditActions.InvitationAutoApplied,
                        AuditCategories.Invitation,
                        AuditSeverities.Info,
                        actor,
                        pendingInvitation.TenantId,
                        pendingInvitation.TargetOrgNodeId,
                        details: OrgAudit.InviteDetails(pendingInvitation, new { result.GrantCreated }),
                        subjectId: pendingInvitation.SubjectId));
                    autoApplied++;
                }
            }
        }

        await platformSession.SaveChangesAsync(ct);
        var updated = await platformSession.LoadAsync<InvitationReadModel>(invitation.Id, ct)
            ?? throw new InvalidOperationException("Invitation projection missing after acceptance.");

        return Results.Ok(new AcceptInvitationResponse(
            updated.PublicId,
            updated.Status,
            currentTarget.GrantCreated,
            subjectOnboarded,
            autoApplied));
    }

    public static InvitationResponse ToResponse(InvitationReadModel invitation) =>
        new(
            invitation.PublicId,
            invitation.TenantPublicId,
            invitation.TargetOrgNodePublicId,
            invitation.Email,
            invitation.SubjectPublicId,
            invitation.Role,
            invitation.Status,
            invitation.CreatedAt,
            invitation.ExpiresAt,
            invitation.AcceptedAt,
            invitation.LastSentAt,
            invitation.Failure);

    internal static async Task<InvitationReadModel> LoadInvitationAsync(InvitationId invitationId, IDocumentStore store, CancellationToken ct)
    {
        await using var query = store.QuerySession();
        return await LoadInvitationAsync(invitationId, query, ct);
    }

    internal static async Task<InvitationReadModel> LoadInvitationAsync(InvitationId invitationId, IQuerySession session, CancellationToken ct)
    {
        return await session.LoadAsync<InvitationReadModel>(invitationId.Value, ct)
            ?? throw new DomainException("Invitation was not found.", 404);
    }

    internal static async Task<OrgNodeReadModel> LoadAndAuthorizeTargetAsync(
        IQuerySession tenantSession,
        OrgAuthorizationService authorization,
        ActorContext actor,
        string orgNodeId,
        OrgRole role,
        Guid tenantId,
        CancellationToken ct)
    {
        if (!OrgPublicId.TryParseOrgNode(orgNodeId, out var targetId, out _))
            throw new DomainException("Invalid org node ID.");

        var target = await OrgValidation.LoadActiveNodeAsync(tenantSession, targetId, ct);
        if (target.TenantId != tenantId)
            throw new DomainException("Org node does not belong to the requested brand tenant.");
        if (!OrgRules.CanGrantRoleAt(role, target.Type))
            throw new DomainException($"{role} can only be invited at the Brand root.");

        if (role == OrgRole.Owner)
            authorization.EnforceSuperAdmin(actor);
        else
            await authorization.EnforceRoleAsync(tenantSession, actor, target, OrgRole.Admin, ct);

        return target;
    }

    private static async Task EnsureInvitationVisibleAsync(
        IDocumentStore store,
        OrgAuthorizationService authorization,
        ActorContext actor,
        InvitationReadModel invitation,
        CancellationToken ct)
    {
        if (!await CanManageInvitationAsync(store, authorization, actor, invitation, ct))
            throw new DomainException("The invitation is outside your authorization scope.", 403);
    }

    internal static Task EnsureInvitationManageableAsync(
        IDocumentStore store,
        OrgAuthorizationService authorization,
        ActorContext actor,
        InvitationReadModel invitation,
        CancellationToken ct) =>
        EnsureInvitationVisibleAsync(store, authorization, actor, invitation, ct);

    private static async Task<bool> CanManageInvitationAsync(
        IDocumentStore store,
        OrgAuthorizationService authorization,
        ActorContext actor,
        InvitationReadModel invitation,
        CancellationToken ct)
    {
        if (actor.IsSuperAdmin)
            return true;

        if (actor.SubjectId is null)
            return false;

        await using var session = store.QuerySession(invitation.TenantId.ToString());
        var target = await session.LoadAsync<OrgNodeReadModel>(invitation.TargetOrgNodeId, ct);
        if (target is null || target.HardDeleted)
            return false;

        var required = invitation.Role == OrgRole.Owner ? OrgRole.Owner : OrgRole.Admin;
        var result = await authorization.RequireRoleAsync(session, actor, target, required, TimeProvider.System.GetUtcNow(), ct);
        return result.Succeeded;
    }

    private static async Task<InvitationApplyResult> ExecuteInvitationAgainstTargetAsync(
        IDocumentStore store,
        IDocumentSession platformSession,
        OrgAuthorizationService authorization,
        ActorContext actor,
        InvitationReadModel invitation,
        InvitationId invitationId,
        DateTimeOffset now,
        bool accepted,
        IAuditWriter audit,
        CancellationToken ct)
    {
        await using var tenantSession = store.LightweightSession(invitation.TenantId.ToString());
        SessionMetadata.StampActor(tenantSession, actor);
        var target = await tenantSession.LoadAsync<OrgNodeReadModel>(invitation.TargetOrgNodeId, ct);
        if (target is null || target.HardDeleted || target.EffectiveArchived)
        {
            platformSession.Events.Append(invitation.Id,
                new InvitationObsoleted(invitationId, now, "The target org node is inactive."));
            audit.Record(platformSession, OrgAudit.Record(
                OrgAuditActions.InvitationObsoleted,
                AuditCategories.Invitation,
                AuditSeverities.Warn,
                actor,
                invitation.TenantId,
                invitation.TargetOrgNodeId,
                details: OrgAudit.InviteDetails(invitation, new { Reason = "The target org node is inactive." }),
                subjectId: invitation.SubjectId));
            return new InvitationApplyResult(false, InvitationApplyOutcome.Obsolete);
        }

        var subjectId = new SubjectId(invitation.SubjectId ?? throw new DomainException("Invitation is missing a bound subject."));
        var redundant = await authorization.WouldBeRedundantAsync(tenantSession, target, subjectId, invitation.Role, now, ct);
        if (redundant)
            return new InvitationApplyResult(false, InvitationApplyOutcome.NoChange);

        var grantId = OrgAccessGrantReadModel.BuildId(new OrgNodeId(invitation.TenantId), new OrgNodeId(invitation.TargetOrgNodeId), subjectId);
        var existingGrant = await tenantSession.LoadAsync<OrgAccessGrantReadModel>(grantId, ct);
        var created = false;

        if (existingGrant is { Status: OrgAccessGrantStatus.Active })
        {
            if (existingGrant.Role < invitation.Role)
            {
                tenantSession.Events.Append(existingGrant.StreamId,
                    new OrgAccessRoleChanged(new OrgNodeId(invitation.TenantId), new OrgNodeId(invitation.TargetOrgNodeId), subjectId, existingGrant.Role, invitation.Role));
                created = true;
            }
        }
        else if (existingGrant is { StreamId: var streamId } && streamId != Guid.Empty)
        {
            tenantSession.Events.Append(existingGrant.StreamId,
                new OrgAccessGranted(new OrgNodeId(invitation.TenantId), new OrgNodeId(invitation.TargetOrgNodeId), subjectId, invitation.Role, null, OrgAccessGrantSource.Invitation, invitationId));
            created = true;
        }
        else
        {
            // Deterministic stream id per .scratch/deterministic-stream-ids/PRD.md.
            var newStreamId = DeterministicGuid.From(invitation.TenantId, invitation.TargetOrgNodeId, subjectId.Value);
            tenantSession.Events.StartStream<OrgAccessGrantReadModel>(newStreamId,
                new OrgAccessGranted(new OrgNodeId(invitation.TenantId), new OrgNodeId(invitation.TargetOrgNodeId), subjectId, invitation.Role, null, OrgAccessGrantSource.Invitation, invitationId));
            created = true;
        }

        await tenantSession.SaveChangesAsync(ct);
        return new InvitationApplyResult(created, InvitationApplyOutcome.Applied);
    }

    private static async Task<IReadOnlyList<InvitationReadModel>> OrderInvitationsForAutoApplyAsync(
        IDocumentStore store,
        IReadOnlyList<InvitationReadModel> invitations,
        CancellationToken ct)
    {
        var depths = new Dictionary<Guid, int>();
        foreach (var invitation in invitations)
        {
            await using var session = store.QuerySession(invitation.TenantId.ToString());
            var node = await session.LoadAsync<OrgNodeReadModel>(invitation.TargetOrgNodeId, ct);
            depths[invitation.Id] = node?.AncestorIds.Count ?? int.MaxValue;
        }

        return invitations
            .OrderByDescending(x => x.Role)
            .ThenBy(x => depths[x.Id])
            .ThenBy(x => x.CreatedAt)
            .ToArray();
    }

    private sealed record InvitationApplyResult(bool GrantCreated, InvitationApplyOutcome Outcome);

    private enum InvitationApplyOutcome
    {
        Applied,
        NoChange,
        Obsolete
    }
}
