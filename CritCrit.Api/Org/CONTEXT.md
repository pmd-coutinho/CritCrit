# Org Context

Org tree modelling, identity, auth, and invitations.

## Language

**Org Node**:
A node in the brand organization tree (e.g. brand, region, store).
_Avoid_: Group, tenant unit

**Subject**:
A user identity that can be granted access to an org node. Distinct from the human — one human may map to many subjects across identity providers.
_Avoid_: User, account, principal

**Actor**:
The subject performing the current request, captured per-session as actor metadata on every event and audit record.
_Avoid_: Caller, current user

**Org Access Grant**:
A durable assignment of an org role to a subject at a specific org node, modelled as an event stream identified by `(tenant, node, subject)`. Inheritable down the tree.
_Avoid_: Permission, ACL entry, membership

**Owner Grant**:
An Org Access Grant with `Owner` role at the brand root. Only super-admin actors may issue, downgrade, or revoke.

**Redundant Grant**:
A descendant Org Access Grant fully subsumed by an ancestor grant of equal or stronger role. Cleaned up asynchronously when stronger grants are added.

**Invitation**:
A pending grant of access to an org node for a subject who has not yet joined. Modelled as a saga from `Requested → Provisioning → Pending → Accepted`.
_Avoid_: Invite token, request

**Decide Function**:
A pure `(aggregate, command) → events[]` function inside a Wolverine `[AggregateHandler]`. No IO, no session, no side effects. Cross-aggregate lookups happen in a sibling static `LoadAsync` method.

## Rule placement convention

Every input rule lives in exactly one of three layers:

| Layer | Lives in | Concerned with | Failure mode |
|-------|----------|----------------|--------------|
| **Shape rules** | Static `Validate(TRequest cmd)` on handler class (Wolverine convention) | Null, length, regex, format. No DB, no session. | 400 `ProblemDetails` |
| **Existence rules** | Sibling static `LoadAsync(cmd, IQuerySession, ...)` | "Does the referenced entity exist and is it in a usable state?" | `DomainException` → 404 / 422 |
| **Business rules** | `*Rules.cs` pure module called from the decide fn | Aggregate-state invariants, role compatibility, redundancy, conflict | `DomainException` → 409 / 422 |

FluentValidation is not used. Validators in `Org/Validators/` are being deleted as part of `.scratch/validator-removal/`.

## Relationships

- An **Org Node** belongs to exactly one tenant
- An **Org Access Grant** binds one **Subject** to one **Org Node** under one role
- An **Owner Grant** is an **Org Access Grant** rooted at a Brand node with `Owner` role
- An **Invitation** produces one **Org Access Grant** on acceptance
- Every domain event records the **Actor** who triggered it

## Flagged Ambiguities

<!-- Anything contested — record here so the next /grill-with-docs run resolves it. -->
