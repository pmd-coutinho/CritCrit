using CritCrit.Api.Org.Domain;
using Marten;

namespace CritCrit.Api.Org.Infrastructure;

public static class OrgValidation
{
    public static string ValidateAndNormalizeCode(OrgNodeType type, string code)
    {
        if (!OrgCode.IsValid(type, code))
            throw new DomainException($"Invalid {type} code.");

        return OrgCode.Normalize(type, code);
    }

    public static async Task EnsureCodeAvailableAsync(
        IQuerySession session, OrgNodeId tenantId, string normalized, CancellationToken ct)
    {
        var existing = await session.LoadAsync<OrgNodeCodeIndex>(
            OrgNodeCodeIndex.BuildId(tenantId, normalized), ct);
        if (existing is { HardDeleted: false })
            throw new DomainException("Org node code is already reserved in this tenant.");
    }

    public static async Task<OrgNodeReadModel> LoadNodeAsync(
        IQuerySession session, OrgNodeId id, CancellationToken ct)
    {
        var node = await session.LoadAsync<OrgNodeReadModel>(id.Value, ct);
        return node ?? throw new DomainException("Org node was not found.");
    }

    public static async Task<OrgNodeReadModel> LoadActiveNodeAsync(
        IQuerySession session, OrgNodeId id, CancellationToken ct)
    {
        var node = await LoadNodeAsync(session, id, ct);
        if (node.HardDeleted || node.EffectiveArchived)
            throw new DomainException("Org node is inactive.");
        return node;
    }
}
