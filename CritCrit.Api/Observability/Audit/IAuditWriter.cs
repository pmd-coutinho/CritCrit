using Marten;

namespace CritCrit.Api.Observability.Audit;

public interface IAuditWriter
{
    void Record(IDocumentSession session, AuditRecord record);

    Task RecordDeniedAsync(AuditRecord record, CancellationToken ct);
}
