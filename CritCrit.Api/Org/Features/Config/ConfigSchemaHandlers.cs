using CritCrit.Api.Observability.Audit;
using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Infrastructure;
using Marten;
using Wolverine.Http;

namespace CritCrit.Api.Org.Features.Config;

public static class ConfigSchemaHandlers
{
    // ─── List + read ───

    [WolverineGet("/api/platform/config-schemas")]
    public static async Task<IReadOnlyList<ConfigSchemaResponse>> ListSchemas(
        bool? includeArchived,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        ActorContext actor,
        CancellationToken ct)
    {
        authorization.EnforceSuperAdmin(actor);

        await using var session = store.QuerySession(PlatformTenant.Id);
        var query = session.Query<ConfigSchemaReadModel>();

        var rows = includeArchived == true
            ? await query.OrderBy(x => x.Code).ToListAsync(ct)
            : await query.Where(x => !x.Archived).OrderBy(x => x.Code).ToListAsync(ct);

        return rows.Select(ToResponse).ToArray();
    }

    [WolverineGet("/api/platform/config-schemas/{schemaCode}")]
    public static async Task<IResult> GetSchema(
        string schemaCode,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        ActorContext actor,
        CancellationToken ct)
    {
        authorization.EnforceSuperAdmin(actor);

        await using var session = store.QuerySession(PlatformTenant.Id);
        var schema = await LoadSchemaByCodeAsync(session, schemaCode, ct);
        return schema is null
            ? Results.NotFound()
            : Results.Ok(ToResponse(schema));
    }

    // ─── Create / archive / restore ───

    [WolverinePost("/api/platform/config-schemas")]
    public static async Task<IResult> CreateSchema(
        CreateConfigSchemaRequest request,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        ConfigValidationService validation,
        IAuditWriter audit,
        ActorContext actor,
        CancellationToken ct)
    {
        authorization.EnforceSuperAdmin(actor);
        ConfigCode.EnsureValidSchemaCode(request.Code);

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new DomainException("Schema name is required.");
        if (string.IsNullOrWhiteSpace(request.DraftName))
            throw new DomainException("Initial draft name is required.");

        // Definition must be valid before we persist anything.
        validation.ValidateSchemaDefinition(request.Definition);

        var code = request.Code.Trim();
        var codeNormalized = ConfigCode.Normalize(code);

        await using var session = SessionFactory.PlatformSession(store);
        SessionMetadata.StampActor(session, actor);

        var existing = await session.Query<ConfigSchemaReadModel>()
            .Where(x => x.CodeNormalized == codeNormalized)
            .FirstOrDefaultAsync(ct);
        if (existing is not null)
            throw new DomainException($"A config schema with code '{code}' already exists.", 409);

        var schemaId = ConfigSchemaId.New();
        var draftId = ConfigDraftId.New();
        var now = TimeProvider.System.GetUtcNow();

        session.Events.StartStream<ConfigSchemaReadModel>(schemaId.Value,
            new ConfigSchemaCreated(schemaId, code, codeNormalized, request.Name.Trim(), request.Description?.Trim()));

        session.Events.StartStream<ConfigSchemaDraftReadModel>(draftId.Value,
            new ConfigSchemaDraftCreated(
                draftId,
                schemaId,
                codeNormalized,
                request.DraftName.Trim(),
                BaseVersion: null,
                request.Definition,
                now,
                actor.ExternalId));

        audit.Record(session, new AuditRecord(
            ConfigAuditActions.SchemaCreated,
            AuditCategories.Config,
            AuditSeverities.Info,
            Actor: actor,
            Details: new { Code = codeNormalized, request.Name, DraftId = draftId.Value }));

        await session.SaveChangesAsync(ct);

        var publicId = $"/api/platform/config-schemas/{codeNormalized}";
        return Results.Created(publicId, new
        {
            Schema = new ConfigSchemaResponse(codeNormalized, request.Name.Trim(), request.Description?.Trim(), null, false, now, now, 1),
            DraftId = draftId.Value
        });
    }

    [WolverinePost("/api/platform/config-schemas/{schemaCode}/archive")]
    [EmptyResponse]
    public static async Task ArchiveSchema(
        string schemaCode,
        ArchiveConfigSchemaRequest request,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IAuditWriter audit,
        ActorContext actor,
        CancellationToken ct)
    {
        authorization.EnforceSuperAdmin(actor);

        await using var session = SessionFactory.PlatformSession(store);
        SessionMetadata.StampActor(session, actor);

        var schema = await LoadSchemaByCodeAsync(session, schemaCode, ct)
            ?? throw new DomainException("Schema not found.", 404);
        if (schema.Archived)
            throw new DomainException("Schema is already archived.");

        session.Events.Append(schema.Id, new ConfigSchemaArchived(new ConfigSchemaId(schema.Id), request.Reason));
        audit.Record(session, new AuditRecord(
            ConfigAuditActions.SchemaArchived,
            AuditCategories.Config,
            AuditSeverities.Warn,
            Actor: actor,
            Reason: request.Reason,
            Details: new { schema.Code }));

        await session.SaveChangesAsync(ct);
    }

    [WolverinePost("/api/platform/config-schemas/{schemaCode}/restore")]
    [EmptyResponse]
    public static async Task RestoreSchema(
        string schemaCode,
        RestoreConfigSchemaRequest request,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        IAuditWriter audit,
        ActorContext actor,
        CancellationToken ct)
    {
        authorization.EnforceSuperAdmin(actor);

        await using var session = SessionFactory.PlatformSession(store);
        SessionMetadata.StampActor(session, actor);

        var schema = await LoadSchemaByCodeAsync(session, schemaCode, ct)
            ?? throw new DomainException("Schema not found.", 404);
        if (!schema.Archived)
            throw new DomainException("Schema is not archived.");

        session.Events.Append(schema.Id, new ConfigSchemaRestored(new ConfigSchemaId(schema.Id), request.Reason));
        audit.Record(session, new AuditRecord(
            ConfigAuditActions.SchemaRestored,
            AuditCategories.Config,
            AuditSeverities.Info,
            Actor: actor,
            Reason: request.Reason,
            Details: new { schema.Code }));

        await session.SaveChangesAsync(ct);
    }

    // ─── Versions ───

    [WolverineGet("/api/platform/config-schemas/{schemaCode}/versions")]
    public static async Task<IResult> ListVersions(
        string schemaCode,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        ActorContext actor,
        CancellationToken ct)
    {
        authorization.EnforceSuperAdmin(actor);

        await using var session = store.QuerySession(PlatformTenant.Id);
        var code = ConfigCode.Normalize(schemaCode);
        var rows = await session.Query<ConfigSchemaVersionReadModel>()
            .Where(x => x.SchemaCode == code)
            .OrderBy(x => x.Version)
            .ToListAsync(ct);

        return Results.Ok(rows.Select(v => new ConfigSchemaVersionResponse(
            v.SchemaCode, v.Version, v.Definition, v.PublishedAt, v.PublishedByExternalId)).ToArray());
    }

    [WolverineGet("/api/platform/config-schemas/{schemaCode}/versions/{version}")]
    public static async Task<IResult> GetVersion(
        string schemaCode,
        int version,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        ActorContext actor,
        CancellationToken ct)
    {
        authorization.EnforceSuperAdmin(actor);

        await using var session = store.QuerySession(PlatformTenant.Id);
        var v = await session.LoadAsync<ConfigSchemaVersionReadModel>(
            ConfigSchemaVersionReadModel.BuildId(ConfigCode.Normalize(schemaCode), version), ct);
        return v is null
            ? Results.NotFound()
            : Results.Ok(new ConfigSchemaVersionResponse(v.SchemaCode, v.Version, v.Definition, v.PublishedAt, v.PublishedByExternalId));
    }

    // ─── Drafts ───

    [WolverineGet("/api/platform/config-schemas/{schemaCode}/drafts")]
    public static async Task<IReadOnlyList<ConfigSchemaDraftResponse>> ListDrafts(
        string schemaCode,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        ActorContext actor,
        CancellationToken ct)
    {
        authorization.EnforceSuperAdmin(actor);

        await using var session = store.QuerySession(PlatformTenant.Id);
        var code = ConfigCode.Normalize(schemaCode);
        var rows = await session.Query<ConfigSchemaDraftReadModel>()
            .Where(x => x.SchemaCode == code)
            .OrderByDescending(x => x.UpdatedAt)
            .ToListAsync(ct);
        return rows.Select(ToResponse).ToArray();
    }

    [WolverineGet("/api/platform/config-schemas/{schemaCode}/drafts/{draftId}")]
    public static async Task<IResult> GetDraft(
        string schemaCode,
        Guid draftId,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        ActorContext actor,
        CancellationToken ct)
    {
        authorization.EnforceSuperAdmin(actor);

        await using var session = store.QuerySession(PlatformTenant.Id);
        var draft = await session.LoadAsync<ConfigSchemaDraftReadModel>(draftId, ct);
        if (draft is null || draft.SchemaCode != ConfigCode.Normalize(schemaCode))
            return Results.NotFound();
        return Results.Ok(ToResponse(draft));
    }

    [WolverinePost("/api/platform/config-schemas/{schemaCode}/drafts")]
    public static async Task<IResult> CreateDraft(
        string schemaCode,
        CreateConfigDraftRequest request,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        ConfigValidationService validation,
        IAuditWriter audit,
        ActorContext actor,
        CancellationToken ct)
    {
        authorization.EnforceSuperAdmin(actor);
        validation.ValidateSchemaDefinition(request.Definition);
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new DomainException("Draft name is required.");

        await using var session = SessionFactory.PlatformSession(store);
        SessionMetadata.StampActor(session, actor);

        var schema = await LoadSchemaByCodeAsync(session, schemaCode, ct)
            ?? throw new DomainException("Schema not found.", 404);
        if (schema.Archived)
            throw new DomainException("Cannot create drafts on an archived schema.");

        if (request.BaseVersion is null)
        {
            if (schema.LatestPublishedVersion is not null)
                throw new DomainException("BaseVersion is required once at least one version is published.");
        }
        else
        {
            if (schema.LatestPublishedVersion is null)
                throw new DomainException("Cannot specify BaseVersion before any version is published.");

            var snap = await session.LoadAsync<ConfigSchemaVersionReadModel>(
                ConfigSchemaVersionReadModel.BuildId(schema.Code, request.BaseVersion.Value), ct);
            if (snap is null)
                throw new DomainException($"Base version {request.BaseVersion.Value} not found.", 404);
        }

        var draftId = ConfigDraftId.New();
        var now = TimeProvider.System.GetUtcNow();
        session.Events.StartStream<ConfigSchemaDraftReadModel>(draftId.Value,
            new ConfigSchemaDraftCreated(
                draftId,
                new ConfigSchemaId(schema.Id),
                schema.Code,
                request.Name.Trim(),
                request.BaseVersion,
                request.Definition,
                now,
                actor.ExternalId));

        audit.Record(session, new AuditRecord(
            ConfigAuditActions.DraftCreated,
            AuditCategories.Config,
            AuditSeverities.Info,
            Actor: actor,
            Details: new { schema.Code, DraftId = draftId.Value, request.BaseVersion }));

        await session.SaveChangesAsync(ct);
        return Results.Created(
            $"/api/platform/config-schemas/{schema.Code}/drafts/{draftId.Value}",
            new { DraftId = draftId.Value });
    }

    [WolverinePut("/api/platform/config-schemas/{schemaCode}/drafts/{draftId}")]
    public static async Task<IResult> UpdateDraft(
        string schemaCode,
        Guid draftId,
        UpdateConfigDraftRequest request,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        ConfigValidationService validation,
        IAuditWriter audit,
        ActorContext actor,
        CancellationToken ct)
    {
        authorization.EnforceSuperAdmin(actor);
        validation.ValidateSchemaDefinition(request.Definition);

        await using var session = SessionFactory.PlatformSession(store);
        SessionMetadata.StampActor(session, actor);

        var draft = await session.LoadAsync<ConfigSchemaDraftReadModel>(draftId, ct)
            ?? throw new DomainException("Draft not found.", 404);
        if (draft.SchemaCode != ConfigCode.Normalize(schemaCode))
            throw new DomainException("Draft does not belong to the requested schema.");
        if (draft.Archived)
            throw new DomainException("Draft is archived.");
        if (draft.Published)
            throw new DomainException("Draft is already published; create a new draft.");
        if (draft.Version != request.ExpectedVersion)
            throw new DomainException($"Expected version {request.ExpectedVersion}, found {draft.Version}.", 409);

        var now = TimeProvider.System.GetUtcNow();
        if (!string.IsNullOrWhiteSpace(request.Name) && request.Name.Trim() != draft.Name)
            session.Events.Append(draft.Id, new ConfigSchemaDraftRenamed(new ConfigDraftId(draft.Id), request.Name.Trim(), now));

        session.Events.Append(draft.Id, new ConfigSchemaDraftUpdated(
            new ConfigDraftId(draft.Id),
            request.Definition,
            now,
            actor.ExternalId));

        audit.Record(session, new AuditRecord(
            ConfigAuditActions.DraftUpdated,
            AuditCategories.Config,
            AuditSeverities.Info,
            Actor: actor,
            Details: new { draft.SchemaCode, DraftId = draft.Id }));

        await session.SaveChangesAsync(ct);

        var refreshed = await session.LoadAsync<ConfigSchemaDraftReadModel>(draftId, ct);
        return Results.Ok(ToResponse(refreshed!));
    }

    // ArchiveDraft moved to ArchiveConfigDraftEndpoint with [WriteAggregate].

    [WolverinePost("/api/platform/config-schemas/{schemaCode}/drafts/{draftId}/publish")]
    public static async Task<IResult> PublishDraft(
        string schemaCode,
        Guid draftId,
        PublishConfigDraftRequest request,
        IDocumentStore store,
        OrgAuthorizationService authorization,
        ConfigValidationService validation,
        IAuditWriter audit,
        ActorContext actor,
        CancellationToken ct)
    {
        authorization.EnforceSuperAdmin(actor);

        await using var session = SessionFactory.PlatformSession(store);
        SessionMetadata.StampActor(session, actor);

        var draft = await session.LoadAsync<ConfigSchemaDraftReadModel>(draftId, ct)
            ?? throw new DomainException("Draft not found.", 404);
        if (draft.SchemaCode != ConfigCode.Normalize(schemaCode))
            throw new DomainException("Draft does not belong to the requested schema.");
        if (draft.Archived)
            throw new DomainException("Cannot publish an archived draft.");
        if (draft.Published)
            throw new DomainException("Draft is already published.");
        if (draft.Version != request.ExpectedVersion)
            throw new DomainException($"Expected version {request.ExpectedVersion}, found {draft.Version}.", 409);

        // Re-validate at publish time — the schema may have been updated since
        // the draft was last saved.
        validation.ValidateSchemaDefinition(draft.Definition);

        var schema = await LoadSchemaByCodeAsync(session, schemaCode, ct)
            ?? throw new DomainException("Schema not found.", 404);
        if (schema.Archived)
            throw new DomainException("Cannot publish into an archived schema.");

        // Base-version freshness: must still be latest if specified.
        if (draft.BaseVersion is { } baseVer && baseVer != schema.LatestPublishedVersion)
            throw new DomainException(
                $"Draft was based on version {baseVer} but latest is {schema.LatestPublishedVersion?.ToString() ?? "(none)"}. Rebase the draft.",
                409);

        var nextVersion = (schema.LatestPublishedVersion ?? 0) + 1;
        var now = TimeProvider.System.GetUtcNow();

        session.Events.Append(schema.Id, new ConfigSchemaVersionPublished(
            new ConfigSchemaId(schema.Id),
            new ConfigDraftId(draft.Id),
            schema.Code,
            nextVersion,
            draft.Definition,
            now,
            actor.ExternalId));

        audit.Record(session, new AuditRecord(
            ConfigAuditActions.VersionPublished,
            AuditCategories.Config,
            AuditSeverities.Info,
            Actor: actor,
            Reason: request.Reason,
            Details: new { schema.Code, Version = nextVersion, DraftId = draft.Id }));

        await session.SaveChangesAsync(ct);

        return Results.Ok(new ConfigSchemaVersionResponse(schema.Code, nextVersion, draft.Definition, now, actor.ExternalId));
    }

    // ─── Helpers ───

    private static async Task<ConfigSchemaReadModel?> LoadSchemaByCodeAsync(IQuerySession session, string schemaCode, CancellationToken ct)
    {
        var code = ConfigCode.Normalize(schemaCode);
        return await session.Query<ConfigSchemaReadModel>()
            .Where(x => x.CodeNormalized == code)
            .FirstOrDefaultAsync(ct);
    }

    internal static ConfigSchemaResponse ToResponse(ConfigSchemaReadModel s) =>
        new(s.Code, s.Name, s.Description, s.LatestPublishedVersion, s.Archived, s.CreatedAt, s.UpdatedAt, s.Version);

    internal static ConfigSchemaDraftResponse ToResponse(ConfigSchemaDraftReadModel d) =>
        new(d.Id, d.SchemaCode, d.Name, d.BaseVersion, d.Definition, d.Archived, d.Published, d.PublishedAsVersion, d.Version, d.CreatedAt, d.UpdatedAt);
}
