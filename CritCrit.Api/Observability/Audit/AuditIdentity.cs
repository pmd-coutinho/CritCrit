using System.Text.RegularExpressions;
using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;

namespace CritCrit.Api.Observability.Audit;

public static partial class AuditIdentity
{
    public static AuditActor FromActor(ActorContext? actor)
    {
        if (actor is null || !actor.IsAuthenticated)
            return AuditActor.UnauthenticatedSystem();

        var subjectPublicId = actor.SubjectId is null ? null : OrgPublicId.FormatSubject(actor.SubjectId.Value);
        var externalId = SafeIdentifier(actor.ExternalId, subjectPublicId);
        return new AuditActor(AuditActorKinds.User, externalId, actor.SubjectId?.Value, subjectPublicId);
    }

    public static string? MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var trimmed = email.Trim();
        var at = trimmed.IndexOf('@');
        if (at <= 0 || at == trimmed.Length - 1)
            return "***";

        return $"{trimmed[0]}***{trimmed[at..]}";
    }

    public static string SafeIdentifier(string? candidate, string? subjectPublicId = null)
    {
        if (!string.IsNullOrWhiteSpace(candidate) && !EmailLike().IsMatch(candidate))
            return candidate;

        return subjectPublicId ?? "unknown:user";
    }

    [GeneratedRegex("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailLike();
}
