namespace CritCrit.Api.Org.Domain;

// ─── Schema lifecycle ───

public sealed record ConfigSchemaCreated(
    ConfigSchemaId Id,
    string Code,
    string CodeNormalized,
    string Name,
    string? Description);

public sealed record ConfigSchemaRenamed(
    ConfigSchemaId Id,
    string Name,
    string? Description);

public sealed record ConfigSchemaArchived(ConfigSchemaId Id, string? Reason);

public sealed record ConfigSchemaRestored(ConfigSchemaId Id, string? Reason);

// ─── Drafts ───

public sealed record ConfigSchemaDraftCreated(
    ConfigDraftId Id,
    ConfigSchemaId SchemaId,
    string SchemaCode,
    string Name,
    int? BaseVersion,
    ConfigSchemaDefinition Definition,
    DateTimeOffset CreatedAt,
    string CreatedByExternalId);

public sealed record ConfigSchemaDraftUpdated(
    ConfigDraftId Id,
    ConfigSchemaDefinition Definition,
    DateTimeOffset UpdatedAt,
    string UpdatedByExternalId);

public sealed record ConfigSchemaDraftRenamed(
    ConfigDraftId Id,
    string Name,
    DateTimeOffset UpdatedAt);

public sealed record ConfigSchemaDraftArchived(
    ConfigDraftId Id,
    string? Reason,
    DateTimeOffset ArchivedAt);

public sealed record ConfigSchemaVersionPublished(
    ConfigSchemaId SchemaId,
    ConfigDraftId DraftId,
    string SchemaCode,
    int Version,
    ConfigSchemaDefinition Definition,
    DateTimeOffset PublishedAt,
    string PublishedByExternalId);

// ─── Assignments ───

public sealed record ConfigSchemaAssigned(
    ConfigAssignmentId Id,
    OrgNodeId TenantId,
    OrgNodeId RootOrgNodeId,
    string SchemaCode,
    int SchemaVersion,
    DateTimeOffset AssignedAt,
    string AssignedByExternalId);

public sealed record ConfigAssignmentArchived(
    ConfigAssignmentId Id,
    string? Reason,
    DateTimeOffset ArchivedAt);

public sealed record ConfigAssignmentRestored(
    ConfigAssignmentId Id,
    string? Reason,
    DateTimeOffset RestoredAt);

public sealed record ConfigAssignmentUpgraded(
    ConfigAssignmentId Id,
    string SchemaCode,
    int OldVersion,
    int NewVersion,
    DateTimeOffset UpgradedAt,
    string UpgradedByExternalId);

// ─── Node values ───

public sealed record ConfigNodeValueSetInitialized(
    Guid StreamId,
    OrgNodeId TenantId,
    OrgNodeId OrgNodeId,
    string SchemaCode);

public sealed record ConfigNodeValuesPatched(
    OrgNodeId TenantId,
    OrgNodeId OrgNodeId,
    string SchemaCode,
    IReadOnlyList<ConfigValuePatchApplied> Operations,
    DateTimeOffset AppliedAt,
    string AppliedByExternalId);
