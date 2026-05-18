namespace CritCrit.Api.Org.Domain;

public enum AssetKind
{
    Image,
    Video,
    Pdf,
    Markdown
}

public enum AssetEntryState
{
    Set,
    Unset
}

public enum AssetPatchOperationKind
{
    Set,
    Inherit,
    Unset
}

public enum AssetResolutionSource
{
    Local,
    Inherited,
    Unset,
    Missing
}

public sealed record AssetStoredFile(
    string BlobName,
    string FileName,
    string ContentType,
    AssetKind Kind,
    long Length,
    string Sha256,
    DateTimeOffset UploadedAt,
    string UploadedByExternalId);

public sealed record AssetEntry(
    string Key,
    AssetEntryState State,
    AssetStoredFile? File,
    DateTimeOffset UpdatedAt,
    string? UpdatedByExternalId);

public sealed record AssetPatchApplied(
    string Key,
    AssetPatchOperationKind Operation,
    AssetStoredFile? File,
    string? UpdatedByExternalId);
