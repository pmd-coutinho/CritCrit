using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Features.Config;
using CritCrit.Api.Platform.Errors;

namespace CritCrit.UnitTests;

public class ConfigSchemaValidationTests
{
    private readonly ConfigValidationService _v = new();

    [Fact]
    public void rejects_duplicate_key_codes()
    {
        var def = new ConfigSchemaDefinition(
            "Bridge",
            null,
            [
                Key("one", ConfigValueType.String),
                Key("One", ConfigValueType.Integer),
            ]);

        var ex = Assert.Throws<DomainException>(() => _v.ValidateSchemaDefinition(def));
        Assert.Contains("Duplicate", ex.Message);
    }

    [Fact]
    public void requires_json_schema_for_json_object_keys()
    {
        var def = new ConfigSchemaDefinition(
            "S",
            null,
            [Key("obj", ConfigValueType.JsonObject, jsonSchema: null)]);

        Assert.Throws<DomainException>(() => _v.ValidateSchemaDefinition(def));
    }

    [Fact]
    public void rejects_json_schema_on_primitive_keys()
    {
        var def = new ConfigSchemaDefinition(
            "S",
            null,
            [Key("name", ConfigValueType.String, jsonSchema: """{"type":"string"}""")]);

        Assert.Throws<DomainException>(() => _v.ValidateSchemaDefinition(def));
    }

    [Fact]
    public void rejects_ref_anywhere_in_json_schema()
    {
        var def = new ConfigSchemaDefinition(
            "S",
            null,
            [
                Key(
                    "obj",
                    ConfigValueType.JsonObject,
                    jsonSchema: """
                    {"type":"object","properties":{"nested":{"$ref":"#/definitions/X"}}}
                    """),
            ]);

        var ex = Assert.Throws<DomainException>(() => _v.ValidateSchemaDefinition(def));
        Assert.Contains("$ref", ex.Message);
    }

    [Fact]
    public void rejects_default_for_encrypted_keys()
    {
        var def = new ConfigSchemaDefinition(
            "S",
            null,
            [
                new ConfigKeyDefinition(
                    "secret",
                    "Secret",
                    null,
                    ConfigValueType.EncryptedString,
                    null,
                    null,
                    new ConfigDefaultValue("\"oops\"")),
            ]);

        Assert.Throws<DomainException>(() => _v.ValidateSchemaDefinition(def));
    }

    [Fact]
    public void rejects_invalid_default_against_constraints()
    {
        var def = new ConfigSchemaDefinition(
            "S",
            null,
            [
                new ConfigKeyDefinition(
                    "n",
                    "N",
                    null,
                    ConfigValueType.Integer,
                    new ConfigValueConstraints(Min: 10, Max: 20),
                    null,
                    new ConfigDefaultValue("5")),
            ]);

        Assert.Throws<DomainException>(() => _v.ValidateSchemaDefinition(def));
    }

    [Fact]
    public void accepts_well_formed_schema()
    {
        var def = new ConfigSchemaDefinition(
            "Bridge",
            "POS bridge settings",
            [
                Key("usetaxcalc", ConfigValueType.Boolean, defaultJson: "true"),
                Key("menuname", ConfigValueType.String, constraints: new(MinLength: 1, MaxLength: 64), defaultJson: "\"main\""),
                Key("retries", ConfigValueType.Integer, constraints: new(Min: 0, Max: 10), defaultJson: "3"),
                Key("connection-string", ConfigValueType.EncryptedString),
                Key(
                    "endpoints",
                    ConfigValueType.JsonArray,
                    jsonSchema: """{"type":"array","items":{"type":"string"}}"""),
            ]);

        _v.ValidateSchemaDefinition(def);
    }

    private static ConfigKeyDefinition Key(
        string code,
        ConfigValueType type,
        ConfigValueConstraints? constraints = null,
        string? jsonSchema = null,
        string? defaultJson = null) =>
        new(
            code,
            code,
            null,
            type,
            constraints,
            jsonSchema,
            defaultJson is null ? null : new ConfigDefaultValue(defaultJson));
}
