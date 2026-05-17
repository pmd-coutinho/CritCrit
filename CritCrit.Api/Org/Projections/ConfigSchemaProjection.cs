using CritCrit.Api.Org.Domain;
using JasperFx.Events;
using Marten;
using Marten.Events.Projections;

namespace CritCrit.Api.Org.Projections;

/// <summary>
/// Projects schema + draft events into:
/// <list type="bullet">
///   <item><see cref="ConfigSchemaReadModel"/> (one per schema)</item>
///   <item><see cref="ConfigSchemaVersionReadModel"/> (immutable snapshot per published version)</item>
///   <item><see cref="ConfigSchemaDraftReadModel"/> (one per draft, multiple per schema)</item>
/// </list>
/// EventProjection because publish-version writes both the schema doc
/// (LatestPublishedVersion bump) and a brand-new version snapshot doc.
/// </summary>
public sealed class ConfigSchemaProjection : EventProjection
{
    public void Project(IEvent<ConfigSchemaCreated> e, IDocumentOperations ops)
    {
        ops.Store(new ConfigSchemaReadModel
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
        });
    }

    public async Task Project(IEvent<ConfigSchemaRenamed> e, IDocumentOperations ops, CancellationToken ct)
    {
        var schema = await ops.LoadAsync<ConfigSchemaReadModel>(e.Data.Id.Value, ct);
        if (schema is null) return;
        schema.Name = e.Data.Name;
        schema.Description = e.Data.Description;
        schema.UpdatedAt = e.Timestamp;
        schema.Version++;
        ops.Store(schema);
    }

    public async Task Project(IEvent<ConfigSchemaArchived> e, IDocumentOperations ops, CancellationToken ct)
    {
        var schema = await ops.LoadAsync<ConfigSchemaReadModel>(e.Data.Id.Value, ct);
        if (schema is null) return;
        schema.Archived = true;
        schema.UpdatedAt = e.Timestamp;
        schema.Version++;
        ops.Store(schema);
    }

    public async Task Project(IEvent<ConfigSchemaRestored> e, IDocumentOperations ops, CancellationToken ct)
    {
        var schema = await ops.LoadAsync<ConfigSchemaReadModel>(e.Data.Id.Value, ct);
        if (schema is null) return;
        schema.Archived = false;
        schema.UpdatedAt = e.Timestamp;
        schema.Version++;
        ops.Store(schema);
    }

    public void Project(IEvent<ConfigSchemaDraftCreated> e, IDocumentOperations ops)
    {
        ops.Store(new ConfigSchemaDraftReadModel
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
        });
    }

    public async Task Project(IEvent<ConfigSchemaDraftUpdated> e, IDocumentOperations ops, CancellationToken ct)
    {
        var draft = await ops.LoadAsync<ConfigSchemaDraftReadModel>(e.Data.Id.Value, ct);
        if (draft is null) return;
        draft.Definition = e.Data.Definition;
        draft.UpdatedAt = e.Data.UpdatedAt;
        draft.Version++;
        ops.Store(draft);
    }

    public async Task Project(IEvent<ConfigSchemaDraftRenamed> e, IDocumentOperations ops, CancellationToken ct)
    {
        var draft = await ops.LoadAsync<ConfigSchemaDraftReadModel>(e.Data.Id.Value, ct);
        if (draft is null) return;
        draft.Name = e.Data.Name;
        draft.UpdatedAt = e.Data.UpdatedAt;
        draft.Version++;
        ops.Store(draft);
    }

    public async Task Project(IEvent<ConfigSchemaDraftArchived> e, IDocumentOperations ops, CancellationToken ct)
    {
        var draft = await ops.LoadAsync<ConfigSchemaDraftReadModel>(e.Data.Id.Value, ct);
        if (draft is null) return;
        draft.Archived = true;
        draft.UpdatedAt = e.Data.ArchivedAt;
        draft.Version++;
        ops.Store(draft);
    }

    public async Task Project(IEvent<ConfigSchemaVersionPublished> e, IDocumentOperations ops, CancellationToken ct)
    {
        // Snapshot the published version.
        ops.Store(new ConfigSchemaVersionReadModel
        {
            Id = ConfigSchemaVersionReadModel.BuildId(e.Data.SchemaCode, e.Data.Version),
            SchemaId = e.Data.SchemaId.Value,
            SchemaCode = e.Data.SchemaCode,
            Version = e.Data.Version,
            Definition = e.Data.Definition,
            PublishedAt = e.Data.PublishedAt,
            PublishedByExternalId = e.Data.PublishedByExternalId
        });

        // Bump latest-published on the schema doc.
        var schema = await ops.LoadAsync<ConfigSchemaReadModel>(e.Data.SchemaId.Value, ct);
        if (schema is not null)
        {
            schema.LatestPublishedVersion = e.Data.Version;
            schema.UpdatedAt = e.Data.PublishedAt;
            schema.Version++;
            ops.Store(schema);
        }

        // Mark the draft as published.
        var draft = await ops.LoadAsync<ConfigSchemaDraftReadModel>(e.Data.DraftId.Value, ct);
        if (draft is not null)
        {
            draft.Published = true;
            draft.PublishedAsVersion = e.Data.Version;
            draft.UpdatedAt = e.Data.PublishedAt;
            draft.Version++;
            ops.Store(draft);
        }
    }
}
