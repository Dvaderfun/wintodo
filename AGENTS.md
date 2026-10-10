# AGENTS.md

Instructions for AI coding agents (Codex, Claude Code, Copilot, Cursor, Gemini…).
Humans: start with [README.md](README.md).

## What this is

**Wintodo** — a native, packaged WinUI 3 task manager for Windows. v1 is a faithful
Microsoft To Do clone built on open standards (iCalendar `VTODO`, CalDAV) instead of
Microsoft Graph. Native AOT, x64 + ARM64, MSIX. Implemented: M0–M9 and M13 (CalDAV
sync), with some manual gate rows still open. Not started: M10 packaging, M11 deep
subtasks, M12 tags. `Wintodo` is a placeholder name.

## Read before changing code

1. [ARCHITECTURE.md](ARCHITECTURE.md) — projects, dependency rules, data flow. **Mandatory.**
2. [docs/PROGRESS.md](docs/PROGRESS.md) — read only the **Current state** section at the top.
3. The spec for the area you touch — [docs/DESIGN.md](docs/DESIGN.md) before any XAML,
   [docs/DATA-MODEL.md](docs/DATA-MODEL.md) before model or storage code.
4. The guide for your task in [docs/guides/](docs/guides/) — index in [docs/README.md](docs/README.md).
5. Milestone work: that milestone's section in [PLAN.md](PLAN.md) §8 (deliverables + gate).
   UX work packages (`UX-xx`): [docs/UX-PLAN.md](docs/UX-PLAN.md) — claim one in its status table first.

## Commands

```powershell
dotnet build Wintodo.sln -p:Platform=x64 -warnaserror   # build everything, analyzers on
dotnet test  Wintodo.sln -p:Platform=x64                # Core + Data + Sync unit tests
.\BuildAndRun.ps1                                       # build + launch attached (run async)
.\BuildAndRun.ps1 --detach --json                       # launch, get PID for winapp ui
.\tests\ui\run-m8-gate.ps1                              # a UI gate (needs a Debug x64 build)
.\scripts\Publish-Aot.ps1                               # Native AOT publish, x64 + ARM64
.\scripts\Measure-Perf.ps1 -Milestone <Mx>              # perf rows into docs/PERF.md
winapp find-ui "<control or pattern>"                   # real WinUI samples before writing XAML
```

Details: [docs/guides/build-and-run.md](docs/guides/build-and-run.md),
[docs/guides/testing.md](docs/guides/testing.md).

## Definition of done

- `dotnet build … -warnaserror` is clean and `dotnet test` passes.
- New logic has unit tests (Core/Data/Sync). UI changes pass the affected `tests/ui` gate.
- UI work went through `/winui-code-review` with nothing above informational.
- Docs the change touched are updated; a new decision has an ADR in `docs/adr/`.
- `docs/PROGRESS.md` has an entry: gate result, what shipped, deviations. Update
  **Current state** if it changed.

## Skills

WinUI skills are vendored at a pinned tag in `.agents/skills/` (mirrored to
`.claude/skills/`). Load the skill before the work, not after.

| Situation | Skill |
|---|---|
| Machine prep, missing prerequisite | `winui-setup` — **owner-invoked only** |
| Building, running, build errors, crashes, AOT runs | `winui-dev-workflow` |
| Any new XAML, control choice, theming question | `winui-design` |
| Before committing UI work or a milestone | `winui-code-review` |
| Writing or running a UI gate | `winui-ui-testing` |
| MSIX, signing, release CI, Store | `winui-packaging` |

Before writing XAML for a new surface: **one** batch of `winapp find-ui` queries for
every control you need, `--id` the best few, then write XAML from those samples. Check
API shapes with `winapp find-api … --project-dir src\Wintodo.App`. Do not interleave
searching with coding. More: [docs/guides/ai-tooling.md](docs/guides/ai-tooling.md).

## Hard rules — never violate

**Build and run**
- This is the owner's main machine. Never run UI automation, synthetic mouse/
  keyboard input, or UI-driven performance/contrast tests on the host desktop
  without explicit permission for that test session. Prefer Windows Sandbox or
  a dedicated VM; if isolation is unavailable, leave UI gates pending and continue
  builds/unit tests. Never silently fall back to local. Never set
  `WINTODO_ALLOW_LOCAL_UI=1` without that permission. Legacy UI scripts are local
  and guarded; adding `--on sandbox` only to launch is insufficient.
- Never run the packaged `.exe` directly. Use `BuildAndRun.ps1` or `winapp run`.
- Never add `<WindowsPackageType>None</WindowsPackageType>`. Never delete `Package.appxmanifest`.
- Never target `AnyCPU`. x64 or ARM64 only.
- Never pass `--version` to `dotnet add package` — take the latest stable. Versions live
  only in `Directory.Packages.props`.
- `TreatWarningsAsErrors` is on. Fix warnings. Suppress only when the fix is impossible,
  as narrowly as possible, with a comment, and record why in `docs/PROGRESS.md`.

**Code**
- `{x:Bind}` only, with an explicit `Mode`. Zero `{Binding}`.
- Zero `IValueConverter` — `x:Bind` to static functions. Never `Converter={x:Null}`.
- `TextBox` two-way bindings **must** include `UpdateSourceTrigger=PropertyChanged`.
- Commands, not `Click`/`Tapped` handlers. `VisualStateManager` for visual state.
- No reflection: no `Activator.CreateInstance`, `Type.GetType`, assembly scanning, or
  reflection-based JSON (source-generated `JsonSerializerContext` only). Native AOT ships.
- `[ObservableProperty]` on **partial properties**, not the field-backed form.
- `DispatcherQueue` for UI marshalling. Never `Dispatcher`, `CoreDispatcher`, `Window.Current`.
- No `Windows.UI.Xaml.*`. The only UWP-era APIs allowed: `Windows.UI.Notifications`
  (scheduled toasts) and `Windows.Security.Credentials` (`PasswordVault`).
- Anything time-dependent takes `IClock`. No `DateTimeOffset.Now` outside the clock.
- Check every new dependency against the banned list in `PLAN.md` §3 and confirm it
  is AOT-clean.

**Design**
- No hardcoded colours, corner radii, font sizes, or font weights. Theme resources only.
- Every spacing value is a multiple of 4.
- Three theme dictionaries: `Light`, `Dark`, `HighContrast`. Never `x:Key="Default"`.
  High contrast uses only the 8 permitted `SystemColor*` brushes.
- `AutomationProperties.AutomationId` on every interactive element, named
  `Todo.<Area>.<Element>`. UI gates select by these — treat them as API.
- Every user-visible string in `Strings/en-US/Resources.resw`.
- v1 is a Microsoft To Do clone. Do not invent new navigation patterns.

**Data**
- Never store a credential in SQLite. `PasswordVault` only.
- Never concatenate user input into SQL. Parameterised commands only.
- Never destroy an iCalendar property you don't understand — round-trip it.
- Soft delete plus a `change_log` row. Never hard-delete on a user action.
- Schema changes are new forward migrations ([docs/guides/migrations.md](docs/guides/migrations.md)).

## Working style

- Batch fixes: read all build errors, fix all, rebuild once.
- Do not change the plan or an accepted decision silently. Say so, write an ADR,
  get the owner's acknowledgement, then change it.
- Never report a gate as passed unless it fully passed. Name what failed or was skipped.
- Measure, don't assume. Perf claims need rows in `docs/PERF.md`.
- Commit per milestone at minimum, naming the milestone.
- Ask the owner before: enabling Developer Mode or Windows Sandbox (UAC), installing
  Visual Studio, changing an ADR, or anything outside this repo.

## Where things are

| Need to… | Go to |
|---|---|
| Change shell layout, panes, navigation | `src/Wintodo.App/Pages/ShellPage.xaml`, `Pages/ShellPage.*.cs`, `ViewModels/ShellViewModel*.cs`, DESIGN §2–§5 |
| Change the task list or a row | `Controls/TaskListView`, `Controls/TaskRow`, `ViewModels/TaskListViewModel*.cs` |
| Change the detail pane | `Controls/TaskDetailView`, `ViewModels/TaskDetailViewModel*.cs`, DESIGN §5 |
| Add or change a theme resource | `src/Wintodo.App/Themes/Colors.xaml`, DESIGN §9 |
| Add a user-visible string | `src/Wintodo.App/Strings/en-US/Resources.resw` |
| Change the schema | `src/Wintodo.Data/Migrations/`, [migrations guide](docs/guides/migrations.md) |
| Change store queries | `src/Wintodo.Data/SqliteTaskStore.<Area>.cs`, `Wintodo.Core/Queries/` |
| Change iCalendar import/export | `src/Wintodo.Core/Ical/`, `tests/fixtures/ical/`, DATA-MODEL §4 |
| Change recurrence | `src/Wintodo.Core/Recurrence/RecurrenceRule.cs`, [ADR-006](docs/adr/ADR-006-recurrence-on-completion.md) |
| Change CalDAV sync | `src/Wintodo.Sync/`, [ADR-002](docs/adr/ADR-002-caldav-webdav-sync.md) |
| Toasts, badge, jump list, startup | `src/Wintodo.App/Services/WindowsIntegrationService.cs`, `StartupTaskService.cs` |
| App startup and object graph | `src/Wintodo.App/App.xaml.cs` (`OnLaunched`) |
| Perf budgets / measurement | `PLAN.md` §7, [performance guide](docs/guides/performance.md), `docs/PERF.md` |
| Record a decision | [docs/adr/README.md](docs/adr/README.md) |
| Update vendored skills | `scripts/Update-AgentSkills.ps1`, [ai-tooling guide](docs/guides/ai-tooling.md) |
