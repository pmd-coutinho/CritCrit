using System.Text.RegularExpressions;

namespace CritCrit.Api.Org.Domain;

public static partial class OrgCode
{
    public static string Normalize(OrgNodeType type, string code)
    {
        var trimmed = code.Trim();
        return type switch
        {
            OrgNodeType.Country => trimmed.ToUpperInvariant(),
            OrgNodeType.Device => trimmed.ToLowerInvariant(),
            _ => trimmed.ToLowerInvariant()
        };
    }

    public static bool IsValid(OrgNodeType type, string code)
    {
        var trimmed = code.Trim();
        if (trimmed.Length is 0 or > 128)
            return false;

        return type switch
        {
            OrgNodeType.Country => CountryCodeRegex().IsMatch(trimmed),
            OrgNodeType.Device => trimmed.Length <= 128,
            _ => SlugCodeRegex().IsMatch(trimmed)
        };
    }

    [GeneratedRegex("^[A-Za-z]{2}$")]
    private static partial Regex CountryCodeRegex();

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$")]
    private static partial Regex SlugCodeRegex();
}
