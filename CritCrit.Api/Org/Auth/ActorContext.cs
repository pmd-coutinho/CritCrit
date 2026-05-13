using System.Security.Claims;
using CritCrit.Api.Org.Domain;
using Marten;

namespace CritCrit.Api.Org.Auth;

public sealed record ActorContext(
    bool IsAuthenticated,
    bool IsSuperAdmin,
    SubjectId? SubjectId,
    string ExternalId,
    string? Email);

public static class ActorContextResolver
{
    public const string SuperAdminRole = "critcrit.superadmin";

    public static async Task<ActorContext> ResolveAsync(IQuerySession query, ClaimsPrincipal user, CancellationToken ct)
    {
        if (user.Identity?.IsAuthenticated != true)
            return new ActorContext(false, false, null, "", null);

        var externalId = user.FindFirstValue(ClaimTypes.NameIdentifier) ??
                         user.FindFirstValue("sub") ??
                         user.Identity.Name ??
                         "";
        var email = user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue("email");
        var isSuperAdmin = user.IsInRole(SuperAdminRole);
        SubjectId? subjectId = null;

        var provider = user.FindFirstValue("idp") ?? "test";
        var providerTenant = user.FindFirstValue("idp_tenant") ?? "default";
        if (!string.IsNullOrWhiteSpace(externalId))
        {
            var linkId = ExternalIdentityReadModel.BuildId(provider, providerTenant, externalId);
            var link = await query.LoadAsync<ExternalIdentityReadModel>(linkId, ct);
            if (link is not null)
                subjectId = new SubjectId(link.SubjectId);
        }

        return new ActorContext(true, isSuperAdmin, subjectId, externalId, email);
    }
}
