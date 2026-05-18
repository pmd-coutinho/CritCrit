# CritCrit

CritCrit models brand organization trees and the operational data attached to nodes in those trees.

See [`CONTEXT.md`](./CONTEXT.md) for the root glossary and [`CONTEXT-MAP.md`](./CONTEXT-MAP.md) for the per-context layout.

## Agent skills

### Issue tracker

Issues and PRDs live as local markdown files under `.scratch/<feature-slug>/`. See [`docs/agents/issue-tracker.md`](./docs/agents/issue-tracker.md).

### Triage labels

Five canonical roles (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`) used as-is on the `Status:` line of each issue file. See [`docs/agents/triage-labels.md`](./docs/agents/triage-labels.md).

### Domain docs

Multi-context layout: root `CONTEXT-MAP.md` indexes per-context `CONTEXT.md` files under `CritCrit.Api/Org/`, `CritCrit.Api/Platform/`, `CritCrit.Api/Observability/`, and `CritCrit.AppHost/`. See [`docs/agents/domain.md`](./docs/agents/domain.md).
