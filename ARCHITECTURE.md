# Architecture

Packaged WinUI 3 desktop app (Windows App SDK 2.7.0, .NET 10 LTS, C# 14), shipped
as Native AOT, self-contained MSIX for x64 and ARM64. Plain MVVM with
`CommunityToolkit.Mvvm`, a hand-written SQLite store, and iCalendar `VTODO` as
the canonical task format. No server, no account system of our own.

The original design rationale is `PLAN.md` §3 and §6; this file describes the
code **as it is**. Where they disagree, this file wins and the gap is listed
under [Known divergences](#known-divergences-from-planmd).

## Tech stack

| Concern | Choice | Decision |
|---|---|---|
| UI | WinUI 3, Windows App SDK 2.7.0, Mica, `TitleBar`, `NavigationView` | `PLAN.md` §3 |
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
    Presentation/      TaskGrouping (ordered UID groups), TaskSorting (query sort terms/comparison),
                       SharedTaskContent (local Text/WebLink title/note mapping),
                       TaskMetadataLine (subtitle parts), SequenceDiff (keyed collection operations),
                       display-culture/date helpers
    Import/            ImportSnapshot (source-neutral import model), IImportStore, ImportIdentity (UUIDv5, fingerprint)
    Services/          interfaces: ITaskStore, ISyncStore, ISettingsStore, ICredentialStore, IClock; TaskPreferences
                       UndoService (session history), TaskUndoState (field restore snapshot)
  Wintodo.Data/        net10.0, Core + Microsoft.Data.Sqlite, AOT-compatible
    Migrations/        NNN_*.sql, embedded resources, forward-only
    Mapping/           SqliteMapper (row <-> model)
    SqliteTaskStore*.cs  one partial per area: Tasks, Lists, Groups, Alarms, Ical, Import, Sync, Settings, Maintenance
  Wintodo.Sync/        net10.0, Core only, AOT-compatible
                       CalDavClient (HTTP/XML), CalDavSyncService (push/pull/conflicts)
  Wintodo.Import/      net10.0, Core only, AOT-compatible (ADR-016)
                       MicrosoftTodo/: GraphTodoClient (read-only Graph, paging, retry),
                       MicrosoftTodoReader, GraphTodoMapper (Graph -> ImportSnapshot),
                       PatternedRecurrenceConverter, GraphDateConverter, FixtureGraphHandler;
                       HtmlNoteConverter
  Wintodo.App/         the WinUI app; the only project with XAML or Windows APIs
    Program.cs         captures activation before WinUI initialization (one platform query)
    App.xaml.cs        composition root, activation routing (launch / toast / startup task)
    App.Share.cs       share-only activation, local save and broker completion
    ShareWindow.xaml   minimal native progress/error window; no main-shell services
    MainWindow.xaml    Mica, title bar, root Frame hosting ShellPage
    Pages/             ShellPage (composition + view wiring), with Navigation, Dialogs,
                       Accelerators, Theme, Transfers and Lifecycle partials
    Controls/          TaskListView, TaskDetailView, TaskRow, StepRow, AddTaskComposer,
                       SettingsView, ResponsiveTitleSearch, TitleEditBox, ScrollPerformanceProbe
    ViewModels/        ShellViewModel (+ Navigation/Dialogs/Appearance/Transfers/Settings/Sync),
                       TaskListViewModel (+ Loading/Lists/Composer/Tasks/Presentation/Selection),
                       TaskDetailViewModel (+ Tasks/Dates/Recurrence/Attachments/Persistence), item VMs
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

Shell and view-model partials keep the same runtime classes and binding/handler
names. Each C# file under Pages/ViewModels stays below 600 lines. The base files
hold the shared object graph and observable state; area partials hold operations.
Shell transfer filename policy and quick-add fallback choice belong to
ShellViewModel; file pickers, drag events and dialog rendering stay in the page.

```
Wintodo.App ──► Wintodo.Data ──► Wintodo.Core
Wintodo.App ──► Wintodo.Sync ──► Wintodo.Core
Wintodo.App ──► Wintodo.Import ──► Wintodo.Core
Wintodo.Core ──X──► anything but the BCL
Wintodo.Data ──X──► Wintodo.Sync   (and vice versa; both talk through Core interfaces)
Wintodo.Data ──X──► Wintodo.Import (Import builds an ImportSnapshot; Data persists it via IImportStore)
UI types     ──X──► below Wintodo.App
```

Core, Data, Sync and Import set `IsAotCompatible`. A new library project must too.

The App explicitly selects `Microsoft.Windows.AI.MachineLearning 2.5.96`,
the minimum requested by Windows App SDK 2.7.0's ML module. The Data project
also pins the native SQLite engine independently of the managed SQLitePCLRaw
bundle. Review both architecture locks and supplier notices when servicing
these components. See the [November dependency update](docs/plans/2026-11-21-dependency-servicing.md)
and the [earlier ML provenance review](docs/plans/dep02-vendor-provenance-resolution.md).

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
- Core presentation helpers build groups from already ordered query results,
  define sort terms shared with the SQLite query builder, and return unformatted
  subtitle parts. The App maps these to localised group headers, cached row view
  models and metadata text, preserving UI selection and completed expansion.
  TaskSorting also defines each list kind's default and compares effective sort
  terms for the header indicator; resetting restores default mode/direction via
  the same App command path and existing user-list persistence as sort changes.
- Bound groups and rows update through Core SequenceDiff insert/remove/move
  operations. Stable TaskGroupCollection instances explicitly implement
  IObservableVector for Release/AOT projection. Group headers bind to observable
  metadata; the view preserves selection, focus and the viewport around updates.
- Task selection uses the grouped ListView's Extended mode. The list view model
  owns the selected task snapshot; detail opens only for a single task. Bulk
  commands use `ITaskStore.ApplyTaskBatchAsync`: field changes, recurrence,
  delete/restore and moves commit in one writer transaction with per-task change
  log rows and one StoreChanged notification. One undo entry restores a deleted
  selection together.
  Navigation drops onto My Day or Important use that same batch path to set
  membership/importance for the dragged UIDs while retaining their real lists.
- Responsive breakpoints are `VisualStateManager` states (`docs/DESIGN.md` §6).
- ShellViewModel owns a shared 20-entry UndoService for list/detail destructive
  actions. Snackbar expiry hides the message; Ctrl+Z retains history across lists.
  Undo flushes pending detail edits, restores only captured fields atomically,
  then refreshes navigation and the open detail pane. Remote StoreChanged events
  invalidate affected entries; local writes and push acknowledgements retain them.
  List deletion records task membership in migration 003; restore retains earlier
  task deletions and queues CalDAV collection creation before task uploads when
  the collection delete has already been pushed.
  Row completion, including recurring occurrences, adds a five-second Undo snackbar
  after the row update; detail completion remains quiet.

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

`Services/LaunchOptions` owns the platform-free argument, database-selection,
onboarding and clock decisions. App retains activation routing and the Debug-only
notification fallback; lazy lookups preserve command-line precedence and avoid
opening package LocalState when an override wins. Core.Tests links this same
BCL/Core-only helper and file clock without referencing the WinUI app project.
The selected DEP-12 policy retains these local controls in Release/AOT; broker
shares continue to use package LocalState and real time.

ShareTarget is routed before notifications, main window, integration and sync.
Program captures the consumable activation payload before `Application.Start`;
App queries `IShareTargetActivatedEventArgs` explicitly for its AOT projection.
Its native window keeps ShareOperation calls on the UI dispatcher and writes one
task to the local default Tasks list before reporting completion. Success disposes
the store and exits; failures show localized Close-only feedback. HTTP(S) links
stay local and are preserved in notes. Main-window activation uses the writer's
SQLite `data_version` to reload external commits while retaining task selection
and editor drafts. No storage override is accepted by broker shares in Release.

### Data access

- One writer connection, WAL mode, writes serialised by a `SemaphoreSlim(1,1)`.
  Reads open short-lived read-only connections, at most four at once.
- Pragmas on open: `journal_mode=WAL`, `synchronous=NORMAL`, `foreign_keys=ON`,
  `busy_timeout=5000`, `temp_store=MEMORY`.
- Parameterised commands only. Soft delete plus a `change_log` row; the change
  log also drives CalDAV push.
- Schema and the `VTODO` mapping: [docs/DATA-MODEL.md](docs/DATA-MODEL.md).
  Adding a migration: [docs/guides/migrations.md](docs/guides/migrations.md).
- A newer-than-supported schema is rejected before journal-mode changes or
  writes. Startup gives localized update guidance and closes without saving
  placement; supported forward upgrades and backups retain their existing path.
- Pull applies through a guarded writer transaction using the observed local
  `SEQUENCE`, dirty/pending state, membership, tombstone and remote version.
  Intervening edits become conflicts with committed counts, and unresolved
  changes retain the prior list sync token so restart can retry them. Explicit
  conflict choices retain their separate write path and handle direct steps.
- Task moves use the credential-free `task_move_outbox` added by migration 005
  under [ADR-017](docs/adr/ADR-017-recoverable-synced-task-moves.md). Single,
  bulk and undo membership changes record source/destination obligations in the
  same writer transaction; a parent and its steps form one recovery group.
  `CalDavSyncService.Moves`, `.MoveContent` and `.MoveResolution` persist exact
  attempts before conditional creation, confirm destinations before source
  removal, and retain source snapshots for group compensation. Newer edits and
  ordered successor moves remain queued. Account/list changes that could discard
  pending obligations are blocked, with guidance through the existing sync UI.
- CalDAV's production HTTP handler disables automatic redirects. All response
  paths reject 3xx before reading a response body, including same-origin
  redirects; discovery hrefs retain their existing origin checks. Headers-first
  bodies enforce 16 MiB XML and 32 MiB calendar byte limits, declared-length
  consistency and an operation deadline through XML parsing. Unsupported
  Content-Encoding is rejected; the production handler does not decompress.

### Time

Everything that depends on "now" takes `IClock`. Launching with
`--clock-file <path>` swaps in `FileBackedClock`, which reads the current time
from that file, so UI gate scripts can move time (My Day rollover, reminders)
without touching the OS clock.

## Import and onboarding

ADR-016 and [docs/IMPORT-PLAN.md](docs/IMPORT-PLAN.md) cover the design.
`ImportWizardViewModel` owns the Microsoft To Do flow: connect → read → choose →
review → import → done. `ImportWizardView` hosts it in the list pane, like
`SettingsView`. It opens from the onboarding `WelcomeView` and from
Settings → Data.

- **Sign-in.** `MicrosoftSignInService` uses the Windows App SDK
  `OAuth2Manager` (authorization code + PKCE, system browser) with a loopback
  redirect the app listens on. Scopes are `Tasks.ReadWrite` (the list delta call requires it; ADR-016 amendment 1) and `User.Read`; the client sends GET requests only. The
  access token lives in memory for one import and is never stored.
- **Session lifetime.** `MicrosoftTodoImportSession` owns the client, reader and
  cancellation/completion boundary. Success and expired sign-in release the
  authenticated connection; final cancel, navigation dismissal, reset and window
  shutdown clear wizard state. Dismissal waits for in-flight work, including an
  already-started atomic save, before disposing the client or the store. Delayed
  progress callbacks cannot restore a dismissed session. Settings import history
  is read independently from persisted import-source metadata.
- **Graph transport.** The production client disables automatic redirects.
  Paging and attachment paths validate the resolved scheme, normalized host,
  effective port, userinfo and fragment before creating an authenticated request.
  Responses and decoded attachment output have streamed byte boundaries; each
  attachment is capped at 25 MiB and an import attempt at 256 MiB of attachment
  output. Deadlines cover retries, bodies and decoding. Failed staging removes
  only the newly created partial file; committed imports and existing files
  remain intact. Exact limits and parser rules are in
  [the network limits guide](docs/guides/network-limits.md).
  `MicrosoftTodoRegistration.ClientId` holds the public client id of the Entra
  app; when it is empty the wizard says sign-in isn't set up.
- **Reading and mapping.** `MicrosoftTodoReader` reads the account into a
  `MicrosoftTodoExport`. `GraphTodoMapper` turns it into an `ImportSnapshot`
  for the lists the person selected. Attachments download to `import-staging\`
  and are copied under `attachments\<uid>\` inside the import transaction.
- **Saving.** `IImportStore` (implemented by `SqliteTaskStore.Import.cs`,
  DATA-MODEL §3.6) previews in a rolled-back transaction and applies in one
  writer transaction. Completed Graph downloads and final attachment copies
  receive Internet-zone metadata on Windows filesystems supporting named
  streams, without source URLs or account details. Ordinary marking failures
  use the existing owned-file rollback cleanup. Attachment opening retains
  Windows Launcher and requests `TreatAsUntrusted`; see
  [the provenance policy](docs/plans/dep10-attachment-provenance.md).
- **Onboarding.** `ShellViewModel` shows Welcome when `onboarding.completed`
  is unset and the database has no tasks and only the default list. Starting
  fresh, importing, or navigating away sets the key. Launches with
  `--database-path` skip onboarding unless `--show-onboarding` is passed. The
  Debug-only `--import-fixture <dir>` serves recorded Graph responses through
  `FixtureGraphHandler` instead of signing in; adding
  `--import-fixture-expire-once` makes the first session's attachment
  downloads return 401, like a sign-in that expired.

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
| §5: `Wintodo.Data` / `Wintodo.Core` only | Plus `Wintodo.Sync` (M13) | Planned in M13, missing from the §5 tree. |
| §1: built "instead of Microsoft Graph" | `Wintodo.Import` reads Graph once, read-only, to import from Microsoft To Do | ADR-016. Graph is never storage or sync. |
