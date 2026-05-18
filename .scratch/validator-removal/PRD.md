# Validator Removal: FluentValidation → Wolverine Validate Method

Status: design-locked
Triage: ready-for-human
Driver: improve-codebase-architecture run on master, 2026-05-18

## Goal

Replace 15 per-request FluentValidation validators with Wolverine `Validate` static method convention, drop the `FluentValidation` / `Wolverine.FluentValidation` / `Wolverine.Http.FluentValidation` packages, and codify the rule-placement convention so future contributors know where each kind of rule lives.

## Why

- Every validator today is a one-adapter hypothetical seam. None varies across environments. Most are 1–3 `NotEmpty()` calls.
- Many duplicate work the handler already does (e.g. `GrantOwnerRequestValidator.SubjectId.NotEmpty()` vs handler's `OrgPublicId.TryParseSubject` — handler's check is strictly stronger).
- Rule placement is currently inconsistent: structural rules in `Org/Validators/`, business rules in handlers. Readers must check two files per request.

## Decisions (locked)

| # | Question | Decision |
|---|----------|----------|
| 5.1 | Replacement strategy | (a) Wolverine `Validate` static method per handler class returning `ProblemDetails` / `IEnumerable<ProblemDetails>` |
| 5.2 | Rule placement convention | Tripartite — see "Convention" below — and document in `Org/CONTEXT.md` |
| 5.3 | FluentValidation packages | (a) Drop all three after migration complete |
| 5.4 | Migration order | (b) Per-feature PR — Owner first (3 validators), then Brand, OrgNode, Subject, etc. Keep FluentValidation wired until last feature migrated. |
| 5.5 | Error pathway consolidation | Scope-bound — only delete validators, leave error middleware untouched. Error-code taxonomy is a separate concern. |

## Convention (added to `Org/CONTEXT.md`)

Three layers, each rule lives in exactly one:

| Layer | Lives in | Concerned with | Failure mode |
|-------|----------|----------------|--------------|
| **Shape** | Static `Validate(TRequest cmd)` on handler class | Null, length, regex, format. No DB, no session. | 400 `ProblemDetails` |
| **Existence** | Sibling static `LoadAsync(cmd, IQuerySession, ...)` | "Does this entity exist?" Throws `DomainException` if not. | 404 / 422 via existing middleware |
| **Business** | `*Rules.cs` pure module called from decide fn | Aggregate-state invariants, role compatibility, redundancy, conflict | 409 / 422 via existing middleware |

Validators **deleted entirely**. No `IValidator<T>` adapters, no FluentValidation `AbstractValidator`. `Validate` is a static method, no DI, no allocations per request.

## Out of scope

- 4xx error-code normalisation
- Validation messages localisation
- Cross-field validation patterns (none currently exist)

## Pilot — Owner feature

3 validators, 41 LOC total:
- `GrantOwnerRequestValidator.cs`
- `DowngradeOwnerRequestValidator.cs`
- `RevokeOwnerRequestValidator.cs`

### Before

```csharp
public class GrantOwnerRequestValidator : AbstractValidator<GrantOwnerRequest>
{
    public GrantOwnerRequestValidator()
    {
        RuleFor(x => x.SubjectId).NotEmpty();
    }
}
```

### After (inside `OwnerLifecycleHandlers`)

```csharp
public static IEnumerable<ProblemDetails> Validate(GrantOwnerRequest cmd)
{
    if (string.IsNullOrWhiteSpace(cmd.SubjectId))
        yield return new ProblemDetails { Title = "subjectId", Detail = "SubjectId is required.", Status = 400 };
}
```

Wolverine auto-discovers `Validate`. Runs before handler. Multiple `ProblemDetails` yielded → returned as combined response.

### File deltas

- Delete: `Org/Validators/GrantOwnerRequestValidator.cs`, `DowngradeOwnerRequestValidator.cs`, `RevokeOwnerRequestValidator.cs`
- Edit: `Org/Features/Owners/OwnerLifecycleHandlers.cs` — add three `Validate` methods

## Issues

01. Pilot — Owner (3 validators)
02. Brand (`CreateBrandRequestValidator`)
03. OrgNode (`CreatePlainOrgNodeRequestValidator`, `CreateStoreRequestValidator`, `CreateDeviceRequestValidator`, `MoveOrgNodeRequestValidator`, `ArchiveOrgNodeRequestValidator`, `HardDeleteOrgNodeRequestValidator`)
04. AccessGrant (`GrantRoleRequestValidator`, `SetGrantExpirationRequestValidator`)
05. Subject (`CreateSubjectRequestValidator`)
06. Invitation (`CreateInvitationRequestValidator`, `CancelInvitationRequestValidator`)
07. Final cleanup — remove `Wolverine.FluentValidation` + `Wolverine.Http.FluentValidation` packages from `.csproj`, remove `opts.UseFluentValidation()` + `opts.UseFluentValidationProblemDetail()` + `c.UseFluentValidationProblemDetailMiddleware()` wiring lines in `CritCritApiConfiguration.cs`, delete `Org/Validators/` directory.

## Acceptance per issue

- All FluentValidation `using` lines in the feature gone
- Every endpoint in the feature has a sibling `Validate` method
- No `IValidator<T>` resolved from DI
- `Org/Validators/*RequestValidator.cs` for the feature deleted
- Existing Alba contract tests green
- New unit tests: one `ValidateTests` class per handler covering each `yield return ProblemDetails` path

## Risks

- `Wolverine.Http.FluentValidation` may install ProblemDetails 400 wiring that we'd lose. Verify Wolverine's plain `Validate` convention produces the same 400 shape before deleting middleware lines.
- Existing tests may assert specific error-payload shapes from FluentValidation. Run all Alba tests after each per-feature PR.
