using CritCrit.Api.Org.Auth;
using CritCrit.Api.Org.Domain;

namespace CritCrit.Api.Observability.Audit;

public static class AuditCategories
{
    public const string Org = "org";
    public const string Access = "access";
    public const string Invitation = "invitation";
    public const string Subject = "subject";
    public const string Security = "security";
    public const string Config = "config";
    public const string Asset = "asset";
}

public static class AuditSeverities
{
    public const string Info = "info";
    public const string Warn = "warn";
    public const string Critical = "critical";
}

public static class AuditActorKinds
{
    public const string User = "user";
    public const string SystemBackground = "system_background";
    public const string SystemUnauthenticated = "system_unauthenticated";
}

public sealed class ImmutableAuditEvent
{
    public Guid Id { get; set; }
    public string Action { get; set; } = "";
    public string Category { get; set; } = AuditCategories.Org;
    public string Severity { get; set; } = AuditSeverities.Info;
    public Guid? TenantId { get; set; }
    public string? TenantPublicId { get; set; }
    public Guid? TargetOrgNodeId { get; set; }
    public Guid? SubjectId { get; set; }
    public string? SubjectPublicId { get; set; }
    public string? SupportId { get; set; }
    public string? CorrelationId { get; set; }
    public string? CausationId { get; set; }
    public string? Reason { get; set; }
    public string ActorKind { get; set; } = AuditActorKinds.User;
    public string ActorExternalId { get; set; } = "";
    public Guid? ActorSubjectId { get; set; }
    public string? ActorSubjectPublicId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public AuditResourceRef? Target { get; set; }
    public List<AuditResourceRef> RelatedResources { get; set; } = [];
    public List<AuditFieldChange> Changes { get; set; } = [];
    public AuditRequestMetadata? Request { get; set; }
    public object? Details { get; set; }
}

public sealed record AuditRecord(
    string Action,
    string Category,
    string Severity,
    ActorContext? Actor = null,
    AuditActor? SystemActor = null,
    Guid? TenantId = null,
    string? TenantPublicId = null,
    AuditResourceRef? Target = null,
    Guid? SubjectId = null,
    string? SubjectPublicId = null,
    string? Reason = null,
    IReadOnlyList<AuditResourceRef>? RelatedResources = null,
    IReadOnlyList<AuditFieldChange>? Changes = null,
    object? Details = null,
    string? CausationId = null);

public sealed record AuditActor(
    string Kind,
    string ExternalId,
    Guid? SubjectId = null,
    string? SubjectPublicId = null)
{
    public static AuditActor BackgroundSystem() => new(AuditActorKinds.SystemBackground, "system:background");

    public static AuditActor UnauthenticatedSystem() => new(AuditActorKinds.SystemUnauthenticated, "system:unauthenticated");
}

public sealed record AuditResourceRef(
    string Type,
    Guid? Id = null,
    string? PublicId = null,
    string? Label = null);

public sealed record AuditFieldChange(string Field, object? OldValue, object? NewValue);

public sealed record AuditRequestMetadata(string? Method, string? Path, string? Route);
