# Aggregate Handler Workflow + Decider Adoption

Status: design-locked
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
