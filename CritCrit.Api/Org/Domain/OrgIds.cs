using System.Diagnostics.CodeAnalysis;

namespace CritCrit.Api.Org.Domain;

// IParsable<T> implementations let Wolverine.Http and ASP.NET Core route binders
// take public-id strings (e.g. "brand_…", "subj_…", "inv_…") directly as
// route parameters typed as the strong-typed id. Without this, callers must
// declare `string nodeId` and parse manually. With it, the route param can
// be `OrgNodeId nodeId` and Wolverine attempts TryParse, returning 404 on
// failure. Required for [Aggregate]/[WriteAggregate] adoption.

public readonly record struct OrgNodeId(Guid Value) : IParsable<OrgNodeId>
{
    public static OrgNodeId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();

    public static OrgNodeId Parse(string s, IFormatProvider? provider) =>
        TryParse(s, provider, out var result) ? result : throw new FormatException($"Not a valid org-node public id: {s}");

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out OrgNodeId result)
    {
        result = default;
        if (s is null) return false;
        if (OrgPublicId.TryParseOrgNode(s, out var id, out _)) { result = id; return true; }
        if (Guid.TryParse(s, out var raw)) { result = new OrgNodeId(raw); return true; }
        return false;
    }
}

public readonly record struct SubjectId(Guid Value) : IParsable<SubjectId>
{
    public static SubjectId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();

    public static SubjectId Parse(string s, IFormatProvider? provider) =>
        TryParse(s, provider, out var result) ? result : throw new FormatException($"Not a valid subject public id: {s}");

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out SubjectId result)
    {
        result = default;
        if (s is null) return false;
        if (OrgPublicId.TryParseSubject(s, out var id)) { result = id; return true; }
        if (Guid.TryParse(s, out var raw)) { result = new SubjectId(raw); return true; }
        return false;
    }
}

public readonly record struct InvitationId(Guid Value) : IParsable<InvitationId>
{
    public static InvitationId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();

    public static InvitationId Parse(string s, IFormatProvider? provider) =>
        TryParse(s, provider, out var result) ? result : throw new FormatException($"Not a valid invitation public id: {s}");

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out InvitationId result)
    {
        result = default;
        if (s is null) return false;
        if (OrgPublicId.TryParseInvitation(s, out var id)) { result = id; return true; }
        if (Guid.TryParse(s, out var raw)) { result = new InvitationId(raw); return true; }
        return false;
    }
}

public readonly record struct AuditEventId(Guid Value)
{
    public static AuditEventId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
