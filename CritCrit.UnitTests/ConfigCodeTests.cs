using CritCrit.Api.Org.Features.Config;

namespace CritCrit.UnitTests;

public class ConfigCodeTests
{
    [Theory]
    [InlineData("posbridge", true)]
    [InlineData("pos-bridge", true)]
    [InlineData("a", true)]
    [InlineData("0", true)]
    [InlineData("kebab-case-allowed", true)]
    [InlineData("a-9", true)]
    [InlineData("UPPER", false)]
    [InlineData("with.dot", false)]    // dots reserved for lookup paths
    [InlineData("-leading", false)]
    [InlineData("with_underscore", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsValid_matrix(string input, bool expected)
    {
        Assert.Equal(expected, ConfigCode.IsValid(input));
    }

    [Fact]
    public void IsValid_rejects_codes_over_64_chars()
    {
        Assert.False(ConfigCode.IsValid(new string('a', 65)));
        Assert.True(ConfigCode.IsValid(new string('a', 64)));
    }

    [Theory]
    [InlineData("  ACME  ", "acme")]
    [InlineData("Foo-Bar", "foo-bar")]
    public void Normalize_trims_and_lowercases(string input, string expected)
    {
        Assert.Equal(expected, ConfigCode.Normalize(input));
    }

    [Fact]
    public void EnsureValidSchemaCode_throws_for_dot()
    {
        Assert.Throws<CritCrit.Api.Platform.Errors.DomainException>(
            () => ConfigCode.EnsureValidSchemaCode("with.dot"));
    }
}
