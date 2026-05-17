using CritCrit.Api.Org.Domain;

namespace CritCrit.Api.Org.Features.Config;

// ─── Schema / draft requests + responses ───

public sealed record CreateConfigSchemaRequest(
    string Code,
    string Name,
    string? Description,
    string DraftName,
    ConfigSchemaDefinition Definition);

public sealed record CreateConfigDraftRequest(
    string Name,
    int? BaseVersion,
    ConfigSchemaDefinition Definition);

public sealed record UpdateConfigDraftRequest(
    long ExpectedVersion,
    string? Name,
    ConfigSchemaDefinition Definition);

public sealed record PublishConfigDraftRequest(
    long ExpectedVersion,
    string? Reason);

public sealed record ArchiveConfigSchemaRequest(string? Reason);

public sealed record RestoreConfigSchemaRequest(string? Reason);

public sealed record ArchiveConfigDraftRequest(long ExpectedVersion, string? Reason);

public sealed record ConfigSchemaResponse(
    string Code,
    string Name,
    string? Description,
    int? LatestPublishedVersion,
    bool Archived,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Version);

public sealed record ConfigSchemaVersionResponse(
    string SchemaCode,
    int Version,
    ConfigSchemaDefinition Definition,
    DateTimeOffset PublishedAt,
    string PublishedByExternalId);

public sealed record ConfigSchemaDraftResponse(
    Guid Id,
    string SchemaCode,
    string Name,
    int? BaseVersion,
    ConfigSchemaDefinition Definition,
    bool Archived,
    bool Published,
    int? PublishedAsVersion,
    long Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

// ─── Assignment requests + responses (used in Phase 3) ───

public sealed record AssignConfigSchemaRequest(
    string SchemaCode,
    int SchemaVersion,
    string? Reason);

public sealed record ArchiveConfigAssignmentRequest(long ExpectedVersion, string? Reason);

public sealed record RestoreConfigAssignmentRequest(long ExpectedVersion, string? Reason);

public sealed record UpgradeConfigAssignmentRequest(
    long ExpectedVersion,
    int TargetSchemaVersion,
    string? Reason);

public sealed record ConfigAssignmentResponse(
    Guid Id,
    string RootOrgNodePublicId,
    string SchemaCode,
    int SchemaVersion,
    bool Archived,
    long Version,
    DateTimeOffset AssignedAt,
    DateTimeOffset? ArchivedAt);

public sealed record ConfigAssignmentUpgradePreviewResponse(
    string SchemaCode,
    int FromVersion,
    int ToVersion,
    IReadOnlyList<string> CompatibleKeys,
    IReadOnlyList<string> RemovedKeys,
    IReadOnlyList<string> TypeIncompatibleKeys,
    int LocalValueCountsImpacted,
    bool Publishable);

// ─── Lookup + patch (Phase 4) ───

public sealed record NodeConfigSchemaSummary(
    string SchemaCode,
    string SchemaName,
    int SchemaVersion,
    Guid AssignmentId,
    string AssignmentRootOrgNodePublicId,
    long ValueSetVersion);

public sealed record ConfigLookupMetadataResponse(
    string SchemaCode,
    int SchemaVersion,
    string NodeId,
    ConfigAssignmentSummary Assignment,
    long ValueSetVersion,
    IReadOnlyDictionary<string, ConfigLookupValueMetadata> Values);

public sealed record ConfigAssignmentSummary(
    Guid Id,
    string RootOrgNodePublicId,
    string SchemaCode,
    int SchemaVersion);

public sealed record ConfigLookupValueMetadata(
    string State,
    object? Value,
    string? Source,
    bool Encrypted,
    bool? HasValue = null,
    string? MaskedValue = null);

public sealed record PatchConfigValuesRequest(
    long ExpectedVersion,
    IReadOnlyList<ConfigValuePatchOperationInput> Operations,
    string? Reason);

public sealed record ConfigValuePatchOperationInput(
    string KeyCode,
    ConfigValuePatchOperationKind Operation,
    string? JsonValue);
