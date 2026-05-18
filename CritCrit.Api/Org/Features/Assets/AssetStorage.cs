using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using CritCrit.Api.Org.Domain;

namespace CritCrit.Api.Org.Features.Assets;

public interface IAssetStorage
{
    Task UploadAsync(AssetStoredFile file, Stream content, CancellationToken ct);
    Task<Stream> OpenReadAsync(AssetStoredFile file, CancellationToken ct);
}

public sealed class BlobAssetStorage(BlobContainerClient container) : IAssetStorage
{
    public async Task UploadAsync(AssetStoredFile file, Stream content, CancellationToken ct)
    {
        await container.CreateIfNotExistsAsync(cancellationToken: ct);
        var blob = container.GetBlobClient(file.BlobName);
        await blob.UploadAsync(
            content,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = file.ContentType },
                Metadata = new Dictionary<string, string>
                {
                    ["asset_key_sha256"] = file.Sha256,
                    ["asset_kind"] = file.Kind.ToString()
                }
            },
            ct);
    }

    public async Task<Stream> OpenReadAsync(AssetStoredFile file, CancellationToken ct)
    {
        var blob = container.GetBlobClient(file.BlobName);
        return await blob.OpenReadAsync(cancellationToken: ct);
    }
}
