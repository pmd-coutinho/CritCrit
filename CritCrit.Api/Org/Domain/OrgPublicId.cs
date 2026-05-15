namespace CritCrit.Api.Org.Domain;

public static class OrgPublicId
{
    private static readonly Dictionary<OrgNodeType, string> Prefixes = new()
    {
        [OrgNodeType.Brand] = "brand",
        [OrgNodeType.Country] = "country",
        [OrgNodeType.Franchise] = "franchise",
        [OrgNodeType.Store] = "store",
        [OrgNodeType.Device] = "device"
    };

    public static string Format(OrgNodeType type, OrgNodeId id) => $"{Prefixes[type]}_{id.Value}";

    public static bool TryParseOrgNode(string value, out OrgNodeId id, out OrgNodeType type)
    {
        id = default;
        type = default;

        var separator = value.IndexOf('_', StringComparison.Ordinal);
        if (separator <= 0 || separator == value.Length - 1)
            return false;

        var prefix = value[..separator];
        var rawId = value[(separator + 1)..];
        var match = Prefixes.SingleOrDefault(x => x.Value == prefix);
        if (string.IsNullOrWhiteSpace(match.Value) || !Guid.TryParse(rawId, out var guid))
            return false;

        id = new OrgNodeId(guid);
        type = match.Key;
        return true;
    }

    public static bool TryParseOrgNode(string value, OrgNodeType expectedType, out OrgNodeId id)
    {
        if (TryParseOrgNode(value, out id, out var actualType) && actualType == expectedType)
            return true;

        id = default;
        return false;
    }

    public static string FormatSubject(SubjectId id) => $"subj_{id.Value}";

    public static bool TryParseSubject(string value, out SubjectId id)
    {
        id = default;
        const string prefix = "subj_";
        if (!value.StartsWith(prefix, StringComparison.Ordinal) ||
            !Guid.TryParse(value[prefix.Length..], out var guid))
            return false;

        id = new SubjectId(guid);
        return true;
    }

    public static string FormatInvitation(InvitationId id) => $"inv_{id.Value}";

    public static bool TryParseInvitation(string value, out InvitationId id)
    {
        id = default;
        const string prefix = "inv_";
        if (!value.StartsWith(prefix, StringComparison.Ordinal) ||
            !Guid.TryParse(value[prefix.Length..], out var guid))
            return false;

        id = new InvitationId(guid);
        return true;
    }
}
