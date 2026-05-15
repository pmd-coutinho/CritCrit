using CritCrit.Api.Org.Domain;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Patching;

namespace CritCrit.Api.Org.Projections;

public sealed class DeviceProfileProjection : EventProjection
{
    public async Task Project(DeviceProfileCreated e, IDocumentOperations ops, CancellationToken ct)
    {
        var node = await ops.LoadAsync<OrgNodeReadModel>(e.DeviceId.Value, ct);
        ops.Store(new DeviceProfileReadModel
        {
            Id = e.DeviceId.Value,
            TenantId = node?.TenantId ?? Guid.Empty,
            SerialNumber = e.SerialNumber,
            SerialNumberNormalized = e.SerialNumber.Trim().ToLowerInvariant(),
            DeviceType = e.DeviceType
        });
    }

    public void Project(DeviceProfileHardDeleted e, IDocumentOperations ops)
    {
        ops.Patch<DeviceProfileReadModel>(e.DeviceId.Value)
            .Set(x => x.HardDeleted, true);
    }
}
