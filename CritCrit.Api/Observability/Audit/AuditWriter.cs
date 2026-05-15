using CritCrit.Api.Observability.Logging;
using CritCrit.Api.Observability.Support;
using Marten;

namespace CritCrit.Api.Observability.Audit;

public sealed class AuditWriter(
    IDocumentStore store,
    IHttpContextAccessor httpContextAccessor,
    TimeProvider timeProvider,
    ILogger<AuditWriter> logger) : IAuditWriter
{
    public void Record(IDocumentSession session, AuditRecord record)
    {
        session.Store(ToEvent(record));
    }

    public async Task RecordDeniedAsync(AuditRecord record, CancellationToken ct)
    {
        try
        {
            await using var session = SessionFactory.PlatformSession(store);
            SessionMetadata.StampSystem(session, record.SystemActor ?? AuditActor.UnauthenticatedSystem(), record.CausationId);
            session.Store(ToEvent(record));
            await session.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.AuditDeniedWriteFailed(ex, record.Action, SupportId.Current);
        }
    }

    private ImmutableAuditEvent ToEvent(AuditRecord record)
    {
        var http = httpContextAccessor.HttpContext;
        var actor = record.SystemActor ?? AuditIdentity.FromActor(record.Actor);
        var supportId = SupportId.Current;
        var request = http is null
            ? null
            : new AuditRequestMetadata(
                http.Request.Method,
                http.Request.Path.Value,
                http.GetEndpoint()?.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.RouteNameMetadata>()?.RouteName
                ?? http.GetEndpoint()?.DisplayName);

        var targetOrgNodeId = record.Target?.Type is "org_node" or "brand" or "country" or "franchise" or "store" or "device"
            ? record.Target.Id
            : null;

        return new ImmutableAuditEvent
        {
            Id = Guid.CreateVersion7(),
            Action = record.Action,
            Category = record.Category,
            Severity = record.Severity,
            TenantId = record.TenantId,
            TenantPublicId = record.TenantPublicId,
            TargetOrgNodeId = targetOrgNodeId,
            SubjectId = record.SubjectId,
            SubjectPublicId = record.SubjectPublicId,
            SupportId = supportId,
            CorrelationId = supportId,
            CausationId = record.CausationId,
            Reason = record.Reason,
            ActorKind = actor.Kind,
            ActorExternalId = actor.ExternalId,
            ActorSubjectId = actor.SubjectId,
            ActorSubjectPublicId = actor.SubjectPublicId,
            OccurredAt = timeProvider.GetUtcNow(),
            Target = record.Target,
            RelatedResources = record.RelatedResources?.ToList() ?? [],
            Changes = record.Changes?.ToList() ?? [],
            Request = request,
            Details = record.Details
        };
    }
}
