using CritCrit.Api.Org.Domain;
using JasperFx.Events;
using Marten;
using Marten.Events.Projections;

namespace CritCrit.Api.Org.Projections;

/// <summary>
/// Per-tenant assignment doc. Stream per assignment; events Assigned →
/// Archived/Restored/Upgraded all flow into the same stream and patch the
/// doc state. Tenant scoping comes from the multi-tenanted session that
/// appended the event.
/// </summary>
public sealed class ConfigAssignmentProjection : EventProjection
{
    public void Project(IEvent<ConfigSchemaAssigned> e, IDocumentOperations ops)
    {
        ops.Store(new ConfigAssignmentReadModel
        {
            Id = e.Data.Id.Value,
            TenantId = e.Data.TenantId.Value,
            RootOrgNodeId = e.Data.RootOrgNodeId.Value,
            RootOrgNodePublicId = OrgPublicId.Format(OrgNodeType.Brand, e.Data.RootOrgNodeId),
            SchemaCode = e.Data.SchemaCode,
            SchemaVersion = e.Data.SchemaVersion,
            Archived = false,
            AssignedAt = e.Data.AssignedAt,
            ArchivedAt = null,
            UpdatedAt = e.Data.AssignedAt,
            Version = 1
        });
    }

    public async Task Project(IEvent<ConfigAssignmentArchived> e, IDocumentOperations ops, CancellationToken ct)
    {
        var doc = await ops.LoadAsync<ConfigAssignmentReadModel>(e.Data.Id.Value, ct);
        if (doc is null) return;
        doc.Archived = true;
        doc.ArchivedAt = e.Data.ArchivedAt;
        doc.UpdatedAt = e.Data.ArchivedAt;
        doc.Version++;
        ops.Store(doc);
    }

    public async Task Project(IEvent<ConfigAssignmentRestored> e, IDocumentOperations ops, CancellationToken ct)
    {
        var doc = await ops.LoadAsync<ConfigAssignmentReadModel>(e.Data.Id.Value, ct);
        if (doc is null) return;
        doc.Archived = false;
        doc.ArchivedAt = null;
        doc.UpdatedAt = e.Data.RestoredAt;
        doc.Version++;
        ops.Store(doc);
    }

    public async Task Project(IEvent<ConfigAssignmentUpgraded> e, IDocumentOperations ops, CancellationToken ct)
    {
        var doc = await ops.LoadAsync<ConfigAssignmentReadModel>(e.Data.Id.Value, ct);
        if (doc is null) return;
        doc.SchemaVersion = e.Data.NewVersion;
        doc.UpdatedAt = e.Data.UpgradedAt;
        doc.Version++;
        ops.Store(doc);
    }
}
