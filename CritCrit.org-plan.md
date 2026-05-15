# CritCrit Org Hierarchy Plan

## Goal

Model a brand-scoped org hierarchy on Marten with roles, inheritance, invitations, and auditable destructive operations.

## What Is Already Implemented

- Brand is the tenant root.
- Brand public id is the Marten/Wolverine tenant id.
- Tenant-scoped routes use Wolverine HTTP handlers, with middleware only for tenant resolution and archived-brand gating.
- Org nodes use typed UUIDv7-based public ids.
- Code-defined hierarchy rules exist.
- Fixed roles exist: `Owner`, `Admin`, `Member`, `Viewer`.
- Subject provisioning exists as internal identity records.
- Invitation onboarding exists with a Wolverine-backed provisioning workflow.
- Direct grants exist with one role per subject per node.
- Redundant grant creation is rejected.
- Org lifecycle commands exist: move, archive, restore, and hard delete.
- Brand archive/restore now requires `Owner` or `SuperAdmin`.
- Hard delete now behaves as app-level hard delete:
  - `OrgNodeReadModel`, `StoreProfileReadModel`, `DeviceProfileReadModel`, and `OrgNodeCodeIndex` are marked `HardDeleted`
  - hard-deleted nodes are hidden from normal reads
  - active grants in the deleted subtree are revoked with `TargetHardDeleted`
  - Brand root hard delete creates a tenant-neutral tombstone
- Immutable audit events are persisted for:
  - hard delete
  - forced subtree archive
  - Brand archive
  - Brand restore
  - move
  - owner grant / owner downgrade
- Event writes are stamped with actor metadata through Marten session metadata and headers.
- Alba coverage now includes lifecycle, authorization edge cases, and immutable audit assertions.
- Alba coverage now also includes:
  - invitation creation
  - acceptance
  - onboarded-subject rejection
  - pending invitation supersede
  - first-accept auto-apply

## Current Domain Decisions

- Tree only, one parent per non-root node.
- Downward inheritance is additive.
- Effective authorization uses the highest effective role.
- `Owner` is root Brand only.
- `SuperAdmin` comes from the identity provider live.
- `Brand` archive/hard-delete rules are sensitive operations and require audit.
- `Brand` archive/restore is owner-level, not admin-level.
- `Country` and `Franchise` are plain org nodes for now.
- `Store` and `Device` are special node types with required operational data.
- `Device` nodes are terminal.
- `Country` codes use ISO alpha-2.
- All org codes are tenant-wide unique, immutable, and normalized case-insensitively.
- Hard delete is app-level first; physical purge is deferred.
- Companion profile lifecycle follows OrgNode lifecycle and cannot be deleted independently.
- Invitations are onboarding-only:
  - one subject per normalized email
  - one active pending invitation per email + node
  - new invite for the same email + node supersedes the previous pending invite
  - first accepted invitation marks the subject onboarded
  - later invite attempts for onboarded subjects are rejected
  - remaining pending invitations for that subject auto-apply on first acceptance

## Remaining Work

### 1. Cleanup Processors

- Add a background processor for expired invitations.
- Add a background processor for expired grants.
- Add a background processor for redundant grants after ancestor grants, moves, and role changes.
- Cleanup should revoke, not hard-delete, expired/redundant grants.

### 2. Audit Read Surface

- Add a tenant-neutral audit query/read API for platform support and admin tooling.
- Decide which audit events should be visible to Brand owners vs SuperAdmins only.
- Decide whether to expose event metadata headers in audit/debug endpoints.

### 3. Owner Lifecycle Completeness

- Add an explicit revoke/downgrade API if owner removal should become a first-class operation instead of piggybacking on grant role change.
- Enforce any future “last owner” flow on that dedicated command if the API surface grows.

### 4. Invitation Enhancements

- Add explicit invitation expiry test coverage once the scheduled processor is exercised deterministically in Alba.
- Decide whether notification retries should stay immediate/local or move to delayed scheduled retries.
- Decide whether invitation emails should use a configured public base URL instead of the current API-relative accept link.
- Decide whether to expose invitation resend/cancel actions in immutable audit read endpoints.

### 5. Testing

- Add Alba coverage for:
  - expired/redundant grant cleanup
  - Brand tombstone behavior
  - audit query endpoints once they exist

## Current Notes

- The org API already runs on Wolverine HTTP handlers; this is no longer an open routing question.
- Tenant resolution remains middleware-based because it must happen before the tenant-scoped Marten session is opened.
- `StoreProfileCreated` and `DeviceProfileCreated` currently append to the same OrgNode stream id. That is acceptable for now; separate profile streams are optional future refactoring, not a blocker.
- Invitation provisioning uses a provider-neutral abstraction:
  - `InMemoryIdentityProviderProvisioning` in Alba/tests
  - `KeycloakIdentityProviderProvisioning` for local/dev
- Mailpit is now wired through Aspire and referenced by the API for local invitation-email testing.
