namespace CritCrit.Api.Org.Domain;

public enum ConfigValueType
{
    Boolean,
    String,
    Integer,
    Decimal,
    EncryptedString,
    JsonObject,
    JsonArray
}

public enum ConfigValueEntryState
{
    Set,
    Unset
}

public enum ConfigValuePatchOperationKind
{
    Set,
    Inherit,
    Unset
}

public enum ConfigChangeKind
{
    SchemaPublished,
    SchemaArchived,
    AssignmentChanged,
    ValuesChanged
}

public enum ConfigValueResolutionSource
{
    Local,
    Inherited,
    Default,
    Unset,
    Missing
}

/// <summary>Constraint set for primitive (non-JSON) config keys.</summary>
public sealed record ConfigValueConstraints(
    IReadOnlyList<string>? Enum = null,
    string? Regex = null,
    int? MinLength = null,
    int? MaxLength = null,
    decimal? Min = null,
    decimal? Max = null);

public sealed record ConfigDefaultValue(string JsonValue);

public sealed record ConfigKeyDefinition(
    string Code,
    string Name,
    string? Description,
    ConfigValueType ValueType,
    ConfigValueConstraints? Constraints,
    string? JsonSchema,
    ConfigDefaultValue? DefaultValue);

public sealed record ConfigSchemaDefinition(
    string Name,
    string? Description,
    IReadOnlyList<ConfigKeyDefinition> Keys);

public sealed record ConfigStoredValue(
    ConfigValueType ValueType,
    string? JsonValue,
    string? Ciphertext,
    string? ContentHash);

public sealed record ConfigValueEntry(
    string KeyCode,
    ConfigValueEntryState State,
    ConfigStoredValue? Value,
    DateTimeOffset UpdatedAt,
    string? UpdatedByExternalId);

public sealed record ConfigValuePatchApplied(
    string KeyCode,
    ConfigValuePatchOperationKind Operation,
    ConfigStoredValue? Value,
    string? UpdatedByExternalId);
