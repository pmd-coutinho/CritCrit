using CritCrit.Api.Org.Domain;
using JasperFx.Events;
using Marten;
using Marten.Events.Projections;
using Marten.Patching;

namespace CritCrit.Api.Org.Projections;

/// <summary>
/// Deliberate narrow cross-stream exception: <see cref="ConfigSchemaVersionPublished"/>
/// is appended to the schema stream but needs to mark the draft (on its own
/// stream) as published. Uses Marten Patch operations so the SET sidesteps the
/// SingleStreamProjection version tracking owned by
/// <see cref="ConfigSchemaDraftProjection"/>. Do NOT copy this pattern elsewhere
/// — it is the documented exception, not a template.
/// </summary>
public sealed class ConfigSchemaDraftPublishedTracker : EventProjection
{
    public void Project(IEvent<ConfigSchemaVersionPublished> e, IDocumentOperations ops)
    {
        ops.Patch<ConfigSchemaDraftReadModel>(e.Data.DraftId.Value)
            .Set(x => x.Published, true);
        ops.Patch<ConfigSchemaDraftReadModel>(e.Data.DraftId.Value)
            .Set(x => x.PublishedAsVersion, (int?)e.Data.Version);
        ops.Patch<ConfigSchemaDraftReadModel>(e.Data.DraftId.Value)
            .Set(x => x.UpdatedAt, e.Data.PublishedAt);
        ops.Patch<ConfigSchemaDraftReadModel>(e.Data.DraftId.Value)
            .Increment(x => x.Version);
    }
}
