# Conjoined Event-Store Tenancy

Status: **SHIPPED** — commit fb0b333

## Shipped

Conjoined event-store tenancy enabled. 124/124 integration tests green.

- `m.Events.TenancyStyle = Marten.Storage.TenancyStyle.Conjoined` in `CritCritApiConfiguration.ConfigureEventStore`
- `PlatformTenant.Id = "PLATFORM"` sentinel in `CritCrit.Api/Platform/Tenancy/PlatformTenant.cs`
- `SessionFactory.PlatformSession(store)` returns a session tenanted to `"PLATFORM"`; new `PlatformQuerySession` helper added
- Doc tenancy switches: `SubjectReadModel`, `InvitationReadModel`, `ConfigSchemaReadModel`, `ConfigSchemaDraftReadModel` → `MultiTenanted()`
- Cross-tenant lookup pattern: brand-tenanted handlers that touch a platform-scoped doc open a parallel `store.QuerySession(PlatformTenant.Id)`. Applied at the ~10 surgical sites (Owner LoadAsync × 3, AccessGrant Revoke + List, GrantRole, SetGrantExpiration, plus Subject/Invitation/Config platform reads).
- Test code adjusted to query platform docs via `DocumentStore.QuerySession("PLATFORM")` and append platform events via `LightweightSession("PLATFORM")`.

Unlocked aggregates for `SingleStreamProjection<T>` + `[WriteAggregate]`:
`ConfigAssignmentReadModel`, `OrgNodeReadModel`, `OrgAccessGrantReadModel`, `ConfigNodeValueReadModel`, `AssetNodeValueReadModel`.

## Caveats discovered

1. **`ConfigAssignmentReadModel` SingleStream blocked by `Version` field interaction.** First migration attempt regressed the `archived_then_restored_assignment_cycle_works` test (doc.Version returned 0 after archive instead of 2). Suspected `UseIdentityMapForAggregates` + custom `Version` field clash. Needs deeper Marten research before retry — left as `EventProjection`.
2. **Production data migration not addressed.** Tests run against fresh containers so no backfill needed. Production deployments still need a one-shot `tenant_id` backfill on `mt_events` / `mt_streams` and on the four newly-MultiTenanted doc tables.

## Original design (pre-attempt)

Status: **DESIGN — full attempt iterated to 50→40 failures then reverted**
Triage: ready-for-human
Driver: projection-cleanup pilot discovery (commit fec84b1, ConfigAssignment migration attempt) + full attempt iteration (reverted)
Unblocks: `SingleStreamProjection<T>` for multi-tenanted aggregates → `[AggregateHandler]` adoption on Org / AccessGrant / Config / Asset write models

## Second attempt — full-scope iteration (reverted)

Time-boxed full attempt this session:

1. Added `PlatformTenant.Id = "PLATFORM"` sentinel in `Platform/Tenancy/PlatformTenant.cs`
2. Switched `SessionFactory.PlatformSession(store)` to `store.LightweightSession(PlatformTenant.Id)`
3. Flipped `m.Events.TenancyStyle = Marten.Storage.TenancyStyle.Conjoined`
4. Switched `SubjectReadModel`, `InvitationReadModel`, `ConfigSchemaReadModel`, `ConfigSchemaDraftReadModel` from `SingleTenanted()` → `MultiTenanted()`
5. Bulk-replaced `store.QuerySession()` → `store.QuerySession(PlatformTenant.Id)` in subject/invitation/config handlers

After all of the above, **40 tests still failed** (124 → 84 passing). The root cause of the remaining failures is **cross-tenant lookup from brand-tenanted handlers**: when a brand-tenanted handler (e.g. `GrantRoleEndpoint`, `OwnerLifecycleHandlers.GrantOwnerEndpoint`) needs to look up a `SubjectReadModel` (now MultiTenanted-platform), it does so via the brand-tenanted session, which now filters by brand tenant and returns no rows. Error string: `"Subject does not exist or is inactive."` from every command that takes a subject id parameter.

~13 callsites identified that need surgery:

- Owner/Brand/Access handlers — subject lookup from brand-tenanted session
- `AccessGrantHandlers.GrantRole`, `SetGrantExpirationEndpoint` — subject lookup
- `InvitationWorkflow` — subject lookup + email-uniqueness query
- `OwnerLifecycleHandlers` (3 endpoints) — subject lookup
- `ConfigHandlers.PatchValues` — schema lookup from brand-tenanted session

Each needs a separate platform-tenanted session opened just for the cross-tenant lookup, OR Marten cross-tenant query API (`AnyTenant`/`UseTenancyFilter`) applied per-call.

Untested but expected to surface next:

- Wolverine outbox + envelope storage compatibility with conjoined events
- `RequestActorMiddleware` un-tenanted session resolving actor via ExternalIdentity (Single-tenanted) and Subject (now MultiTenanted)
- `BrandTenantMiddleware` un-tenanted brand resolution
- Audit denied-path session

Attempt reverted; tests back to 124/124. Findings preserved for a focused future pass.

### Required follow-up work (out of scope for the architecture run)

1. Research Marten cross-tenant query API: `session.AnyTenant()` / `UseTenancyFilter(false)` / equivalent — confirm which is the supported pattern.
2. Refactor every cross-tenant lookup (~13 sites identified, more probably below the iceberg) to use the chosen pattern OR open a parallel platform-tenanted session.
3. Verify Wolverine outbox + envelope storage interact correctly with conjoined events.
4. Migrate `RequestActorMiddleware` / `BrandTenantMiddleware` un-tenanted sessions to the appropriate tenant context.
5. Production data migration: backfill `tenant_id` on existing `mt_events`, `mt_streams`, and every newly-MultiTenanted doc.


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
