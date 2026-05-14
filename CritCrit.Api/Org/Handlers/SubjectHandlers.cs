using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Api.Org.Infrastructure;
using Marten;
using Wolverine.Http;

namespace CritCrit.Api.Org.Handlers;

public static class SubjectHandlers
{
    [WolverinePost("/api/platform/subjects")]
    public static async Task<IResult> CreateSubject(
        CreateSubjectRequest request,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var actor = await HandlerContext.ResolveActorAsync(httpContext, store, ct);
        authorization.EnforceSuperAdmin(actor);

        await using var session = HandlerContext.PlatformSession(store);
        var id = SubjectId.New();

        session.Events.StartStream<SubjectReadModel>(id.Value,
            new SubjectCreated(id, SubjectKind.User, request.Email.Trim(), request.DisplayName?.Trim()));
        session.Events.Append(id.Value,
            new ExternalIdentityLinked(id, request.Provider, request.ProviderTenant, request.ExternalId));

        await session.SaveChangesAsync(ct);
        var subject = await session.LoadAsync<SubjectReadModel>(id.Value, ct)
            ?? throw new InvalidOperationException("Projection failed to create SubjectReadModel.");
        var publicId = OrgPublicId.FormatSubject(id);
        return Results.Created($"/api/platform/subjects/{publicId}",
            new SubjectResponse(subject.PublicId, subject.Email, subject.DisplayName));
    }
}
