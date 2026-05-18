using System.Security.Cryptography;
using CritCrit.Api.Org.Domain;

namespace CritCrit.Api.Org.Features.Assets;

public static class AssetValidation
{
    public const long VideoMaxBytes = 100L * 1024 * 1024;
    public const long StandardMaxBytes = 25L * 1024 * 1024;

    private static readonly Dictionary<string, (AssetKind Kind, long Limit)> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/png"] = (AssetKind.Image, StandardMaxBytes),
        ["image/jpeg"] = (AssetKind.Image, StandardMaxBytes),
        ["image/webp"] = (AssetKind.Image, StandardMaxBytes),
        ["image/gif"] = (AssetKind.Image, StandardMaxBytes),
        ["video/mp4"] = (AssetKind.Video, VideoMaxBytes),
        ["video/webm"] = (AssetKind.Video, VideoMaxBytes),
        ["video/quicktime"] = (AssetKind.Video, VideoMaxBytes),
        ["application/pdf"] = (AssetKind.Pdf, StandardMaxBytes),
        ["text/markdown"] = (AssetKind.Markdown, StandardMaxBytes),
        ["text/x-markdown"] = (AssetKind.Markdown, StandardMaxBytes),
        ["text/plain"] = (AssetKind.Markdown, StandardMaxBytes)
    };

    public static (AssetKind Kind, long Limit) Classify(string contentType, string fileName)
    {
        var normalized = contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
        if (Allowed.TryGetValue(normalized, out var match))
            return match;

        if (fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            return (AssetKind.Markdown, StandardMaxBytes);

        throw new DomainException("Unsupported asset type. Allowed: raster images, videos, PDF, and Markdown.");
    }

    public static async Task<(MemoryStream Content, string Sha256)> BufferAndHashAsync(Stream input, long maxBytes, CancellationToken ct)
    {
        await using var limited = new LimitedReadStream(input, maxBytes + 1);
        var buffer = new MemoryStream();
        await limited.CopyToAsync(buffer, ct);
        if (buffer.Length > maxBytes)
            throw new DomainException($"Asset exceeds the maximum size of {maxBytes / 1024 / 1024} MB.");

        buffer.Position = 0;
        var hash = await SHA256.HashDataAsync(buffer, ct);
        buffer.Position = 0;
        return (buffer, Convert.ToHexString(hash).ToLowerInvariant());
    }

    private sealed class LimitedReadStream(Stream inner, long maxBytes) : Stream
    {
        private long _read;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            var remaining = maxBytes - _read;
            if (remaining <= 0) return 0;
            var read = inner.Read(buffer, offset, (int)Math.Min(count, remaining));
            _read += read;
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var remaining = maxBytes - _read;
            if (remaining <= 0) return 0;
            var read = await inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length, remaining)], cancellationToken);
            _read += read;
            return read;
        }
    }
}
