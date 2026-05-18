using CritCrit.Api.Org.Domain;

namespace CritCrit.Api.Org.Features.Assets;

public sealed record AssetFileResponse(
    string FileName,
    string ContentType,
    AssetKind Kind,
    long Length,
    string Sha256,
    DateTimeOffset UploadedAt,
    string UploadedByExternalId);

public sealed record AssetLookupResponse(
    string Key,
    string Group,
    string State,
    string Source,
    string? SourceNodeId,
    long ValueSetVersion,
    AssetFileResponse? File,
    string? ContentUrl);

public sealed record AssetMutationResponse(long ValueSetVersion);

public sealed record PatchAssetRequest(long ExpectedVersion, string? Reason);
