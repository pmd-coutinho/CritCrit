using System.Text.RegularExpressions;
using CritCrit.Api.Org.Domain;

namespace CritCrit.Api.Org.Features.Assets;

public static partial class AssetKey
{
    public const int MaxLength = 128;

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,62}(\\.[a-z0-9][a-z0-9-]{0,62})*$")]
    private static partial Regex Pattern();

    public static string Normalize(string key) => key.Trim().ToLowerInvariant();

    public static bool IsValid(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var trimmed = key.Trim();
        if (trimmed.Length is 0 or > MaxLength) return false;
        if (trimmed.Contains('/') || trimmed.Contains('\\')) return false;
        return Pattern().IsMatch(trimmed);
    }

    public static void EnsureValid(string key)
    {
        if (!IsValid(key))
            throw new DomainException(
                "Asset key must be lowercase dot-separated kebab segments; 1-128 chars; no spaces or slashes.");
    }
}
