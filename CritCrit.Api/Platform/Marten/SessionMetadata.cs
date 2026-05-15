using CritCrit.Api.Org.Auth;
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
    public const string ActorEmailHeader = "actor_email";
    public const string ActorIsSuperAdminHeader = "actor_is_superadmin";

    public static void StampActor(IDocumentSession session, ActorContext actor)
    {
        session.LastModifiedBy = string.IsNullOrWhiteSpace(actor.ExternalId)
            ? actor.Email ?? "unknown"
            : actor.ExternalId;

        session.SetHeader(ActorExternalIdHeader, actor.ExternalId);
        session.SetHeader(ActorIsSuperAdminHeader, actor.IsSuperAdmin);

        if (actor.SubjectId is not null)
            session.SetHeader(ActorSubjectIdHeader, actor.SubjectId.Value.Value);

        if (!string.IsNullOrWhiteSpace(actor.Email))
            session.SetHeader(ActorEmailHeader, actor.Email);
    }
}
