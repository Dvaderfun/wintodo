# Testing

| Layer | Where | How |
|---|---|---|
| Core (recurrence, iCalendar, queries, emoji validation, list presentation) | `tests/Wintodo.Core.Tests` | xunit v3, pure functions; presentation cases under `Presentation/` |
| Data (store, migrations, perf assertions, iCal import/export) | `tests/Wintodo.Data.Tests` | xunit v3, temp-file SQLite per test (`StoreFixture`) |
| Sync (CalDAV client and sync service) | `tests/Wintodo.Sync.Tests` | xunit v3, fake HTTP handler |
| App shell and features | `tests/ui/*.ps1` | `winapp ui` (UI Automation) scripts against the running packaged app |
| Accessibility | `tests/ui/m8-design-accessibility.ps1` | `winapp ui` audit, gated at zero errors |
| Performance | `scripts/Measure-Perf.ps1` | see [performance.md](performance.md) |

## Unit tests

```powershell
dotnet test Wintodo.sln -p:Platform=x64                                   # everything, ~10 s after build
dotnet test --project tests/Wintodo.Data.Tests -p:Platform=x64 --filter-class "*MigrationAndListTests"
dotnet test --solution Wintodo.sln -p:Platform=x64 --report-trx --coverage  # what CI runs
```

The test projects are xunit v3 executables on Microsoft Testing Platform
(`global.json` opts `dotnet test` into MTP). VSTest options such as `--logger` and
`--filter "FullyQualifiedName~…"` do not apply; use `--report-trx`, `--coverage`,
`--filter-class`, `--filter-method`. Every call that accepts a `CancellationToken`
passes `TestContext.Current.CancellationToken` (analyzer xUnit1051, an error here).

Rules:
- Anything date-dependent takes `IClock`. Never call `DateTimeOffset.Now` outside
  the clock implementation.
- Data tests use a fresh temp database per test; never a shared file.
- New iCalendar behaviour gets a fixture in `tests/fixtures/ical/` and a
  round-trip assertion. Unknown properties must survive byte-for-byte.
- Perf assertions (10 000-task query ≤ 5 ms, search ≤ 60 ms) live in
  `SmartListPerformanceTests` / `PerformanceFixtureTests` — keep them green.

## UI gate scripts

Each `run-*-gate.ps1` launches the **already-built Debug x64 output**
(`src\Wintodo.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64`) with
`winapp run <folder> --detach --json`, points it at a throwaway database under
`tests\ui\artifacts\<area>\run-<timestamp>\`, runs the matching suite, then
stops the app. Artifacts (results JSON, screenshots, UI trees) are gitignored.

```powershell
dotnet build Wintodo.sln -p:Platform=x64          # or .\BuildAndRun.ps1 once
.\tests\ui\run-m8-gate.ps1                        # design + accessibility, 17 checks
.\tests\ui\run-composer-layout-gate.ps1           # detail-open/narrow input, options, resize; -Aot for publish
.\tests\ui\run-navigation-startup-gate.ps1       # one selected category on first launch, restart and hover
.\tests\ui\run-m5-gate.ps1                        # smart lists, search, restart
.\tests\ui\run-m13-gate.ps1                       # starts a loopback CalDAV server
```

| Script | Covers |
|---|---|
| `m0-smoke.ps1 -AppPid <pid>` | Window exists, interactive tree, screenshot |
| `m2-shell.ps1`, `m2-theme.ps1` | Shell layout, Light/Dark/High Contrast |
| `run-m3-gate.ps1` / `m3-lists-tasks.ps1` | List CRUD, add/complete/reorder/delete/undo tasks; runner uses a fresh database with delete confirmation off |
| `run-m4-gate.ps1` | Detail pane fields and restart persistence |
| `run-m5-gate.ps1` | Smart lists, search, sort, restart |
| `run-m6-gate.ps1` | `.ics` import/export through the UI |
| `run-m7-gate.ps1` | Reminders, toasts, badge, jump list (`-VerifyToastCompletion`, `-VerifyToastSnooze`) |
| `run-m8-gate.ps1` | Design, responsive layout, all four Windows contrast palettes (selected/unselected custom list, search border and header focus), accessibility audit |
| `run-m13-gate.ps1` | CalDAV Configure dialog, cancel/retry, sync, list toggles, footer visibility before connect/after connect/restart/disconnect (`-Aot` for published x64) |
| `run-notification-settings-gate.ps1` | Reminder enable/disable, actual scheduled payload defaults, restart, Data/About and accessibility |
| `run-settings-gate.ps1` | Tasks settings, immediate delete/undo, restart, reminder default, ordering and accessibility |
| `run-list-icon-gate.ps1` | Emoji list icons and navigation counts |

The scripts drive real input. Don't use the mouse or keyboard while they run.
`/winui-ui-testing` can run suites in Windows Sandbox instead; ask the owner
before enabling Sandbox.

## AutomationIds are API

Every interactive element has `AutomationProperties.AutomationId` named
`Todo.<Area>.<Element>` (e.g. `Todo.List.AddTaskBox`, `Todo.Row.CompleteToggle`,
`Todo.Settings.Sync.Connect`). The UI scripts select by these ids. Renaming one
breaks a gate — search `tests/ui/` before you change an id.

## Writing a new UI suite

1. Load `/winui-ui-testing`. Generate **one** batch script covering assertions,
   interactions and the accessibility audit; run it in a single pass.
2. Follow the existing shape: `param([int]$AppPid, [string]$ArtifactDirectory)`,
   a `Test-UI` helper counting pass/fail, results written to
   `$ArtifactDirectory\results.json`, non-zero exit on any failure.
3. Add a `run-<area>-gate.ps1` that launches with a scratch `--database-path`
   (and `--clock-file` if time matters) and always stops the app in `finally`.
4. Record the gate result in `docs/PROGRESS.md`. A partial pass is reported as
   partial, naming what failed.

## CI

`.github/workflows/ci.yml` builds x64 Debug with warnings as errors and runs the
three test projects (TRX + coverage uploaded as artifacts) on `windows-latest`. UI gates are local-only: they need an
interactive desktop session and Developer Mode.

On `v*` tag pushes, a separate release job publishes Native AOT and packages
unsigned x64 and ARM64 MSIX artifacts after build/test succeeds. Main pushes
and PRs do not run that job. Production signing and install/uninstall gates
remain open; see [packaging.md](packaging.md).
