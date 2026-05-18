# Deterministic Stream IDs for Find-or-Create Aggregates

Status: **MOSTLY SHIPPED** (was design)
Shipped commits: 9880f78, feba3cc, 16912fa, e7da6c6, c2e8347, 254df23, f9361a2, 0afb5e9, 326c30a

## Shipped

- `CritCrit.Api/Platform/DeterministicGuid.cs` — UUID v5 helper with fixed CritCrit namespace, validated against RFC 4122 DNS-namespace canonical vector.
- Migrated all five find-or-create-aggregate stream-id sites to `DeterministicGuid.From(...)`:
  - `OwnerLifecycleHandlers` (Owner pilot)
  - `AccessGrantHandlers` (general grant)
  - `InvitationHandlers` (re-grant on accept)
  - `ConfigHandlers` (config value bag)
  - `AssetHandlers` (asset value bag)
- Lazy dual-mode preserved everywhere — pre-existing grants/bags still honour their stored random `StreamId`.
- `IParsable<T>` on `OrgNodeId` / `SubjectId` / `InvitationId` for route-binding via Wolverine.Http.
- `JsonConverter<T>` on the same types (DTO field migration deferred).
- Per-endpoint class shape proven across all multi-endpoint slices (Org/Brand/Owner/Access/Subject/Invitation/Config/Asset).

## Still missing

- **DTO request-body field migration** to strong-typed IDs. JsonConverters are in place, but DTO fields like `GrantOwnerRequest(string SubjectId)` haven't been switched to `GrantOwnerRequest(SubjectId SubjectId)` because 141 test construction sites pass positional string args and need parallel updates. Deferred until a focused test-rewrite pass.
- **Backfill of legacy random-stream-id records** (Option A.b in the design space). Indefinite defer until needed.

## Unblocks downstream

- `[AggregateHandler]` adoption — three of four prereqs now satisfied (deterministic IDs, `IParsable<T>`, per-endpoint shape). Last gate is the SingleStreamProjection migration in `.scratch/projection-cleanup/`.
Triage: ready-for-human
Driver: improve-codebase-architecture follow-up after Owner pilot landed (commit 2eaef61)
Unblocks: `.scratch/aggregate-handler-workflow/` `[AggregateHandler]` adoption for Owner / Config-node-value / Asset-node-value handlers
Related ADRs: `docs/adr/0002-decider-pattern-over-aggregate-base.md` (to be written)

## Problem

Three doc types follow a "find-or-create by composite key" pattern with **deterministic document id** but **random Guid stream id**:

| Doc type | Composite key | `BuildId` helper |
|----------|---------------|------------------|
| `OrgAccessGrantReadModel` | `(tenant, node, subject)` | `OrgAccessGrantReadModel.BuildId(tenant, node, subject)` |
| `ConfigNodeValueReadModel` | `(tenant, node, schemaCode)` | `ConfigNodeValueReadModel.BuildId(tenant, node, schemaCode)` |
| `AssetNodeValueReadModel` | `(tenant, node)` | `AssetNodeValueReadModel.BuildId(tenant, node)` |

Today's pattern in handlers:

```csharp
var bagId = OrgAccessGrantReadModel.BuildId(tenant, node, subject);
var bag = await session.LoadAsync<OrgAccessGrantReadModel>(bagId, ct);
var streamId = bag?.StreamId ?? Guid.CreateVersion7();  // random for new
// ...
session.Events.StartStream<...>(streamId, ...);   // or Events.Append(streamId, ...)
```

Random stream IDs block Wolverine `[AggregateHandler]` adoption because:
- `[Aggregate]` parameter strategy needs the stream id at route-dispatch time
- Composite-key routes (e.g. `POST /api/brands/{brandId}/owners/{subjectId}/revoke`) carry the *composite* but not the stream id
- A pre-query to fetch the doc just to learn the stream id defeats the purpose of the workflow

## Goal

Eliminate random Guid stream IDs for find-or-create aggregates. Make the stream id derivable from the composite key alone, so `[AggregateHandler]` can route directly from route params to aggregate state without a pre-query.

## Design space

Three approaches, ranked by preference.

### Option A — Deterministic Guid v5 derived from composite key (Recommended)

Define a helper:

```csharp
public static class DeterministicGuid
{
    private static readonly Guid CritCritNamespace = new("/* fixed namespace UUID */");

    public static Guid From(params object[] keyParts)
    {
        var canonical = string.Join(":", keyParts.Select(p => p?.ToString() ?? ""));
        // UUID v5 (SHA-1, namespace+name). Marten accepts any Guid; v5 gives
        // stable cross-process derivation.
        return Uuid5.Create(CritCritNamespace, canonical);
    }
}
```

For grants:

```csharp
var streamId = DeterministicGuid.From(tenantId, orgNodeId, subjectId);
session.Events.StartStream<OrgAccessGrantReadModel>(streamId, ...);
```

Read-model `Id` (string) can stay as `BuildId(...)`, but `StreamId` field on the doc becomes derivable — and arguably removable.

**Migration**: existing grant streams have random IDs. Options to cut over:
- **(a)** Lazy dual-mode: on first read after deploy, look up by composite doc id → if found, keep using the random `StreamId`; for new grants, write deterministic. Existing grants permanently keep their random stream id. Eventually the doc field `StreamId` is removed from all *new* records but the legacy field stays.
- **(b)** Event-stream rebuild: scripted migration reads every old grant event, re-emits under a deterministic stream key, drops the old stream. Requires downtime or daemon coordination. Cleanest but heavy.
- **(c)** Daemon-driven projection rebuild: leave existing streams as-is, accept that `[AggregateHandler]` only works for *new* grants until next major version. Mixed-mode confusing for readers.

**Recommendation:** (a) for grants/config-node-value/asset-node-value. Document the legacy-streams exception inline on `DeterministicGuid.From`. Eventually (b) for a clean cut at a planned major-version boundary.

### Option B — Marten string stream keys

Marten supports `m.Events.StreamIdentity = StreamIdentity.AsString`. Stream key becomes the composite string directly (e.g. `"tenant:node:subject"`).

**Problem:** the setting is **global per event store**. Switching means every existing Guid-keyed stream (OrgNode, Subject, Invitation, ConfigSchema, ConfigAssignment) also switches. Massive migration. Not worth it.

If we ever split into ancillary stores (`.scratch/modular-configuration/`), per-store stream identity becomes feasible. Until then: rejected.

### Option C — Custom Wolverine parameter strategy that pre-queries

Wolverine has `IParameterStrategy` extension points. Implement a strategy that:
1. Reads composite-key route params (e.g. `subjectId`, `brandId`)
2. Pre-queries `OrgAccessGrantReadModel.BuildId(...)` doc id → reads `StreamId` field
3. Hands `StreamId` to `[Aggregate]` parameter

**Problem:** every handler invocation runs an extra round-trip just to learn the stream id. Defeats the latency win that `[AggregateHandler]` promises (it would otherwise let Marten cache via `FetchForWriting`). Effectively buys us syntax sugar at runtime cost. Rejected.

## Locked decision

**Option A.** Deterministic Guid v5 derived from the composite key, with lazy dual-mode migration. New writes use deterministic; existing reads honour whatever stream id the doc records.

## Implementation outline

1. Add `CritCrit.Api/Platform/DeterministicGuid.cs` — UUID v5 helper. Pick a fixed namespace UUID for the app, document it inline.
2. For each find-or-create doc type, update the handler at the stream-id derivation site:
   - `OwnerLifecycleHandlers.GrantOwnerEndpoint.Handle` — replaces `var streamId = Guid.CreateVersion7();` with `DeterministicGuid.From(tenantId, tenantId, subjectId)` for new grants
   - `AccessGrantHandlers` (`GrantRole`) — same
   - `InvitationHandlers` (re-grant on accept, line 564) — same
   - `ConfigHandlers` (`PatchValues`) — same
   - `AssetHandlers` (`PatchValues`) — same
3. Add `Org/Features/Owners/DeterministicGuidTests.cs` — stable across processes, idempotent for identical input.
4. Document on the helper: "Existing records may store a random `StreamId` predating deterministic IDs. Read paths must accept either."
5. **NOT IN SCOPE for this PRD**: actually migrating to `[AggregateHandler]`. That ships in a follow-up PRD once deterministic stream IDs are in place.

## Acceptance

- New find-or-create writes use `DeterministicGuid.From(...)` exclusively
- `Guid.CreateVersion7()` is gone from every command handler in `Org/Features/`
- Existing grants/config/assets still queryable and writable (lazy dual-mode honoured)
- `DeterministicGuidTests` proves stability for fixed inputs across runs
- All 124 integration tests stay green

## Follow-ups

- `[AggregateHandler]` adoption PRD (after this lands)
- Optional major-version event-stream rebuild (Option A.b above) — defer indefinitely

## Exploration findings (added after first migration wave)

Attempting `[AggregateHandler]` adoption uncovered two further prereqs beyond deterministic stream IDs:

### Prereq 2 — `IParsable<T>` on strong-typed IDs

CritCrit uses public-id strings (`brand_<guid>`, `subj_<guid>`, `inv_<guid>`) as route parameters. Wolverine's `[Aggregate]` / `[WriteAggregate]` expects the route param type to either be the raw stream identity type (Guid) or implement `TryParse()` so Wolverine can convert.

**Resolved**: `OrgNodeId`, `SubjectId`, `InvitationId` now implement `IParsable<T>` with `TryParse` that accepts both public-id format and raw Guid. Route params can be typed as `OrgNodeId nodeId` instead of `string nodeId` with manual parsing. Validated via `RestoreOrgNode` migration — 6/6 restore tests + 124/124 full integration green.

### Prereq 3 — `SingleStreamProjection<T>` for aggregates Wolverine fetches

`[Aggregate]` / `[WriteAggregate]` triggers `FetchForWriting<TAggregate>` under the hood. Marten requires the aggregate type to be registered as either a `SingleStreamProjection<T>` snapshot or a `SelfAggregate`. EventProjections that write to multiple doc types per event (e.g. `OrgNodeProjection` which maintains `OrgNodeReadModel` + `OrgNodeCodeIndex` + ancestry data) are **not** fetchable.

This is exactly the migration described in `.scratch/projection-cleanup/PRD.md` (candidate #3). Each aggregate's projection must be split into:
- One `SingleStreamProjection<TAggregate, Guid>` that is the canonical write-model snapshot
- Zero or more sibling projections (`EventProjection` / `MultiStreamProjection`) that maintain auxiliary documents

Without this split, `[AggregateHandler]` is not viable for that aggregate.

### Net findings

`[AggregateHandler]` adoption is a **three-prereq chain** per aggregate type:

1. **Deterministic stream IDs** (this PRD) — done for grants/config/asset
2. **IParsable on strong-typed IDs** — done globally (small change)
3. **SingleStreamProjection** (candidate #3) — must be done per aggregate type

Aggregates where step 3 is straightforward (no cross-doc updates per event): suitable migration targets first. Aggregates where the EventProjection genuinely needs to write multiple doc types per event: a longer-running design problem (the auxiliary docs need to be maintained from sibling projections or from cascading messages).

### Prereq 6 — Raw-Guid route param OR Marten strong-typed-id registration

Discovered during `[WriteAggregate]` adoption attempt on Subject endpoints (commits reverted, never landed).

Wolverine's `[WriteAggregate("argName")]` extracts the stream id from the route value `argName` by direct Guid parsing. It does **not** auto-unwrap strong-typed identifiers (`SubjectId`, `InvitationId`, etc.) — the route value must already be Guid-parseable as a string.

CritCrit routes use public-id strings (`subj_<guid>`, `inv_<guid>`, etc.). Wolverine's TryParse on `Guid` fails on these, so the endpoint resolves to 404 at runtime even though the aggregate exists.

Workable today: routes that take a raw `Guid` directly (e.g. ConfigSchemaDraft endpoints' `{draftId}`). `ArchiveConfigDraftEndpoint` (462e744) and `UpdateConfigDraftEndpoint` (23a164f) are the shipped proofs.

### Investigation results (later sessions)

**A. Marten strong-typed-id registration alone is NOT sufficient.** Per Marten docs, `FetchForWriting` "requires passing the underlying primitive value rather than the strongly typed identifier directly". Wolverine still uses raw `Guid.Parse` on the route value. Strong-typed-id Marten registration only helps the natural-key resolution path, which Wolverine applies to **request-body properties**, not route parameters.

**B. Before/LoadAsync method returning IEventStream**: blocked. `IEventStream<T>` has no `SaveAsync` and doesn't expose its owning `IDocumentSession`. Multi-entity tuple return from LoadAsync isn't a documented Wolverine convention.

**C. Dual routes** (public-id + raw-Guid companion per command): technically works but doubles API surface. Bad ergonomics not worth the `[WriteAggregate]` gain.

**Decision: switch command-endpoint routes to raw `Guid` (with `:guid` route constraint).** Consumers go through an SDK that exposes strong-typed IDs at the call surface — URL format is an implementation detail between SDK and API, not part of the consumer contract. Response payloads keep the public-id form (`subj_<guid>`, `brand_<guid>`, etc.) for human readability in logs and dashboards. SDK does the wrap on the way in (extract `.Value.ToString()` from the strong-typed ID) and the unwrap on the way out (parse the public-id string into the strong-typed ID).

Migration scope (one focused session):
- Command endpoints (`POST` / `PUT` / `PATCH` / `DELETE`) get `{xxxId:guid}` route constraint on their id placeholders.
- Handler signatures stay strong-typed (`SubjectId subjectId`) — `IParsable<T>` already accepts both formats so the change is non-breaking at the C# level.
- Test URL construction updated to pass the raw `Guid` form (test helpers centralised).
- `CritCrit.Web` updated to call raw-Guid routes.
- Query endpoints (`GET`) where humans paste URLs may keep public-id placeholders if useful.

Once the route migration lands, `[WriteAggregate]` adoption unblocks on every command endpoint. Transitional pattern (Validate + LoadAsync + Handle + manual session/events/save) retires.

### Prereq 4 — Per-endpoint class shape

Discovered while attempting validator removal: **Wolverine.Http convention methods (`Validate`, `LoadAsync`, `Before`, ...) are resolved per-class, not per-endpoint.** A class can declare at most one `Validate(TRequest)` method; Wolverine wires that single method into every endpoint on the class regardless of the endpoint's actual request type. Multi-endpoint classes with multiple request types produce JasperFx codegen errors like `UnResolvableVariableException: was unable to resolve a variable of type CreatePlainOrgNodeRequest as part of the method POST_api_brands_brandId_stores`.

The same limitation showed up earlier during the Owner pilot: two `LoadAsync` overloads on the same class produced the same codegen failure.

Practical consequence: any feature with multiple commands that wants to use the `Validate` / `LoadAsync` conventions must follow the **per-endpoint-class shape** demonstrated by the Owner pilot (`GrantOwnerEndpoint` / `DowngradeOwnerEndpoint` / `RevokeOwnerEndpoint`). Multi-endpoint handler classes like `OrgNodeHandlers` (6 endpoints) and `AccessGrantHandlers` (3 endpoints) need restructuring before their FluentValidation validators can be replaced with the Wolverine `Validate` convention.

Alternative for narrow cases: inline shape guards at the top of the handler body, throwing `DomainException` directly. Loses the structured `ProblemDetails` 400 response (becomes a generic `DomainException` 400 via the existing middleware) but avoids the restructure. Acceptable when shape rules are trivial; the structured-error-response value isn't worth the per-endpoint-class restructure on its own.
