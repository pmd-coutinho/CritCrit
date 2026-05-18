# Invitation Aggregate Workflow

Status: design-locked
Triage: ready-for-human
Driver: improve-codebase-architecture run on master, 2026-05-18
Depends-on: `.scratch/aggregate-handler-workflow/` (aggregate-handler convention must land first)

## Goal

Consolidate the invitation state machine (currently spread across `InvitationHandlers.cs` HTTP entry point, `InvitationWorkflow.cs` four message handlers, and `InvitationProjection.cs`) under the **aggregate-handler workflow on the existing invitation stream**. Split IdP-provisioning side effects into a pure step graph of messages so each transition has its own pure decide fn.

## Why

- Today the invitation lifecycle is assembled from three files. To understand "what happens when an invitation is accepted" a reader must read all three.
- Email-retry state lives inside the projection (it tracks attempt count for the read model), tangling read concerns with workflow concerns.
- The `Provision → SendEmail → Expire → Accept` flow is the natural definition of a saga, but the codebase is already event-sourced — adopting Wolverine's `Saga` primitive would duplicate state. We model the saga as **aggregate-handlers on the existing event stream**, the Critter-Stack-idiomatic shape.

## Decisions (locked)

| # | Question | Decision |
|---|----------|----------|
| 4.1 | Saga primitive | (a) Aggregate-handler on `InvitationAggregate` (= `InvitationReadModel` per Q1.1). No `Wolverine.Saga.Saga` class. |
| 4.2 | IdP side effect | (c) Split into pure step graph — `ExternalUserProvisionRequested` → IdP handler → `ExternalUserProvisioned` / `ExternalUserProvisionFailed` |
| 4.3 | Email retry | (c) Wolverine native retry policy for transient delivery failures + emit `InvitationEmailRetryScheduled` event for audit-trail visibility |
| 4.4 | Expiration | (a) Keep current `bus.ScheduleAsync(ExpireInvitation, expiresAt)` with idempotent ExpiresAt-match guard |
| 4.5 | Projection migration | (a) Migrate `InvitationProjection` to `SingleStreamProjection<InvitationReadModel, Guid>` as part of this candidate |

## Out of scope

- `Subject` lifecycle migration (carved out — see follow-up #2)
- `ExternalIdentity` lifecycle migration (follow-up)
- Email body content / template (no behavioural change)

## New events on invitation stream

Additive — no event upcaster needed for accepted invitations; pre-existing streams continue to project through the old apply path until consumed.

```csharp
public record ExternalUserProvisionRequested(InvitationId InvitationId, string EmailNormalized);
public record ExternalUserProvisioned(InvitationId InvitationId, SubjectId SubjectId, string Provider, string ExternalId, bool WasCreated);
public record ExternalUserProvisionFailed(InvitationId InvitationId, string Code, string Summary);
public record InvitationEmailRetryScheduled(InvitationId InvitationId, int NextAttempt, TimeSpan Delay, string FailureCode);
```

Existing events (`InvitationProvisioningStarted`, `InvitationSubjectBound`, `InvitationTokenIssued`, `InvitationMarkedPending`, `InvitationEmailDispatched`, `InvitationExpired`, `InvitationFailed`) preserved.

## Step graph

```
                          [HTTP POST /invitations]
                                  │
                                  ▼
                    InvitationCreated  (decide fn: validate+yield)
                                  │
                                  ▼
                  ExternalUserProvisionRequested  (cascading msg)
                                  │
                          ┌───────┴────────┐
                          │                │
                  IdP succeeds       IdP fails
                          │                │
                          ▼                ▼
        ExternalUserProvisioned    ExternalUserProvisionFailed
                          │                │
                          ▼                ▼
              (decide fn appends)    InvitationFailed
                InvitationSubjectBound
                InvitationTokenIssued
                InvitationMarkedPending
                SendInvitationEmail (cascading msg)
                ExpireInvitation@expiresAt (scheduled)
                          │
                          ▼
                  Email delivery handler
                          │
                  ┌───────┴────────┐
                  │                │
              Success         Transient failure
                  │                │
                  ▼                ▼
        InvitationEmailDispatched  Wolverine retry policy
                                   │ (with cooldown)
                                   ▼
                          InvitationEmailRetryScheduled
                          (audit trail event)
```

## Module shape

### `Org/Features/Invitations/InvitationAggregate.cs` (new)

Reuse `InvitationReadModel` as the aggregate (per Q1.1 (a)). No new write-model type.

### `Org/Features/Invitations/InvitationRules.cs` (new, pure)

```csharp
public static class InvitationRules
{
    public static void RequireRequested(InvitationReadModel inv);
    public static void RequirePending(InvitationReadModel inv);
    public static void RequireSubjectBindable(SubjectReadModel? existing, ExternalIdentityReadModel? link);
}
```

### `Org/Features/Invitations/InvitationHandlers.cs` (rewrite)

`CreateInvitation` decide fn:
- Inputs: command, loaded `OrgNodeReadModel` target, loaded `ActorContext`, tenancy
- Yields: `InvitationCreated` + cascading `ExternalUserProvisionRequested`

### `Org/Features/Invitations/InvitationWorkflow.cs` (rewrite, much smaller)

Three message handlers, each pure-shaped:

```csharp
[AggregateHandler]
public static IEnumerable<object> Handle(
    ExternalUserProvisioned msg,
    [Aggregate] InvitationReadModel inv,
    InvitationTokenService tokens)
{
    InvitationRules.RequireRequested(inv);
    var raw = tokens.GenerateRawToken();
    var expiresAt = TimeProvider.System.GetUtcNow().AddDays(1);
    yield return new InvitationSubjectBound(msg.InvitationId, msg.SubjectId, /* email */);
    yield return new InvitationTokenIssued(msg.InvitationId, tokens.Hash(raw), expiresAt);
    yield return new InvitationMarkedPending(msg.InvitationId, TimeProvider.System.GetUtcNow());
    yield return new SendInvitationEmail(msg.InvitationId, msg.WasCreated, 1);  // cascading
    yield return new ScheduledMessage<ExpireInvitation>(
        new ExpireInvitation(msg.InvitationId, expiresAt), expiresAt);
}

[AggregateHandler]
public static IEnumerable<object> Handle(ExternalUserProvisionFailed msg, [Aggregate] InvitationReadModel inv) { ... }

[AggregateHandler]
public static IEnumerable<object> Handle(ExpireInvitation msg, [Aggregate] InvitationReadModel inv) { ... }
```

### `Org/Features/Invitations/IdentityProvisioningHandler.cs` (new, the *only* impure handler)

```csharp
public sealed class IdentityProvisioningHandler(
    IIdentityProviderProvisioning idp,
    IDocumentStore store)
{
    [WolverineHandler]
    public async Task<object> Handle(
        ExternalUserProvisionRequested cmd,
        BrandTenantContext tenant,
        CancellationToken ct)
    {
        try
        {
            await using var s = SessionFactory.PlatformSession(store);
            var inv = await s.LoadAsync<InvitationReadModel>(cmd.InvitationId.Value, ct);
            // Subject reconciliation: identity lookup, email-conflict check
            // Returns ExternalUserProvisioned or ExternalUserProvisionFailed
        }
        catch (Exception ex)
        {
            return new ExternalUserProvisionFailed(cmd.InvitationId, "idp_call_failed", ex.Message);
        }
    }
}
```

This is the **only** handler that does external IO. Mockable via `IIdentityProviderProvisioning` for tests. Output is one of two strongly-typed events.

### `Org/Features/Invitations/InvitationEmailHandler.cs` (new)

```csharp
public sealed class InvitationEmailHandler(IInvitationEmailSender sender, InvitationTokenService tokens, IConfiguration config)
{
    [WolverineHandler]
    public async Task<object> Handle(SendInvitationEmail cmd, IDocumentSession session, CancellationToken ct)
    {
        // Build URL, send, on success return InvitationEmailDispatched event to append.
        // On transient failure, throw — Wolverine retry policy handles cooldown.
        // After Wolverine retries exhausted, dead-letter; emit InvitationFailed.
    }
}
```

Wolverine wiring (`CritCritApiConfiguration`):
```csharp
opts.OnException<HttpRequestException>().Or<SmtpException>()
    .RetryWithCooldown(1.Minute(), 5.Minutes(), 15.Minutes())
    .Then.MoveToErrorQueue();
```

Each retry emits `InvitationEmailRetryScheduled` event for the audit trail (via Wolverine `[OnRetry]` callback or middleware).

### `Org/Projections/InvitationProjection.cs` (rewrite as SingleStream)

```csharp
public sealed class InvitationProjection : SingleStreamProjection<InvitationReadModel, Guid>
{
    public InvitationReadModel Create(IEvent<InvitationCreated> e) => /* ... */;
    public void Apply(InvitationProvisioningStarted e, InvitationReadModel inv) { ... }
    public void Apply(InvitationSubjectBound e, InvitationReadModel inv) { ... }
    public void Apply(InvitationTokenIssued e, InvitationReadModel inv) { ... }
    public void Apply(InvitationMarkedPending e, InvitationReadModel inv) { ... }
    public void Apply(InvitationEmailDispatched e, InvitationReadModel inv) { ... }
    public void Apply(InvitationEmailRetryScheduled e, InvitationReadModel inv) { ... }
    public void Apply(InvitationExpired e, InvitationReadModel inv) => inv.Status = InvitationStatus.Expired;
    public void Apply(InvitationFailed e, InvitationReadModel inv) { ... }
    public void Apply(ExternalUserProvisionFailed e, InvitationReadModel inv) { ... }
}
```

No async loads. No cross-stream lookups. Pure folds.

## Tests

- `InvitationRulesTests` — pure rule assertions
- `CreateInvitationDecideTests`, `ExternalUserProvisionedDecideTests`, `ExternalUserProvisionFailedDecideTests`, `ExpireInvitationDecideTests` — pure decide-fn tests
- `IdentityProvisioningHandlerTests` — integration with mocked `IIdentityProviderProvisioning`
- `InvitationEmailHandlerTests` — integration with mocked `IInvitationEmailSender`
- `InvitationProjectionTests` — pure `Apply` tests
- Existing Alba contract tests under `CritCrit.AlbaTests/Invitations/*` stay as smoke

## Acceptance

- `InvitationWorkflow.cs` ≤ 120 LOC, no `try/catch(Exception)`, no inline IdP/email IO
- `IdentityProvisioningHandler` is the only file in `Org/Features/Invitations/` touching `IIdentityProviderProvisioning`
- `InvitationEmailHandler` is the only file touching `IInvitationEmailSender`
- `InvitationProjection.cs` has zero `LoadAsync` calls
- All existing Alba invitation tests green
- New decide-fn unit tests run in <50 ms (no infra)

## Risks

- `ExternalUserProvisioned` carries `SubjectId` + provider — current code creates the subject *inside* provisioning, then appends events to subject stream. New shape: IdP handler does subject reconciliation, but subject stream events emitted from the invitation aggregate-handler. Re-check ordering.
- Wolverine `[OnRetry]` for `InvitationEmailRetryScheduled` event emission needs prototyping — confirm API exists in current Wolverine version before committing to shape.

## Issues

01. New event types + event registration
02. `IdentityProvisioningHandler` extraction
03. `InvitationEmailHandler` extraction + Wolverine retry policy wiring
04. Invitation aggregate-handler workflow (`Handle(Provisioned/Failed/Expired, [Aggregate] InvitationReadModel)`)
05. `InvitationProjection` migration to SingleStream
06. Delete legacy `InvitationWorkflow` once cutover complete
