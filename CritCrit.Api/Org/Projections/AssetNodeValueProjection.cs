using CritCrit.Api.Org.Domain;
using JasperFx.Events;
using Marten;
using Marten.Events.Projections;

namespace CritCrit.Api.Org.Projections;

public sealed class AssetNodeValueProjection : EventProjection
{
    public void Project(IEvent<AssetNodeValueSetInitialized> e, IDocumentOperations ops)
    {
        ops.Store(new AssetNodeValueReadModel
        {
            Id = AssetNodeValueReadModel.BuildId(e.Data.TenantId.Value, e.Data.OrgNodeId.Value),
            StreamId = e.Data.StreamId,
            TenantId = e.Data.TenantId.Value,
            OrgNodeId = e.Data.OrgNodeId.Value,
            Entries = new Dictionary<string, AssetEntry>(StringComparer.Ordinal),
            Version = 1,
            UpdatedAt = e.Timestamp
        });
    }

    public async Task Project(IEvent<AssetNodeValuesPatched> e, IDocumentOperations ops, CancellationToken ct)
    {
        var id = AssetNodeValueReadModel.BuildId(e.Data.TenantId.Value, e.Data.OrgNodeId.Value);
        var doc = await ops.LoadAsync<AssetNodeValueReadModel>(id, ct) ?? new AssetNodeValueReadModel
        {
            Id = id,
            StreamId = e.StreamId,
            TenantId = e.Data.TenantId.Value,
            OrgNodeId = e.Data.OrgNodeId.Value,
            Entries = new Dictionary<string, AssetEntry>(StringComparer.Ordinal),
            Version = 0,
            UpdatedAt = e.Data.AppliedAt
        };

        foreach (var op in e.Data.Operations)
        {
            switch (op.Operation)
            {
                case AssetPatchOperationKind.Set:
                    doc.Entries[op.Key] = new AssetEntry(
                        op.Key,
                        AssetEntryState.Set,
                        op.File,
                        e.Data.AppliedAt,
                        op.UpdatedByExternalId);
                    break;

                case AssetPatchOperationKind.Inherit:
                    doc.Entries.Remove(op.Key);
                    break;

                case AssetPatchOperationKind.Unset:
                    doc.Entries[op.Key] = new AssetEntry(
                        op.Key,
                        AssetEntryState.Unset,
                        null,
                        e.Data.AppliedAt,
                        op.UpdatedByExternalId);
                    break;
            }
        }

        doc.UpdatedAt = e.Data.AppliedAt;
        doc.Version++;
        ops.Store(doc);
    }
}
