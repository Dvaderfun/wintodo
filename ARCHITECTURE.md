# Architecture

Packaged WinUI 3 desktop app (Windows App SDK 2.5.1, .NET 10 LTS, C# 14), shipped
as Native AOT, self-contained MSIX for x64 and ARM64. Plain MVVM with
`CommunityToolkit.Mvvm`, a hand-written SQLite store, and iCalendar `VTODO` as
the canonical task format. No server, no account system of our own.

The original design rationale is `PLAN.md` §3 and §6; this file describes the
code **as it is**. Where they disagree, this file wins and the gap is listed
under [Known divergences](#known-divergences-from-planmd).

## Tech stack

| Concern | Choice | Decision |
|---|---|---|
| UI | WinUI 3, Windows App SDK 2.5.1, Mica, `TitleBar`, `NavigationView` | `PLAN.md` §3 |
| Runtime | `net10.0-windows10.0.26100.0` (app), `net10.0` (libraries); SDK pinned in `global.json` | `PLAN.md` §3 |
| Packaging | Packaged MSIX, x64 + ARM64, Native AOT + self-contained in Release | `PLAN.md` §3, M9 |
| MVVM | `CommunityToolkit.Mvvm` 8.4, `[ObservableProperty]` on partial properties | `PLAN.md` §3 |
| Composition | Manual constructor wiring in `App.OnLaunched` — no DI container | see divergences |
| Storage | `Microsoft.Data.Sqlite` + `SQLitePCLRaw.bundle_e_sqlite3`, FTS5, hand-written mappers, no ORM | `PLAN.md` §3 |
| Task format | iCalendar `VTODO` (RFC 5545), hand-rolled reader/writer, unknown-property passthrough | [ADR-001](docs/adr/ADR-001-ical-vtodo-canonical-format.md), [ADR-007](docs/adr/ADR-007-no-icalendar-library.md) |
| Internal model | Shaped after JSCalendar semantics, serialised as `VTODO` | [ADR-004](docs/adr/ADR-004-jscalendar-internal-model.md) |
| Sync | CalDAV + WebDAV Sync, ETag conditional writes, `PasswordVault` credentials | [ADR-002](docs/adr/ADR-002-caldav-webdav-sync.md) |
| Reminders | UWP `ScheduledToastNotification` (Windows App SDK has no scheduling API) | `PLAN.md` M7 |
| Analyzers | Trim/AOT/single-file analyzers, `WinUIAnalyzer` NuGet, warnings as errors | `Directory.Build.props`, [ADR-010](docs/adr/ADR-010-ai-friendly-repo-docs.md) |
| Tests | xunit v3 on Microsoft Testing Platform for Core/Data/Sync; `winapp ui` PowerShell gate scripts for the app | [docs/guides/testing.md](docs/guides/testing.md) |

Banned dependencies and the AOT rationale: `PLAN.md` §3.

## Projects

```
src/
  Wintodo.Core/        net10.0, BCL only, AOT-compatible
    Models/            TodoTask, TodoList, ListGroup, TaskAlarm, SavedView, SyncModels, ...
    Recurrence/        RRULE parse/serialise + ComputeNext
    Ical/              IcalReader / IcalWriter / IcalUnknownCodec (verbatim passthrough)
    Queries/           ListQuery (smart-list and filter descriptors)
    Services/          interfaces: ITaskStore, ISyncStore, ISettingsStore, ICredentialStore, IClock; TaskPreferences
  Wintodo.Data/        net10.0, Core + Microsoft.Data.Sqlite, AOT-compatible
    Migrations/        NNN_*.sql, embedded resources, forward-only
    Mapping/           SqliteMapper (row <-> model)
    SqliteTaskStore*.cs  one partial per area: Tasks, Lists, Groups, Alarms, Ical, Sync, Settings, Maintenance
  Wintodo.Sync/        net10.0, Core only, AOT-compatible
                       CalDavClient (HTTP/XML), CalDavSyncService (push/pull/conflicts)
  Wintodo.App/         the WinUI app; the only project with XAML or Windows APIs
    App.xaml.cs        composition root, activation routing (launch / toast / startup task)
    MainWindow.xaml    Mica, title bar, root Frame hosting ShellPage
    Pages/             ShellPage (NavigationView + list + detail + settings host)
    Controls/          TaskListView, TaskDetailView, TaskRow, StepRow, AddTaskComposer,
                       SettingsView, ResponsiveTitleSearch, TitleEditBox, ScrollPerformanceProbe
    ViewModels/        ShellViewModel(.Sync), TaskListViewModel, TaskDetailViewModel, item VMs
    Services/          WindowsIntegrationService (toasts, badge, jump list), StartupTaskService,
                       PasswordVaultCredentialStore, ShellStateService, FileBackedClock
    Themes/Colors.xaml Light / Dark / HighContrast dictionaries
    Strings/en-US/Resources.resw   every user-visible string
tests/
  Wintodo.Core.Tests/  Wintodo.Data.Tests/  Wintodo.Sync.Tests/   xunit v3 (MTP executables)
  fixtures/ical/       real exports (Nextcloud, Tasks.org, Thunderbird, Apple)
  fixtures/perf/       seeded perf databases
  ui/                  winapp ui gate scripts (run-*-gate.ps1) and per-area suites
scripts/               Measure-Perf.ps1, Publish-Aot.ps1, Update-AgentSkills.ps1
```

### Dependency rules

```
Wintodo.App ──► Wintodo.Data ──► Wintodo.Core
Wintodo.App ──► Wintodo.Sync ──► Wintodo.Core
Wintodo.Core ──X──► anything but the BCL
Wintodo.Data ──X──► Wintodo.Sync   (and vice versa; both talk through Core interfaces)
UI types     ──X──► below Wintodo.App
```

Core, Data and Sync set `IsAotCompatible`. A new library project must too.

## Shell composition

One `MainWindow`; its root `Frame` hosts one `ShellPage` for the app's lifetime.
`ShellPage` is the three-pane layout from `PLAN.md` §6.1: `NavigationView` pane
(smart lists, default "Tasks" list, user lists and groups, footer composer) →
`TaskListView` → `TaskDetailView` (`x:Load`-deferred).

- Switching lists swaps the bound data on the single `TaskListViewModel`;
  there is no page navigation between lists.
- Settings is `SettingsView`, an `x:Load`-deferred control inside `ShellPage`
  toggled by `ShellViewModel.IsSettingsVisible` (the `NavigationView`'s built-in
  settings item is off).
- Tasks settings share one Core `TaskPreferences` instance between Settings, list
  and detail view models. It validates values, queues persistence and notifies
  consumers; task ordering options are explicit store parameters.
- Responsive breakpoints are `VisualStateManager` states (`docs/DESIGN.md` §6).

## Layers and data flow

```
XAML (x:Bind only, explicit Mode)
  │ commands, OneWay/TwoWay bindings
ViewModels (CommunityToolkit.Mvvm; DispatcherQueue is the only UI type)
  │ ITaskStore / ISyncStore / ISettingsStore      ◄── StoreChanged (list ids + task uids)
SqliteTaskStore (all IO off the UI thread)        CalDavSyncService ──► CalDavClient ──► server
  │ Microsoft.Data.Sqlite
%LOCALAPPDATA%\Packages\<PFN>\LocalState\wintodo.db   (override: --database-path <file>)
```

`App.OnLaunched` builds the graph by hand: `IClock` → `SqliteTaskStore` →
`WindowsIntegrationService` → `CalDavSyncService` → `MainWindow`. Toast and
startup-task activations initialise only what they need and may exit without
showing a window.

### Data access

- One writer connection, WAL mode, writes serialised by a `SemaphoreSlim(1,1)`.
  Reads open short-lived read-only connections, at most four at once.
- Pragmas on open: `journal_mode=WAL`, `synchronous=NORMAL`, `foreign_keys=ON`,
  `busy_timeout=5000`, `temp_store=MEMORY`.
- Parameterised commands only. Soft delete plus a `change_log` row; the change
  log also drives CalDAV push.
- Schema and the `VTODO` mapping: [docs/DATA-MODEL.md](docs/DATA-MODEL.md).
  Adding a migration: [docs/guides/migrations.md](docs/guides/migrations.md).

### Time

Everything that depends on "now" takes `IClock`. Launching with
`--clock-file <path>` swaps in `FileBackedClock`, which reads the current time
from that file, so UI gate scripts can move time (My Day rollover, reminders)
without touching the OS clock.

## Windows integration

`WindowsIntegrationService` owns reminder toasts (scheduled through
`ToastNotificationManager`, `Tag` = alarm uid, `Group` = task uid, with a
rescheduling sweep per `PLAN.md` M7), Complete/Snooze toast actions (including
activation while the app is closed), the taskbar badge, and the jump list.
`NotificationPreferences` loads the enabled flag and default snooze before
the scheduling sweep, including background activations. Settings shares that
instance. Turning reminders off cancels scheduled toasts; stored alarms remain.
Changing snooze rebuilds scheduled payloads with the selected default.
`StartupTaskService` wraps the optional launch-at-login toggle. Credentials go
to `PasswordVault` via `PasswordVaultCredentialStore` — never SQLite.

## Known divergences from `PLAN.md`

| Plan says | Code does | Note |
|---|---|---|
| §3: `Microsoft.Extensions.DependencyInjection` | Manual wiring in `App.OnLaunched` | Fewer dependencies, AOT-trivial. Fine at this size. |
| §5: `Views/` with `ShellPage`, `SettingsPage` | `Pages/ShellPage`, `Controls/*`, `SettingsView` control | Settings is in-shell, not `Frame`-navigated. |
| §6.4: diff-update bound collections, never `Clear()` + refill | `TaskListViewModel.RebuildVisibleItems` clears and refills `VisibleItems` | Candidate perf work; measure before changing. |
| §5: `Wintodo.Data` / `Wintodo.Core` only | Plus `Wintodo.Sync` (M13) | Planned in M13, missing from the §5 tree. |
