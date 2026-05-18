# 01 — Owner combined pilot (aggregate-handler + validator removal + audit projection)

Status: ready-for-human
Parent PRDs:
- `.scratch/aggregate-handler-workflow/PRD.md` (candidate #1)
- `.scratch/validator-removal/PRD.md` (candidate #5)
- `.scratch/projection-cleanup/PRD.md` (candidate #3) — pattern exercised, no migration

## Scope

Single PR exercising the first taste of three patterns on the smallest feature in the codebase — the Owner lifecycle. After this PR, the Owner slice is the reference implementation that subsequent feature migrations follow.

Three concerns combined:

1. **Aggregate-handler workflow** — rewrite `OwnerLifecycleHandlers.cs` (211 LOC) as pure decide fns on the `OrgAccessGrantReadModel` aggregate.
2. **Validator removal** — delete the 3 FluentValidation validators for Owner requests, add Wolverine `Validate` static methods.
3. **Audit projection** — introduce `AuditLogProjection` consuming the new `OrgAccess*` events, replacing inline `audit.Record(session, ...)` calls. Demonstrates the SingleStream / narrow EventProjection pattern from candidate #3.

Owner is the smallest decision tree in the Org feature set; owner stream is one per `(tenant, brand, subject)` and only super-admin actors operate on it. Three commands. Three validators. One handler file.

## Deliverables

### 1. Event schema evolution

`CritCrit.Api/Org/Domain/OrgAccessEvents.cs`:

```csharp
public record OrgAccessRoleChanged(
    TenantId TenantId, OrgNodeId NodeId, SubjectId SubjectId,
    OrgRole OldRole, OrgRole NewRole,
    string? Reason);                    // NEW

public record OrgAccessRevoked(
    TenantId TenantId, OrgNodeId NodeId, SubjectId SubjectId,
    OrgAccessRevokedReason Cause,
    string? Reason);                    // NEW
```

Register Marten event upcasters for existing streams that have null `Reason`.

### 2. Pure rules module

New file `Org/Features/Owners/OwnerRules.cs`:

```csharp
public static class OwnerRules
{
    public static void RequireBrandRoot(OrgNodeReadModel node);
    public static void RequireActiveSubject(SubjectReadModel? subject);
    public static void RequireNotAlreadyOwner(OrgAccessGrantReadModel? grant);
    public static void RequireActiveOwner(OrgAccessGrantReadModel? grant);
    public static void RequireDowngradeTarget(OrgRole newRole);
}
```

All throw `DomainException`. Pure. Unit-testable without Marten.

### 3. Handler rewrite with `Validate` methods

`Org/Features/Owners/OwnerLifecycleHandlers.cs`:

```csharp
public static class OwnerLifecycleHandlers
{
    // --- GrantOwner ---

    public static IEnumerable<ProblemDetails> Validate(GrantOwnerRequest cmd)
    {
        if (string.IsNullOrWhiteSpace(cmd.SubjectId))
            yield return new ProblemDetails { Title = "subjectId", Detail = "SubjectId is required.", Status = 400 };
    }

    public static async Task<(OrgNodeReadModel, SubjectReadModel)> LoadAsync(
        GrantOwnerRequest cmd, IQuerySession s, BrandTenantContext t, CancellationToken ct);

    [AggregateHandler]
    [WolverinePost("/api/brands/{brandId}/owners")]
    public static (CreationResponse, OrgAccessGranted) GrantOwner(
        GrantOwnerRequest cmd,
        [Aggregate] OrgAccessGrantReadModel? grant,
        OrgNodeReadModel root,
        SubjectReadModel subject,
        OrgAuthorizationService auth,
        ActorContext actor,
        BrandTenantContext tenant);

    // --- DowngradeOwner --- (Validate, [AggregateHandler], decide fn)
    // --- RevokeOwner ---    (Validate, [AggregateHandler], decide fn)
}
```

Decide fns: ~25 LOC each. No session, no `audit.Record`, no `StartStream`/`Append`/`SaveChangesAsync`. `Validate` methods replace the deleted FluentValidation classes.

### 4. Audit log projection

New `Observability/Audit/AuditLogProjection.cs`:

```csharp
public sealed class AuditLogProjection : EventProjection
{
    public async Task<AuditLogReadModel?> Create(
        IEvent<OrgAccessGranted> e, IQuerySession q, CancellationToken ct)
    {
        var subject = await q.LoadAsync<SubjectReadModel>(e.Data.SubjectId.Value, ct);
        return OrgAccessAudit.From(e, subject);
    }

    public async Task<AuditLogReadModel?> Create(IEvent<OrgAccessRoleChanged> e, ...) { ... }
    public async Task<AuditLogReadModel?> Create(IEvent<OrgAccessRevoked> e, ...) { ... }
}
```

Until candidate #2 lands, actor read from `SessionMetadata` stamped by the existing `AddMartenTenancyDetection` callback in `CritCritApiConfiguration.cs`. Document the inline-load on Subject as the deliberate exception per candidate #3's `ConfigSchemaDraftPublishedTracker` precedent — replaced by envelope-header propagation in candidate #2.

### 5. Wiring deltas

`CritCritApiConfiguration.cs`:
- Add `m.Projections.Add<AuditLogProjection>(ProjectionLifecycle.Inline);` in `ConfigureProjections`
- No removal of `IAuditWriter` registration yet — other handlers still use it
- Keep `opts.UseFluentValidation()` wired — other features still have validators

### 6. File deletions

- `Org/Validators/GrantOwnerRequestValidator.cs`
- `Org/Validators/DowngradeOwnerRequestValidator.cs`
- `Org/Validators/RevokeOwnerRequestValidator.cs`

### 7. Tests

New:
- `Org.UnitTests/Owners/OwnerRulesTests.cs` — pure rule assertions
- `Org.UnitTests/Owners/GrantOwnerDecideTests.cs` — `(cmd, grant=null, root, subject, ...) → OrgAccessGranted`
- `Org.UnitTests/Owners/DowngradeOwnerDecideTests.cs`, `RevokeOwnerDecideTests.cs`
- `Org.UnitTests/Owners/OwnerValidateTests.cs` — covers each `yield return ProblemDetails` path
- `Org.UnitTests/Audit/AuditLogProjectionTests.cs` — `Create(event, query, ct)` with seeded subject

Existing `CritCrit.AlbaTests/Owners/*` contract tests stay as smoke. Should pass without modification.

## Acceptance

- All existing Alba tests green
- New unit tests cover happy path + each rule violation + each Validate path + audit projection
- `OwnerLifecycleHandlers.cs` ≤ 100 LOC
- `audit.Record(...)` call sites in `OwnerLifecycleHandlers` = 0
- `SessionFactory.TenantSession(...)` call sites in `OwnerLifecycleHandlers` = 0
- `IDocumentStore` parameter on Owner handlers = 0
- `Org/Validators/*OwnerRequestValidator.cs` = 0 (all three deleted)
- `using FluentValidation;` in any Owner handler/test file = 0
- `AuditLogReadModel` rows created via projection for each Owner event in Alba tests

## Patterns exercised (what subsequent migrations copy)

| Pattern | Where to look |
|---------|---------------|
| Pure rules module + decide fn | `Org/Features/Owners/OwnerRules.cs`, decide fns in handler |
| Wolverine `Validate` static method | `OwnerLifecycleHandlers.Validate(...)` |
| Sibling `LoadAsync` for cross-aggregate reads | `OwnerLifecycleHandlers.LoadAsync(...)` |
| `[AggregateHandler]` + nullable `[Aggregate]` | `GrantOwner` signature |
| Tuple return `(CreationResponse, …event)` | `GrantOwner` return |
| Audit-as-event-consumer | `AuditLogProjection.Create(...)` |
| Narrow async cross-load in projection (documented exception) | `AuditLogProjection` Subject load — marked with comment |

## Risks

- Audit projection is the new seam; if it lags behind the inline audit-write timing, audit reads-after-write may miss the latest record. Mitigate: `ProjectionLifecycle.Inline` until candidate #3 separates concerns. Documented explicitly.
- Marten event upcasters required for `Reason` field default. Test against pre-existing streams in a snapshot before merge.
- Other features still use `IAuditWriter` directly — keep registration alive. Audit projection writes additional `AuditLogReadModel`; tests must distinguish from existing `ImmutableAuditEvent` rows.

## Out of scope for this PR

- Cross-cutting middleware (candidate #2) — Owner handlers still take `OrgAuthorizationService`, `ActorContext`, `BrandTenantContext` as parameters. Removed in candidate #2 follow-up.
- Other feature migrations — Brand, OrgNode, AccessGrant, Subject, Config, Asset, Invitation all remain on old shape.
- Tenancy detection mechanism — stays on current `AddMartenTenancyDetection` callback.
- FluentValidation package removal — deferred until last feature migrates per candidate #5 issue #07.
