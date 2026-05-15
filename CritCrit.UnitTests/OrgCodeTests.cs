using CritCrit.Api.Org.Domain;

namespace CritCrit.UnitTests;

public class OrgCodeTests
{
    [Theory]
    [InlineData(OrgNodeType.Country, "US", true)]
    [InlineData(OrgNodeType.Country, "us", true)]
    [InlineData(OrgNodeType.Country, "USA", false)]   // too long for ISO alpha-2
    [InlineData(OrgNodeType.Country, "U1", false)]    // digit rejected
    [InlineData(OrgNodeType.Country, "", false)]
    [InlineData(OrgNodeType.Brand, "acme-co", true)]
    [InlineData(OrgNodeType.Brand, "acme_co", false)] // underscore not in slug regex
    [InlineData(OrgNodeType.Brand, "ACME", false)]    // uppercase rejected
    [InlineData(OrgNodeType.Brand, "-acme", false)]   // can't start with hyphen
    [InlineData(OrgNodeType.Brand, "a", true)]
    [InlineData(OrgNodeType.Franchise, "fr-1", true)]
    [InlineData(OrgNodeType.Store, "st-001", true)]
    [InlineData(OrgNodeType.Device, "DV3PX-xyz", true)] // device allows mixed case
    public void IsValid_per_type(OrgNodeType type, string code, bool expected)
    {
        Assert.Equal(expected, OrgCode.IsValid(type, code));
    }

    [Fact]
    public void IsValid_rejects_codes_over_128_chars()
    {
        var tooLong = new string('a', 129);
        Assert.False(OrgCode.IsValid(OrgNodeType.Brand, tooLong));
        Assert.False(OrgCode.IsValid(OrgNodeType.Device, tooLong));
    }

    [Theory]
    [InlineData(OrgNodeType.Country, "us", "US")]
    [InlineData(OrgNodeType.Country, "  us  ", "US")]
    [InlineData(OrgNodeType.Brand, "ACME-Co", "acme-co")]
    [InlineData(OrgNodeType.Device, "DV3PX-XYZ", "dv3px-xyz")]
    [InlineData(OrgNodeType.Store, "  St-001  ", "st-001")]
    public void Normalize_trims_and_casefolds(OrgNodeType type, string input, string expected)
    {
        Assert.Equal(expected, OrgCode.Normalize(type, input));
    }
}
