using CritCrit.Api.Org.Domain;
using Marten;

namespace CritCrit.Api.Org.Features.Assets;

public sealed class AssetResolutionService(IDocumentStore store)
{
    public sealed record ResolvedAsset(
        string Key,
        AssetResolutionSource Source,
        AssetStoredFile? File,
        string? SourceNodeId,
        long ValueSetVersion);

    public async Task<IReadOnlyList<ResolvedAsset>> ResolveAllAsync(OrgNodeReadModel target, CancellationToken ct)
    {
        var boundary = target.AncestorIds.Append(target.Id).ToArray();
        var bags = await LoadBagsAsync(target.TenantId, boundary, ct);
        var localVersion = bags.GetValueOrDefault(target.Id)?.Version ?? 0;
        var keys = bags.Values
            .SelectMany(b => b.Entries.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        return keys.Select(k => ResolveOne(k, boundary, bags, localVersion)).ToArray();
    }

    public async Task<ResolvedAsset> ResolveOneAsync(OrgNodeReadModel target, string key, CancellationToken ct)
    {
        var normalized = AssetKey.Normalize(key);
        var boundary = target.AncestorIds.Append(target.Id).ToArray();
        var bags = await LoadBagsAsync(target.TenantId, boundary, ct);
        var localVersion = bags.GetValueOrDefault(target.Id)?.Version ?? 0;
        return ResolveOne(normalized, boundary, bags, localVersion);
    }

    // ValueSetVersion always reflects the target node's local bag version so
    // callers can round-trip it as PatchAsset.expectedVersion regardless of
    // whether the resolved entry came from a local set, an inherited ancestor,
    // or a local unset.
    private static ResolvedAsset ResolveOne(
        string key,
        IReadOnlyList<Guid> boundary,
        Dictionary<Guid, AssetNodeValueReadModel> bags,
        long localValueSetVersion)
    {
        for (var i = boundary.Count - 1; i >= 0; i--)
        {
            if (!bags.TryGetValue(boundary[i], out var bag)) continue;
            if (!bag.Entries.TryGetValue(key, out var entry)) continue;

            return entry.State switch
            {
                AssetEntryState.Set => new ResolvedAsset(
                    key,
                    i == boundary.Count - 1 ? AssetResolutionSource.Local : AssetResolutionSource.Inherited,
                    entry.File,
                    boundary[i].ToString(),
                    localValueSetVersion),
                AssetEntryState.Unset => new ResolvedAsset(
                    key,
                    AssetResolutionSource.Unset,
                    null,
                    boundary[i].ToString(),
                    localValueSetVersion),
                _ => new ResolvedAsset(key, AssetResolutionSource.Missing, null, null, localValueSetVersion)
            };
        }

        return new ResolvedAsset(key, AssetResolutionSource.Missing, null, null, localValueSetVersion);
    }

    private async Task<Dictionary<Guid, AssetNodeValueReadModel>> LoadBagsAsync(
        Guid tenantId,
        IReadOnlyList<Guid> boundary,
        CancellationToken ct)
    {
        await using var session = store.QuerySession(tenantId.ToString());
        var bags = await session.Query<AssetNodeValueReadModel>()
            .Where(x => x.TenantId == tenantId && boundary.Contains(x.OrgNodeId))
            .ToListAsync(ct);
        return bags.ToDictionary(b => b.OrgNodeId);
    }
}
