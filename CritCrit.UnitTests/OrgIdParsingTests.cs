using CritCrit.Api.Org.Domain;

public sealed class OrgIdParsingTests
{
    [Fact]
    public void org_node_id_parses_brand_public_id()
    {
        var guid = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var public_id = OrgPublicId.Format(OrgNodeType.Brand, new OrgNodeId(guid));

        Assert.True(OrgNodeId.TryParse(public_id, null, out var parsed));
        Assert.Equal(guid, parsed.Value);
    }

    [Fact]
    public void org_node_id_parses_raw_guid_fallback()
    {
        var guid = Guid.Parse("22222222-2222-2222-2222-222222222222");

        Assert.True(OrgNodeId.TryParse(guid.ToString(), null, out var parsed));
        Assert.Equal(guid, parsed.Value);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("")]
    [InlineData("brand_not-a-guid")]
    [InlineData("unknown_11111111-1111-1111-1111-111111111111")]
    public void org_node_id_rejects_invalid_input(string input)
    {
        Assert.False(OrgNodeId.TryParse(input, null, out _));
    }

    [Fact]
    public void org_node_id_null_returns_false()
    {
        Assert.False(OrgNodeId.TryParse(null, null, out _));
    }

    [Fact]
    public void subject_id_parses_public_id()
    {
        var guid = Guid.Parse("33333333-3333-3333-3333-333333333333");
        Assert.True(SubjectId.TryParse(OrgPublicId.FormatSubject(new SubjectId(guid)), null, out var parsed));
        Assert.Equal(guid, parsed.Value);
    }

    [Fact]
    public void invitation_id_parses_public_id()
    {
        var guid = Guid.Parse("44444444-4444-4444-4444-444444444444");
        Assert.True(InvitationId.TryParse(OrgPublicId.FormatInvitation(new InvitationId(guid)), null, out var parsed));
        Assert.Equal(guid, parsed.Value);
    }
}
