using CritCrit.Api.Org.Domain;

namespace CritCrit.UnitTests;

public class OrgPublicIdTests
{
    [Theory]
    [InlineData(OrgNodeType.Brand, "brand")]
    [InlineData(OrgNodeType.Country, "country")]
    [InlineData(OrgNodeType.Franchise, "franchise")]
    [InlineData(OrgNodeType.Store, "store")]
    [InlineData(OrgNodeType.Device, "device")]
    public void Format_emits_the_prefix_plus_underscore_plus_guid(OrgNodeType type, string expectedPrefix)
    {
        var id = OrgNodeId.New();
        var formatted = OrgPublicId.Format(type, id);

        Assert.StartsWith(expectedPrefix + "_", formatted);
        Assert.EndsWith(id.Value.ToString(), formatted);
    }

    [Fact]
    public void TryParseOrgNode_round_trips_format()
    {
        var original = OrgNodeId.New();
        var formatted = OrgPublicId.Format(OrgNodeType.Store, original);

        var parsed = OrgPublicId.TryParseOrgNode(formatted, out var id, out var type);

        Assert.True(parsed);
        Assert.Equal(original.Value, id.Value);
        Assert.Equal(OrgNodeType.Store, type);
    }

    [Theory]
    [InlineData("")]
    [InlineData("noprefix")]
    [InlineData("_only-prefix")]
    [InlineData("brand_")]
    [InlineData("brand_not-a-guid")]
    [InlineData("unknown_00000000-0000-0000-0000-000000000000")]
    public void TryParseOrgNode_rejects_malformed_input(string input)
    {
        Assert.False(OrgPublicId.TryParseOrgNode(input, out _, out _));
    }

    [Fact]
    public void TryParseOrgNode_with_expected_type_succeeds_when_type_matches()
    {
        var id = OrgNodeId.New();
        var formatted = OrgPublicId.Format(OrgNodeType.Country, id);

        Assert.True(OrgPublicId.TryParseOrgNode(formatted, OrgNodeType.Country, out var parsed));
        Assert.Equal(id.Value, parsed.Value);
    }

    [Fact]
    public void TryParseOrgNode_with_expected_type_fails_when_type_mismatches()
    {
        var formatted = OrgPublicId.Format(OrgNodeType.Country, OrgNodeId.New());
        Assert.False(OrgPublicId.TryParseOrgNode(formatted, OrgNodeType.Store, out _));
    }

    [Fact]
    public void TryParseSubject_round_trips_subj_prefix()
    {
        var original = SubjectId.New();
        var formatted = OrgPublicId.FormatSubject(original);

        Assert.StartsWith("subj_", formatted);
        Assert.True(OrgPublicId.TryParseSubject(formatted, out var parsed));
        Assert.Equal(original.Value, parsed.Value);
    }

    [Theory]
    [InlineData("inv_00000000-0000-0000-0000-000000000000", true)]
    [InlineData("subj_00000000-0000-0000-0000-000000000000", false)]
    [InlineData("inv_xxxx", false)]
    [InlineData("", false)]
    public void TryParseInvitation_only_accepts_inv_prefix(string input, bool expected)
    {
        Assert.Equal(expected, OrgPublicId.TryParseInvitation(input, out _));
    }
}
