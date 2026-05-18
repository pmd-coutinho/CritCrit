using System.Security.Cryptography;
using System.Text;

namespace CritCrit.Api.Platform;

/// <summary>
/// UUID v5 helper. Given the same namespace and name, always returns the same
/// <see cref="Guid"/>. Used to derive Marten event-stream IDs from composite
/// keys for find-or-create aggregates (Org Access Grant, Config Node Value,
/// Asset Node Value) so Wolverine <c>[AggregateHandler]</c> can route directly
/// from the composite key without a pre-query.
///
/// See <c>.scratch/deterministic-stream-ids/PRD.md</c> for the full rationale
/// and the lazy dual-mode migration strategy.
/// </summary>
public static class DeterministicGuid
{
    /// <summary>
    /// Fixed CritCrit namespace UUID. Never change this — every previously
    /// derived stream id depends on this value.
    /// </summary>
    public static readonly Guid Namespace = new("9b3a7e2c-8d4f-4a6b-bc2d-1e0f9a8b7c6d");

    /// <summary>Derive a stream id from one or more key parts joined with ":".</summary>
    public static Guid From(params object?[] parts)
    {
        var canonical = string.Join(":", parts.Select(p => p?.ToString() ?? ""));
        return Create(Namespace, canonical);
    }

    /// <summary>Create a UUID v5 from an explicit namespace and name.</summary>
    public static Guid Create(Guid ns, string name)
    {
        var nsBytes = ns.ToByteArray();
        ToBigEndian(nsBytes);

        var nameBytes = Encoding.UTF8.GetBytes(name);
        var input = new byte[nsBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(nsBytes, 0, input, 0, nsBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, input, nsBytes.Length, nameBytes.Length);

        Span<byte> hash = stackalloc byte[20];
        SHA1.HashData(input, hash);

        var guidBytes = new byte[16];
        hash[..16].CopyTo(guidBytes);

        // Set version 5 (top 4 bits of byte 6, big-endian time_hi_and_version)
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        // Set RFC 4122 variant (top 2 bits of byte 8 = 10)
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);

        FromBigEndian(guidBytes);
        return new Guid(guidBytes);
    }

    // .NET's Guid byte layout stores the first three fields little-endian.
    // UUID RFC 4122 is fully big-endian. Swap before hashing and after assembling.
    private static void ToBigEndian(byte[] bytes)
    {
        Array.Reverse(bytes, 0, 4);
        Array.Reverse(bytes, 4, 2);
        Array.Reverse(bytes, 6, 2);
    }

    private static void FromBigEndian(byte[] bytes) => ToBigEndian(bytes);
}
