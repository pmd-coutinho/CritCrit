using System.Text.RegularExpressions;
using CritCrit.Api.Org.Domain;

namespace CritCrit.Api.Org.Features.Config;

/// <summary>
/// Validators + normalizers for config schema codes and key codes.
/// Lowercase a-z, digits, hyphens only. Dots are reserved as path separator
/// in lookup paths ("schemaCode.keyCode") and so must never appear inside
/// either component.
/// </summary>
public static partial class ConfigCode
{
    public const int MaxLength = 64;

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$")]
    private static partial Regex Pattern();

    public static string Normalize(string code) => code.Trim().ToLowerInvariant();

    public static bool IsValid(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        var trimmed = code.Trim();
        if (trimmed.Length is 0 or > MaxLength) return false;
        if (trimmed.Contains('.')) return false;
        return Pattern().IsMatch(trimmed);
    }

    public static void EnsureValidSchemaCode(string code)
    {
        if (!IsValid(code))
            throw new DomainException(
                "Schema code must be lowercase a-z, 0-9, and hyphens; 1-64 chars; cannot start with a hyphen.");
    }

    public static void EnsureValidKeyCode(string code)
    {
        if (!IsValid(code))
            throw new DomainException(
                "Key code must be lowercase a-z, 0-9, and hyphens; 1-64 chars; cannot start with a hyphen.");
    }
}
