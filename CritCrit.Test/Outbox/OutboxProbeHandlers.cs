using CritCrit.Api.Observability.Audit;
using Marten;
using Wolverine;
using Wolverine.Attributes;
using Wolverine.Marten;

namespace CritCrit.Test.Outbox;

public sealed record CommitOutboxProbe(Guid Id);

public sealed record RollbackOutboxProbe(Guid Id);

public sealed record OutboxProbeDelivered(Guid Id);

public sealed class OutboxProbeStore
{
    private readonly object _gate = new();
    private readonly List<Guid> _delivered = [];

    public IReadOnlyList<Guid> Delivered
    {
        get
        {
            lock (_gate)
                return _delivered.ToArray();
        }
    }

    public void MarkDelivered(Guid id)
    {
        lock (_gate)
            _delivered.Add(id);
    }

    public void Clear()
    {
        lock (_gate)
            _delivered.Clear();
    }
}

[WolverineHandler]
public static class OutboxProbeHandlers
{
    public static async Task Handle(
        CommitOutboxProbe command,
        IDocumentStore store,
        IMartenOutbox outbox,
        CancellationToken ct)
    {
        await using var session = store.LightweightSession();
        outbox.Enroll(session);
        await outbox.PublishAsync(new OutboxProbeDelivered(command.Id));
        session.Store(ProbeAudit(command.Id, "outbox-probe-committed"));
        await session.SaveChangesAsync(ct);
    }

    public static async Task Handle(
        RollbackOutboxProbe command,
        IDocumentStore store,
        IMartenOutbox outbox,
        CancellationToken ct)
    {
        await using var session = store.LightweightSession();
        outbox.Enroll(session);
        await outbox.PublishAsync(new OutboxProbeDelivered(command.Id));
        session.Store(ProbeAudit(command.Id, "outbox-probe-rolled-back"));

        throw new InvalidOperationException("Simulated failure after outbox publish but before Marten commit.");
    }

    public static void Handle(OutboxProbeDelivered message, OutboxProbeStore store)
    {
        store.MarkDelivered(message.Id);
    }

    private static ImmutableAuditEvent ProbeAudit(Guid id, string action) => new()
    {
        Id = Guid.CreateVersion7(),
        Action = action,
        Category = AuditCategories.Org,
        Severity = AuditSeverities.Info,
        ActorKind = AuditActorKinds.SystemBackground,
        ActorExternalId = "outbox-probe",
        OccurredAt = TimeProvider.System.GetUtcNow(),
        Details = new { id }
    };
}
