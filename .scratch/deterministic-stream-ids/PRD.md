# Deterministic Stream IDs for Find-or-Create Aggregates

Status: design
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
