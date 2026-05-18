using CritCrit.Api.Org.Domain;
using JasperFx.Events;
using Marten.Events.Aggregation;

namespace CritCrit.Api.Org.Projections;

public sealed class ConfigAssignmentProjection : SingleStreamProjection<ConfigAssignmentReadModel, Guid>
{
    public ConfigAssignmentReadModel Create(IEvent<ConfigSchemaAssigned> e) => new()
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
        DocVersion = 1
    };

    public void Apply(ConfigAssignmentArchived e, ConfigAssignmentReadModel doc)
    {
        doc.Archived = true;
        doc.ArchivedAt = e.ArchivedAt;
        doc.UpdatedAt = e.ArchivedAt;
        doc.DocVersion++;
    }

    public void Apply(ConfigAssignmentRestored e, ConfigAssignmentReadModel doc)
    {
        doc.Archived = false;
        doc.ArchivedAt = null;
        doc.UpdatedAt = e.RestoredAt;
        doc.DocVersion++;
    }

    public void Apply(ConfigAssignmentUpgraded e, ConfigAssignmentReadModel doc)
    {
        doc.SchemaVersion = e.NewVersion;
        doc.UpdatedAt = e.UpgradedAt;
        doc.DocVersion++;
    }
}
