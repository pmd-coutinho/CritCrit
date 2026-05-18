# Cross-Cutting Middleware: Tenant + Actor + Auth + Audit

Status: design-locked
Triage: ready-for-human
Driver: improve-codebase-architecture run on master, 2026-05-18
Depends-on: `.scratch/aggregate-handler-workflow/` (handlers must be aggregate-handler shape first)

## Goal

Stop threading `BrandTenantContext` + `ActorContext` + `OrgAuthorizationService` + `IAuditWriter` through every command handler signature. Move them behind Wolverine middleware / Marten conjoined tenancy / envelope-header propagation. Handlers stop knowing about cross-cutting concerns entirely.

## Why

- Every handler today takes 6+ injected dependencies, most of which are cross-cutting.
- `SessionMetadata.StampActor`, `EnforceSuperAdmin`, `outbox.Enroll`, `audit.Record` repeat verbatim. Adapter count = 1 (no variation across environments) — this is a hypothetical seam, not a real one.
- Cascading messages must carry actor identity into projections + sagas. Today they don't; audit projection (per `.scratch/aggregate-handler-workflow/`) needs it.

## Design decisions (locked)

| # | Question | Decision |
|---|----------|----------|
| 2.1 | Auth gate placement | (b) Static `Authorize(cmd, ActorContext, OrgAuthorizationService)` per handler class via Wolverine convention |
| 2.2 | Tenant resolution | (c) Wolverine extension at host level (`opts.UseTenancyDetection(...)`) reading from route + claims |
| 2.3 | Actor stamping | (b) Wolverine envelope-header propagation (`Envelope.Headers["actor.id"]`) + Marten metadata listener that copies headers to event headers |
| 2.4 | Validation migration | (a) Keep FluentValidation in place; defer to candidate #5 |
| 2.5 | Audit projection location | (a) `Observability/Audit/AuditLogProjection.cs` — observability context owns audit |

## Out of scope

- FluentValidation removal (candidate #5)
- Projection cleanup (candidate #3)

## Issues

01. Wolverine tenancy detection extension
02. Actor envelope-header propagation + Marten metadata listener
03. `Authorize` convention adoption per handler class
04. Deprecate `SessionFactory.TenantSession` and `SessionMetadata.StampActor` once all handlers migrated
05. Move `AuditLogProjection` to `Observability/Audit/` (after `.scratch/aggregate-handler-workflow/` lands)

## Acceptance

- Handler signatures contain only: command/request, `[Aggregate]` (if applicable), loaded entities, command-specific services (rarely)
- No handler takes `IDocumentStore`, `BrandTenantContext`, `ActorContext`, `IAuditWriter`, `OrgAuthorizationService` as parameters
- Cascading Wolverine messages carry actor + tenant headers end-to-end
- Audit projection reads actor from event metadata
