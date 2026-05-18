# Config Service Implementation Plan

Status: implementation-ready plan
Last updated: 2026-05-17

## 1. Sources And Constraints

This plan is grounded in:

- Current CritCrit API structure: Wolverine HTTP endpoints under `CritCrit.Api/Org/Features/*`, Marten event streams plus inline projections, brand tenant detection, `OrgAuthorizationService`, and immutable audit documents.
- Current CritCrit frontend structure: Vue 3, TanStack Vue Query, generated OpenAPI types, brand/platform layouts, and `CritCrit.Web/DESIGN.md`.
- Local docs in `~/dev/llm-wiki`: Marten indexing, optimistic concurrency, transactional outbox, and greenfield Critter Stack guidance.
- Context7 docs: Marten stream writes/projections/expected-version concurrency, Wolverine Marten outbox, and NJsonSchema validation.
- Frontend-design skill, applied within the existing CritCrit design language rather than replacing it.

Important constraints:

- Do not add API2 work. `CritCrit.AlbaTests` target API2 and are out of scope.
- Keep the main API aligned with current patterns: Wolverine endpoint handlers, Marten sessions, inline projections, audit writer, and tenant sessions.
- V1 prepares for consumers and caching, but does not implement runtime service/device auth, caching, public integration events, or plaintext secret reads.

## 2. Product Decisions

Locked decisions:

- SuperAdmins define global config schemas.
- Schemas have a stable human code and name.
- Schema codes and key codes use lowercase `a-z0-9-`; dots are reserved for lookup path separation.
- Value types in v1: `boolean`, `string`, `integer`, `decimal`, `encrypted-string`, `json-object`, `json-array`.
- JSON object/array keys must include a self-contained JSON Schema. Reject remote refs and local `$ref`.
- Primitive keys support basic constraints: enum, regex, string length, numeric min/max.
- Required keys are out of v1. All keys are optional.
- Keys may define defaults. Defaults live inside published schema versions.
- Schema edits create versions. Assignments are pinned to a published version.
- Multiple named drafts are allowed. Each draft has an ID, name, base published version, status, timestamps, and expected version.
- Drafts must be valid before saving. Publishing requires the draft base version to still be the latest published version.
- Existing assignments stay pinned until explicitly upgraded.
- Assignment upgrade has a preview endpoint before applying.
- Compatible values survive schema upgrades. Removed or type-incompatible values remain stored/history but are ignored by lookup under the new assignment version.
- Schemas can apply to any org node type.
- Assignments enable a schema version for a node subtree. Assignments do not store values.
- Any schema can be assigned at any org node. Values can be set at any org node inside an active assignment subtree.
- If the same schema is assigned at multiple ancestors, nearest active assignment wins and resets the inheritance boundary.
- Per-key nearest value wins inside the active assignment boundary.
- A node value entry can be:
  - `set`: local value wins.
  - `inherit`: no local entry; continue upward.
  - `unset`: local tombstone wins; suppress inherited/default value.
- Full-object lookup returns a composed object for one schema.
- Single-key lookup returns one effective value.
- Pure lookup responses are default. Source/default metadata is opt-in.
- Full-object pure lookups omit encrypted keys.
- Single encrypted-key pure lookups return unavailable until runtime plaintext access exists.
- Metadata lookups show encrypted presence and masked state only.
- Missing/unassigned schema or missing single key returns `404`.
- Full-object lookup for an assigned schema returns `{}` when no non-secret keys resolve.
- Read access requires Viewer+ on the target node.
- Value write access, including encrypted replacement/clear, requires Admin+ on the target node.
- SuperAdmin can operate everywhere.
- Archive/deactivate instead of hard delete for schemas and assignments.
- Audit every schema, assignment, and value mutation with safe diffs.
- Emit Wolverine internal invalidation messages transactionally for v2 consumers/caching. Do not emit public integration events in v1.

## 3. Backend Structure

Add a new Org feature slice:

```text
CritCrit.Api/Org/Features/Config/
  ConfigHandlers.cs
  ConfigAssignmentHandlers.cs
  ConfigSchemaHandlers.cs
  ConfigContracts.cs
  ConfigResolutionService.cs
  ConfigValidationService.cs
  ConfigEncryptionService.cs
  ConfigAudit.cs
  ConfigMessages.cs
  ConfigMessageHandlers.cs

CritCrit.Api/Org/Domain/
  ConfigIds.cs
  ConfigEvents.cs
  ConfigDocuments.cs
  ConfigTypes.cs

CritCrit.Api/Org/Projections/
  ConfigSchemaProjection.cs
  ConfigAssignmentProjection.cs
  ConfigNodeValueProjection.cs

CritCrit.Api/Org/Validators/
  Config*.cs
```

Rationale:

- Keep config in the Org slice because authorization and resolution depend on org nodes.
- Keep feature handlers grouped by product capability, not by CRUD type.
- Keep domain records split from the existing `OrgEvents.cs` once config event volume grows.
- Keep resolution, validation, encryption, and audit as small services so Wolverine endpoint methods stay thin.

Add package:

```xml
<PackageReference Include="NJsonSchema" Version="<latest compatible>" />
```

Use the latest compatible package version chosen by restore/update at implementation time.

## 4. Domain Model

### 4.1 Strong IDs

Add strongly typed IDs following existing `OrgNodeId` style:

```csharp
public readonly record struct ConfigSchemaId(Guid Value)
{
    public static ConfigSchemaId New() => new(Guid.CreateVersion7());
}

public readonly record struct ConfigDraftId(Guid Value)
{
    public static ConfigDraftId New() => new(Guid.CreateVersion7());
}

public readonly record struct ConfigAssignmentId(Guid Value)
{
    public static ConfigAssignmentId New() => new(Guid.CreateVersion7());
}
```

Public identifiers:

- Schema public identity is `code`, not a generated public ID.
- Drafts and assignments use generated IDs in API responses.
- If public prefixes are added later, prefer `cfgdraft_` and `cfgassign_`; do not block v1 on that if raw GUIDs are consistent with internal APIs.

### 4.2 Schema Definition

Core records:

```csharp
public enum ConfigValueType
{
    Boolean,
    String,
    Integer,
    Decimal,
    EncryptedString,
    JsonObject,
    JsonArray
}

public sealed record ConfigSchemaDefinition(
    string Name,
    string? Description,
    IReadOnlyList<ConfigKeyDefinition> Keys);

public sealed record ConfigKeyDefinition(
    string Code,
    string Name,
    string? Description,
    ConfigValueType ValueType,
    ConfigValueConstraints? Constraints,
    string? JsonSchema,
    ConfigDefaultValue? DefaultValue);

public sealed record ConfigValueConstraints(
    IReadOnlyList<string>? Enum,
    string? Regex,
    int? MinLength,
    int? MaxLength,
    decimal? Min,
    decimal? Max);
```

Rules:

- `JsonSchema` is required only for `JsonObject` and `JsonArray`.
- `JsonSchema` is rejected for primitive/encrypted keys.
- `DefaultValue` is not allowed for `EncryptedString` in v1.
- `DefaultValue` must validate against the key type and constraints.
- Duplicate key codes are rejected case-insensitively after normalization.
- Key order is preserved for UI display.

### 4.3 Value Storage

Node values are stored per node plus schema:

```csharp
public enum ConfigValueEntryState
{
    Set,
    Unset
}

public sealed record ConfigValueEntry(
    string KeyCode,
    ConfigValueEntryState State,
    ConfigStoredValue? Value,
    DateTimeOffset UpdatedAt,
    string? UpdatedByExternalId);

public sealed record ConfigStoredValue(
    ConfigValueType ValueType,
    object? Value,
    string? Ciphertext,
    string? ContentHash);
```

Rules:

- Missing entry means inherit.
- `Unset` has no value and suppresses inherited/default value.
- `Set` for encrypted values stores only ciphertext and metadata.
- For non-secret primitive values, store the normalized typed value.
- For JSON values, store normalized JSON and a stable content hash for audit summaries.
- Store raw encrypted plaintext nowhere.

Implementation note:

- If `object? Value` is awkward with Marten serialization, use `JsonElement`/`JsonDocument` consistently for all non-secret values and convert at API boundaries.

## 5. Marten Persistence

### 5.1 Read Models

Global/single-tenanted:

```csharp
public sealed class ConfigSchemaReadModel
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string CodeNormalized { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int? LatestPublishedVersion { get; set; }
    public bool Archived { get; set; }
    public long Version { get; set; }
}

public sealed class ConfigSchemaVersionReadModel
{
    public string Id { get; set; } = ""; // "{code}:{version}"
    public Guid SchemaId { get; set; }
    public string SchemaCode { get; set; } = "";
    public int Version { get; set; }
    public ConfigSchemaDefinition Definition { get; set; } = default!;
    public DateTimeOffset PublishedAt { get; set; }
    public string PublishedByExternalId { get; set; } = "";
}

public sealed class ConfigSchemaDraftReadModel
{
    public Guid Id { get; set; }
    public Guid SchemaId { get; set; }
    public string SchemaCode { get; set; } = "";
    public string Name { get; set; } = "";
    public int? BaseVersion { get; set; }
    public ConfigSchemaDefinition Definition { get; set; } = default!;
    public bool Archived { get; set; }
    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

Multi-tenanted:

```csharp
public sealed class ConfigAssignmentReadModel
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid RootOrgNodeId { get; set; }
    public string RootOrgNodePublicId { get; set; } = "";
    public string SchemaCode { get; set; } = "";
    public int SchemaVersion { get; set; }
    public bool Archived { get; set; }
    public long Version { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
}

public sealed class ConfigNodeValueReadModel
{
    public string Id { get; set; } = ""; // stable "{tenant}:{node}:{schemaCode}"
    public Guid StreamId { get; set; }
    public Guid TenantId { get; set; }
    public Guid OrgNodeId { get; set; }
    public string OrgNodePublicId { get; set; } = "";
    public string SchemaCode { get; set; } = "";
    public Dictionary<string, ConfigValueEntry> Entries { get; set; } = [];
    public long Version { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
```

### 5.2 Store Configuration

Extend `CritCritApiConfiguration.ConfigureDocumentStorage`:

- `ConfigSchemaReadModel`: `SingleTenanted()`, index `CodeNormalized`, unique per store.
- `ConfigSchemaVersionReadModel`: `SingleTenanted()`, index `SchemaCode`, index `Version`.
- `ConfigSchemaDraftReadModel`: `SingleTenanted()`, index `SchemaCode`, index `Archived`.
- `ConfigAssignmentReadModel`: `MultiTenanted()`, index `TenantId`, `RootOrgNodeId`, `SchemaCode`, `SchemaVersion`, `Archived`.
- `ConfigNodeValueReadModel`: `MultiTenanted()`, index `TenantId`, `OrgNodeId`, `SchemaCode`.

Add projections in `ConfigureProjections`:

```csharp
m.Projections.Add<ConfigSchemaProjection>(ProjectionLifecycle.Inline);
m.Projections.Add<ConfigAssignmentProjection>(ProjectionLifecycle.Inline);
m.Projections.Add<ConfigNodeValueProjection>(ProjectionLifecycle.Inline);
```

Use current greenfield Critter Stack settings already present:

- `UseLightweightSessions()`
- `QuickWithServerTimestamps`
- archived stream partitioning
- advanced async tracking
- event skipping
- mandatory stream type declaration
- Wolverine-managed event subscription distribution

### 5.3 Event Streams And Concurrency

Use Marten event streams as the write authority:

- Schema stream: one stream per schema.
- Draft stream: one stream per draft.
- Assignment stream: one stream per assignment.
- Node value stream: one stream per node plus schema. The read model stores the stream ID; the stable document ID is `{tenant}:{node}:{schemaCode}`.

Expected-version behavior:

- All mutation requests include `expectedVersion`.
- For new streams, no `expectedVersion` is required unless the endpoint is updating an existing resource.
- Existing stream writes use Marten expected version or `FetchForWriting` equivalent.
- Map Marten concurrency exceptions to `409 Conflict`.
- Return current version/etag in read responses used by UI mutation forms.

Do not use last-write-wins anywhere in config admin flows.

## 6. Domain Events

Schema events:

```csharp
public sealed record ConfigSchemaCreated(ConfigSchemaId Id, string Code, string CodeNormalized, string Name, string? Description);
public sealed record ConfigSchemaRenamed(ConfigSchemaId Id, string Name, string? Description);
public sealed record ConfigSchemaArchived(ConfigSchemaId Id, string? Reason);
public sealed record ConfigSchemaRestored(ConfigSchemaId Id, string? Reason);

public sealed record ConfigSchemaDraftCreated(ConfigDraftId Id, ConfigSchemaId SchemaId, string SchemaCode, string Name, int? BaseVersion, ConfigSchemaDefinition Definition);
public sealed record ConfigSchemaDraftUpdated(ConfigDraftId Id, ConfigSchemaDefinition Definition);
public sealed record ConfigSchemaDraftRenamed(ConfigDraftId Id, string Name);
public sealed record ConfigSchemaDraftArchived(ConfigDraftId Id, string? Reason);
public sealed record ConfigSchemaVersionPublished(ConfigSchemaId SchemaId, ConfigDraftId DraftId, string SchemaCode, int Version, ConfigSchemaDefinition Definition);
```

Assignment events:

```csharp
public sealed record ConfigSchemaAssigned(ConfigAssignmentId Id, Guid TenantId, OrgNodeId RootOrgNodeId, string SchemaCode, int SchemaVersion);
public sealed record ConfigAssignmentArchived(ConfigAssignmentId Id, string? Reason);
public sealed record ConfigAssignmentRestored(ConfigAssignmentId Id, string? Reason);
public sealed record ConfigAssignmentUpgraded(ConfigAssignmentId Id, string SchemaCode, int OldVersion, int NewVersion);
```

Value events:

```csharp
public sealed record ConfigNodeValueSetInitialized(Guid StreamId, Guid TenantId, OrgNodeId OrgNodeId, string SchemaCode);
public sealed record ConfigNodeValuesPatched(Guid TenantId, OrgNodeId OrgNodeId, string SchemaCode, IReadOnlyList<ConfigValuePatchApplied> Operations);
```

Internal invalidation messages:

```csharp
public enum ConfigChangeKind
{
    SchemaPublished,
    SchemaArchived,
    AssignmentChanged,
    ValuesChanged
}

public sealed record ConfigInvalidationRequested(
    ConfigChangeKind Kind,
    Guid? TenantId,
    Guid? ScopeOrgNodeId,
    string SchemaCode,
    int? SchemaVersion,
    IReadOnlyList<string> AffectedKeys);
```

Rules:

- Draft create/update/archive does not emit invalidation.
- Schema publish/archive, assignment change, and value patch emit invalidation.
- Invalidation messages contain no values.
- Use Wolverine plus Marten outbox so messages are persisted atomically with Marten events.

## 7. Validation

Add `ConfigValidationService` responsibilities:

- Normalize and validate schema codes/key codes.
- Validate schema definitions.
- Validate primitive constraints.
- Validate defaults.
- Validate JSON Schema documents through NJsonSchema.
- Reject `$ref` anywhere in JSON Schema.
- Validate JSON values against the stored JSON Schema.
- Validate patch operations against the assignment-pinned schema version.
- Validate value compatibility during assignment upgrade preview.

NJsonSchema usage:

- Load schemas with `JsonSchema.FromJsonAsync(schemaJson)`.
- Validate values with `schema.Validate(jsonString)`.
- Return validation errors with path and kind in problem details.

JSON Schema self-contained rule:

- Parse the schema JSON before passing to NJsonSchema.
- Reject any object property named `$ref`, regardless of depth.
- Reject schema documents that are not valid JSON objects.
- For `JsonObject`, require schema root type compatible with object.
- For `JsonArray`, require schema root type compatible with array.

Primitive constraints:

- `Enum` is allowed for string/integer/decimal/boolean after type-normalization.
- `Regex`, `MinLength`, `MaxLength` apply only to string and encrypted-string input shape. Encrypted values validate before encryption.
- `Min`, `Max` apply only to integer/decimal.
- Decimal values use invariant culture and JSON number serialization.

## 8. Encryption

Add `ConfigEncryptionService`:

```csharp
public interface IConfigEncryptionService
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
}
```

Implementation:

- Inject `IDataProtectionProvider`.
- Use a stable purpose string: `CritCrit.Org.Config.EncryptedValue.v1`.
- Store only ciphertext.
- Do not expose `Unprotect` through public lookup endpoints in v1.

Operational requirement:

- Ensure Data Protection keys are persistent in non-dev deployments before encrypted values are used for real secrets. If keys are ephemeral, encrypted config becomes unrecoverable after key loss.

API/UI behavior:

- Metadata reads expose:
  - `encrypted: true`
  - `hasValue: true|false`
  - `maskedValue: "********"` when present
- Pure full-object lookup omits encrypted keys.
- Pure single encrypted-key lookup returns `404` or `403`-style unavailable. Use `404` for consistency with unavailable values unless product later needs distinguishable secret-not-readable semantics.
- Patch write semantics:
  - `set` with plaintext replaces the secret.
  - `inherit` clears local secret entry.
  - `unset` suppresses inherited/default secret presence.
  - blank omitted secret fields in UI preserve current entry.

## 9. Resolution Algorithm

Inputs:

- `tenant`
- `targetNodeId`
- `path`: either `schemaCode` or `schemaCode.keyCode`
- `includeMetadata`
- `includeSecretsPlaintext = false` in v1

Algorithm:

1. Parse path. Reject invalid code segments.
2. Load target org node in tenant session.
3. Return `404` if node does not exist, is hard-deleted, or is effectively archived.
4. Enforce Viewer+ unless actor is SuperAdmin.
5. Build ordered path IDs from assignment root candidates:
   - `root-to-target = target.AncestorIds + target.Id`.
6. Query active assignments for:
   - current tenant
   - schema code
   - root node in `root-to-target`
   - not archived
7. Pick the assignment whose root node is deepest in `root-to-target`.
8. If no assignment exists, return `404`.
9. Load assigned schema version. If schema or version is missing/archived, return `404`.
10. Define the inheritance boundary as assignment root through target.
11. Query value sets for the target schema where node ID is inside that boundary.
12. For each key in schema order:
    - Walk nodes from target up to assignment root.
    - First `Set` wins.
    - First `Unset` wins and suppresses default.
    - No entry means continue upward.
    - If no entry exists in boundary, use schema default if defined.
    - Otherwise key is missing.
13. For full-object pure lookup:
    - Include non-secret keys that resolved to a value.
    - Omit missing/unset keys.
    - Omit encrypted keys.
14. For single-key pure lookup:
    - Return resolved non-secret value.
    - Return `404` for missing/unset/encrypted keys.
15. For metadata lookup:
    - Return an envelope with assignment, schema version, value states, source node IDs/public IDs, defaults, unset state, and encrypted presence.

Metadata response shape:

```json
{
  "schemaCode": "posbridgesettings",
  "schemaVersion": 3,
  "nodeId": "store_...",
  "assignment": {
    "id": "...",
    "rootOrgNodeId": "brand_...",
    "schemaCode": "posbridgesettings",
    "schemaVersion": 3
  },
  "valueSetVersion": 12,
  "values": {
    "usetaxcalc": {
      "state": "set",
      "value": true,
      "source": "store_...",
      "encrypted": false
    },
    "connection-string": {
      "state": "set",
      "source": "brand_...",
      "encrypted": true,
      "hasValue": true,
      "maskedValue": "********"
    },
    "menuname": {
      "state": "default",
      "value": "main",
      "source": null,
      "encrypted": false
    }
  }
}
```

## 10. HTTP API

All endpoints are Wolverine HTTP endpoints.

### 10.1 Platform Schema Endpoints

SuperAdmin only.

```text
GET  /api/platform/config-schemas?includeArchived=false
POST /api/platform/config-schemas
GET  /api/platform/config-schemas/{schemaCode}
POST /api/platform/config-schemas/{schemaCode}/archive
POST /api/platform/config-schemas/{schemaCode}/restore

GET  /api/platform/config-schemas/{schemaCode}/versions
GET  /api/platform/config-schemas/{schemaCode}/versions/{version}

GET  /api/platform/config-schemas/{schemaCode}/drafts
POST /api/platform/config-schemas/{schemaCode}/drafts
GET  /api/platform/config-schemas/{schemaCode}/drafts/{draftId}
PUT  /api/platform/config-schemas/{schemaCode}/drafts/{draftId}
POST /api/platform/config-schemas/{schemaCode}/drafts/{draftId}/archive
POST /api/platform/config-schemas/{schemaCode}/drafts/{draftId}/publish
```

Requests:

```csharp
public sealed record CreateConfigSchemaRequest(
    string Code,
    string Name,
    string? Description,
    string DraftName,
    ConfigSchemaDefinition Definition);

public sealed record CreateConfigSchemaDraftRequest(
    string Name,
    int? BaseVersion,
    ConfigSchemaDefinition Definition);

public sealed record UpdateConfigSchemaDraftRequest(
    long ExpectedVersion,
    string? Name,
    ConfigSchemaDefinition Definition);

public sealed record PublishConfigSchemaDraftRequest(
    long ExpectedVersion,
    string? Reason);
```

Behavior:

- Creating a schema creates the schema and an initial valid draft; it does not publish automatically.
- Publishing the first draft creates version `1`.
- Creating a new draft without `BaseVersion` uses latest published version.
- If no version exists yet, `BaseVersion` must be null.
- Publishing a draft whose base version is not latest returns `409`.

### 10.2 Assignment Endpoints

SuperAdmin only for mutations. Viewer+/Admin UI may read effective assignment metadata through config metadata endpoints.

```text
GET  /api/brands/{brandId}/org-nodes/{nodeId}/config-assignments?includeArchived=false
POST /api/brands/{brandId}/org-nodes/{nodeId}/config-assignments
POST /api/brands/{brandId}/org-nodes/{nodeId}/config-assignments/{assignmentId}/archive
POST /api/brands/{brandId}/org-nodes/{nodeId}/config-assignments/{assignmentId}/restore
POST /api/brands/{brandId}/org-nodes/{nodeId}/config-assignments/{assignmentId}/upgrade-preview
POST /api/brands/{brandId}/org-nodes/{nodeId}/config-assignments/{assignmentId}/upgrade
```

Requests:

```csharp
public sealed record AssignConfigSchemaRequest(
    string SchemaCode,
    int SchemaVersion,
    string? Reason);

public sealed record ArchiveConfigAssignmentRequest(
    long ExpectedVersion,
    string? Reason);

public sealed record UpgradeConfigAssignmentRequest(
    long ExpectedVersion,
    int TargetSchemaVersion,
    string? Reason);
```

Behavior:

- Reject assignment if schema/version is archived or missing.
- Reject more than one active assignment for the same tenant/root node/schema.
- Allow assigning the same schema at different ancestors/descendants.
- Upgrade preview returns:
  - compatible keys
  - removed keys
  - type-incompatible keys
  - local value counts impacted inside assignment subtree
  - whether upgrade is publishable
- Upgrade apply does not delete stored values.

### 10.3 Lookup And Value Endpoints

Viewer+ reads, Admin+ writes.

```text
GET   /api/brands/{brandId}/org-nodes/{nodeId}/config
GET   /api/brands/{brandId}/org-nodes/{nodeId}/config/{path}?includeMetadata=false
PATCH /api/brands/{brandId}/org-nodes/{nodeId}/config/{schemaCode}
```

`GET /config` returns effective assigned schemas for the node so the UI can render available editors:

```json
[
  {
    "schemaCode": "posbridgesettings",
    "schemaName": "POS Bridge Settings",
    "schemaVersion": 3,
    "assignmentId": "...",
    "assignmentRootOrgNodeId": "brand_...",
    "valueSetVersion": 7
  }
]
```

Patch request:

```csharp
public sealed record PatchConfigValuesRequest(
    long ExpectedVersion,
    IReadOnlyList<ConfigValuePatchOperation> Operations,
    string? Reason);

public sealed record ConfigValuePatchOperation(
    string KeyCode,
    ConfigValuePatchOperationKind Operation,
    object? Value);

public enum ConfigValuePatchOperationKind
{
    Set,
    Inherit,
    Unset
}
```

Behavior:

- `Set` validates the value against the assigned schema version.
- `Set` for `EncryptedString` accepts plaintext and stores ciphertext.
- `Inherit` removes the local entry so lookup walks upward.
- `Unset` writes a tombstone that suppresses inherited/default values.
- Patch is atomic for one node plus one schema.
- Unknown key, incompatible value, unassigned schema, archived assignment, or inactive node returns a problem response.
- Stale `ExpectedVersion` returns `409`.

## 11. Authorization

Use existing `OrgAuthorizationService`.

Rules:

- Platform schema endpoints: `EnforceSuperAdmin`.
- Assignment mutation endpoints: `EnforceSuperAdmin`.
- Config list/lookup endpoint:
  - SuperAdmin allowed.
  - Otherwise `EnforceRoleAsync(session, actor, targetNode, OrgRole.Viewer, ct)`.
- Config value patch endpoint:
  - SuperAdmin allowed.
  - Otherwise `EnforceRoleAsync(session, actor, targetNode, OrgRole.Admin, ct)`.

No new roles or permissions are required in v1, but add explicit permission constants if the project wants auditability in `OrgPermissions`:

- `ConfigRead`
- `ConfigWrite`
- `ConfigManageSchemas`
- `ConfigManageAssignments`

Recommended v1 mapping:

- Viewer+: `ConfigRead`
- Admin+: `ConfigWrite`
- SuperAdmin only: schema/assignment management

## 12. Audit

Add `AuditCategories.Config = "config"`.

Add action constants:

```csharp
config.schema.created
config.schema.archived
config.schema.restored
config.schema.draft.created
config.schema.draft.updated
config.schema.draft.archived
config.schema.version.published
config.assignment.created
config.assignment.archived
config.assignment.restored
config.assignment.upgraded
config.values.patched
```

Audit payload rules:

- Include schema code/version and draft/assignment IDs in details.
- Include tenant and target org node when applicable.
- Include safe field changes:
  - Primitive non-secret values: old/new values allowed.
  - Encrypted values: old/new become `"********"` or presence booleans.
  - JSON values: do not store full JSON. Store content hash, approximate byte size, and changed key path summary if easily available.
- Include patch operation kinds and key codes.
- Do not store plaintext secrets in audit.
- Do not store full JSON payload bodies in audit.

Frontend audit behavior:

- Existing platform/brand audit pages must show config events through current filters.
- Node config panel also shows recent config-related events for that node by querying brand audit with `category=config` and `targetOrgNodeId`.

## 13. Wolverine Messaging

Use the Marten outbox for internal invalidation messages.

Recommended handler pattern:

- In mutation endpoints, append Marten events and enroll `ConfigInvalidationRequested` with `IMartenOutbox`.
- Call `SaveChangesAsync` once.
- Do not publish invalidation messages before validation succeeds.
- Do not manually dispatch messages outside the transaction.

Message consumers in v1:

- Add a no-op or logging handler for `ConfigInvalidationRequested` so tracking tests can assert delivery.
- Handler must not mutate config state.
- Handler can emit structured logs:
  - `ConfigInvalidationRequested`
  - `TenantId`
  - `ScopeOrgNodeId`
  - `SchemaCode`
  - `SchemaVersion`
  - `AffectedKeys.Count`

V2 preparation:

- The payload is sufficient for targeted cache invalidation:
  - schema publish/archive invalidates schema/version cache by code/version.
  - assignment change invalidates effective assignment cache for scope subtree.
  - value change invalidates node/schema effective value cache for node descendants inside current assignment boundary.
- Do not include actual values in invalidation messages.

## 14. Frontend Plan

Follow `CritCrit.Web/DESIGN.md`:

- Dense tables, no decorative gradients.
- IBM Plex Sans and Plex Mono.
- Single cyan accent.
- Hairline borders.
- 13px default UI text.
- No large animation beyond existing low-motion conventions.
- Lucide icons only.
- Copy affordances only where IDs/codes need precision.

### 14.1 Platform Config Page

Add route:

```text
/platform/config
```

Update `PlatformLayout.vue` nav:

- Subjects
- Config
- Audit

Page layout:

- Left/main table: schemas.
- Right inspector or detail section: selected schema versions/drafts.
- Use compact tabs or segmented buttons:
  - Published versions
  - Drafts
  - Assignments

Schema table columns:

- Code, mono
- Name
- Latest version
- Draft count
- Archived badge
- Updated/published timestamp
- Actions

Draft editor:

- Structured fields for schema name/description.
- Key table with add/remove/reorder.
- Per-key editor:
  - code
  - name
  - description
  - type
  - default
  - constraints
  - JSON Schema editor for JSON keys
- JSON Schema editor is a raw textarea in v1 with server validation feedback.
- Show validation errors as exact server messages with paths.

Assignment UI:

- SuperAdmin can assign schema version to a node.
- Use brand/node selector from existing brand tree data where possible.
- Show active assignments and archived assignments.
- Upgrade flow:
  - Select target version.
  - Run preview.
  - Show compatible/ignored/incompatible summary.
  - Confirm upgrade.

### 14.2 Brand Tree Integrated Config

Keep config editing inside the existing tree workflow.

Update `Tree.vue` selected-node panel:

- Add a compact `Config` section below node details/actions.
- Query `GET /config` for effective schemas on selected node.
- Show schema rows with:
  - schema code
  - version
  - assignment source
  - local value status
  - stale/conflict state
- Selecting a schema opens a dense key editor.

Key editor states:

- Effective value display.
- Source label: local, inherited from ancestor, default, unset, missing.
- For inherited values, show source node public ID in mono.
- For encrypted keys, show only `has value` and masked state.
- Actions per key:
  - Set local value
  - Inherit
  - Unset
  - Replace secret
  - Clear local secret via inherit or unset

Avoid whole-object replace UX:

- UI builds a schema-level patch with explicit operations.
- Secret fields left blank do not create operations.
- Only changed rows generate operations.

Conflict UX:

- If patch returns `409`, show exact error and refresh metadata.
- Do not auto-merge secret changes.
- Preserve unsaved draft form state locally if possible.

### 14.3 Frontend API Layer

Update generated OpenAPI types after backend endpoints exist.

Add query keys:

```ts
configSchemas
configSchema(schemaCode)
configDrafts(schemaCode)
configAssignments(brandId, nodeId)
nodeConfigSchemas(brandId, nodeId)
nodeConfig(brandId, nodeId, path, includeMetadata)
```

Add query/mutation hooks:

- `useConfigSchemas`
- `useConfigSchema`
- `useCreateConfigSchema`
- `useCreateConfigDraft`
- `useUpdateConfigDraft`
- `usePublishConfigDraft`
- `useArchiveConfigSchema`
- `useNodeConfigSchemas`
- `useNodeConfig`
- `usePatchNodeConfig`
- `useConfigAssignments`
- `useAssignConfigSchema`
- `useArchiveConfigAssignment`
- `useUpgradeConfigAssignmentPreview`
- `useUpgradeConfigAssignment`

Invalidation:

- Schema mutation invalidates schema lists, schema details, drafts, versions.
- Assignment mutation invalidates assignments and selected node config.
- Value patch invalidates selected node config and brand audit query.
- Keep tree query invalidation only when necessary; config value changes should not reload the whole org tree.

## 15. Implementation Phases

### Phase 1: Backend Domain And Storage

1. Add config domain records, events, read models, IDs, and enums.
2. Add NJsonSchema package.
3. Register Marten document storage and projections.
4. Add validation service for codes, schema definition, constraints, JSON Schema, and values.
5. Add encryption service using Data Protection.
6. Add unit tests for validation and encryption.

Exit criteria:

- Project builds.
- Unit tests cover schema/value validation and encryption roundtrip.
- Marten database changes apply on startup.

### Phase 2: Schema/Draft APIs

1. Add platform schema endpoints.
2. Implement draft create/update/archive/publish.
3. Implement expected-version concurrency.
4. Implement audit for schema/draft/publish.
5. Add integration tests for schema lifecycle.

Exit criteria:

- SuperAdmin can create schema, create multiple drafts, update drafts, publish version 1, publish version N.
- Stale draft publish returns `409`.
- Non-SuperAdmin access is denied.

### Phase 3: Assignment APIs

1. Add assignment endpoints under brand node route.
2. Validate tenant, node, schema version, and duplicate active assignment.
3. Implement archive/restore.
4. Implement upgrade preview and upgrade.
5. Emit transactional invalidation on assignment changes.
6. Add integration tests.

Exit criteria:

- SuperAdmin can assign a schema version to any active node.
- Nearest assignment can be nested below another assignment for same schema.
- Upgrade preview reports compatible/ignored/incompatible values.
- Assignment changes produce audit and outbox-tracked invalidation.

### Phase 4: Resolution And Value Patch APIs

1. Implement `ConfigResolutionService`.
2. Add `GET /config`, `GET /config/{path}`, and `PATCH /config/{schemaCode}`.
3. Implement per-key nearest-wins, defaults, explicit unset, assignment boundary reset, and encrypted omission/masking.
4. Implement expected-version concurrency for node-schema value patches.
5. Emit audit and invalidation for value changes.
6. Add integration tests for resolution behavior.

Exit criteria:

- `GET .../config/posbridgesettings` returns composed object.
- `GET .../config/posbridgesettings.usetaxcalc` returns scalar value.
- `includeMetadata=true` returns source/default/encrypted metadata.
- Admin+ can patch values.
- Viewer can read but not patch.
- Missing/unassigned single values return `404`.

### Phase 5: Frontend

1. Regenerate OpenAPI types.
2. Add API query/mutation hooks.
3. Add platform Config route/page and nav item.
4. Add schema/draft editor and JSON Schema validation feedback.
5. Add assignment management UI.
6. Add tree-integrated node config panel.
7. Add node config audit slice.
8. Add frontend tests.

Exit criteria:

- SuperAdmin can define schema drafts, publish versions, assign versions, and upgrade assignments from UI.
- Brand Admin can set/inherit/unset node values in the tree panel.
- Encrypted fields behave as write-only replacements.
- UI follows `DESIGN.md` density, typography, color, and motion rules.

### Phase 6: Observability And Hardening

1. Add structured logs for config validation failures, concurrency conflicts, and invalidation handling.
2. Ensure config audit events are filterable by category/action.
3. Add tests for outbox transactional delivery and rollback behavior for config invalidation messages.
4. Run full backend and frontend test suites.
5. Document operational requirement for persistent Data Protection keys.

Exit criteria:

- Aspire dashboard still shows normal API/Marten/Wolverine traces and metrics.
- Config invalidation is visible in Wolverine tracking tests.
- No secret plaintext appears in audit/logs/messages.

## 16. Test Plan

### Unit Tests: `CritCrit.UnitTests`

Add:

- `ConfigCodeTests`
  - valid schema/key codes
  - invalid dots/uppercase/empty/illegal chars
- `ConfigSchemaValidationTests`
  - duplicate keys rejected
  - JSON keys require schema
  - primitive keys reject JSON schema
  - `$ref` rejected at root and nested levels
  - invalid default rejected
  - primitive constraints enforced
- `ConfigValueValidationTests`
  - booleans, strings, integers, decimals normalize correctly
  - JSON object/array values validate against schema
  - validation errors include paths
- `ConfigResolutionTests`
  - nearest key wins
  - explicit unset suppresses inherited/default
  - default used when no value exists
  - nearest assignment resets boundary
  - encrypted keys omitted from pure lookup
- `ConfigEncryptionTests`
  - protect/unprotect roundtrip
  - ciphertext differs from plaintext
  - wrong purpose cannot unprotect

### Integration Tests: `CritCrit.Test`

Add:

- `ConfigSchemaLifecycleTests`
  - SuperAdmin create schema with valid draft
  - publish first and later versions
  - multiple named drafts
  - stale draft publish returns `409`
  - archived schema does not resolve
- `ConfigAssignmentTests`
  - assign schema to brand/country/franchise/store/device
  - duplicate active same schema/root rejected
  - nearest assignment wins
  - archived assignment ignored
  - upgrade preview and apply retain compatible values
- `ConfigResolutionEndpointTests`
  - full object lookup
  - single key lookup
  - defaults
  - unset tombstone
  - metadata response source nodes
  - missing key returns `404`
  - unassigned schema returns `404`
- `ConfigValuePatchTests`
  - Admin+ set/inherit/unset
  - Viewer denied patch
  - stale expected version returns `409`
  - invalid value returns problem details
  - encrypted value write stores ciphertext and pure lookup omits it
- `ConfigAuditTests`
  - schema, assignment, and value changes write audit
  - encrypted audit masks values
  - JSON audit stores summary not full payload
- `ConfigOutboxTests`
  - schema publish emits invalidation after commit
  - assignment change emits invalidation after commit
  - value patch emits invalidation after commit
  - rollback prevents invalidation delivery

Do not add config tests to `CritCrit.AlbaTests`.

### Frontend Tests: `CritCrit.Web`

Add Vitest coverage for:

- API hooks unwrap config responses and support IDs.
- Schema form validates code/key names.
- JSON Schema editor maps server validation errors.
- Node config patch builder emits only explicit changed operations.
- Secret blank field preserves existing secret.
- Secret replace emits `set`.
- Inherit/unset operations are distinct.
- Conflict response path displays exact server error.

## 17. Rollout And Migration

Database:

- Marten `ApplyAllDatabaseChangesOnStartup()` will create new document tables/indexes in dev.
- Review generated SQL before production rollout if this becomes production-bound.
- Existing org/audit data is unaffected.

Feature rollout:

1. Ship backend schema/draft APIs first behind SuperAdmin auth.
2. Add assignments with no UI value editing yet.
3. Add lookup/value endpoints and tests.
4. Add frontend schema/assignment UI.
5. Add tree-integrated value editor.
6. Validate audit and outbox behavior.

Operational checks:

- Persistent Data Protection key storage configured before real encrypted config values are created.
- Marten schema changes applied successfully.
- Wolverine outbox tables healthy.
- Audit volume acceptable for JSON summary payloads.
- No plaintext secret values in logs, audit, traces, or messages.

## 18. Non-Goals For V1

- Service-account config consumers.
- Device-auth config consumers.
- Plaintext encrypted-value read endpoint.
- Cache or precomputed effective config read models.
- Batch config lookup.
- Public integration events.
- Deep JSON path lookup.
- Required keys.
- Node-type restricted schemas.
- Hard delete of schemas/assignments/values.
- Full JSON body audit diffs.

## 19. V2 Hooks Already Prepared

V1 should intentionally leave these seams:

- `ConfigInvalidationRequested` already contains enough metadata for cache invalidation.
- Resolution service is a single seam where cache can be introduced later.
- Assignment boundary logic is explicit, so subtree invalidation can be derived later.
- Encrypted values are stored using a service abstraction, so future key management or vault integration can replace Data Protection.
- Existing actor-only runtime auth can be expanded with service accounts/device identity without changing schema/value storage.
- Public integration events can be mapped from internal invalidation messages later, after consumer contracts are known.

