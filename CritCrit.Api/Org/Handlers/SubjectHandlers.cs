using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Api.Org.Infrastructure;
using Marten;
using Wolverine.Http;

namespace CritCrit.Api.Org.Handlers;

public static class SubjectHandlers
{
    [WolverineGet("/api/platform/subjects")]
    public static async Task<IReadOnlyList<SubjectListItem>> ListSubjects(
        string? emailContains,
        bool? onboarded,
        int? limit,
        int? offset,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        ActorContext actor,
        CancellationToken ct)
    {
        authorization.EnforceSuperAdmin(actor);

        await using var session = store.QuerySession();
        var query = session.Query<SubjectReadModel>().AsQueryable();

        if (!string.IsNullOrWhiteSpace(emailContains))
        {
            var needle = emailContains.Trim().ToLowerInvariant();
            query = query.Where(x => x.EmailNormalized.Contains(needle));
        }

        if (onboarded == true)
            query = query.Where(x => x.OnboardedAt != null);
        else if (onboarded == false)
            query = query.Where(x => x.OnboardedAt == null);

        var take = Math.Min(limit ?? 50, 200);
        var skip = offset ?? 0;

        var items = await query
            .OrderBy(x => x.EmailNormalized)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        return items.Select(x => new SubjectListItem(
            x.PublicId,
            x.Email,
            x.DisplayName,
            x.Kind,
            x.Active,
            x.OnboardedAt)).ToArray();
    }

    [WolverinePost("/api/platform/subjects")]
    public static async Task<IResult> CreateSubject(
        CreateSubjectRequest request,
        IDocumentStore store,
        OrgAuthorizationService authorization,
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

        await session.SaveChangesAsync(ct);
        var subject = await session.LoadAsync<SubjectReadModel>(id.Value, ct)
            ?? throw new InvalidOperationException("Projection failed to create SubjectReadModel.");
        var publicId = OrgPublicId.FormatSubject(id);
        return Results.Created($"/api/platform/subjects/{publicId}",
            new SubjectResponse(subject.PublicId, subject.Email, subject.DisplayName));
    }
}
