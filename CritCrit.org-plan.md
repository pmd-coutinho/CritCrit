# CritCrit Org Hierarchy Plan

## Goal

Model a brand-scoped org hierarchy on Marten with roles, inheritance, invitations, and auditable destructive operations.

## What Is Already Implemented

- Brand is the tenant root.
- Brand public id is the Marten/Wolverine tenant id.
- Org nodes use typed UUIDv7-based public ids.
- Code-defined hierarchy rules exist.
- Fixed roles exist: `Owner`, `Admin`, `Member`, `Viewer`.
- Subject provisioning exists as internal identity records.
- Direct grants exist with one role per subject per node.
- Redundant grant creation is rejected.
- Alba tests cover the initial happy path and core hierarchy/access invariants.

## Current Domain Decisions

- Tree only, one parent per non-root node.
- Downward inheritance is additive.
- Effective authorization uses the highest effective role.
- `Owner` is root Brand only.
- `SuperAdmin` comes from the identity provider live.
- `Brand` archive/hard-delete rules are sensitive operations and require audit.
- `Country` and `Franchise` are plain org nodes for now.
- `Store` and `Device` are special node types with required operational data.
- `Device` nodes are terminal.
- `Country` codes use ISO alpha-2.
- All org codes are tenant-wide unique, immutable, and normalized case-insensitively.

## Remaining Work

### 1. Replace Middleware Edge With Cleaner Routing

- Decide whether to keep the current middleware-based API edge or convert org routes back to Wolverine HTTP handlers.
- If Wolverine HTTP is kept, make response and DI binding explicit and testable under Alba.
- If middleware stays, remove unused Wolverine endpoint artifacts and keep the org API surface centralized.

### 2. Org Lifecycle Commands

- Implement `MoveOrgNode`.
- Implement `ArchiveOrgNode` with force-confirmed cascading behavior.
- Implement `RestoreOrgNode`.
- Implement app-level hard delete for org subtrees.
- Add explicit handling for Brand root archive/restore/hard delete.

### 3. Profile Lifecycle

- Decide the final write-model strategy for `StoreProfile` and `DeviceProfile`.
- Reintroduce separate event streams only if stream identity strategy supports it cleanly.
- Keep profile lifecycle subordinate to OrgNode lifecycle.
- Add profile archive/hard-delete behavior to match org subtree lifecycle.

### 4. Invitations

- Add invitation saga for onboarding.
- Invitation should target exactly one node and one role.
- Invitation should provision the IDP user if needed.
- Invitation should create internal `Subject` and `ExternalIdentityRef`.
- Invitation acceptance should require IDP auth.
- Invitation should grant access immediately on acceptance.
- Invitation expiry should be 1 day.
- Invite emails should be sent via a separate notification message/component.

### 5. Cleanup Processors

- Add a background processor for expired invitations.
- Add a background processor for expired grants.
- Add a background processor for redundant grants after ancestor grants, moves, and role changes.
- Cleanup should revoke, not hard-delete, expired/redundant grants.

### 6. Audit

- Add tenant-neutral immutable audit log.
- Persist sensitive operations there:
  - hard delete
  - forced cascade archive
  - Brand archive/restore
  - Owner grant/revoke
  - move operations
- Include actor metadata on normal domain events too.

### 7. Read Models

- Materialize ancestor lists and code-based paths.
- Keep explicit `Archived` and `EffectiveArchived`.
- Keep `HardDeleted` flags in projections.
- Add lookup/projection support for tenant-wide code uniqueness and subject identity resolution.

### 8. Tests

- Expand Alba coverage to:
  - move validation
  - archive/restore behavior
  - hard delete authorization
  - invitation flow
  - expired/redundant grant cleanup
  - Brand tombstone behavior
- Keep tests running against the real API with Testcontainers Postgres.

## Open Design Question

- Whether the org API should remain middleware-driven or be moved back onto Wolverine HTTP handlers.
- Current implementation uses middleware because it was the fastest path to deterministic Alba-tested behavior.

