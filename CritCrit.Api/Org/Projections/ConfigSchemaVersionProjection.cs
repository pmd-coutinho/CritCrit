using CritCrit.Api.Org.Domain;
using JasperFx.Events;
using Marten.Events.Projections;

namespace CritCrit.Api.Org.Projections;

/// <summary>
/// Immutable snapshot writer: one <see cref="ConfigSchemaVersionPublished"/>
/// event produces one new <see cref="ConfigSchemaVersionReadModel"/> doc. No
/// updates afterwards — published versions are append-only.
/// </summary>
public sealed class ConfigSchemaVersionProjection : EventProjection
{
    public ConfigSchemaVersionReadModel Create(IEvent<ConfigSchemaVersionPublished> e) => new()
    {
        Id = ConfigSchemaVersionReadModel.BuildId(e.Data.SchemaCode, e.Data.Version),
        SchemaId = e.Data.SchemaId.Value,
        SchemaCode = e.Data.SchemaCode,
        Version = e.Data.Version,
        Definition = e.Data.Definition,
        PublishedAt = e.Data.PublishedAt,
        PublishedByExternalId = e.Data.PublishedByExternalId
    };
}
