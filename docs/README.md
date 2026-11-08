# Documentation index

Read what the task needs, not everything. Start from `AGENTS.md`, then
`ARCHITECTURE.md`, then the spec or guide for your task.

## Specs — read before changing the area

| Doc | Read before |
|---|---|
| [DESIGN.md](DESIGN.md) | Writing or changing any XAML, theme resource, motion, or keyboard map |
| [DATA-MODEL.md](DATA-MODEL.md) | Changing models, the SQLite schema, or the `VTODO` mapping |
| [../PLAN.md](../PLAN.md) | Starting a milestone (scope, deliverables, gate) or touching a §7 budget |
| [UX-PLAN.md](UX-PLAN.md) | Picking up a UX work package (`UX-xx`): status table, scope, acceptance |
| [IMPORT-PLAN.md](IMPORT-PLAN.md) | Onboarding and Microsoft To Do import (`IMP-xx`): status, design, open owner steps |

## Plans

All new planning documents belong under `docs/plans/`.

| Plan | Scope |
|---|---|
| [Deployment readiness and GitHub release](plans/deployment-readiness.md) | Audit remediation, data/API security, privacy, CI/CD, signing and final deployment gates (`DEP-01`–`DEP-20`) |
| [Recoverable synced moves implementation plan](plans/dep04-synced-moves.md) | DEP-04 baseline reproduction and accepted durable recovery under ADR-017 |
| [Downloaded attachment provenance](plans/dep10-attachment-provenance.md) | DEP-10 Internet-zone metadata, Windows opening policy and verification limits |
| [Production override policy proposal](plans/dep12-production-overrides.md) | DEP-12 launch/storage/clock inventory, affected AOT gates and concrete owner policy choices |
| [Native dependency/toolchain review](plans/dep02-toolchain-review.md) | DEP-02 exact native origins, upstream advisory dispositions and remaining runner/tool provenance gaps |
| [Source archive and asset privacy review](plans/dep13-source-privacy-review.md) | DEP-13 archive exclusions, fixture byte preservation, asset metadata and pending attribution/licensing choices |
| [Exact-package inspection preparation](plans/dep16-package-inspection.md) | DEP-16 reusable MSIX structural/privacy gate and pending final identity/payload/deployment evidence |
| [Clean-source unsigned candidate validation](plans/dep16-local-candidate-validation.md) | DEP-16 exact x64/ARM64 package hashes, locked packaging correction and remaining signed/deployment gates |
| [Exact native/toolchain evidence](plans/dep15-exact-toolchain-evidence.md) | DEP-02/15 numeric tool versions/hashes, evaluated SDK selections and pending actual invocation/hosted proof |
| [Release documentation preparation](plans/dep17-release-docs.md) | DEP-17 source-backed privacy/installation drafts and remaining owner/publication decisions |
| [Shipped dependency notices review](plans/dep17-third-party-notices-review.md) | DEP-17 exact inherited notice additions, runtime/tool distinctions and unresolved redistribution/ownership proof |

Release review drafts: [privacy](releases/PRIVACY.md) and
[installation, update and recovery](releases/INSTALLATION.md). These are
unpublished; final identity, signing and deployment evidence remain pending.

## Records

| Doc | What it holds |
|---|---|
| [PROGRESS.md](PROGRESS.md) | Current state at the top, then one entry per milestone or change: gate result, what shipped, deviations |
| [PERF.md](PERF.md) | Every measured perf row from `scripts/Measure-Perf.ps1`. No estimates. |
| [adr/README.md](adr/README.md) | Architecture decisions — index and template |

## Guides — how to do a task

- [guides/build-and-run.md](guides/build-and-run.md) — prerequisites, build, run, debug a crash, AOT publish
- [guides/testing.md](guides/testing.md) — unit tests, UI gate scripts, AutomationIds, writing a new gate
- [guides/diagnostics.md](guides/diagnostics.md) — safe app traces, private diagnostic limits and sanitized CI test summaries
- [guides/network-limits.md](guides/network-limits.md) — response, attachment and import byte budgets, deadlines and staging cleanup
- [guides/migrations.md](guides/migrations.md) — adding a SQLite schema migration
- [guides/performance.md](guides/performance.md) — measuring, recording, and the budget rules
- [guides/packaging.md](guides/packaging.md) — unsigned tag artifacts, placeholder identity, local test signatures and open release gates
- [guides/ci.md](guides/ci.md) — pinned Actions, redacted scanner/audit gates, trusted AOT cadence and remaining enforcement decisions
- [guides/release-checklist.md](guides/release-checklist.md) — UX-20 owner-run checks, M10 decisions and clean-install acceptance
- [guides/ai-tooling.md](guides/ai-tooling.md) — instruction files, vendored skills, WinApp CLI, MCP servers

## Fixtures

- [../tests/fixtures/ical/README.md](../tests/fixtures/ical/README.md) — provenance of the iCalendar corpus
- [../tests/fixtures/mstodo/README.md](../tests/fixtures/mstodo/README.md) — hand-written Microsoft Graph To Do responses
