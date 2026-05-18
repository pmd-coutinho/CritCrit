using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Api.Org.Infrastructure;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;

namespace CritCrit.Api.Org.Features.Invitations;

public static class CancelInvitationEndpoint
{
    public static ProblemDetails Validate(CancelInvitationRequest request)
    {
        if (request.Reason is { Length: > 500 })
            return new ProblemDetails { Title = "reason", Detail = "Reason must be 500 characters or fewer.", Status = 400 };
        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/brands/{brandId}/invitations/{invitationId}/cancel")]
    public static async Task<InvitationResponse> Handle(
        string brandId,
        InvitationId invitationId,
        CancelInvitationRequest request,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IAuditWriter audit,
        ActorContext actor,
        CancellationToken ct)
    {
        await using var platformSession = SessionFactory.PlatformSession(store);
        SessionMetadata.StampActor(platformSession, actor);
        var invitation = await InvitationHandlers.LoadInvitationAsync(invitationId, platformSession, ct);

        if (!invitation.IsPendingLike())
            throw new DomainException("Only pending invitations can be cancelled.");

        await InvitationHandlers.EnsureInvitationManageableAsync(store, authorization, actor, invitation, ct);

        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        platformSession.Events.Append(invitation.Id,
            new InvitationCancelled(new InvitationId(invitation.Id), TimeProvider.System.GetUtcNow(), reason));

        audit.Record(platformSession, OrgAudit.Record(
            OrgAuditActions.InvitationCancelled,
            AuditCategories.Invitation,
            AuditSeverities.Info,
            actor,
            invitation.TenantId,
            invitation.TargetOrgNodeId,
            reason,
            OrgAudit.InviteDetails(invitation)));

        await platformSession.SaveChangesAsync(ct);
        var updated = await platformSession.LoadAsync<InvitationReadModel>(invitation.Id, ct)
            ?? throw new InvalidOperationException("Invitation projection missing after cancellation.");
        return InvitationHandlers.ToResponse(updated);
    }
}
