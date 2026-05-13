using CritCrit.Api.Org.Domain;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Patching;

namespace CritCrit.Api.Org.Projections;

public sealed class StoreProfileProjection : EventProjection
{
    public async Task Project(StoreProfileCreated e, IDocumentOperations ops, CancellationToken ct)
    {
        var node = await ops.LoadAsync<OrgNodeReadModel>(e.StoreId.Value, ct);
        ops.Store(new StoreProfileReadModel
        {
            Id = e.StoreId.Value,
            TenantId = node?.TenantId ?? Guid.Empty,
            TimeZone = e.TimeZone
        });
    }

    public void Project(StoreProfileUpdated e, IDocumentOperations ops)
    {
        ops.Patch<StoreProfileReadModel>(e.StoreId.Value)
            .Set(x => x.TimeZone, e.TimeZone);
    }

    public void Project(StoreProfileHardDeleted e, IDocumentOperations ops)
    {
        ops.Delete<StoreProfileReadModel>(e.StoreId.Value);
    }
}
