# Aggregate Handler Workflow + Decider Adoption

Status: **PARTIALLY SHIPPED — transitional shape only** (was design-locked)
Shipped commits: 2eaef61 (Owner pilot), 586b18a (Brand pilot)

## Shipped — transitional pattern

The Owner and Brand pilots ship a transitional shape that captures the *testability and locality* wins without yet using `[AggregateHandler]`:

- Pure invariants in `OwnerRules` module (unit-testable in isolation).
- Sibling `LoadAsync` convention method per endpoint for cross-aggregate reads.
- Wolverine `Validate` static method for shape validation.
- Per-endpoint static class so the conventions dispatch correctly.

Three Owner endpoint classes (`GrantOwnerEndpoint`, `DowngradeOwnerEndpoint`, `RevokeOwnerEndpoint`) and one Brand endpoint class (`CreateBrandEndpoint`) follow this shape today. Eight more under `Org/Features/OrgNodes/` (`Create*Endpoint`, `*OrgNodeEndpoint`) follow the per-endpoint shape without `LoadAsync` / `Rules`.

## Still missing — real `[AggregateHandler]` adoption

The decide-fn-purity centerpiece (no `IDocumentStore`, no inline `Events.Append/StartStream`, no inline `audit.Record`, no `SessionFactory.TenantSession`) requires:

1. ✅ Deterministic stream IDs — shipped (see `.scratch/deterministic-stream-ids/`)
2. ✅ `IParsable<T>` on strong-typed IDs — shipped
3. ✅ Per-endpoint class shape — shipped
4. 🟡 **`SingleStreamProjection<T>` per aggregate** — done for single-tenanted aggregates (ConfigSchema family, Subject, Invitation); blocked for multi-tenanted aggregates by prereq 5
5. ❌ **Conjoined event-store tenancy (`m.Events.TenancyStyle = TenancyStyle.Conjoined`)** — required to bridge SingleStreamProjection with multi-tenanted doc types. Schema migration + backfill required. Multi-tenanted aggregates: OrgNode, OrgAccessGrant, ConfigAssignment, ConfigNodeValue, AssetNodeValue.

Pending (after prereq 4):
- `[AggregateHandler]` + `[Aggregate]` / `[WriteAggregate]` on Owner / Brand / Access / Subject endpoints.
- Tuple-return `(CreationResponse, …event)` for write endpoints.
- `m.Events.UseIdentityMapForAggregates = true` greenfield critter setting.
- `AuditLogProjection` deriving `ImmutableAuditEvent` from domain events (replaces inline `audit.Record`).
- Removal of `IDocumentStore` / `IAuditWriter` / `IMartenOutbox` from handler signatures.
Triage: ready-for-human
Driver: improve-codebase-architecture run on master, 2026-05-18

## Goal

Collapse hand-threaded command handlers in `CritCrit.Api/Org/Features/*/` behind Wolverine's **Aggregate Handler Workflow** with a **Decider Pattern** shape. Eliminate the ~25-occurrence preamble pattern `IDocumentStore` + `SessionFactory.TenantSession` + `SessionMetadata.StampActor` + manual `StartStream`/`Append` + `audit.Record` + `SaveChangesAsync`.

## Why

- Handlers today are shallow modules: interface (6+ injected dependencies + manual orchestration) nearly as wide as implementation. Adding one feature handler = 50–95 LOC of boilerplate before any business logic.
- Decide functions are not unit-testable in isolation — every test path goes through `IAlbaHost`.
- Marten team explicitly recommends Decider over legacy `AggregateBase`/repository (per `~/dev/llm-wiki/wiki/concepts/Decider Pattern.md`).
- Greenfield critter recipe (`m.Events.UseIdentityMapForAggregates = true`) requires Decider/AggregateHandler shape.

## Design decisions (locked)

| # | Question | Decision |
|---|----------|----------|
| 1.1 | Write-model vs read-model split | (a) Reuse `OrgNodeReadModel` / `OrgAccessGrantReadModel` as both |
| 1.2 | Tenancy mechanism | (a) Marten conjoined multi-tenancy + Wolverine `[Tenant]` |
| 1.3 | Audit emission | (a) Audit as domain event consequence — `AuditLogProjection` |
| 1.4 | Pilot slice | (c) `OwnerLifecycleHandlers` first |
| 1.5 | Read-after-write | Switch to Wolverine `(CreationResponse, …event)` tuple + `UpdatedAggregate` directive |
| 1.6 | Tests | Keep Alba contract tests as smoke; add decide-fn + projection unit tests below |
| 1.7 | Audit event shape | (a) Fatten domain events (`Reason` on `OrgAccessRoleChanged` + `OrgAccessRevoked`) |
| 1.8 | `[Aggregate]` nullability | (a) Inline branches in decide fn for null/active states |
| 1.9 | Cross-aggregate loads | (a) Sibling static `LoadAsync(cmd, IQuerySession, ...)` method per handler class |

## Out of scope

- Replacing FluentValidation (candidate #5)
- Middleware layer for auth/tenant/actor/audit (candidate #2 — sibling issue)
- Modular `CritCritApiConfiguration` (candidate #6)
- Marten ancillary store split (future, not now)

## Issues

01. Owner lifecycle pilot — `.scratch/aggregate-handler-workflow/issues/01-owner-pilot.md`
02. Brand handlers migration
03. OrgNode handlers migration
04. AccessGrant handlers migration
05. Subject handlers migration
06. Config schema handlers migration (depends on candidate #3 projection cleanup)
07. Config assignment handlers migration
08. Asset handlers migration
09. Audit log projection + event fattening
10. Greenfield critter setting (`UseIdentityMapForAggregates`) enablement
