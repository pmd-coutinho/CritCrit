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
    [WolverinePost("/api/brands/{brandId}/invitations")]
    public static async Task<IResult> CreateInvitation(
        CreateInvitationRequest request,
        string brandId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IMessageBus bus,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var tenantSession = SessionFactory.TenantSession(store, tenant);
        var target = await LoadAndAuthorizeTargetAsync(tenantSession, authorization, actor, request.OrgNodeId, request.Role, tenant.TenantId.Value, ct);

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        await using var platformSession = SessionFactory.PlatformSession(store);
        SessionMetadata.StampActor(platformSession, actor);

        var existingSubject = await platformSession.Query<SubjectReadModel>()
            .Where(x => x.EmailNormalized == normalizedEmail)
            .SingleOrDefaultAsync(ct);
        if (existingSubject is { Active: false })
            throw new DomainException("This subject is deactivated. Reactivate them before re-inviting.");

        var activeInvitation = await platformSession.Query<InvitationReadModel>()
            .Where(x =>
                x.TenantId == tenant.TenantId.Value &&
                x.TargetOrgNodeId == target.Id &&
                x.EmailNormalized == normalizedEmail &&
                (x.Status == InvitationStatus.Requested ||
                 x.Status == InvitationStatus.Provisioning ||
                 x.Status == InvitationStatus.Pending))
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);

        var invitationId = InvitationId.New();
        var now = TimeProvider.System.GetUtcNow();

        if (activeInvitation is not null)
        {
            platformSession.Events.Append(activeInvitation.Id,
                new InvitationSuperseded(new InvitationId(activeInvitation.Id), invitationId, now));
        }

        platformSession.Events.StartStream<InvitationReadModel>(invitationId.Value,
            new InvitationRequested(
                invitationId,
                tenant.TenantId,
                new OrgNodeId(target.Id),
                request.OrgNodeId,
                request.Email.Trim(),
                request.Role,
                actor.SubjectId,
                actor.ExternalId,
                now));

        AuditLog.Write(platformSession, OrgAuditActions.InvitationRequested, actor, tenant.TenantId.Value, target.Id, null, new
        {
            InvitationId = OrgPublicId.FormatInvitation(invitationId),
            SubjectEmail = request.Email.Trim(),
            Role = request.Role.ToString()
        });

        await platformSession.SaveChangesAsync(ct);
        await bus.InvokeAsync(new ProvisionInvitation(invitationId));

        var created = await platformSession.LoadAsync<InvitationReadModel>(invitationId.Value, ct)
            ?? throw new InvalidOperationException("Projection failed to create InvitationReadModel.");

        return Results.Accepted($"/api/brands/{brandId}/invitations/{created.PublicId}", ToResponse(created));
    }

    [WolverineGet("/api/brands/{brandId}/invitations/{invitationId}")]
    public static async Task<InvitationResponse> GetInvitation(
        string brandId,
        string invitationId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
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

    [WolverinePost("/api/brands/{brandId}/invitations/{invitationId}/cancel")]
    public static async Task<InvitationResponse> CancelInvitation(
        string brandId,
        string invitationId,
        CancelInvitationRequest request,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var platformSession = SessionFactory.PlatformSession(store);
        SessionMetadata.StampActor(platformSession, actor);
        var invitation = await LoadInvitationAsync(invitationId, platformSession, ct);

        if (!invitation.IsPendingLike())
            throw new DomainException("Only pending invitations can be cancelled.");

        await EnsureInvitationManageableAsync(store, authorization, actor, invitation, ct);

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        platformSession.Events.Append(invitation.Id,
            new InvitationCancelled(new InvitationId(invitation.Id), TimeProvider.System.GetUtcNow(), reason));

        AuditLog.Write(platformSession, OrgAuditActions.InvitationCancelled, actor, invitation.TenantId, invitation.TargetOrgNodeId, reason, new
        {
            invitation.PublicId,
            invitation.Email,
            Role = invitation.Role.ToString()
        });

        await platformSession.SaveChangesAsync(ct);
        var updated = await platformSession.LoadAsync<InvitationReadModel>(invitation.Id, ct)
            ?? throw new InvalidOperationException("Invitation projection missing after cancellation.");
        return ToResponse(updated);
    }

    [WolverinePost("/api/brands/{brandId}/invitations/{invitationId}/resend")]
    public static async Task<InvitationResponse> ResendInvitation(
        string brandId,
        string invitationId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IMessageBus bus,
        InvitationTokenService tokens,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var platformSession = SessionFactory.PlatformSession(store);
        SessionMetadata.StampActor(platformSession, actor);
        var invitation = await LoadInvitationAsync(invitationId, platformSession, ct);

        if (invitation.Status != InvitationStatus.Pending)
            throw new DomainException("Only pending invitations can be resent.");

        await EnsureInvitationManageableAsync(store, authorization, actor, invitation, ct);

        var rawToken = tokens.GenerateRawToken();
        var expiresAt = TimeProvider.System.GetUtcNow().AddDays(1);
        platformSession.Events.Append(invitation.Id,
            new InvitationTokenIssued(new InvitationId(invitation.Id), tokens.Hash(rawToken), expiresAt));
        platformSession.Events.Append(invitation.Id,
            new InvitationMarkedPending(new InvitationId(invitation.Id), TimeProvider.System.GetUtcNow()));
        await platformSession.SaveChangesAsync(ct);

        await bus.InvokeAsync(new SendInvitationEmail(new InvitationId(invitation.Id), rawToken, RequiresPasswordSetup: false, 1));
        await bus.ScheduleAsync(new ExpireInvitation(new InvitationId(invitation.Id), expiresAt), expiresAt.UtcDateTime);

        var updated = await platformSession.LoadAsync<InvitationReadModel>(invitation.Id, ct)
            ?? throw new InvalidOperationException("Invitation projection missing after resend.");
        return ToResponse(updated);
    }

    [WolverineGet("/api/invitations/accept")]
    public static async Task<IResult> AcceptInvitation(
        string token,
        IDocumentStore store,
        OrgAuthorizationService authorization,
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
            throw new DomainException("Invitation token is invalid or has expired.", 404);

        if (httpContext.User.Identity?.IsAuthenticated != true)
            return Results.Challenge();

        await using var resolveSession = store.QuerySession();
        var actor = await ActorContextResolver.ResolveAsync(resolveSession, httpContext.User, ct);
        if (!actor.IsAuthenticated)
            throw new DomainException("Authentication required.", 401);
        SessionMetadata.StampActor(platformSession, actor);

        if (actor.SubjectId is null || invitation.SubjectId != actor.SubjectId.Value.Value)
            throw new DomainException("The authenticated user does not match this invitation.", 403);

        var acceptedId = new InvitationId(invitation.Id);
        var acceptedAt = TimeProvider.System.GetUtcNow();

        if (invitation.Status != InvitationStatus.Pending)
            throw new DomainException("Invitation is no longer pending.");
        if (invitation.ExpiresAt is not null && invitation.ExpiresAt <= acceptedAt)
            throw new DomainException("Invitation has expired.");

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
        AuditLog.Write(platformSession, OrgAuditActions.InvitationAccepted, actor, invitation.TenantId, invitation.TargetOrgNodeId, null, new
        {
            invitation.PublicId,
            invitation.Email,
            Role = invitation.Role.ToString(),
            currentTarget.GrantCreated
        });

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
                    ct);

                if (result.Outcome != InvitationApplyOutcome.Obsolete)
                {
                    platformSession.Events.Append(pendingInvitation.Id,
                        new InvitationAutoApplied(new InvitationId(pendingInvitation.Id), acceptedAt, result.GrantCreated));
                    AuditLog.Write(platformSession, OrgAuditActions.InvitationAutoApplied, actor, pendingInvitation.TenantId, pendingInvitation.TargetOrgNodeId, null, new
                    {
                        pendingInvitation.PublicId,
                        pendingInvitation.Email,
                        Role = pendingInvitation.Role.ToString(),
                        result.GrantCreated
                    });
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

    private static async Task<InvitationReadModel> LoadInvitationAsync(string invitationId, IDocumentStore store, CancellationToken ct)
    {
        await using var query = store.QuerySession();
        return await LoadInvitationAsync(invitationId, query, ct);
    }

    private static async Task<InvitationReadModel> LoadInvitationAsync(string invitationId, IQuerySession session, CancellationToken ct)
    {
        if (!OrgPublicId.TryParseInvitation(invitationId, out var parsed))
            throw new DomainException("Invalid invitation ID.");

        return await session.LoadAsync<InvitationReadModel>(parsed.Value, ct)
            ?? throw new DomainException("Invitation was not found.", 404);
    }

    private static async Task<OrgNodeReadModel> LoadAndAuthorizeTargetAsync(
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

    private static Task EnsureInvitationManageableAsync(
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
        CancellationToken ct)
    {
        await using var tenantSession = store.LightweightSession(invitation.TenantId.ToString());
        SessionMetadata.StampActor(tenantSession, actor);
        var target = await tenantSession.LoadAsync<OrgNodeReadModel>(invitation.TargetOrgNodeId, ct);
        if (target is null || target.HardDeleted || target.EffectiveArchived)
        {
            platformSession.Events.Append(invitation.Id,
                new InvitationObsoleted(invitationId, now, "The target org node is inactive."));
            AuditLog.Write(platformSession, OrgAuditActions.InvitationObsoleted, actor, invitation.TenantId, invitation.TargetOrgNodeId, null, new
            {
                invitation.PublicId,
                invitation.Email
            });
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
            var newStreamId = Guid.CreateVersion7();
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
