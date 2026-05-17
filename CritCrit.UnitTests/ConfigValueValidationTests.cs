using CritCrit.Api.Org.Domain;
using CritCrit.Api.Org.Features.Config;

namespace CritCrit.UnitTests;

public class ConfigValueValidationTests
{
    private readonly ConfigValidationService _v = new();

    [Fact]
    public void boolean_accepts_true_false()
    {
        var k = new ConfigKeyDefinition("b", "B", null, ConfigValueType.Boolean, null, null, null);
        Assert.True(_v.ValidateValue(k, "true").IsValid);
        Assert.True(_v.ValidateValue(k, "false").IsValid);
        Assert.False(_v.ValidateValue(k, "\"true\"").IsValid);
        Assert.False(_v.ValidateValue(k, "1").IsValid);
    }

    [Fact]
    public void integer_respects_min_max()
    {
        var k = new ConfigKeyDefinition("n", "N", null, ConfigValueType.Integer, new(Min: 0, Max: 10), null, null);
        Assert.True(_v.ValidateValue(k, "0").IsValid);
        Assert.True(_v.ValidateValue(k, "10").IsValid);
        Assert.False(_v.ValidateValue(k, "-1").IsValid);
        Assert.False(_v.ValidateValue(k, "11").IsValid);
        Assert.False(_v.ValidateValue(k, "\"3\"").IsValid);
    }

    [Fact]
    public void string_respects_length_and_regex()
    {
        var k = new ConfigKeyDefinition(
            "s",
            "S",
            null,
            ConfigValueType.String,
            new(MinLength: 2, MaxLength: 5, Regex: "^[a-z]+$"),
            null,
            null);

        Assert.True(_v.ValidateValue(k, "\"abc\"").IsValid);
        Assert.False(_v.ValidateValue(k, "\"a\"").IsValid);
        Assert.False(_v.ValidateValue(k, "\"toolong\"").IsValid);
        Assert.False(_v.ValidateValue(k, "\"AB\"").IsValid);
    }

    [Fact]
    public void string_enum_constraint_enforced()
    {
        var k = new ConfigKeyDefinition(
            "mode",
            "Mode",
            null,
            ConfigValueType.String,
            new(Enum: ["lazy", "eager"]),
            null,
            null);

        Assert.True(_v.ValidateValue(k, "\"lazy\"").IsValid);
        Assert.False(_v.ValidateValue(k, "\"other\"").IsValid);
    }

    [Fact]
    public void decimal_accepts_decimals_and_integers()
    {
        var k = new ConfigKeyDefinition("d", "D", null, ConfigValueType.Decimal, null, null, null);
        Assert.True(_v.ValidateValue(k, "1.5").IsValid);
        Assert.True(_v.ValidateValue(k, "0").IsValid);
        Assert.False(_v.ValidateValue(k, "\"1.5\"").IsValid);
    }

    [Fact]
    public void json_object_validates_against_self_contained_schema()
    {
        var k = new ConfigKeyDefinition(
            "obj",
            "Obj",
            null,
            ConfigValueType.JsonObject,
            null,
            """{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""",
            null);

        Assert.True(_v.ValidateValue(k, """{"name":"x"}""").IsValid);
        Assert.False(_v.ValidateValue(k, """{}""").IsValid);
        Assert.False(_v.ValidateValue(k, """{"name":123}""").IsValid);
    }

    [Fact]
    public void encrypted_string_validates_plaintext_constraints()
    {
        // Constraints apply to the plaintext shape before encryption.
        var k = new ConfigKeyDefinition(
            "secret",
            "Secret",
            null,
            ConfigValueType.EncryptedString,
            new(MinLength: 8),
            null,
            null);

        Assert.True(_v.ValidateValue(k, "\"long-enough-secret\"").IsValid);
        Assert.False(_v.ValidateValue(k, "\"short\"").IsValid);
    }
}
