using System.Text.Json;
using CritCrit.Api.Org.Domain;
using Marten;

namespace CritCrit.Api.Org.Features.Config;

/// <summary>
/// Per-key nearest-wins lookup with explicit-unset + defaults. Encrypted keys
/// are masked or omitted depending on caller mode. Pure read-side service; no
/// audit / events here.
/// </summary>
public sealed class ConfigResolutionService(IDocumentStore store)
{
    public sealed record ResolvedKey(
        string KeyCode,
        ConfigValueResolutionSource Source,
        ConfigValueType ValueType,
        string? JsonValue,
        string? SourceNodeId,
        bool Encrypted);

    public sealed record AssignmentLookup(
        ConfigAssignmentReadModel Assignment,
        ConfigSchemaVersionReadModel SchemaVersion);

    /// <summary>
    /// Pick the deepest active assignment for <paramref name="schemaCode"/>
    /// that roots at the target node or any of its ancestors. Returns null if
    /// no assignment covers the node.
    /// </summary>
    public async Task<AssignmentLookup?> ResolveAssignmentAsync(
        OrgNodeReadModel target,
        string schemaCode,
        CancellationToken ct)
    {
        var schemaNormalized = ConfigCode.Normalize(schemaCode);
        var path = target.AncestorIds.Append(target.Id).ToArray();

        await using var tenantSession = store.QuerySession(target.TenantId.ToString());
        var assignments = await tenantSession.Query<ConfigAssignmentReadModel>()
            .Where(x => x.TenantId == target.TenantId
                        && x.SchemaCode == schemaNormalized
                        && !x.Archived
                        && path.Contains(x.RootOrgNodeId))
            .ToListAsync(ct);

        if (assignments.Count == 0) return null;

        // Choose the assignment whose root sits deepest in path (largest index).
        var indexOf = new Dictionary<Guid, int>();
        for (var i = 0; i < path.Length; i++) indexOf[path[i]] = i;

        var deepest = assignments.OrderByDescending(a => indexOf[a.RootOrgNodeId]).First();

        await using var platform = store.QuerySession();
        var snap = await platform.LoadAsync<ConfigSchemaVersionReadModel>(
            ConfigSchemaVersionReadModel.BuildId(deepest.SchemaCode, deepest.SchemaVersion), ct);
        return snap is null ? null : new AssignmentLookup(deepest, snap);
    }

    /// <summary>
    /// Resolve every key in the schema. Returns one entry per key including
    /// missing/unset states; callers filter for the response shape they want.
    /// </summary>
    public async Task<IReadOnlyList<ResolvedKey>> ResolveAllAsync(
        OrgNodeReadModel target,
        AssignmentLookup lookup,
        CancellationToken ct)
    {
        var boundary = BuildBoundary(target, lookup.Assignment);
        var bags = await LoadValueBagsAsync(target.TenantId, boundary, lookup.Assignment.SchemaCode, ct);

        var result = new List<ResolvedKey>(lookup.SchemaVersion.Definition.Keys.Count);
        foreach (var key in lookup.SchemaVersion.Definition.Keys)
        {
            result.Add(ResolveOne(key, boundary, bags));
        }
        return result;
    }

    public ResolvedKey ResolveOne(ConfigKeyDefinition key, IReadOnlyList<Guid> boundary, Dictionary<Guid, ConfigNodeValueReadModel> bags)
    {
        // Walk from deepest (last in boundary) up to assignment root.
        for (var i = boundary.Count - 1; i >= 0; i--)
        {
            if (!bags.TryGetValue(boundary[i], out var bag)) continue;
            if (!bag.Entries.TryGetValue(key.Code, out var entry)) continue;

            return entry.State switch
            {
                ConfigValueEntryState.Set => new ResolvedKey(
                    key.Code,
                    i == boundary.Count - 1 ? ConfigValueResolutionSource.Local : ConfigValueResolutionSource.Inherited,
                    key.ValueType,
                    entry.Value?.JsonValue,
                    boundary[i].ToString(),
                    key.ValueType == ConfigValueType.EncryptedString),
                ConfigValueEntryState.Unset => new ResolvedKey(
                    key.Code,
                    ConfigValueResolutionSource.Unset,
                    key.ValueType,
                    null,
                    boundary[i].ToString(),
                    key.ValueType == ConfigValueType.EncryptedString),
                _ => new ResolvedKey(key.Code, ConfigValueResolutionSource.Missing, key.ValueType, null, null, false),
            };
        }

        if (key.DefaultValue is not null && key.ValueType != ConfigValueType.EncryptedString)
            return new ResolvedKey(key.Code, ConfigValueResolutionSource.Default, key.ValueType, key.DefaultValue.JsonValue, null, false);

        return new ResolvedKey(
            key.Code,
            ConfigValueResolutionSource.Missing,
            key.ValueType,
            null,
            null,
            key.ValueType == ConfigValueType.EncryptedString);
    }

    private static List<Guid> BuildBoundary(OrgNodeReadModel target, ConfigAssignmentReadModel assignment)
    {
        var fullPath = target.AncestorIds.Append(target.Id).ToList();
        var rootIndex = fullPath.IndexOf(assignment.RootOrgNodeId);
        if (rootIndex < 0) return [target.Id];
        return fullPath.Skip(rootIndex).ToList();
    }

    private async Task<Dictionary<Guid, ConfigNodeValueReadModel>> LoadValueBagsAsync(
        Guid tenantId,
        IReadOnlyList<Guid> boundaryNodes,
        string schemaCode,
        CancellationToken ct)
    {
        await using var session = store.QuerySession(tenantId.ToString());
        var bags = await session.Query<ConfigNodeValueReadModel>()
            .Where(x => x.TenantId == tenantId
                        && x.SchemaCode == schemaCode
                        && boundaryNodes.Contains(x.OrgNodeId))
            .ToListAsync(ct);
        return bags.ToDictionary(b => b.OrgNodeId);
    }
}
