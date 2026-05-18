using CritCrit.Api.Org.Domain;
using JasperFx.Events;
using Marten.Events.Aggregation;

namespace CritCrit.Api.Org.Projections;

/// <summary>
/// Single-stream snapshot of the draft doc, sliced by the draft event stream.
/// VersionPublished lives on the schema stream (not the draft stream), so the
/// draft.Published flag is maintained separately by
/// <see cref="ConfigSchemaDraftPublishedTracker"/>.
/// </summary>
public sealed class ConfigSchemaDraftProjection : SingleStreamProjection<ConfigSchemaDraftReadModel, Guid>
{
    public ConfigSchemaDraftReadModel Create(IEvent<ConfigSchemaDraftCreated> e) => new()
    {
        Id = e.Data.Id.Value,
        SchemaId = e.Data.SchemaId.Value,
        SchemaCode = e.Data.SchemaCode,
        Name = e.Data.Name,
        BaseVersion = e.Data.BaseVersion,
        Definition = e.Data.Definition,
        Archived = false,
        Published = false,
        PublishedAsVersion = null,
        CreatedAt = e.Data.CreatedAt,
        UpdatedAt = e.Data.CreatedAt,
        Version = 1
    };

    public void Apply(ConfigSchemaDraftUpdated e, ConfigSchemaDraftReadModel d)
    {
        d.Definition = e.Definition;
        d.UpdatedAt = e.UpdatedAt;
        d.Version++;
    }

    public void Apply(ConfigSchemaDraftRenamed e, ConfigSchemaDraftReadModel d)
    {
        d.Name = e.Name;
        d.UpdatedAt = e.UpdatedAt;
        d.Version++;
    }

    public void Apply(ConfigSchemaDraftArchived e, ConfigSchemaDraftReadModel d)
    {
        d.Archived = true;
        d.UpdatedAt = e.ArchivedAt;
        d.Version++;
    }
}
