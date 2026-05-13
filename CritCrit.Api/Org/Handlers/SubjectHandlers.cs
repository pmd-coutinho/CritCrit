using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Api.Org.Infrastructure;
using Marten;
using Wolverine.Http;

namespace CritCrit.Api.Org.Handlers;

public static class SubjectHandlers
{
    [WolverinePost("/api/platform/subjects")]
    public static async Task<SubjectResponse> CreateSubject(
        CreateSubjectRequest request,
        IDocumentStore store,
        OrgCommandService commands,
        OrgAuthorizationService authorization,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var actor = httpContext.GetActor();
        authorization.EnforceSuperAdmin(actor);

        await using var session = HandlerContext.PlatformSession(store);

        var subject = await commands.CreateSubjectAsync(
            session, actor,
            request.Email, request.DisplayName,
            request.Provider, request.ProviderTenant, request.ExternalId,
            ct);

        return new SubjectResponse(subject.PublicId, subject.Email, subject.DisplayName);
    }
}
