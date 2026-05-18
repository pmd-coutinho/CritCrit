using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Api.Org.Infrastructure;
using CritCrit.Api.Org.Invitations;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine;
using Wolverine.Http;

namespace CritCrit.Api.Org.Features.Invitations;

public static class CreateInvitationEndpoint
{
    public static ProblemDetails Validate(CreateInvitationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OrgNodeId))
            return new ProblemDetails { Title = "orgNodeId", Detail = "orgNodeId is required.", Status = 400 };
        if (string.IsNullOrWhiteSpace(request.Email))
            return new ProblemDetails { Title = "email", Detail = "email is required.", Status = 400 };
        var at = request.Email.IndexOf('@');
        if (at <= 0 || at == request.Email.Length - 1 || request.Email.IndexOf('.', at) < 0)
            return new ProblemDetails { Title = "email", Detail = "email must be a valid email.", Status = 400 };
        if (!Enum.IsDefined(request.Role))
            return new ProblemDetails { Title = "role", Detail = "role is not a recognised value.", Status = 400 };
        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/brands/{brandId}/invitations")]
    public static async Task<IResult> Handle(
        CreateInvitationRequest request,
        string brandId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IMessageBus bus,
        IAuditWriter audit,
        BrandTenantContext tenant,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var tenantSession = SessionFactory.TenantSession(store, tenant);
        var target = await InvitationHandlers.LoadAndAuthorizeTargetAsync(tenantSession, authorization, actor, request.OrgNodeId, request.Role, tenant.TenantId.Value, ct);

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

        audit.Record(platformSession, OrgAudit.Record(
            OrgAuditActions.InvitationRequested,
            AuditCategories.Invitation,
            AuditSeverities.Info,
            actor,
            tenant.TenantId.Value,
            target.Id,
            details: new
            {
                InvitationId = OrgPublicId.FormatInvitation(invitationId),
                InviteeEmailMasked = AuditIdentity.MaskEmail(request.Email),
                Role = request.Role.ToString()
            },
            targetPublicId: target.PublicId,
            targetType: target.Type.ToString().ToLowerInvariant(),
            targetLabel: target.Name));

        await platformSession.SaveChangesAsync(ct);
        await bus.InvokeAsync(new ProvisionInvitation(invitationId, new MessageAuditContext(SupportId.Current)));

        var created = await platformSession.LoadAsync<InvitationReadModel>(invitationId.Value, ct)
            ?? throw new InvalidOperationException("Projection failed to create InvitationReadModel.");

        return Results.Accepted($"/api/brands/{brandId}/invitations/{created.PublicId}", InvitationHandlers.ToResponse(created));
    }
}
