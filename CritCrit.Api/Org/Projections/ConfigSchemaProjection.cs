using CritCrit.Api.Org.Domain;
using JasperFx.Events;
using Marten.Events.Aggregation;

namespace CritCrit.Api.Org.Projections;

/// <summary>
/// Single-stream snapshot of the schema doc, sliced by the schema event stream.
/// Pure apply methods — Marten supplies the snapshot, no async load required.
/// VersionPublished events bump the LatestPublishedVersion field; the immutable
/// version-snapshot doc lives in <see cref="ConfigSchemaVersionProjection"/>;
/// the draft-published flag lives in <see cref="ConfigSchemaDraftPublishedTracker"/>.
/// </summary>
public sealed class ConfigSchemaProjection : SingleStreamProjection<ConfigSchemaReadModel, Guid>
{
    public ConfigSchemaReadModel Create(IEvent<ConfigSchemaCreated> e) => new()
    {
        Id = e.Data.Id.Value,
        Code = e.Data.Code,
        CodeNormalized = e.Data.CodeNormalized,
        Name = e.Data.Name,
        Description = e.Data.Description,
        LatestPublishedVersion = null,
        Archived = false,
        CreatedAt = e.Timestamp,
        UpdatedAt = e.Timestamp,
        Version = 1
    };

    public void Apply(IEvent<ConfigSchemaRenamed> e, ConfigSchemaReadModel s)
    {
        s.Name = e.Data.Name;
        s.Description = e.Data.Description;
        s.UpdatedAt = e.Timestamp;
        s.Version++;
    }

    public void Apply(IEvent<ConfigSchemaArchived> e, ConfigSchemaReadModel s)
    {
        s.Archived = true;
        s.UpdatedAt = e.Timestamp;
        s.Version++;
    }

    public void Apply(IEvent<ConfigSchemaRestored> e, ConfigSchemaReadModel s)
    {
        s.Archived = false;
        s.UpdatedAt = e.Timestamp;
        s.Version++;
    }

    public void Apply(ConfigSchemaVersionPublished e, ConfigSchemaReadModel s)
    {
        s.LatestPublishedVersion = e.Version;
        s.UpdatedAt = e.PublishedAt;
        s.Version++;
    }
}
