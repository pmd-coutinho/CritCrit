using CritCrit.Api.Observability.Audit;
using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Endpoints;
using CritCrit.Api.Org.Infrastructure;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;
using Wolverine.Marten;

namespace CritCrit.Api.Org.Features.Config;

/// <summary>
/// Update a draft via Wolverine [WriteAggregate]. Single-stream snapshot for
/// ConfigSchemaDraftReadModel is registered in
/// <see cref="Projections.ConfigSchemaDraftProjection"/>. Marten FetchForWriting
/// hands us the IEventStream wrapping the latest draft state; the handler emits
/// a rename + update event pair atomically.
/// </summary>
public static class UpdateConfigDraftEndpoint
{
    [WolverinePut("/api/platform/config-schemas/{schemaCode}/drafts/{draftId}")]
    public static IResult Handle(
        string schemaCode,
        UpdateConfigDraftRequest request,
        [WriteAggregate("draftId")] IEventStream<ConfigSchemaDraftReadModel> stream,
        OrgAuthorizationService authorization,
        ConfigValidationService validation,
        IAuditWriter audit,
        IDocumentSession session,
        ActorContext actor)
    {
        authorization.EnforceSuperAdmin(actor);
        validation.ValidateSchemaDefinition(request.Definition);

        var draft = stream.Aggregate
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
            stream.AppendOne(new ConfigSchemaDraftRenamed(new ConfigDraftId(draft.Id), request.Name.Trim(), now));

        stream.AppendOne(new ConfigSchemaDraftUpdated(
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

        // Pre-event state for response; projection updates draft.Version after apply.
        return Results.Ok(ConfigSchemaHandlers.ToResponse(draft));
    }
}
