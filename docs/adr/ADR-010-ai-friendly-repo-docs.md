# ADR-010 — AI-friendly repository layout, vendored skills, and toolchain 0.7

- **Status:** accepted
- **Date:** 2026-10-10
- **Supersedes:** `PLAN.md` §0 reading order, §4 machine setup, the §5 layout
  tree, and the §10 XAML-compiler risk row. No product, data, design, or
  performance decision changes.
- **Owner acknowledgement:** the repository owner asked for the EVIHelper-style
  agent-friendly structure and approved doing all of the proposed changes.

## Context

The repo was set up for one agent (Codex) and one phase (greenfield, M0). By
M13 several things had drifted:

- **Context cost.** `AGENTS.md` made every session read `PLAN.md` (47 KB),
  `docs/DESIGN.md` (38 KB) and `docs/DATA-MODEL.md` (22 KB) — roughly 28k
  tokens before any work — while `docs/PROGRESS.md` grew to 74 KB.
- **Mixed concerns in `PLAN.md`.** Product scope, seven inline ADRs, the tech
  stack, machine setup, repo layout, architecture, perf budgets and milestones
  all lived in one file. Only ADR-008 and ADR-009 were files.
- **Stale facts.** The §5 layout showed `Views/`; the code has `Pages/` and
  `Controls/`, plus `Wintodo.Sync`. `AGENTS.md` said `winapp` was not
  installed (0.5.0 was), required `winui-search.exe` (removed in win-dev-skills
  0.6 in favour of `winapp find-ui`), and told agents to blame the last-edited
  `.xaml` for silent build failures — a Windows App SDK bug fixed in 2.1.3
  (we are on 2.3.1).
- **Tooling outside the repo.** The WinUI skills lived only in per-user agent
  directories, at different versions (Codex copy from July, Claude Code plugin
  0.6.1). A fresh clone had no skills at all.
- **win-dev-skills 0.7** removed `BuildAndRun.ps1` and the bundled analyzer DLL
  that this repo had copied, replacing them with direct `winapp run` and the
  `Microsoft.Windows.SDK.BuildTools.WinUIAnalyzer` NuGet package, and now
  requires WinApp CLI 0.7+.

## Decision

1. **Progressive disclosure.** `AGENTS.md` is short and canonical for every
   agent: read order, commands, definition of done, hard rules, and a
   "where things are" table. `CLAUDE.md` imports it and adds only Claude Code
   specifics. Specs are read when the task needs them, not every session.
2. **`ARCHITECTURE.md`** at the root describes the code as it is: projects,
   dependency rules, shell composition, layers, data access, and where the
   code deliberately differs from the original plan.
3. **`docs/README.md`** indexes all docs. **`docs/adr/`** holds one file per
   decision; ADR-001…007 are moved out of `PLAN.md` §2 verbatim (text
   unchanged). `docs/adr/README.md` has the index and template.
4. **`docs/guides/`** holds task-oriented how-tos: build and run, testing,
   schema migrations, performance measurement, AI tooling.
5. **`PLAN.md`** keeps product scope, the §7 budgets and the milestones with
   their gates. Section numbers stay stable because `docs/DESIGN.md`,
   `docs/PERF.md`, ADR-008 and `scripts/Measure-Perf.ps1` cite them; moved
   sections become pointers.
6. **`docs/PROGRESS.md`** gains a "Current state" summary at the top so agents
   need not read the full history.
7. **Vendored skills.** `scripts/Update-AgentSkills.ps1` copies the WinUI
   skills from a pinned win-dev-skills release tag into `.agents/skills/` and
   mirrors them into `.claude/skills/`. `.agents/skills/VENDORED.md` records
   the tag and commit. Bumping the tag is a reviewed commit.
8. **Toolchain 0.7.** WinApp CLI 0.7.1, the `WinUIAnalyzer` NuGet package
   (replaces the copied `analyzer/` DLL, so CLI, IDE and CI builds all run the
   analyzer), and `Microsoft.Windows.SDK.BuildTools.WinApp` 0.7.1.
   `BuildAndRun.ps1` stays as a thin wrapper over project-mode `winapp run`.
9. **`.mcp.json`** registers the Microsoft Learn MCP server for grounded
   Windows App SDK / .NET documentation lookup.
10. **CI.** `.github/workflows/ci.yml` builds x64 with warnings as errors and
    runs the Core, Data and Sync test projects. UI gates stay local because
    they need an interactive desktop.

## Consequences

- A session starts with about 18 KB of mandatory context (`AGENTS.md`,
  `ARCHITECTURE.md`, the PROGRESS summary) instead of ~110 KB.
- Any agent on a fresh clone gets the same, pinned WinUI skills.
- The newer analyzer flagged WUI1001 on the deliberate UWP toast-scheduling
  path. It is suppressed in one helper with a comment and recorded in
  `docs/PROGRESS.md`, as the hard rules require.
- Two copies of each skill are committed (`.agents/` and `.claude/`). They are
  generated; never hand-edit them.
- Claude Code users who also have the `winui@win-dev-skills` user plugin will
  see each skill twice (plugin-namespaced and project). That is harmless; the
  project copy is the pinned one.

## Alternatives considered

- **Enable the plugin per project via `.claude/settings.json`.** Works only for
  Claude Code, tracks the rolling marketplace instead of a pinned tag, and
  gives Codex nothing.
- **Keep the copied `analyzer/` DLL and old wrapper.** Upstream deleted both in
  0.7; the copy would silently stop receiving rule updates and only loaded
  through `Directory.Build.props`.
- **Split `PLAN.md` completely and renumber.** Breaks section references in
  accepted ADRs, which are never rewritten.
