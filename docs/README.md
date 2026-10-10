# Documentation index

Read what the task needs, not everything. Start from `AGENTS.md`, then
`ARCHITECTURE.md`, then the spec or guide for your task.

## Specs — read before changing the area

| Doc | Read before |
|---|---|
| [DESIGN.md](DESIGN.md) | Writing or changing any XAML, theme resource, motion, or keyboard map |
| [DATA-MODEL.md](DATA-MODEL.md) | Changing models, the SQLite schema, or the `VTODO` mapping |
| [../PLAN.md](../PLAN.md) | Starting a milestone (scope, deliverables, gate) or touching a §7 budget |

## Records

| Doc | What it holds |
|---|---|
| [PROGRESS.md](PROGRESS.md) | Current state at the top, then one entry per milestone or change: gate result, what shipped, deviations |
| [PERF.md](PERF.md) | Every measured perf row from `scripts/Measure-Perf.ps1`. No estimates. |
| [adr/README.md](adr/README.md) | Architecture decisions — index and template |

## Guides — how to do a task

- [guides/build-and-run.md](guides/build-and-run.md) — prerequisites, build, run, debug a crash, AOT publish
- [guides/testing.md](guides/testing.md) — unit tests, UI gate scripts, AutomationIds, writing a new gate
- [guides/migrations.md](guides/migrations.md) — adding a SQLite schema migration
- [guides/performance.md](guides/performance.md) — measuring, recording, and the budget rules
- [guides/packaging.md](guides/packaging.md) — unsigned tag artifacts, placeholder identity, local test signatures and open release gates
- [guides/ai-tooling.md](guides/ai-tooling.md) — instruction files, vendored skills, WinApp CLI, MCP servers

## Fixtures

- [../tests/fixtures/ical/README.md](../tests/fixtures/ical/README.md) — provenance of the iCalendar corpus
