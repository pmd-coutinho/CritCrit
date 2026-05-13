using CritCrit.Api.Org.Domain;
using Marten;
using Marten.Events;
using Marten.Events.Projections;

namespace CritCrit.Api.Org.Projections;

public sealed class OrgNodeCodeIndexProjection : EventProjection
{
    public void Project(OrgNodeCreated e, IDocumentOperations ops)
    {
        ops.Store(new OrgNodeCodeIndex
        {
            Id = OrgNodeCodeIndex.BuildId(e.TenantId, e.CodeNormalized),
            TenantId = e.TenantId.Value,
            CodeNormalized = e.CodeNormalized,
            OrgNodeId = e.Id.Value
        });
    }

    public async Task Project(OrgNodeHardDeleted e, IDocumentOperations ops, CancellationToken ct)
    {
        var node = await ops.LoadAsync<OrgNodeReadModel>(e.Id.Value, ct);
        if (node is not null)
        {
            var id = OrgNodeCodeIndex.BuildId(new OrgNodeId(node.TenantId), node.CodeNormalized);
            ops.Delete<OrgNodeCodeIndex>(id);
        }
    }
}
