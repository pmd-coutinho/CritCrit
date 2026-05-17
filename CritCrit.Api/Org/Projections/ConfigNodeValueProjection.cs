using CritCrit.Api.Org.Domain;
using JasperFx.Events;
using Marten;
using Marten.Events.Projections;

namespace CritCrit.Api.Org.Projections;

/// <summary>
/// Per-(tenant, node, schemaCode) value bag. One stream per slot; the read
/// model document id is the stable BuildId composite so resolution can load
/// it directly without an extra query.
/// </summary>
public sealed class ConfigNodeValueProjection : EventProjection
{
    public void Project(IEvent<ConfigNodeValueSetInitialized> e, IDocumentOperations ops)
    {
        ops.Store(new ConfigNodeValueReadModel
        {
            Id = ConfigNodeValueReadModel.BuildId(e.Data.TenantId.Value, e.Data.OrgNodeId.Value, e.Data.SchemaCode),
            StreamId = e.Data.StreamId,
            TenantId = e.Data.TenantId.Value,
            OrgNodeId = e.Data.OrgNodeId.Value,
            OrgNodePublicId = "", // filled by writer when needed
            SchemaCode = e.Data.SchemaCode,
            Entries = new Dictionary<string, ConfigValueEntry>(StringComparer.Ordinal),
            Version = 1,
            UpdatedAt = e.Timestamp
        });
    }

    public async Task Project(IEvent<ConfigNodeValuesPatched> e, IDocumentOperations ops, CancellationToken ct)
    {
        var id = ConfigNodeValueReadModel.BuildId(e.Data.TenantId.Value, e.Data.OrgNodeId.Value, e.Data.SchemaCode);
        // Marten's inline projection doesn't always surface an in-flight Initialized
        // doc through subsequent LoadAsync calls; build a doc on the fly if needed.
        var doc = await ops.LoadAsync<ConfigNodeValueReadModel>(id, ct) ?? new ConfigNodeValueReadModel
        {
            Id = id,
            StreamId = e.StreamId,
            TenantId = e.Data.TenantId.Value,
            OrgNodeId = e.Data.OrgNodeId.Value,
            OrgNodePublicId = "",
            SchemaCode = e.Data.SchemaCode,
            Entries = new Dictionary<string, ConfigValueEntry>(StringComparer.Ordinal),
            Version = 0,
            UpdatedAt = e.Data.AppliedAt
        };

        foreach (var op in e.Data.Operations)
        {
            switch (op.Operation)
            {
                case ConfigValuePatchOperationKind.Set:
                    doc.Entries[op.KeyCode] = new ConfigValueEntry(
                        op.KeyCode,
                        ConfigValueEntryState.Set,
                        op.Value,
                        e.Data.AppliedAt,
                        op.UpdatedByExternalId);
                    break;

                case ConfigValuePatchOperationKind.Inherit:
                    doc.Entries.Remove(op.KeyCode);
                    break;

                case ConfigValuePatchOperationKind.Unset:
                    doc.Entries[op.KeyCode] = new ConfigValueEntry(
                        op.KeyCode,
                        ConfigValueEntryState.Unset,
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
