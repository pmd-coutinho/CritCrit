using CritCrit.Api.Observability.Audit;
using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Infrastructure;
using JasperFx.Events;
using Marten;
using Marten.Events;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Http;
using Wolverine.Marten;

namespace CritCrit.Api.Org.Features.Config;

/// <summary>
/// First real [WriteAggregate] adoption (commit-tracker note). Stream id =
/// draftId (Guid in route). ConfigSchemaDraftReadModel is the snapshot target
/// of the SingleStreamProjection from <see cref="Projections.ConfigSchemaDraftProjection"/>,
/// so Marten can FetchForWriting through Wolverine.
/// </summary>
public static class ArchiveConfigDraftEndpoint
{
    public static ProblemDetails Validate(ArchiveConfigDraftRequest request)
    {
        if (request.Reason is { Length: > 500 })
            return new ProblemDetails { Title = "reason", Detail = "Reason must be 500 characters or fewer.", Status = 400 };
        return WolverineContinue.NoProblems;
    }

    [WolverinePost("/api/platform/config-schemas/{schemaCode}/drafts/{draftId}/archive")]
    [EmptyResponse]
    public static void Handle(
        string schemaCode,
        ArchiveConfigDraftRequest request,
        [WriteAggregate("draftId")] IEventStream<ConfigSchemaDraftReadModel> stream,
        OrgAuthorizationService authorization,
        IAuditWriter audit,
        IDocumentSession session,
        ActorContext actor)
    {
        authorization.EnforceSuperAdmin(actor);

        var draft = stream.Aggregate
            ?? throw new DomainException("Draft not found.", 404);
        if (draft.SchemaCode != ConfigCode.Normalize(schemaCode))
            throw new DomainException("Draft does not belong to the requested schema.");
        if (draft.Archived)
            throw new DomainException("Draft is already archived.");
        if (draft.Published)
            throw new DomainException("Cannot archive a published draft.");
        if (draft.Version != request.ExpectedVersion)
            throw new DomainException($"Expected version {request.ExpectedVersion}, found {draft.Version}.", 409);

        var now = TimeProvider.System.GetUtcNow();
        stream.AppendOne(new ConfigSchemaDraftArchived(new ConfigDraftId(draft.Id), request.Reason, now));

        // Audit stays inline (replaced by AuditLogProjection once
        // .scratch/cross-cutting-middleware/ ships). Session is shared with
        // the WriteAggregate stream — single transaction.
        audit.Record(session, new AuditRecord(
            ConfigAuditActions.DraftArchived,
            AuditCategories.Config,
            AuditSeverities.Warn,
            Actor: actor,
            Reason: request.Reason,
            Details: new { draft.SchemaCode, DraftId = draft.Id }));
    }
}
