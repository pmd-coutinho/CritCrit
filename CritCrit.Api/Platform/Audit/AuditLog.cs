using CritCrit.Api.Org.Auth;
using Marten;

namespace CritCrit.Api.Platform.Audit;

/// <summary>
/// Append-only platform audit writer. Any slice can call <see cref="Write"/>
/// to record an actor's action. Records land in <see cref="ImmutableAuditEvent"/>.
/// </summary>
public static class AuditLog
{
    public static void Write(
        IDocumentSession session,
        string action,
        ActorContext actor,
        Guid? tenantId,
        Guid? targetOrgNodeId,
        string? reason,
        object? details = null)
    {
        session.Store(new ImmutableAuditEvent
        {
            Id = Guid.CreateVersion7(),
            Action = action,
            TenantId = tenantId,
            TargetOrgNodeId = targetOrgNodeId,
            Reason = reason,
            ActorExternalId = actor.ExternalId,
            ActorSubjectId = actor.SubjectId?.Value,
            OccurredAt = TimeProvider.System.GetUtcNow(),
            Details = details
        });
    }
}
