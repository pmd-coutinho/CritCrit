namespace CritCrit.Api.Platform.Audit;

/// <summary>
/// Append-only platform audit row. Single-tenanted document. Written by
/// <see cref="AuditLog"/> from any slice that needs to leave a trail.
/// </summary>
public sealed class ImmutableAuditEvent
{
    public Guid Id { get; set; }
    public string Action { get; set; } = "";
    public Guid? TenantId { get; set; }
    public Guid? TargetOrgNodeId { get; set; }
    public string? Reason { get; set; }
    public string ActorExternalId { get; set; } = "";
    public Guid? ActorSubjectId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public object? Details { get; set; }
}
