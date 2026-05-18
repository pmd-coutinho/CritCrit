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
        var keys = bags.Values
            .SelectMany(b => b.Entries.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        return keys.Select(k => ResolveOne(k, boundary, bags)).ToArray();
    }

    public async Task<ResolvedAsset> ResolveOneAsync(OrgNodeReadModel target, string key, CancellationToken ct)
    {
        var normalized = AssetKey.Normalize(key);
        var boundary = target.AncestorIds.Append(target.Id).ToArray();
        var bags = await LoadBagsAsync(target.TenantId, boundary, ct);
        return ResolveOne(normalized, boundary, bags);
    }

    private static ResolvedAsset ResolveOne(
        string key,
        IReadOnlyList<Guid> boundary,
        Dictionary<Guid, AssetNodeValueReadModel> bags)
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
                    bag.Version),
                AssetEntryState.Unset => new ResolvedAsset(
                    key,
                    AssetResolutionSource.Unset,
                    null,
                    boundary[i].ToString(),
                    bag.Version),
                _ => new ResolvedAsset(key, AssetResolutionSource.Missing, null, null, 0)
            };
        }

        return new ResolvedAsset(key, AssetResolutionSource.Missing, null, null, 0);
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
