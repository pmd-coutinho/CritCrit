using CritCrit.Api.Org.Domain;
using Marten;
using Marten.Events;
using Marten.Events.Projections;

namespace CritCrit.Api.Org.Projections;

public sealed class MoveOrgNodeProjection : EventProjection
{
    public async Task Project(OrgNodeMoved e, IDocumentOperations ops, CancellationToken ct)
    {
        // Load moved node and new parent
        var node = await ops.LoadAsync<OrgNodeReadModel>(e.Id.Value, ct);
        var newParent = await ops.LoadAsync<OrgNodeReadModel>(e.NewParentId.Value, ct);
        if (node is null || newParent is null)
            return;

        // Compute new ancestors and path for the moved node
        var newAncestors = newParent.AncestorIds.Append(newParent.Id).ToList();
        var newAncestorPublicIds = newParent.AncestorPublicIds.Append(newParent.PublicId).ToList();
        var segment = $"{node.Type.ToString().ToLowerInvariant()}/{node.Code}";
        var newPath = $"{newParent.Path}/{segment}";

        // Update the moved node
        node.ParentId = e.NewParentId.Value;
        node.ParentPublicId = newParent.PublicId;
        node.AncestorIds = newAncestors;
        node.AncestorPublicIds = newAncestorPublicIds;
        node.Path = newPath;
        ops.Store(node);

        // Recalculate descendants
        await RecalculateDescendants(ops, node, ct);
    }

    private static async Task RecalculateDescendants(
        IDocumentOperations ops, OrgNodeReadModel parent, CancellationToken ct)
    {
        var descendants = await ops.Query<OrgNodeReadModel>()
            .Where(x => x.TenantId == parent.TenantId && x.ParentId == parent.Id && !x.HardDeleted)
            .ToListAsync(ct);

        foreach (var child in descendants)
        {
            child.AncestorIds = parent.AncestorIds.Append(parent.Id).ToList();
            child.AncestorPublicIds = parent.AncestorPublicIds.Append(parent.PublicId).ToList();
            var segment = $"{child.Type.ToString().ToLowerInvariant()}/{child.Code}";
            child.Path = $"{parent.Path}/{segment}";
            ops.Store(child);

            await RecalculateDescendants(ops, child, ct);
        }
    }
}
