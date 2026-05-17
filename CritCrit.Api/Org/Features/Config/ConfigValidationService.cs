using System.Globalization;
using System.Text.Json;
using CritCrit.Api.Org.Domain;
using NJsonSchema;

namespace CritCrit.Api.Org.Features.Config;

public sealed record ConfigValidationError(string Path, string Message);

internal static class ConfigValidationFailure
{
    public static DomainException Build(IReadOnlyList<ConfigValidationError> errors) =>
        new("Config validation failed: " + string.Join("; ", errors.Select(e => $"{e.Path} → {e.Message}")), 400);
}

/// <summary>
/// Pure validation helpers for config schemas and values. No DI dependencies
/// so it stays trivially testable; resolution and persistence services compose
/// on top.
/// </summary>
public sealed class ConfigValidationService
{
    public void ValidateSchemaDefinition(ConfigSchemaDefinition definition)
    {
        var errors = new List<ConfigValidationError>();

        if (string.IsNullOrWhiteSpace(definition.Name))
            errors.Add(new("name", "Schema name is required."));

        if (definition.Keys.Count == 0)
            errors.Add(new("keys", "Schema must define at least one key."));

        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < definition.Keys.Count; i++)
        {
            var key = definition.Keys[i];
            var path = $"keys[{i}]";

            if (!ConfigCode.IsValid(key.Code))
                errors.Add(new($"{path}.code", "Invalid key code."));

            if (!seenCodes.Add(ConfigCode.Normalize(key.Code)))
                errors.Add(new($"{path}.code", "Duplicate key code (case-insensitive)."));

            if (string.IsNullOrWhiteSpace(key.Name))
                errors.Add(new($"{path}.name", "Key name is required."));

            ValidateKeyShape(key, path, errors);
        }

        if (errors.Count > 0)
            throw ConfigValidationFailure.Build(errors);
    }

    private void ValidateKeyShape(ConfigKeyDefinition key, string path, List<ConfigValidationError> errors)
    {
        var jsonExpected = key.ValueType is ConfigValueType.JsonObject or ConfigValueType.JsonArray;

        if (jsonExpected)
        {
            if (string.IsNullOrWhiteSpace(key.JsonSchema))
                errors.Add(new($"{path}.jsonSchema", "JSON keys must include a self-contained JSON Schema."));
            else
                ValidateJsonSchemaSelfContained(key.JsonSchema, key.ValueType, path, errors);
        }
        else if (!string.IsNullOrWhiteSpace(key.JsonSchema))
        {
            errors.Add(new($"{path}.jsonSchema", "Primitive/encrypted keys must not include a JSON Schema."));
        }

        if (key.ValueType == ConfigValueType.EncryptedString && key.DefaultValue is not null)
            errors.Add(new($"{path}.defaultValue", "Encrypted keys cannot have a default."));

        if (key.DefaultValue is not null && !jsonExpected)
            ValidateDefault(key, path, errors);

        if (key.Constraints is not null)
            ValidateConstraints(key, path, errors);
    }

    private static void ValidateConstraints(ConfigKeyDefinition key, string path, List<ConfigValidationError> errors)
    {
        var c = key.Constraints!;
        var type = key.ValueType;

        if (c.MinLength is < 0)
            errors.Add(new($"{path}.constraints.minLength", "minLength must be ≥ 0."));
        if (c.MaxLength is < 0)
            errors.Add(new($"{path}.constraints.maxLength", "maxLength must be ≥ 0."));
        if (c.MinLength is { } lo && c.MaxLength is { } hi && lo > hi)
            errors.Add(new($"{path}.constraints", "minLength must be ≤ maxLength."));
        if (c.Min is { } min && c.Max is { } max && min > max)
            errors.Add(new($"{path}.constraints", "min must be ≤ max."));

        var stringy = type is ConfigValueType.String or ConfigValueType.EncryptedString;
        var numeric = type is ConfigValueType.Integer or ConfigValueType.Decimal;

        if (!stringy && (c.MinLength is not null || c.MaxLength is not null || c.Regex is not null))
            errors.Add(new($"{path}.constraints", "Length/regex constraints apply to string types only."));

        if (!numeric && (c.Min is not null || c.Max is not null))
            errors.Add(new($"{path}.constraints", "Min/max constraints apply to numeric types only."));

        if (c.Regex is not null)
        {
            try { _ = new System.Text.RegularExpressions.Regex(c.Regex); }
            catch (Exception ex) { errors.Add(new($"{path}.constraints.regex", "Invalid regex: " + ex.Message)); }
        }
    }

    private void ValidateDefault(ConfigKeyDefinition key, string path, List<ConfigValidationError> errors)
    {
        try
        {
            var result = ValidatePrimitiveJsonValue(key, key.DefaultValue!.JsonValue);
            if (!result.IsValid)
                foreach (var msg in result.Errors)
                    errors.Add(new($"{path}.defaultValue", msg));
        }
        catch (JsonException ex)
        {
            errors.Add(new($"{path}.defaultValue", "Invalid JSON for default: " + ex.Message));
        }
    }

    private static void ValidateJsonSchemaSelfContained(string schemaJson, ConfigValueType valueType, string path, List<ConfigValidationError> errors)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(schemaJson); }
        catch (JsonException ex)
        {
            errors.Add(new($"{path}.jsonSchema", "Invalid JSON: " + ex.Message));
            return;
        }
        using var __ = doc;

        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            errors.Add(new($"{path}.jsonSchema", "JSON Schema must be an object."));
            return;
        }

        if (HasRef(doc.RootElement))
            errors.Add(new($"{path}.jsonSchema", "$ref is not allowed; schemas must be self-contained."));

        // Root type compatibility with declared value type.
        if (doc.RootElement.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String)
        {
            var t = typeEl.GetString();
            if (valueType == ConfigValueType.JsonObject && t != "object")
                errors.Add(new($"{path}.jsonSchema.type", $"Expected 'object' to match value type, got '{t}'."));
            if (valueType == ConfigValueType.JsonArray && t != "array")
                errors.Add(new($"{path}.jsonSchema.type", $"Expected 'array' to match value type, got '{t}'."));
        }

        // Round-trip through NJsonSchema as a final sanity check.
        try { _ = JsonSchema.FromJsonAsync(schemaJson).GetAwaiter().GetResult(); }
        catch (Exception ex)
        {
            errors.Add(new($"{path}.jsonSchema", "NJsonSchema rejected the document: " + ex.Message));
        }
    }

    private static bool HasRef(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in el.EnumerateObject())
                {
                    if (prop.NameEquals("$ref")) return true;
                    if (HasRef(prop.Value)) return true;
                }
                return false;
            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                    if (HasRef(item)) return true;
                return false;
            default:
                return false;
        }
    }

    public ConfigValueValidationResult ValidateValue(ConfigKeyDefinition key, string jsonValue)
    {
        return key.ValueType switch
        {
            ConfigValueType.JsonObject or ConfigValueType.JsonArray => ValidateJsonValue(key, jsonValue),
            _ => ValidatePrimitiveJsonValue(key, jsonValue),
        };
    }

    private static ConfigValueValidationResult ValidatePrimitiveJsonValue(ConfigKeyDefinition key, string jsonValue)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonValue);
            var el = doc.RootElement;

            return key.ValueType switch
            {
                ConfigValueType.Boolean => ValidateBool(key, el),
                ConfigValueType.String or ConfigValueType.EncryptedString => ValidateString(key, el),
                ConfigValueType.Integer => ValidateInteger(key, el),
                ConfigValueType.Decimal => ValidateDecimal(key, el),
                _ => ConfigValueValidationResult.Fail("Unsupported primitive type."),
            };
        }
        catch (JsonException ex)
        {
            return ConfigValueValidationResult.Fail("Invalid JSON: " + ex.Message);
        }
    }

    private static ConfigValueValidationResult ValidateBool(ConfigKeyDefinition key, JsonElement el)
    {
        if (el.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return ConfigValueValidationResult.Fail("Expected a boolean.");
        return ConfigValueValidationResult.Ok();
    }

    private static ConfigValueValidationResult ValidateString(ConfigKeyDefinition key, JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.String)
            return ConfigValueValidationResult.Fail("Expected a string.");
        var s = el.GetString()!;
        var c = key.Constraints;
        if (c is not null)
        {
            if (c.MinLength is { } lo && s.Length < lo) return ConfigValueValidationResult.Fail($"String shorter than minLength {lo}.");
            if (c.MaxLength is { } hi && s.Length > hi) return ConfigValueValidationResult.Fail($"String longer than maxLength {hi}.");
            if (c.Regex is not null && !System.Text.RegularExpressions.Regex.IsMatch(s, c.Regex))
                return ConfigValueValidationResult.Fail("String does not match regex.");
            if (c.Enum is { Count: > 0 } && !c.Enum.Contains(s, StringComparer.Ordinal))
                return ConfigValueValidationResult.Fail("Value not in enum.");
        }
        return ConfigValueValidationResult.Ok();
    }

    private static ConfigValueValidationResult ValidateInteger(ConfigKeyDefinition key, JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Number || !el.TryGetInt64(out var n))
            return ConfigValueValidationResult.Fail("Expected an integer.");
        var c = key.Constraints;
        if (c is not null)
        {
            if (c.Min is { } min && n < min) return ConfigValueValidationResult.Fail($"Integer below min {min}.");
            if (c.Max is { } max && n > max) return ConfigValueValidationResult.Fail($"Integer above max {max}.");
            if (c.Enum is { Count: > 0 } && !c.Enum.Contains(n.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal))
                return ConfigValueValidationResult.Fail("Value not in enum.");
        }
        return ConfigValueValidationResult.Ok();
    }

    private static ConfigValueValidationResult ValidateDecimal(ConfigKeyDefinition key, JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Number || !el.TryGetDecimal(out var d))
            return ConfigValueValidationResult.Fail("Expected a decimal.");
        var c = key.Constraints;
        if (c is not null)
        {
            if (c.Min is { } min && d < min) return ConfigValueValidationResult.Fail($"Decimal below min {min}.");
            if (c.Max is { } max && d > max) return ConfigValueValidationResult.Fail($"Decimal above max {max}.");
            if (c.Enum is { Count: > 0 } && !c.Enum.Contains(d.ToString(CultureInfo.InvariantCulture), StringComparer.Ordinal))
                return ConfigValueValidationResult.Fail("Value not in enum.");
        }
        return ConfigValueValidationResult.Ok();
    }

    private static ConfigValueValidationResult ValidateJsonValue(ConfigKeyDefinition key, string jsonValue)
    {
        try
        {
            var schema = JsonSchema.FromJsonAsync(key.JsonSchema!).GetAwaiter().GetResult();
            var problems = schema.Validate(jsonValue);
            if (problems.Count == 0) return ConfigValueValidationResult.Ok();
            return ConfigValueValidationResult.Fail(
                string.Join("; ", problems.Select(p => $"{p.Path} → {p.Kind}")));
        }
        catch (Exception ex)
        {
            return ConfigValueValidationResult.Fail("JSON Schema validation failed: " + ex.Message);
        }
    }
}

public readonly record struct ConfigValueValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    public static ConfigValueValidationResult Ok() => new(true, []);
    public static ConfigValueValidationResult Fail(string msg) => new(false, [msg]);
}
