# Projection Cleanup: SingleStream Where Possible

Status: **PILOT SHIPPED** (was design-locked)
Shipped commit: fec84b1

## Shipped — ConfigSchemaProjection pilot

The 152-LOC `EventProjection` writing three doc types is now four single-purpose projections:

- `ConfigSchemaProjection : SingleStreamProjection<ConfigSchemaReadModel, Guid>` — pure `Apply` per event type, zero async loads.
- `ConfigSchemaDraftProjection : SingleStreamProjection<ConfigSchemaDraftReadModel, Guid>` — pure `Apply` per event type, zero async loads.
- `ConfigSchemaVersionProjection : EventProjection` — single `Create` per `ConfigSchemaVersionPublished`; one event → one immutable snapshot doc.
- `ConfigSchemaDraftPublishedTracker : EventProjection` — the **deliberate narrow cross-stream exception**, documented in code. Uses Marten `Patch` (not `Store`) for the field updates so it does not collide with the SingleStreamProjection's version tracking on the same doc type.

Discovered constraint (now documented): when one doc type is the snapshot target of a `SingleStreamProjection<T>`, any sibling `EventProjection` that mutates the *same* doc must use Marten `Patch` operations rather than `ops.Store(...)`. Otherwise Marten's optimistic-concurrency check fires during the inline projection commit and the transaction fails.

124/124 integration tests green; lifecycle, resolution, and assignment tests all pass against the new shape.

## Still missing

## Still missing

All of it. Pilot target unchanged: `ConfigSchemaProjection` (152 LOC, 3 doc types, 7 async loads) → four projections:
1. `ConfigSchemaProjection : SingleStreamProjection<ConfigSchemaReadModel, Guid>`
2. `ConfigSchemaDraftProjection : SingleStreamProjection<ConfigSchemaDraftReadModel, Guid>`
3. `ConfigSchemaVersionProjection : EventProjection` (one event → one immutable doc)
4. `ConfigSchemaDraftPublishedTracker : EventProjection` (deliberate narrow cross-stream exception)

Follow-ups (after pilot lands):
- `ConfigNodeValueProjection`, `ConfigAssignmentProjection` — same pattern.
- `OrgNodeProjection` + `OrgNodeCodeIndexProjection` + `MoveOrgNodeProjection` — needs ancestry-tracking design (the EventProjection writes multiple doc types per event today).
- `SubjectBrandAccessProjection` — multi-stream.
- `AssetNodeValueProjection` — review.

## Why this matters now

`SingleStreamProjection<T>` registration is the **fourth and final prereq** for `[AggregateHandler]` adoption. `FetchForWriting<TAggregate>` requires the aggregate type to be registered as a snapshot projection or self-aggregate; `EventProjection` doesn't qualify. See `.scratch/deterministic-stream-ids/PRD.md` "Prereq 3" for the discovery.
Triage: ready-for-human
Driver: improve-codebase-architecture run on master, 2026-05-18

## Goal

Replace `EventProjection`-with-async-`LoadAsync→mutate→Store` projections with `SingleStreamProjection<T, TId>` where stream topology allows. Where cross-stream mutation is genuinely needed, isolate it into a single narrowly-scoped tracker projection.

## Why

- 7 of 10 `Project(...)` methods in `ConfigSchemaProjection` do `LoadAsync → mutate → Store`. Marten's single-stream projection convention does this via `Apply(TEvent, TDoc)` with no async load.
- Pure `Apply` methods are unit-testable with hand-built events. Async-load projections require integration tests.
- Greenfield critter recipe `UseIdentityMapForAggregates` (per candidate #1) gives best return when projections are inline and pure.

## Design decisions (locked)

| # | Question | Decision |
|---|----------|----------|
| 3.1 | Stream topology (grep result) | Schema stream (`schemaId`) + separate draft streams (`draftId`); `VersionPublished` lives on schema stream and references `DraftId` payload |
| 3.2 | Snapshot projection type | (a) `EventProjection` with single `Create` (one event → one doc) |
| 3.3 | Cross-doc updates on `VersionPublished` | Independent subscribers, no derived events |
| 3.4 | Projection lifecycle | (a) Keep inline for now; revisit after candidate #1 ships |
| 3.5 | Pilot projection | `ConfigSchemaProjection` |

## Out of scope

- `InvitationProjection` → becomes a saga in candidate #4
- `OrgNodeProjection` ancestry tracking — separate issue once pilot pattern proves out
- Async projection daemon migration

## Pilot — `ConfigSchemaProjection` decomposition

Today: one 152-LOC `EventProjection` writing 3 doc types, 7 async loads.

After: four projections, each owning one concern.

### 1. `Org/Projections/ConfigSchemaProjection.cs` (rewrite)

```csharp
public sealed class ConfigSchemaProjection : SingleStreamProjection<ConfigSchemaReadModel, Guid>
{
    public ConfigSchemaReadModel Create(IEvent<ConfigSchemaCreated> e) =>
        new() { Id = e.Data.Id.Value, Code = e.Data.Code, /* ... */ Version = 1 };

    public void Apply(ConfigSchemaRenamed e, ConfigSchemaReadModel s, IEvent meta)
    {
        s.Name = e.Name; s.Description = e.Description;
        s.UpdatedAt = meta.Timestamp; s.Version++;
    }

    public void Apply(ConfigSchemaArchived e, ConfigSchemaReadModel s, IEvent meta) { s.Archived = true; /* ... */ }
    public void Apply(ConfigSchemaRestored e, ConfigSchemaReadModel s, IEvent meta) { s.Archived = false; /* ... */ }
    public void Apply(ConfigSchemaVersionPublished e, ConfigSchemaReadModel s, IEvent meta)
    {
        s.LatestPublishedVersion = e.Version;
        s.UpdatedAt = e.PublishedAt; s.Version++;
    }
}
```

No async load. Marten supplies the snapshot.

### 2. `Org/Projections/ConfigSchemaDraftProjection.cs` (new file, split from current)

```csharp
public sealed class ConfigSchemaDraftProjection : SingleStreamProjection<ConfigSchemaDraftReadModel, Guid>
{
    public ConfigSchemaDraftReadModel Create(IEvent<ConfigSchemaDraftCreated> e) => /* ... */;
    public void Apply(ConfigSchemaDraftUpdated e, ConfigSchemaDraftReadModel d) { d.Definition = e.Definition; /* ... */ }
    public void Apply(ConfigSchemaDraftRenamed e, ConfigSchemaDraftReadModel d) { d.Name = e.Name; /* ... */ }
    public void Apply(ConfigSchemaDraftArchived e, ConfigSchemaDraftReadModel d) { d.Archived = true; /* ... */ }
}
```

### 3. `Org/Projections/ConfigSchemaVersionProjection.cs` (new)

```csharp
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
```

One event → one immutable doc. Legitimate `EventProjection` use.

### 4. `Org/Projections/ConfigSchemaDraftPublishedTracker.cs` (new)

```csharp
public sealed class ConfigSchemaDraftPublishedTracker : EventProjection
{
    public async Task Project(IEvent<ConfigSchemaVersionPublished> e, IDocumentOperations ops, CancellationToken ct)
    {
        var draft = await ops.LoadAsync<ConfigSchemaDraftReadModel>(e.Data.DraftId.Value, ct);
        if (draft is null) return;
        draft.Published = true;
        draft.PublishedAsVersion = e.Data.Version;
        draft.UpdatedAt = e.Data.PublishedAt;
        draft.Version++;
        ops.Store(draft);
    }
}
```

This is the **deliberate exception**. `VersionPublished` lives on the schema stream; the draft lives on its own stream. There is no clean single-stream path. Per Q3.3 (independent subscribers, no derived events) we accept one narrow async cross-load. Do NOT use this as a template for future cross-stream needs — prefer a derived event on the consumer's stream when the situation is not as natural as "this event must update one specific document on a different stream".

## Tests

- `ConfigSchemaProjectionTests` — pure `Apply` tests with hand-built event + read-model instance
- `ConfigSchemaDraftProjectionTests` — same
- `ConfigSchemaVersionProjectionTests` — `Create(IEvent<…>)` test
- `ConfigSchemaDraftPublishedTrackerTests` — integration test against `IDocumentSession` (cross-load makes it unavoidable; minimal seed)
- Existing Alba contract tests under `CritCrit.AlbaTests/Config/*` stay as smoke

## Wiring

`Configuration/CritCritApiConfiguration.cs` lines 256–272 (projection registration):
- Remove `ConfigSchemaProjection` (old)
- Add `ConfigSchemaProjection` (new SingleStream), `ConfigSchemaDraftProjection`, `ConfigSchemaVersionProjection`, `ConfigSchemaDraftPublishedTracker`
- Lifecycle: `ProjectionLifecycle.Inline` for all four (Q3.4 (a))

## Acceptance

- `ConfigSchemaProjection.cs` after rewrite ≤ 60 LOC and has zero `LoadAsync` calls
- Three new projection files added, each ≤ 40 LOC
- All existing Alba `Config/*` contract tests green
- New unit test files cover every `Apply` + `Create` method
- Doc comment on `ConfigSchemaDraftPublishedTracker` explicitly names the cross-stream exception so future readers don't copy the pattern

## Follow-ups (separate issues)

- `ConfigNodeValueProjection`, `ConfigAssignmentProjection`: same pattern
- `OrgNodeProjection` + `OrgNodeCodeIndexProjection` + `MoveOrgNodeProjection`: ancestry-tracking analysis, likely involves a deliberate multi-stream projection
- `SubjectBrandAccessProjection`: multi-stream
- `AssetNodeValueProjection`: review
