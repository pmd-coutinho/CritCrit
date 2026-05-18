# Conjoined Event-Store Tenancy

Status: **DESIGN — flip attempted, reverted** (deeper than originally scoped)
Triage: ready-for-human
Driver: projection-cleanup pilot discovery (commit fec84b1, ConfigAssignment migration attempt)
Unblocks: `SingleStreamProjection<T>` for multi-tenanted aggregates → `[AggregateHandler]` adoption on Org / AccessGrant / Config / Asset write models

## Attempted flip (1e0f907 follow-up, reverted)

Setting `m.Events.TenancyStyle = Marten.Storage.TenancyStyle.Conjoined` against the existing test schema produces:

```
InvalidProjectionException : Tenancy storage style mismatch between the events
(Conjoined) and the aggregate type SubjectReadModel (Single)
Tenancy storage style mismatch between the events (Conjoined) and the aggregate
type InvitationReadModel (Single)
Tenancy storage style mismatch between the events (Conjoined) and the aggregate
type ConfigSchemaReadModel (Single)
Tenancy storage style mismatch between the events (Conjoined) and the aggregate
type ConfigSchemaDraftReadModel (Single)
```

The flip is bidirectional: making events Conjoined forces *every* SingleStreamProjection to also be Conjoined-tenanted. SubjectReadModel, InvitationReadModel, ConfigSchemaReadModel, ConfigSchemaDraftReadModel are currently `SingleTenanted()` (platform-scoped — no per-brand isolation needed). They would all need to switch to MultiTenanted with a sentinel tenant id (Guid.Empty or "platform") for platform-scoped streams.

Scope is larger than originally outlined:

- 5 multi-tenanted aggregates gain SingleStreamProjection eligibility
- 4 single-tenanted aggregates **lose** their current shape and need migration to a platform-tenant placeholder
- Every `SessionFactory.PlatformSession(store)` callsite (subject lifecycle, invitation lifecycle, schema lifecycle, audit denied path) needs explicit platform-tenant binding
- Backfill `tenant_id` on existing `mt_events`/`mt_streams` rows (production data migration)

The conjoined-tenancy migration is a separate multi-day initiative.

## Problem

CritCrit Marten setup has:
- **Documents**: `m.Schema.For<OrgNodeReadModel>().MultiTenanted()` (and four more — see below). Each tenanted doc lives in a tenant-scoped row, addressable only with a tenant id.
- **Events**: default tenancy style = `Single`. No tenant column on `mt_events` / `mt_streams` tables. Every event lives in one global pool.

`SingleStreamProjection<TDoc, TId>` requires the projection's `TDoc` tenancy style to match the event store's tenancy style. Mismatch triggers `InvalidProjectionException: Tenancy storage style mismatch between the events (Single) and the aggregate type {Doc} (Conjoined)` at app startup.

Five doc types are affected:

| Doc type | Currently | Blocks |
|----------|-----------|--------|
| `OrgNodeReadModel` | MultiTenanted | `[AggregateHandler]` for Org/Brand/Country/Franchise/Store/Device commands |
| `OrgAccessGrantReadModel` | MultiTenanted (+ string composite Id) | `[AggregateHandler]` for grant + owner commands |
| `ConfigAssignmentReadModel` | MultiTenanted | `[AggregateHandler]` for assignment commands |
| `ConfigNodeValueReadModel` | MultiTenanted (+ string composite Id) | `[AggregateHandler]` for config-value commands |
| `AssetNodeValueReadModel` | MultiTenanted (+ string composite Id) | `[AggregateHandler]` for asset commands |

Until events are conjoined-tenanted, these aggregates cannot use `SingleStreamProjection`, which means they cannot use `FetchForWriting<T>`, which means they cannot use `[AggregateHandler]`.

## Goal

Migrate event-store tenancy style to `Conjoined` so multi-tenanted document projections can read tenanted streams. Implement schema migration + data backfill safely.

## Design space

### Option A — Switch tenancy globally with schema migration

```csharp
m.Events.TenancyStyle = TenancyStyle.Conjoined;
```

Marten's schema generator adds a `tenant_id` column to `mt_events`, `mt_streams`, and any projection tables that previously assumed single-tenancy. Existing rows need backfill from event payloads (every domain event in this codebase carries `TenantId` somewhere in its payload — `OrgNodeCreated.TenantId`, `OrgAccessGranted.TenantId`, `ConfigSchemaAssigned.TenantId`, etc.).

**Pros**: clean cut, one config flip.

**Cons**: existing event streams must backfill `tenant_id` before the app boots in conjoined mode. Requires:
1. Stop writes
2. Schema migrate (add columns)
3. Backfill script (parse each event's `data->'TenantId'` JSON path into the new column, populate `mt_streams.tenant_id`)
4. Switch config + restart
5. Validate via integration test

### Option B — Keep single tenancy, drop multi-tenanted from docs

Move tenant isolation out of Marten's conjoined model into application-level filtering. Every query and projection becomes responsible for filtering by `TenantId` field. `MultiTenanted()` declarations removed from `Schema.For<…>()`.

**Pros**: no event-store migration.

**Cons**: loses Marten's per-tenant session isolation, every query needs manual tenant filter, projection bugs become tenancy bugs. Strong rejection — this undoes the existing tenancy story.

### Option C — Per-aggregate tenancy override

Marten supports `m.Schema.For<T>().SingleTenanted()` to override the global setting per doc type. Could we go the other way — single-tenanted globally, with per-doc opt-in to MultiTenanted? Already what we have; the projection mismatch is the symptom.

What we'd actually need: per-aggregate event tenancy override. Marten does not currently support this.

Rejected.

### Option D — Migrate one aggregate at a time via ancillary stores

If each multi-tenanted aggregate lived in its own Marten store (per the `.scratch/modular-configuration/` design), each store could have its own tenancy style. Org store gets conjoined events; Subject/Invitation/Config-schema stores stay single-tenanted.

Heaviest path; couples this prereq to `.scratch/modular-configuration/`. Worth considering if both happen together but not a quick win.

## Locked decision (proposed)

**Option A.** Conjoined event-store tenancy globally, with a versioned backfill migration.

Pre-conditions:
- Backfill script tested against a copy of production data.
- All event types verified to carry `TenantId` (or derivable from stream-start event).
- Roll-forward only; no downgrade path (single→conjoined is one-way for our event schema).

## Implementation outline

1. **Backfill script** (one-shot, runs once before flipping `TenancyStyle`):
   - Add `tenant_id uuid` column to `mt_events` and `mt_streams`.
   - For each stream, read the first event's payload, extract `TenantId` (path differs per event type — needs a per-event-type extractor table).
   - For "platform-scoped" streams (Subject, Invitation, ConfigSchema, ExternalIdentity), use the Marten default-tenant placeholder.
   - Index `mt_events.tenant_id` and `mt_streams.tenant_id`.

2. **Config flip** in `CritCritApiConfiguration.ConfigureEventStore`:
   ```csharp
   m.Events.TenancyStyle = TenancyStyle.Conjoined;
   ```

3. **Handler audits**:
   - Every `Events.StartStream` / `Events.Append` call site must run inside a tenanted session (`store.LightweightSession(tenant.TenantId.Value.ToString())`). Currently `SessionFactory.PlatformSession(store)` is used for invitation/subject/schema streams — those need a "platform" tenant or stay single-tenanted via per-aggregate override (if Marten supports it for events; verify).

4. **Test for cross-tenant queries**: any code that queries events across tenants (e.g. async daemon, audit dashboards) needs explicit cross-tenant API (`session.UseTenancyFilter(...)`).

5. **Acceptance**:
   - All 124 integration tests green after backfill + flip.
   - SingleStreamProjection migrations land for `ConfigAssignmentReadModel`, `OrgNodeReadModel`, `OrgAccessGrantReadModel`, `ConfigNodeValueReadModel`, `AssetNodeValueReadModel`.
   - `[AggregateHandler]` becomes viable for those aggregates.

## Out of scope

- Per-aggregate ancillary-store split (`.scratch/modular-configuration/`).
- Cross-tenant projections (none currently exist that span tenants beyond BrandIndex which uses single-tenanted docs).
- Cross-tenant audit/admin tooling.

## Risks

- Backfill correctness on shared-stream events (e.g. invitation events appended to platform session — what tenant do they belong to?). Likely answer: platform-tenant constant.
- Wolverine outbox + envelope persistence interactions with conjoined events.
- Existing `SessionFactory.PlatformSession(store)` call sites (invitation handlers, subject handlers, audit writer denied path) — those open un-tenanted sessions. If events become conjoined, these calls need a tenant or fail.

## Follow-ups

- After this lands, projection-cleanup PRD can complete the remaining 5 SingleStream migrations.
- `[AggregateHandler]` adoption for Org / AccessGrant / Config / Asset becomes unblocked.
