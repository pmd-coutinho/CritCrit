using CritCrit.Api.Org.Domain;
using Marten.Events.Aggregation;

namespace CritCrit.Api.Org.Projections;

public sealed class InvitationProjection : SingleStreamProjection<InvitationReadModel, Guid>
{
    public InvitationReadModel Create(InvitationRequested e)
    {
        return new InvitationReadModel
        {
            Id = e.Id.Value,
            PublicId = OrgPublicId.FormatInvitation(e.Id),
            TenantId = e.TenantId.Value,
            TenantPublicId = OrgPublicId.Format(OrgNodeType.Brand, e.TenantId),
            TargetOrgNodeId = e.TargetOrgNodeId.Value,
            TargetOrgNodePublicId = e.TargetOrgNodePublicId,
            Email = e.Email,
            EmailNormalized = e.Email.Trim().ToLowerInvariant(),
            Role = e.Role,
            Status = InvitationStatus.Requested,
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.CreatedAt,
            InviterExternalId = e.InviterExternalId,
            InviterSubjectId = e.InviterSubjectId?.Value
        };
    }

    public void Apply(InvitationProvisioningStarted e, InvitationReadModel view)
    {
        view.Status = InvitationStatus.Provisioning;
        view.UpdatedAt = e.StartedAt;
    }

    public void Apply(InvitationSubjectBound e, InvitationReadModel view)
    {
        view.SubjectId = e.SubjectId.Value;
        view.SubjectPublicId = OrgPublicId.FormatSubject(e.SubjectId);
        view.Email = e.Email;
        view.EmailNormalized = e.Email.Trim().ToLowerInvariant();
        view.UpdatedAt = TimeProvider.System.GetUtcNow();
    }

    public void Apply(InvitationTokenIssued e, InvitationReadModel view)
    {
        view.TokenHash = e.TokenHash;
        view.ExpiresAt = e.ExpiresAt;
        view.UpdatedAt = e.ExpiresAt;
        view.Status = InvitationStatus.Provisioning;
        view.CompletedAt = null;
        view.Failure = null;
    }

    public void Apply(InvitationMarkedPending e, InvitationReadModel view)
    {
        view.Status = InvitationStatus.Pending;
        view.UpdatedAt = e.MarkedAt;
    }

    public void Apply(InvitationAccepted e, InvitationReadModel view)
    {
        view.Status = InvitationStatus.Accepted;
        view.AcceptedAt = e.AcceptedAt;
        view.CompletedAt = e.AcceptedAt;
        view.TokenHash = null;
        view.UpdatedAt = e.AcceptedAt;
    }

    public void Apply(InvitationAutoApplied e, InvitationReadModel view)
    {
        view.Status = InvitationStatus.AutoApplied;
        view.CompletedAt = e.AppliedAt;
        view.TokenHash = null;
        view.UpdatedAt = e.AppliedAt;
    }

    public void Apply(InvitationCancelled e, InvitationReadModel view)
    {
        view.Status = InvitationStatus.Cancelled;
        view.CompletedAt = e.CancelledAt;
        view.TokenHash = null;
        view.UpdatedAt = e.CancelledAt;
    }

    public void Apply(InvitationSuperseded e, InvitationReadModel view)
    {
        view.Status = InvitationStatus.Superseded;
        view.CompletedAt = e.SupersededAt;
        view.TokenHash = null;
        view.UpdatedAt = e.SupersededAt;
    }

    public void Apply(InvitationExpired e, InvitationReadModel view)
    {
        view.Status = InvitationStatus.Expired;
        view.CompletedAt = e.ExpiredAt;
        view.TokenHash = null;
        view.UpdatedAt = e.ExpiredAt;
    }

    public void Apply(InvitationObsoleted e, InvitationReadModel view)
    {
        view.Status = InvitationStatus.Obsolete;
        view.CompletedAt = e.ObsoletedAt;
        view.TokenHash = null;
        view.Failure = e.Reason;
        view.UpdatedAt = e.ObsoletedAt;
    }

    public void Apply(InvitationFailed e, InvitationReadModel view)
    {
        view.Status = InvitationStatus.Failed;
        view.CompletedAt = e.FailedAt;
        view.TokenHash = null;
        view.Failure = e.Failure;
        view.UpdatedAt = e.FailedAt;
    }

    public void Apply(InvitationEmailDispatched e, InvitationReadModel view)
    {
        view.LastSentAt = e.SentAt;
        view.UpdatedAt = e.SentAt;
    }
}
