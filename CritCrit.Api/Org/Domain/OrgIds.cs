using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CritCrit.Api.Org.Domain;

// IParsable<T> implementations let Wolverine.Http and ASP.NET Core route binders
// take public-id strings (e.g. "brand_…", "subj_…", "inv_…") directly as
// route parameters typed as the strong-typed id. JsonConverters do the same
// for JSON request-body fields. Without these, callers must declare `string`
// route/body params and parse manually. Required for [Aggregate]/[WriteAggregate]
// adoption.

[JsonConverter(typeof(OrgNodeIdJsonConverter))]
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

[JsonConverter(typeof(SubjectIdJsonConverter))]
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

[JsonConverter(typeof(InvitationIdJsonConverter))]
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

// JSON converters: read public-id strings (preferred) or raw Guid strings into
// the strong-typed id. Write the public-id form when the type is known (only
// possible for SubjectId / InvitationId which have a fixed prefix). OrgNodeId
// writes the raw Guid because the public-id requires the OrgNodeType, which a
// bare OrgNodeId doesn't carry — response DTOs always use pre-formatted
// `node.PublicId` strings instead of exposing OrgNodeId directly, so the Write
// path here is only ever a fallback.

internal sealed class OrgNodeIdJsonConverter : JsonConverter<OrgNodeId>
{
    public override OrgNodeId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        if (s is null) throw new JsonException("OrgNodeId cannot be null.");
        return OrgNodeId.TryParse(s, null, out var id)
            ? id
            : throw new JsonException($"Invalid OrgNodeId: {s}");
    }

    public override void Write(Utf8JsonWriter writer, OrgNodeId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value.ToString());
}

internal sealed class SubjectIdJsonConverter : JsonConverter<SubjectId>
{
    public override SubjectId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        if (s is null) throw new JsonException("SubjectId cannot be null.");
        return SubjectId.TryParse(s, null, out var id)
            ? id
            : throw new JsonException($"Invalid SubjectId: {s}");
    }

    public override void Write(Utf8JsonWriter writer, SubjectId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(OrgPublicId.FormatSubject(value));
}

internal sealed class InvitationIdJsonConverter : JsonConverter<InvitationId>
{
    public override InvitationId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        if (s is null) throw new JsonException("InvitationId cannot be null.");
        return InvitationId.TryParse(s, null, out var id)
            ? id
            : throw new JsonException($"Invalid InvitationId: {s}");
    }

    public override void Write(Utf8JsonWriter writer, InvitationId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(OrgPublicId.FormatInvitation(value));
}
