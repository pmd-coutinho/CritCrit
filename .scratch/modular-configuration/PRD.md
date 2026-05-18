# Modular Configuration + Ancillary Stores

Status: design-locked
Triage: ready-for-human
Driver: improve-codebase-architecture run on master, 2026-05-18

## Goal

Restructure `CritCrit.Api/Configuration/CritCritApiConfiguration.cs` (490 LOC) so wiring matches the documented context map: each feature module owns its schemas, projections, services, and (per Q6.4) its own Marten ancillary store. Centralise resource-name strings in `CritCrit.ServiceDefaults` so AppHost and API stop hard-coding them in parallel.

## Why

- `CONTEXT-MAP.md` declares contexts (`org`, `platform`, `observability`, `infra`); code doesn't honour them. Adding a feature touches the central god-config.
- `ConfigureDocumentStorage` (90 LOC, 16 schemas) and `ConfigureProjections` (16 inline registrations) are the biggest concentration of cross-feature knowledge in the codebase.
- `AddOrgModule` bundles 6 unrelated concerns (Org auth, Config service, Assets service, IdP, email, encryption).
- AppHost and API both hard-code `"marten"`, `"rabbitmq"`, `"blobs"`, etc. Renames bite both sides simultaneously.

## Decisions (locked)

| # | Question | Decision |
|---|----------|----------|
| 6.1 | Module granularity | (c) Two-tier — context modules call feature submodules |
| 6.2 | Schema + projection registration | (b) Each feature module owns `ConfigureMarten(StoreOptions)` callback; Marten merges via DI |
| 6.3 | AppHost split | (c) Shared resource-name contracts in `CritCrit.ServiceDefaults` consumed by both AppHost and API |
| 6.4 | Ancillary stores | (c) Full per-module split with shared envelope schema (`MessageStorageSchemaName = "wolverine"`) — accept that cross-store queries must move behind Wolverine messages or read-time joins |
| 6.5 | Pilot order | (a) Assets first, then (b) Config |

## Out of scope

- Splitting the physical Postgres database (all stores share one Postgres instance; only schemas differ)
- Microservices extraction (ancillary stores are the *preparation* for that future, not the conversion itself)
- Renaming existing schema tables (the documents stay where they are; only registration code moves)

## Prerequisite work (must land before ancillary-store split)

Q6.4 (c) creates real consequences. Cross-context references in current code:

1. `SubjectBrandAccessProjection` reads `Subject*` events and `OrgAccess*` events together. Must become either:
   - A multi-stream projection inside one store with copies of both event types (via Wolverine `RaiseSideEffects` re-emitting on the consumer store), OR
   - A read-time SQL join across schemas (Marten supports cross-schema queries on the same Postgres).
2. `InvitationWorkflow` and the new `IdentityProvisioningHandler` (candidate #4) load `SubjectReadModel` + `ExternalIdentityReadModel`. These are in the Subjects/Identity store; Invitations needs cross-store read API.
3. `AuditLogProjection` (candidate #1) consumes events from every domain context. Must subscribe to multiple stores.

Mitigation: stay on single store for pilot (`AddAssetsModule`), and only commit to the per-module ancillary split *after* candidate #1 + candidate #4 land (which is when the cross-store reads crystallise).

## Two-tier module shape

```
CritCritApiConfiguration.AddCritCritApiServices(builder)
├── AddPlatformContext(builder)
│   ├── AddPlatformMarten              (Marten core, envelope schema, conjoined tenancy detection)
│   ├── AddPlatformMessaging           (Wolverine core, RabbitMQ, durability)
│   ├── AddPlatformAuth                (Keycloak JwtBearer / Test scheme)
│   └── AddPlatformErrors              (DomainExceptionMiddleware, ProblemDetails wiring)
├── AddOrgContext(builder)
│   ├── AddOrgFeature                  (OrgNode, Brand, AccessGrant, Subject, Owner)
│   ├── AddConfigFeature               (ConfigSchema, ConfigAssignment, ConfigNodeValue + encryption + resolution)
│   ├── AddAssetsFeature               (AssetNodeValue + BlobContainerClient + IAssetStorage)
│   └── AddInvitationsFeature          (Invitation + IdP + email sender)
├── AddObservabilityContext(builder)
│   ├── AddObservabilityAudit          (AuditLogProjection — own ancillary store)
│   ├── AddObservabilityLogging        (structured logging + SupportId)
│   ├── AddObservabilityTelemetry      (OpenTelemetry exporters)
│   └── AddObservabilitySupport        (SupportIdMiddleware)
└── AddInfraContext(builder)            (CORS, OpenAPI, Scalar)
```

Each `*Feature` extension method registers:
- Its services (`services.AddScoped<...>`)
- Its Marten store via `services.AddMartenStore<IFooStore>(opts => {...}).IntegrateWithWolverine()`
- Its projections inside that store's options
- Its document schema declarations inside that store's options

## Shared resource names

New file `CritCrit.ServiceDefaults/ResourceNames.cs`:

```csharp
public static class ResourceNames
{
    public const string MartenConnection = "marten";
    public const string RabbitMqConnection = "rabbitmq";
    public const string KeycloakService = "keycloak";
    public const string BlobsConnection = "blobs";
    public const string AssetsConnection = "assets";          // legacy fallback
    public const string StorageConnection = "storage";        // legacy fallback
    public const string MailpitService = "mailpit";

    // Marten schema names — one per ancillary store
    public static class Schemas
    {
        public const string Wolverine = "wolverine";           // shared envelope storage
        public const string Org = "org";
        public const string Config = "config";
        public const string Assets = "assets";
        public const string Invitations = "invitations";
        public const string Audit = "audit";
    }
}
```

AppHost references `ResourceNames.MartenConnection` instead of `"marten"` literal. API features reference the same constants. Renames flow through compiler.

## Ancillary store pattern (illustrated for Assets)

```csharp
public interface IAssetsStore : IDocumentStore { }

public static IServiceCollection AddAssetsFeature(this IServiceCollection services, IConfiguration configuration)
{
    services.AddMartenStore<IAssetsStore>(m =>
        {
            m.Connection(configuration.GetConnectionString(ResourceNames.MartenConnection)!);
            m.DatabaseSchemaName = ResourceNames.Schemas.Assets;

            m.Schema.For<AssetNodeValueReadModel>()
                .MultiTenanted()
                .Index(x => x.TenantId)
                .Index(x => x.OrgNodeId);

            m.Projections.Add<AssetNodeValueProjection>(ProjectionLifecycle.Inline);
        })
        .IntegrateWithWolverine();

    services.AddSingleton(sp => /* BlobContainerClient construction */);
    services.AddSingleton<IAssetStorage, BlobAssetStorage>();
    services.AddScoped<AssetResolutionService>();
    return services;
}
```

Handlers in `Org/Features/Assets/AssetHandlers.cs` get tagged with `[MartenStore(typeof(IAssetsStore))]` so Wolverine routes to the right store.

`opts.Durability.MessageStorageSchemaName = ResourceNames.Schemas.Wolverine;` is set once in `AddPlatformMessaging` so all stores share the envelope schema (required for cross-store outbox).

## Pilot — `AddAssetsFeature`

Smallest scope. Self-contained.

### File deltas

- Create `CritCrit.ServiceDefaults/ResourceNames.cs`
- Create `CritCrit.Api/Org/Features/Assets/AssetsFeature.cs` — owns `AddAssetsFeature` extension method, schema, projection registration
- Edit `CritCrit.Api/Configuration/CritCritApiConfiguration.cs`:
  - Remove `AssetNodeValueReadModel` schema declaration (line 228–231)
  - Remove `m.Projections.Add<AssetNodeValueProjection>(...)` (line 272)
  - Remove BlobContainerClient + `IAssetStorage` + `AssetResolutionService` from `AddOrgModule` (lines 329–346)
  - Call `services.AddAssetsFeature(configuration)` from `AddCritCritApiServices`
- Edit `CritCrit.AppHost/AppHost.cs` to reference `ResourceNames.BlobsConnection`, `ResourceNames.AssetsConnection`, `ResourceNames.StorageConnection`
- Add `[MartenStore(typeof(IAssetsStore))]` to `AssetHandlers` class

### Acceptance

- `dotnet build` clean
- All `CritCrit.AlbaTests/Assets/*` contract tests green
- `AssetNodeValueReadModel` lives in `assets.mt_doc_*` tables, not `public.mt_doc_*`
- No string literal `"marten"`, `"blobs"`, `"assets"`, `"storage"` anywhere in `Configuration/` or `AppHost/`

## ADR opportunities

Open ADRs after the pilot proves out:

1. **ADR-0002 — Ancillary store per feature module.** Records that we accept cross-store query restrictions in exchange for schema-level isolation and modular-monolith → microservices optionality. Includes the cross-store reads list as known consequences.
2. **ADR-0003 — Resource-name contracts in ServiceDefaults.** Records that string literals for Aspire resource references are forbidden in both AppHost and feature wiring.

## Issues

01. `ResourceNames.cs` in ServiceDefaults + AppHost rewrite to consume it
02. `AddAssetsFeature` pilot (single-store, Marten schema move)
03. `AddConfigFeature` migration (3 projections, 5 schemas, encryption service)
04. `AddInvitationsFeature` migration (depends on `.scratch/invitation-aggregate-workflow/`)
05. `AddOrgFeature` migration (largest)
06. `AddObservabilityAudit` ancillary store (depends on `.scratch/aggregate-handler-workflow/` AuditLogProjection landing)
07. `AddPlatformContext` consolidation (Marten core + Wolverine + auth)
08. ADR-0002, ADR-0003 write-up
09. Cross-store read patterns documented (for SubjectBrandAccess, Invitation → Subject lookup, audit → all)
