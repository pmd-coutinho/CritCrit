using CritCrit.Api.Org.Auth;
using CritCrit.Api.Observability.Audit;
using CritCrit.Api.Observability.Support;
using Marten;

namespace CritCrit.Api.Platform.Marten;

/// <summary>
/// Stamps Marten session metadata (headers + LastModifiedBy) so events and
/// document writes inherit actor identity for downstream auditing / diagnostics.
/// </summary>
public static class SessionMetadata
{
    public const string ActorExternalIdHeader = "actor_external_id";
    public const string ActorSubjectIdHeader = "actor_subject_id";
    public const string ActorIsSuperAdminHeader = "actor_is_superadmin";
    public const string ActorKindHeader = "actor_kind";
    public const string SupportIdHeader = "support_id";

    public static void StampActor(IDocumentSession session, ActorContext actor)
    {
        var auditActor = AuditIdentity.FromActor(actor);

        session.LastModifiedBy = auditActor.ExternalId;
        session.CorrelationId = SupportId.Current;
        session.CausationId ??= SupportId.Current;

        session.SetHeader(ActorExternalIdHeader, auditActor.ExternalId);
        session.SetHeader(ActorKindHeader, auditActor.Kind);
        session.SetHeader(SupportIdHeader, SupportId.Current);
        session.SetHeader(ActorIsSuperAdminHeader, actor.IsSuperAdmin);

        if (auditActor.SubjectId is not null)
            session.SetHeader(ActorSubjectIdHeader, auditActor.SubjectId.Value);
    }

    public static void StampSystem(IDocumentSession session, AuditActor actor, string? causationId = null)
    {
        session.LastModifiedBy = actor.ExternalId;
        session.CorrelationId = SupportId.Current;
        session.CausationId = causationId ?? SupportId.Current;
        session.SetHeader(ActorExternalIdHeader, actor.ExternalId);
        session.SetHeader(ActorKindHeader, actor.Kind);
        session.SetHeader(SupportIdHeader, SupportId.Current);
    }
}
