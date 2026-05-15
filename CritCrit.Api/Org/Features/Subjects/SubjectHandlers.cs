using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Api.Org.Infrastructure;
using Marten;
using Wolverine.Http;

namespace CritCrit.Api.Org.Features.Subjects;

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
        IAuditWriter audit,
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

    [WolverinePost("/api/platform/subjects/{subjectId}/deactivate")]
    [EmptyResponse]
    public static async Task DeactivateSubject(
        string subjectId,
        DeactivateSubjectRequest request,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IAuditWriter audit,
        ActorContext actor,
        CancellationToken ct)
    {
        authorization.EnforceSuperAdmin(actor);

        if (!OrgPublicId.TryParseSubject(subjectId, out var parsedSubjectId))
            throw new DomainException("Invalid subject ID.");

        await using var platformSession = SessionFactory.PlatformSession(store);
        SessionMetadata.StampActor(platformSession, actor);
        var subject = await platformSession.LoadAsync<SubjectReadModel>(parsedSubjectId.Value, ct)
            ?? throw new DomainException("Subject not found.", 404);

        if (!subject.Active)
            throw new DomainException("Subject is already deactivated.");

        var now = TimeProvider.System.GetUtcNow();
        platformSession.Events.Append(subject.Id,
            new SubjectDeactivated(parsedSubjectId, request.Reason, now));

        audit.Record(platformSession, OrgAudit.Record(
            OrgAuditActions.SubjectDeactivated,
            AuditCategories.Subject,
            AuditSeverities.Critical,
            actor,
            null,
            null,
            request.Reason,
            OrgAudit.SubjectDetails(subject),
            subjectId: subject.Id,
            changes: [new AuditFieldChange("active", true, false)]));

        // Cascade-revoke every active grant the subject holds across all brands.
        // Uses the cross-tenant SubjectBrandAccess index to find affected tenants
        // without scanning every brand's grant table.
        var access = await platformSession.Query<SubjectBrandAccessReadModel>()
            .Where(x => x.SubjectId == parsedSubjectId.Value)
            .ToListAsync(ct);

        await platformSession.SaveChangesAsync(ct);

        foreach (var brand in access)
        {
            await using var tenantSession = store.LightweightSession(brand.TenantId.ToString());
            SessionMetadata.StampActor(tenantSession, actor);

            var grants = await tenantSession.Query<OrgAccessGrantReadModel>()
                .Where(x => x.TenantId == brand.TenantId
                            && x.SubjectId == parsedSubjectId.Value
                            && x.Status == OrgAccessGrantStatus.Active)
                .ToListAsync(ct);

            foreach (var grant in grants)
            {
                tenantSession.Events.Append(grant.StreamId,
                    new OrgAccessRevoked(
                        new OrgNodeId(brand.TenantId),
                        new OrgNodeId(grant.OrgNodeId),
                        parsedSubjectId,
                        OrgAccessRevokedReason.SubjectDeactivated));
            }

            await tenantSession.SaveChangesAsync(ct);
        }
    }

    [WolverinePost("/api/platform/subjects/{subjectId}/reactivate")]
    [EmptyResponse]
    public static async Task ReactivateSubject(
        string subjectId,
        ReactivateSubjectRequest request,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IAuditWriter audit,
        ActorContext actor,
        CancellationToken ct)
    {
        authorization.EnforceSuperAdmin(actor);

        if (!OrgPublicId.TryParseSubject(subjectId, out var parsedSubjectId))
            throw new DomainException("Invalid subject ID.");

        await using var session = SessionFactory.PlatformSession(store);
        SessionMetadata.StampActor(session, actor);
        var subject = await session.LoadAsync<SubjectReadModel>(parsedSubjectId.Value, ct)
            ?? throw new DomainException("Subject not found.", 404);

        if (subject.Active)
            throw new DomainException("Subject is already active.");

        var now = TimeProvider.System.GetUtcNow();
        session.Events.Append(subject.Id,
            new SubjectReactivated(parsedSubjectId, request.Reason, now));

        audit.Record(session, OrgAudit.Record(
            OrgAuditActions.SubjectReactivated,
            AuditCategories.Subject,
            AuditSeverities.Info,
            actor,
            null,
            null,
            request.Reason,
            OrgAudit.SubjectDetails(subject),
            subjectId: subject.Id,
            changes: [new AuditFieldChange("active", false, true)]));

        // Deliberately do NOT auto-restore the prior grants. Operators must
        // re-grant or re-invite explicitly so revoke history stays meaningful.
        await session.SaveChangesAsync(ct);
    }

    [WolverinePost("/api/platform/subjects/{subjectId}/relink")]
    [EmptyResponse]
    public static async Task RelinkSubject(
        string subjectId,
        RelinkSubjectIdentityRequest request,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IAuditWriter audit,
        ActorContext actor,
        CancellationToken ct)
    {
        authorization.EnforceSuperAdmin(actor);

        if (!OrgPublicId.TryParseSubject(subjectId, out var parsedSubjectId))
            throw new DomainException("Invalid subject ID.");

        if (string.IsNullOrWhiteSpace(request.NewExternalId))
            throw new DomainException("New external ID is required.");

        await using var session = SessionFactory.PlatformSession(store);
        SessionMetadata.StampActor(session, actor);

        var subject = await session.LoadAsync<SubjectReadModel>(parsedSubjectId.Value, ct)
            ?? throw new DomainException("Subject not found.", 404);

        var oldLinkId = ExternalIdentityReadModel.BuildId(request.Provider, request.ProviderTenant, request.OldExternalId);
        var oldLink = await session.LoadAsync<ExternalIdentityReadModel>(oldLinkId, ct);
        if (oldLink is null || oldLink.SubjectId != subject.Id)
            throw new DomainException("Existing identity link does not match this subject.");

        // Make sure the new external id isn't already bound to a different subject.
        var newLinkId = ExternalIdentityReadModel.BuildId(request.Provider, request.ProviderTenant, request.NewExternalId);
        var existing = await session.LoadAsync<ExternalIdentityReadModel>(newLinkId, ct);
        if (existing is not null && existing.SubjectId != subject.Id)
            throw new DomainException("Another subject is already linked to this external identity.");

        session.Events.Append(subject.Id, new ExternalIdentityRelinked(
            parsedSubjectId,
            request.Provider,
            request.ProviderTenant,
            request.OldExternalId,
            request.NewExternalId,
            request.Reason));

        audit.Record(session, OrgAudit.Record(
            OrgAuditActions.SubjectRelinked,
            AuditCategories.Subject,
            AuditSeverities.Critical,
            actor,
            null,
            null,
            request.Reason,
            OrgAudit.SubjectDetails(subject, new
            {
                request.Provider,
                request.ProviderTenant,
                request.OldExternalId,
                request.NewExternalId
            }),
            subjectId: subject.Id,
            changes: [new AuditFieldChange("externalId", request.OldExternalId, request.NewExternalId)]));

        await session.SaveChangesAsync(ct);
    }
}
