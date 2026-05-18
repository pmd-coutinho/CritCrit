using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Api.Org.Infrastructure;
using Marten;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;

namespace CritCrit.Api.Org.Features.Subjects;

public static class CreateSubjectEndpoint
{
    public static ProblemDetails Validate(CreateSubjectRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return new ProblemDetails { Title = "email", Detail = "email is required.", Status = 400 };
        var at = request.Email.IndexOf('@');
        if (at <= 0 || at == request.Email.Length - 1 || request.Email.IndexOf('.', at) < 0)
            return new ProblemDetails { Title = "email", Detail = "email must be a valid email.", Status = 400 };
        if (request.DisplayName is { Length: > 200 })
            return new ProblemDetails { Title = "displayName", Detail = "displayName must be 200 characters or fewer.", Status = 400 };
        if (string.IsNullOrWhiteSpace(request.Provider))
            return new ProblemDetails { Title = "provider", Detail = "provider is required.", Status = 400 };
        if (string.IsNullOrWhiteSpace(request.ProviderTenant))
            return new ProblemDetails { Title = "providerTenant", Detail = "providerTenant is required.", Status = 400 };
        if (string.IsNullOrWhiteSpace(request.ExternalId))
            return new ProblemDetails { Title = "externalId", Detail = "externalId is required.", Status = 400 };
        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/platform/subjects")]
    public static async Task<IResult> Handle(
        CreateSubjectRequest request,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IAuditWriter audit,
        ActorContext actor,
        CancellationToken ct)
    {
        authorization.EnforceSuperAdmin(actor);

        await using var session = SessionFactory.PlatformSession(store);
        SessionMetadata.StampActor(session, actor);
        var id = SubjectId.New();

        session.Events.StartStream<SubjectReadModel>(id.Value,
            new SubjectCreated(id, SubjectKind.User, request.Email.Trim(), request.DisplayName?.Trim()));
        session.Events.Append(id.Value,
            new ExternalIdentityLinked(id, request.Provider, request.ProviderTenant, request.ExternalId));
        audit.Record(session, OrgAudit.Record(
            OrgAuditActions.SubjectCreated,
            AuditCategories.Subject,
            AuditSeverities.Info,
            actor,
            null,
            null,
            details: new
            {
                SubjectId = OrgPublicId.FormatSubject(id),
                EmailMasked = AuditIdentity.MaskEmail(request.Email),
                request.Provider,
                request.ProviderTenant
            },
            subjectId: id.Value));

        await session.SaveChangesAsync(ct);
        var subject = await session.LoadAsync<SubjectReadModel>(id.Value, ct)
            ?? throw new InvalidOperationException("Projection failed to create SubjectReadModel.");
        var publicId = OrgPublicId.FormatSubject(id);
        return Results.Created($"/api/platform/subjects/{publicId}",
            new SubjectResponse(subject.PublicId, subject.Email, subject.DisplayName));
    }
}
