# Architecture Migration Handoff

Last updated: commit-in-progress (this session).

## Where things stand

All six original PRDs (under `.scratch/<feature-slug>/PRD.md`) have a **Status** line + **Shipped** + **Still missing** sections. The shipped state is summarised below; see each PRD for detail.

### Prereq chain for `[AggregateHandler]` / `[WriteAggregate]` adoption

| # | Prereq | Status |
|---|--------|--------|
| 1 | Deterministic stream IDs (`DeterministicGuid` UUID v5) | ✅ Shipped |
| 2 | `IParsable<T>` on strong-typed IDs (route binding) | ✅ Shipped |
| 3 | Per-endpoint class shape (Wolverine convention-method dispatch) | ✅ Shipped |
| 4 | `SingleStreamProjection<T>` per aggregate | 🟡 ConfigSchema family + Subject + Invitation + ConfigAssignment done. OrgNode deferred. String-composite-Id aggregates blocked by Marten Id-type constraint. |
| 5 | Conjoined event-store tenancy (`TenancyStyle.Conjoined` + PlatformTenant sentinel) | ✅ Shipped |
| 6 | Route-binding format compatible with `[WriteAggregate]` | 🟢 **Direction locked: switch command routes to raw Guid** (this session in flight) |

### `[WriteAggregate]` adoptions live today

- `ArchiveConfigDraftEndpoint` (commit 462e744)
- `UpdateConfigDraftEndpoint` (commit 23a164f)

### Operational findings (load-bearing)

- **`Version` field clash with SingleStreamProjection.** Marten reserves `Version` for stream-version metadata. Application-level optimistic-concurrency counter must be named `DocVersion` (or anything other than `Version`). API surface can still expose it as `Version` via response DTO mapping. See `ConfigAssignmentReadModel.DocVersion` (commit a822a3a).
- **Cross-tenant lookup pattern.** Brand-tenanted handler + parallel `store.QuerySession(PlatformTenant.Id)` for platform-scoped doc reads. Used at ~10 sites. See `OwnerLifecycleHandlers.GrantOwnerEndpoint.LoadAsync` (commit fb0b333).
- **Per-class Wolverine conventions.** `Validate`, `LoadAsync`, `Before` are resolved per-class. Multi-endpoint classes with different request types fail codegen. Pattern: one endpoint class per command. See `OwnerLifecycleHandlers` (3 classes) and `OrgNodes/CreateOrgNodeEndpoints.cs`.
- **WolverineFx + Marten "as-is".** No upstream PRs or library forks. Architecture decisions stay within current public API surface.

## What to do next session

### Top of the queue: command-route migration to raw `Guid`

PRD reference: `.scratch/deterministic-stream-ids/PRD.md` "Decision" section (committed this session).

**Goal:** every command endpoint (POST / PUT / PATCH / DELETE) takes raw-Guid id placeholders with `:guid` route constraint. Consumers reach API via the SDK (to be built) which hides URL format.

**Touchpoints:**

1. `CritCrit.Api/Org/Features/Owners/OwnerLifecycleHandlers.cs` — `{subjectId}` → `{subjectId:guid}` on Downgrade + Revoke
2. `CritCrit.Api/Org/Features/Subjects/SubjectHandlers.cs` — `{subjectId}` → `{subjectId:guid}` on Deactivate + Reactivate + Relink
3. `CritCrit.Api/Org/Features/Invitations/InvitationHandlers.cs` — `{invitationId}` → `{invitationId:guid}` on Cancel + Resend (Get also if SDK only)
4. `CritCrit.Api/Org/Features/OrgNodes/OrgNodeLifecycleEndpoints.cs` — `{nodeId}` → `{nodeId:guid}` on Archive + Restore + HardDelete + Move
5. `CritCrit.Api/Org/Features/Brands/BrandHandlers.cs` GetNode — `{nodeId}` → `{nodeId:guid}` (or leave for GET path)
6. `CritCrit.Api/Org/Features/Config/ConfigAssignmentHandlers.cs` — `{assignmentId}` (already Guid) + `{nodeId}` constraint
7. `CritCrit.Api/Org/Features/Config/ConfigSchemaHandlers.cs` — schema endpoints use `{schemaCode}` (string). Leave as-is — natural key.
8. `CritCrit.Api/Org/Features/Assets/AssetHandlers.cs` — `{nodeId}` constraint
9. `CritCrit.Api/Org/Features/Config/ConfigHandlers.cs` — `{nodeId}` constraint

Each handler param already uses strong-typed ID (`OrgNodeId nodeId`). `IParsable<T>` already accepts both Guid + public-id formats — the route-constraint switch makes Wolverine pick the Guid path consistently.

**Test impact:** test code constructs URLs as `$"/api/.../{brand.Id}/..."` where `brand.Id` is a public-id string. After the migration, those must pass the raw Guid:
- Helper added: `OrgPublicId.TryParseOrgNode(brand.Id, out var id, out _); id.Value.ToString()`
- Or extend response DTOs to also surface the raw Guid (less clean)
- Or write a `RawGuid()` extension over the public-id string

**`CritCrit.Web` impact:** wherever the frontend constructs API URLs from response IDs — update to use raw Guid form. SDK (when built) hides this entirely.

### After route migration: `[WriteAggregate]` adoption wave

Once routes are Guid, every aggregate with `SingleStreamProjection<T>` is a candidate. Eligible list:

| Aggregate | Endpoints |
|---|---|
| `OrgAccessGrantReadModel` | Owner (Downgrade, Revoke), AccessGrant (RevokeGrant, SetGrantExpiration) — composite-Id; still blocked by Marten Id-type unless refactored |
| `ConfigAssignmentReadModel` | Config-assignment Archive / Restore / Upgrade |
| `ConfigSchemaDraftReadModel` | PublishDraft (multi-stream — also touches schema stream) |
| `SubjectReadModel` | Deactivate / Reactivate / Relink |
| `InvitationReadModel` | Cancel / Resend |
| `OrgNodeReadModel` | Archive / Restore / HardDelete / Move — still gated by OrgNode SingleStream migration |

### Then: AuditLogProjection

PRD reference: `.scratch/cross-cutting-middleware/PRD.md` covers this.

47 inline `audit.Record(...)` sites today. Plan:
- Domain events already carry actor via event headers (`SessionMetadata.StampActor`) and reason for some event types (post-conjoined-tenancy, `Reason` already on `OrgAccessRevoked` etc.).
- New `AuditLogProjection` subscribes to all `Org/Config/Asset/Invitation` events, derives `ImmutableAuditEvent` rows.
- Per-event mapping table (`OrgAuditActions.For<T>(event)` → action constant + severity + category).
- Cross-tenant subject lookup inside projection acceptable (narrow scope, one read per event).
- Inline `audit.Record(...)` removed from handlers; tests verify projection-generated audit rows.

Multi-day work; pilot on ConfigSchema events first (smallest surface).

### Then: OrgNode SingleStreamProjection

PRD reference: `.scratch/projection-cleanup/PRD.md` "Next aggregates" section.

Required refactors:
- `EnrichEventsAsync` for parent lookup on `OrgNodeCreated` (cross-stream)
- Cascade-to-descendants on `OrgNodeArchived` / `OrgNodeRestored` / `OrgNodeMoved`: extract to sibling `EventProjection` using `Patch` operations to avoid SingleStream version clash
- Handler-level descendant `session.Store(descendant)` mutations (in `ArchiveOrgNodeEndpoint`, `RestoreOrgNodeEndpoint`, `MoveOrgNodeEndpoint`) → switch to `Patch` ops
- `MoveOrgNodeProjection` cascade-to-descendants — similar refactor
- Verify `OrgNodeCodeIndexProjection` and `BrandIndexProjection` siblings continue working

Multi-day work.

## How to verify state before starting

```bash
cd ~/RiderProjects/CritCrit
git log --oneline -10
dotnet test CritCrit.Test/CritCrit.Test.csproj --logger "console;verbosity=minimal"
dotnet test CritCrit.UnitTests/CritCrit.UnitTests.csproj --logger "console;verbosity=minimal"
```

Both test suites should be all green. 124 integration + 14 unit tests.

## Resume invocations

### Route migration to raw Guid (the next big thing)

```
/improve-codebase-architecture
"continue from .scratch/HANDOFF.md. tackle the command-route
migration to raw Guid. start with Owner endpoints (smallest blast
radius), then Subject, Invitation, OrgNode lifecycle, ConfigAssignment,
Asset, ConfigHandlers. update test URL construction to use raw Guid
form. run full Alba suite green per slice."
```

### `[WriteAggregate]` adoption wave (after route migration)

```
/improve-codebase-architecture
"now that routes are raw Guid, adopt [WriteAggregate] across the
command endpoint inventory in .scratch/HANDOFF.md. each endpoint
becomes a tiny decide-fn — strip IDocumentStore, SessionFactory,
manual Events.Append, manual SaveChangesAsync. audit.Record stays
inline until AuditLogProjection lands."
```

### `AuditLogProjection` pilot

```
/improve-codebase-architecture
"pilot AuditLogProjection per .scratch/cross-cutting-middleware/PRD.md.
start with ConfigSchema events only. derive ImmutableAuditEvent from
event payload + event headers (actor stamped via SessionMetadata).
remove inline audit.Record from ConfigSchemaHandlers. tests assert
the projected audit row content."
```

### `OrgNode` SingleStream migration

```
/improve-codebase-architecture
"work through .scratch/projection-cleanup/PRD.md OrgNode migration.
EnrichEventsAsync for parent lookup. extract cascade-to-descendants
into sibling EventProjection with Patch ops. refactor the handler-
level descendant Store mutations in ArchiveOrgNodeEndpoint /
RestoreOrgNodeEndpoint / MoveOrgNodeEndpoint."
```

## Key landmarks in code

| Pattern | Reference |
|---|---|
| `[WriteAggregate]` adoption | `Org/Features/Config/ArchiveConfigDraftEndpoint.cs`, `UpdateConfigDraftEndpoint.cs` |
| Per-endpoint class shape | `Org/Features/Owners/OwnerLifecycleHandlers.cs` (three classes in one file) |
| SingleStreamProjection with `DocVersion` rename | `Org/Projections/ConfigAssignmentProjection.cs` |
| Cross-tenant lookup pattern | `Org/Features/Owners/OwnerLifecycleHandlers.cs` `LoadAsync` (parallel platform query) |
| Platform-tenant sentinel | `CritCrit.Api/Platform/Tenancy/PlatformTenant.cs` |
| Deterministic stream id (UUID v5) | `CritCrit.Api/Platform/DeterministicGuid.cs` |
| Strong-typed id `IParsable<T>` + `JsonConverter` | `CritCrit.Api/Org/Domain/OrgIds.cs` |
| `OrgValidators` shape helpers | `CritCrit.Api/Org/Endpoints/OrgValidators.cs` |
| Wolverine `Validate` static method per endpoint class | every `*Endpoint.cs` under `Org/Features/` |

## ADRs

| ADR | Status |
|---|---|
| `docs/adr/0001-schema-less-node-resolved-assets.md` | Original baseline |
| `docs/adr/0002-decider-pattern-over-aggregate-base.md` | Recorded |

ADRs deferred:
- ADR-0003 (ancillary stores per feature module) — `.scratch/modular-configuration/`
- ADR-0004 (resource-name contracts in ServiceDefaults) — `.scratch/modular-configuration/`
