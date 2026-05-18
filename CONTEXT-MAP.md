# Context Map

This repo uses a multi-context domain layout. Each context has its own `CONTEXT.md` glossary. Cross-cutting terms live in the root [`CONTEXT.md`](./CONTEXT.md).

## Contexts

| Context         | Path                                  | Scope                                                       |
| --------------- | ------------------------------------- | ----------------------------------------------------------- |
| `org`           | [`CritCrit.Api/Org/`](./CritCrit.Api/Org/CONTEXT.md)                   | Org tree, auth, identity, invitations                       |
| `platform`      | [`CritCrit.Api/Platform/`](./CritCrit.Api/Platform/CONTEXT.md)         | Audit, auth, configuration, errors, Marten, tenancy         |
| `observability` | [`CritCrit.Api/Observability/`](./CritCrit.Api/Observability/CONTEXT.md) | Audit logs, structured logging, telemetry, support tooling  |
| `infra`         | [`CritCrit.AppHost/`](./CritCrit.AppHost/CONTEXT.md)                   | Aspire host, resource composition, dev-time orchestration   |

## Reading order for agents

1. Root `CONTEXT.md` (cross-cutting language like Static Asset)
2. The context(s) you're touching
3. `docs/adr/` for system-wide decisions
4. Per-context ADRs under `<context-path>/docs/adr/` if present

See [`docs/agents/domain.md`](./docs/agents/domain.md) for consumer rules.
