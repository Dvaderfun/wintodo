# Wintodo — Implementation Plan

> **Audience:** any implementing agent (see `AGENTS.md`) and the human owner.
> **Status:** approved plan, in progress. M0–M9 and M13 implemented (some gate rows
> still open); M10 release preparation is in progress. M11–M12 are outside this release.
> Current state: top of `docs/PROGRESS.md`. Code as built: `ARCHITECTURE.md`.
> Restructured by [ADR-010](docs/adr/ADR-010-ai-friendly-repo-docs.md): section
> numbers are stable; moved sections are now pointers.

---

## 0. How to use this document

1. `AGENTS.md` owns the reading order and the hard rules. Read it first.
2. Read the milestone you are working on in §8 — its deliverables and gate —
   plus §1 (scope) and §7 (budgets) when relevant. You do not need the rest of
   this file to do a task.
3. Work milestone by milestone. **Do not skip ahead.** Each milestone has an
   acceptance gate. If the gate fails, fix before moving on.
4. At the end of every milestone: update `docs/PROGRESS.md` with what shipped,
   what deviated from this plan, and why.

`Wintodo` is the reserved Store name. The production package identity is
assigned by Partner Center and must stay stable across updates. Keep localized
display strings in `Strings/en-US/Resources.resw`.

---

## 1. Product definition

### What we are building

A native Windows task manager. **v1 is a faithful Microsoft To Do clone** —
same information architecture, same interaction model, same feature surface —
built on open standards instead of a proprietary Microsoft Graph backend.
After parity, we iterate beyond To Do.

### What we are NOT building (yet)

- No server. No account system. No cloud of our own.
- No collaboration/sharing (that's a CalDAV-server concern, arrives with M13).
- No AI features, no "suggestions" panel.
- No mobile, no web.

### v1 scope — Microsoft To Do parity

| Area | Must have in v1 |
|---|---|
| Lists | Create, rename, delete, reorder, per-list theme colour, optional emoji icon, list groups, one fixed default list ("Tasks") per account |
| Smart lists | My Day, Important, Planned, All, Completed |
| Tasks | Create, complete, uncomplete, delete, reorder, move to another list |
| Task fields | Title, steps (subtasks), note, due date, reminder, repeat, importance (star), "Added to My Day" |
| Views | Sort (importance / due date / alphabetical / creation date), group completed, show/hide completed |
| Search | Full-text across all lists |
| Reminders | Windows toast at reminder time, with Complete / Snooze actions |
| Interop | Import and export `.ics` (VTODO) per list |
| Shell | Mica window, custom title bar, light/dark/high-contrast, full keyboard operation, screen-reader clean |

### Post-parity direction (owner-chosen, in this order)

1. **Deep subtasks + project trees** (M11) — arbitrary nesting via `RELATED-TO`,
   tree view, promote/demote, roll-up progress.
2. **Tags + saved smart filters** (M12) — `CATEGORIES`-backed tags, a filter
   query language, user-saved views pinned into the nav pane.
3. **CalDAV sync** (M13) — sync against existing open servers.

---

## 2. Standards research and decisions

Researched July 2026. These are locked decisions; changing them requires a new ADR
in `docs/adr/`.

Each decision is its own file in `docs/adr/` (moved verbatim from this section
by ADR-010). Index and template: [`docs/adr/README.md`](docs/adr/README.md).

| ADR | Decision |
|---|---|
| [ADR-001](docs/adr/ADR-001-ical-vtodo-canonical-format.md) | Canonical task format: iCalendar `VTODO` (RFC 5545) |
| [ADR-002](docs/adr/ADR-002-caldav-webdav-sync.md) | Sync protocol (M13): CalDAV (RFC 4791) + WebDAV Sync (RFC 6578) |
| [ADR-003](docs/adr/ADR-003-jmap-rejected.md) | JMAP is rejected for now |
| [ADR-004](docs/adr/ADR-004-jscalendar-internal-model.md) | JSCalendar (RFC 8984) shapes the *internal* model, not the wire |
| [ADR-005](docs/adr/ADR-005-interop-conventions.md) | De-facto interop conventions we must honour |
| [ADR-006](docs/adr/ADR-006-recurrence-on-completion.md) | Recurrence handling on completion |
| [ADR-007](docs/adr/ADR-007-no-icalendar-library.md) | No iCalendar library dependency in v1 |
| [ADR-008](docs/adr/ADR-008-perf-budget-rebaseline.md) | Performance budgets re-baselined to the measured WinUI 3 floor |
| [ADR-009](docs/adr/ADR-009-list-emoji-icons.md) | Optional emoji icons for user lists |
| [ADR-010](docs/adr/ADR-010-ai-friendly-repo-docs.md) | AI-friendly repo layout, vendored skills, toolchain 0.7 |

---

## 3. Tech stack — locked

| Layer | Choice | Why |
|---|---|---|
| UI framework | **WinUI (WinUI 3)**, Windows App SDK **2.3.1+ stable** (2.5.1 since 2026-10-10) | Current native Windows stack. Microsoft now writes "WinUI" for what was WinUI 3. |
| Runtime | **.NET 10** (`net10.0-windows10.0.26100.0`) | Current LTS. Pin the exact SDK with its lockfiles ([ADR-020](docs/adr/ADR-020-pin-sdk-with-lockfiles.md)). |
| Language | C# 14 | Partial properties (required by the modern `[ObservableProperty]` pattern), collection expressions. |
| Packaging | **Packaged MSIX**, x64 + ARM64 | Required for `winapp run`, toasts, jump lists, badges, Store. |
| MVVM | `CommunityToolkit.Mvvm` 8.4+ | Source-generated, zero reflection, AOT-clean. Use **partial properties**, not the legacy field-backed `[ObservableProperty]` — the WinUI analyzer flags the old form. |
| DI | `Microsoft.Extensions.DependencyInjection` | Explicit registration only. **No assembly scanning** (reflection = AOT break). *As built: manual constructor wiring in `App.OnLaunched`, no container — see `ARCHITECTURE.md`.* |
| Storage | `Microsoft.Data.Sqlite` + `SQLitePCLRaw.bundle_e_sqlite3` | Raw ADO.NET. Trim/AOT compatibility fixed in the .NET 8 wave. |
| ORM | **None.** Hand-written mappers. | EF Core is reflection-heavy, costs startup time and tens of MB of working set. This app has ~8 tables. |
| Search | SQLite **FTS5** | Ships in `bundle_e_sqlite3`. No extra dependency, no in-memory index. |
| JSON | `System.Text.Json` **source-generated contexts only** | Settings + export only. `JsonSerializerContext`, never reflection-based overloads. |
| Extra controls | `CommunityToolkit.WinUI.Controls.*` (Segmented, SettingsControls) | v8.2+ is AOT-clean. Pull individual packages, never the metapackage. |
| Tests | `xunit` (v3 on Microsoft Testing Platform since 2026-10-10) + `winapp ui` batch scripts | Unit tests for Core/Data; UI automation for the shell. |

**Banned dependencies** (each breaks AOT, RAM, or both): `Newtonsoft.Json`,
EF Core, AutoMapper, any `Activator.CreateInstance`-based factory, any
DI container with assembly scanning, `System.Reactive`, MahApps/WPF-era libraries,
any package that has not shipped a build in the last 12 months.

### Runtime/deployment profile — the RAM answer

The state-of-the-art 2026 WinUI answer is: **build framework-dependent for the
inner loop, enforce AOT-clean discipline from commit 1, flip to Native AOT +
self-contained at M9 and gate on measured numbers.**

Native AOT for WinUI has been supported since Windows App SDK 1.6 (`PublishAot`,
via the CsWinRT source generator). Microsoft's own AOT'd WinUI apps report
40–55% cold-start reduction and dramatically smaller working sets (Notepad:
under ~20 MB after a clean launch, down from ~50 MB). Those wins are only
available if **nothing in the codebase uses reflection** — which is why the
discipline starts on day one, not at M9.

Enforced from M0 in `Directory.Build.props`:

```xml
<PropertyGroup>
  <IsAotCompatible>true</IsAotCompatible>
  <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
  <EnableAotAnalyzer>true</EnableAotAnalyzer>
  <EnableSingleFileAnalyzer>true</EnableSingleFileAnalyzer>
  <TrimmerSingleWarn>false</TrimmerSingleWarn>
  <Nullable>enable</Nullable>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  <InvariantGlobalization>false</InvariantGlobalization>
</PropertyGroup>
```

`IsAotCompatible` goes on `Wintodo.Core` and `Wintodo.Data` (class libraries).
The WinUI app project sets `PublishAot` only under the `Release` configuration
from M9 onward.

---

## 4. Machine setup (M0 prerequisite work)

Moved and updated by ADR-010:

- Prerequisites, build, run, AOT publish: [`docs/guides/build-and-run.md`](docs/guides/build-and-run.md).
- WinUI skills are vendored in the repo at a pinned win-dev-skills tag
  (`.agents/skills/`, mirrored to `.claude/skills/`), refreshed with
  `scripts/Update-AgentSkills.ps1`: [`docs/guides/ai-tooling.md`](docs/guides/ai-tooling.md).
- Toolchain: .NET SDK 10.0.4xx, WinApp CLI **0.7+**, Developer Mode (ask the
  human before triggering UAC). Visual Studio is optional.

---

## 5. Repository layout

The current tree, with what each folder holds, is in
[`ARCHITECTURE.md`](ARCHITECTURE.md#projects). The original M0 tree is in git
history. Since M0 the repo also gained `src/Wintodo.Sync/` (M13),
`tests/Wintodo.Sync.Tests/`, `docs/guides/`, `docs/README.md`, `ARCHITECTURE.md`,
`CLAUDE.md`, `.agents/skills/`, `.claude/skills/`, `.mcp.json` and `.github/workflows/`.

Rules: `Wintodo.Core` references nothing but the BCL. `Wintodo.Data` references
`Wintodo.Core` + `Microsoft.Data.Sqlite`. `Wintodo.Sync` references `Wintodo.Core`
only. `Wintodo.App` references all three. No project references point backwards.
UI types never appear below `Wintodo.App`.

---

## 6. Architecture

This section is the design rationale. The code as built, including where it
diverges from this section, is [`ARCHITECTURE.md`](ARCHITECTURE.md).

### 6.1 Shell composition

One `MainWindow` hosting one `ShellPage`. `ShellPage` is a 3-column `Grid`:

```
┌──────────────── TitleBar (Microsoft.UI.Xaml.Controls.TitleBar) ─────────────────┐
│  icon │ title            │  [ AutoSuggestBox — search ]      │ caption buttons  │
├─────────────┬──────────────────────────────────┬────────────────────────────────┤
│ NavigationView│  TaskListView                  │  TaskDetailView                │
│ pane (300px)  │  (*)                           │  (360px, x:Load deferred)      │
│               │                                │                                │
│ search-scoped │  header / composer / list      │  steps, dates, note            │
│ smart lists   │  completed section             │                                │
│ user lists    │                                │                                │
│ + New list    │                                │                                │
└─────────────┴──────────────────────────────────┴────────────────────────────────┘
```

**Do not use `Frame` navigation to switch lists.** Selecting a different list
swaps the `TaskListViewModel`'s bound collection. One `TaskListView` instance
lives for the app's lifetime. `Frame` navigation is used only for Settings.
This is a deliberate memory and latency decision — page instances are the single
biggest avoidable allocation source in a WinUI shell.

### 6.2 Layers

```
View (XAML, x:Bind only)
  ↓ commands / OneWay+TwoWay bindings
ViewModel (CommunityToolkit.Mvvm, partial properties, no UI types beyond DispatcherQueue)
  ↓ ITaskStore
Wintodo.Data.SqliteTaskStore  (all IO on a background thread)
  ↓ Microsoft.Data.Sqlite
SQLite file: %LOCALAPPDATA%\Packages\<PFN>\LocalState\wintodo.db
```

### 6.3 Data access rules

- One writer `SqliteConnection`, opened once, kept open, WAL mode. Writes are
  serialised through a `SemaphoreSlim(1,1)`.
- Reads use a small pool of read-only connections (`Mode=ReadOnly;Cache=Shared`).
- Every statement is a prepared, parameterised `SqliteCommand`. Cache the
  commands for hot queries; never build SQL with string concatenation.
- Pragmas on open: `journal_mode=WAL`, `synchronous=NORMAL`, `foreign_keys=ON`,
  `busy_timeout=5000`, `temp_store=MEMORY`.
- All store methods are `async` and run on the thread pool. Marshal back with
  `DispatcherQueue.TryEnqueue` — never `Dispatcher`, never `CoreDispatcher`.

### 6.4 Collection updates

Never `Clear()` + refill an `ObservableCollection` bound to a `ListView` — it
destroys and recreates every container. Implement a small diffing updater
(`ObservableCollectionExtensions.ApplyDiff`) keyed on task `Uid` that issues
minimal Insert/Remove/Move/Replace notifications. This is the difference between
a smooth 60 fps list and a visible hitch on every keystroke in search.

### 6.5 Change notification

`ITaskStore` raises a coalesced `StoreChanged` event carrying a change set
(list ids + task uids touched). ViewModels subscribe and re-query only affected
views. No global "reload everything" path.

---

## 7. Performance and memory budget

These are **gates**, not aspirations. M9 fails if they are not met, and every
milestone from M3 onward appends a measurement row to `docs/PERF.md`.

The startup and working-set rows were re-baselined by
[ADR-008](docs/adr/ADR-008-perf-budget-rebaseline.md) against the measured
WinUI 3 + self-contained Windows App SDK floor. Read that ADR before changing
them again. The framework-dependent build is a development-loop build from M9
onward: its numbers are recorded in `docs/PERF.md` but are not gated.

| Metric | Native AOT (gated) | Hard fail |
|---|---|---|
| App startup work, first frame → first interactive frame, cold | ≤ 700 ms | > 1 000 ms |
| App startup work, first frame → first interactive frame, warm | ≤ 550 ms | > 800 ms |
| Cold start → first interactive frame (total) | ≤ 3 000 ms | > 3 500 ms |
| Warm start → first interactive frame (total) | ≤ 2 300 ms | > 2 800 ms |
| Idle working set at +30 s, empty DB | ≤ 165 MB | > 190 MB |
| Idle working set at +30 s, 5 000 tasks / 20 lists | ≤ 200 MB | > 225 MB |
| Idle working set at +30 s, 20 000 tasks / 20 lists | ≤ 205 MB | > 230 MB |
| Working set at +10 min | ≤ the +30 s sample + 10 MB | any monotonic growth |
| List query, 10 000 tasks | ≤ 5 ms | > 20 ms |
| Search keystroke → results | ≤ 60 ms | > 150 ms |
| Scroll 10 000 tasks | 60 fps, zero per-frame allocations | any dropped frames |
| Installed size (framework pkg) | ≤ 260 MB self-contained | — |

The two startup rows are separate gates on purpose. Total startup is dominated
by first-use XAML runtime and window construction, which app code cannot move;
it is kept only as a ceiling against gross regressions. The first-frame →
interactive segment is the app's own work — store initialisation, shell
hydration, initial query — and is the row that a real regression trips.

### Rules that produce those numbers

1. `{x:Bind}` everywhere, explicit `Mode`. **Zero `{Binding}`. Zero
   `IValueConverter`** — use `x:Bind` to static functions instead.
2. `x:Load` (deferred loading) on: the detail pane, every `ContentDialog`, the
   completed-tasks section, every flyout body, the empty-state panels.
3. `ListView` virtualisation stays on: no `ScrollViewer` wrapper, no
   `Height="Auto"` on the list, no nested scrolling.
4. Row templates stay flat — target ≤ 12 elements per `TaskRow`. Every element
   is ~1 KB of working set × visible rows × recycling headroom.
5. Icons are `FontIcon` glyphs from `SymbolThemeFontFamily`. **No PNG/SVG assets
   in list rows.** Bitmaps only for the app icon and per-list background themes.
6. `Frame.CacheSize = 1`. Settings page is disposed on navigate-away.
7. No LINQ in per-item render or per-keystroke paths. No `string.Format` in
   `DataTemplate` bindings — precompute display strings in the item ViewModel.
8. Item ViewModels are `sealed`, allocated once per task, reused across
   re-queries (keyed by `Uid`).
9. Timers: exactly one `DispatcherQueueTimer` for the app (midnight rollover for
   My Day). Reminders use OS-scheduled toasts, not in-process timers.
10. `GC.Collect` is banned. `ServerGarbageCollector` is off (default for desktop).
    Set `<TieredPGO>true</TieredPGO>`.
11. Every `IDisposable` from SQLite is disposed. `Wintodo.Data.Tests` includes a
    leak test asserting connection/command counts return to baseline.

### Measurement

`scripts/Measure-Perf.ps1` must:
- launch via `winapp run`, sample `WorkingSet64` and `PrivateMemorySize64` at
  +2 s, +30 s, +10 min,
- time cold start by stamping first-frame in `App.OnLaunched` → written to the
  debug output stream that `winapp run --debug-output` captures,
- seed the DB from `tests/fixtures/perf/5k-tasks.sql` for the loaded runs,
- append a dated row to `docs/PERF.md`.

---

## 8. Milestones

Each milestone: **Goal → Deliverables → Skills to invoke → Acceptance gate.**
A milestone is done only when its gate passes. Record the result in
`docs/PROGRESS.md`.

Tooling named in completed milestones (`winui-search.exe`, installing skills into
Codex, `BuildAndRun.ps1` flags) is as it was at the time. Current commands and
skills are in `AGENTS.md` and `docs/guides/`.

---

### M0 — Setup and scaffold

**Goal:** a building, running, empty WinUI app with the repo skeleton and rules
in place.

**Deliverables**
- `win-dev-skills` installed into Codex; `/winui-setup` run green.
- `dotnet new winui-mvvm -n Wintodo` inside `src/Wintodo.App`
  (note: `-n` creates the folder — do **not** `mkdir` first).
- `Wintodo.Core`, `Wintodo.Data`, and both test projects created and referenced.
- `Directory.Build.props`, `Directory.Packages.props`, `global.json`,
  `.editorconfig`, `.gitignore`.
- `git init`, first commit.
- `AGENTS.md`, `docs/PROGRESS.md`, `docs/PERF.md` skeletons.

**Skills:** `/winui-setup`, then `/winui-dev-workflow`.

**Gate**
- `.\BuildAndRun.ps1` (invoked async) builds and launches the template window.
- `winapp run` output shows no first-chance exceptions.
- `dotnet build -warnaserror` clean on all five projects.
- `git log` has one commit; working tree clean.

---

### M1 — Domain model and SQLite store

**Goal:** the whole data layer, headless and unit-tested, before any UI exists.

**Deliverables**
- `Wintodo.Core/Models/*` exactly per `docs/DATA-MODEL.md` §2.
- `Wintodo.Core/Recurrence/RecurrenceRule.cs` — parse/serialise `RRULE`
  (FREQ, INTERVAL, BYDAY, BYMONTHDAY, BYMONTH, COUNT, UNTIL, WKST) and
  `ComputeNext(DateTimeOffset from)`.
- `Wintodo.Data/Migrations/001_init.sql` per `docs/DATA-MODEL.md` §3, embedded.
- `SqliteTaskStore` implementing `ITaskStore`: lists CRUD, tasks CRUD, steps,
  reorder, move, complete (incl. recurrence advance per ADR-006), smart-list
  queries, FTS5 search, change events.
- `Wintodo.Core.Tests` + `Wintodo.Data.Tests`.

**Gate**
- ≥ 80% line coverage on `Wintodo.Core` and `Wintodo.Data`.
- Recurrence tests cover: daily, weekdays, weekly-by-day, monthly-by-date,
  monthly-by-nth-weekday, yearly, every-N, `COUNT` exhaustion, `UNTIL`,
  DST boundary crossing, 29 Feb.
- A 10 000-task seeded DB returns any smart-list query in ≤ 5 ms
  (assert in a test, not by eyeball).
- Migration runs twice with no error (idempotent) and `schema_version` = 1.
- Zero trim/AOT analyzer warnings in both libraries.

---

### M2 — Shell: window, title bar, Mica, navigation

**Goal:** the app frame, correct materials, correct theming, no task features.

**Deliverables**
- `MainWindow`: `MicaBackdrop` (`Kind="Base"`), `ExtendsContentIntoTitleBar`,
  `Microsoft.UI.Xaml.Controls.TitleBar` with icon, title, and a centred
  `AutoSuggestBox` (max width 460).
- Window sized in the constructor per `docs/DESIGN.md` §1 — remember
  `AppWindow.Resize` takes **physical pixels**, so scale by
  `GetDpiForWindow(hwnd) / 96.0`. There is no `SizeToContent` in WinUI.
- `ShellPage` 3-column grid, `NavigationView` (`PaneDisplayMode="Left"`,
  `OpenPaneLength="300"`), smart lists, the default list, and every user list as
  flat `MenuItems` separated by a `NavigationViewItemSeparator` — no "Lists"
  parent node — "New list" in `PaneFooter`, Settings via `IsSettingsVisible`.
- `Themes/Colors.xaml` with `Light`, `Dark`, **and** `HighContrast` dictionaries
  — never `x:Key="Default"`.
- Window size/position and last-selected list persisted to the settings table.
- Responsive breakpoints per `docs/DESIGN.md` §6 via `VisualStateManager`
  (never code-behind property setting).

**Skills:** `/winui-design` — and run its bundled `winui-search.exe` **before**
writing XAML. Front-load one batched `search` call covering NavigationView,
TitleBar, MicaBackdrop, AutoSuggestBox, and adaptive layout; `get` the best 3
IDs; then write XAML from those samples.

**Gate**
- Renders correctly in Light, Dark, and High Contrast (verify by switching the
  OS theme while running — `{ThemeResource}` must update live).
- High-contrast dictionaries contain only the 8 permitted `SystemColor*` brushes.
- Zero hardcoded colours, zero hardcoded `CornerRadius`, zero raw `FontSize`.
- Every spacing/size value is a multiple of 4.
- Resize from 1180 px down to 640 px: no clipping, no horizontal scrollbar,
  breakpoints fire.
- Tab order reaches every interactive element; focus visuals visible in all
  three themes.

---

### M3 — Lists and the task list

**Goal:** the core loop — make a list, add tasks, complete them.

**Deliverables**
- List CRUD: create (inline-editable new row in the pane), rename (F2 / context
  menu), delete (`ContentDialog` confirm), reorder (drag within the pane),
  per-list theme colour picker, optional emoji icon chosen beside the title
  editor (with a custom-entry route in the list menu), list groups (create, drag
  lists into/out of).
- A default list ("Tasks", `lists.is_default`) created on first run: pinned
  directly below the smart lists and above the divider, never renameable,
  deletable, groupable, or reorderable. It is the composer target on every smart list, because
  `tasks.list_id` is `NOT NULL` and a listless task has no CalDAV collection to
  live in. Picking another list in the composer applies to that one task.
- `TaskListView`: header (emoji picker + title + list menu),
  `AddTaskComposer`, `ListView` of `TaskRow`, completed section. Navigation list
  rows show a zero-hidden root-task count for every fixed and user category;
  Completed counts completed roots and the other categories count incomplete
  roots matching their filter.
- `TaskRow` exactly per `docs/DESIGN.md` §3: completion circle, title,
  metadata line, importance star.
- Complete / uncomplete with the animation spec in `docs/DESIGN.md` §7.
- Delete with a 5-second undo snackbar.
- Drag-to-reorder tasks (`CanReorderItems`, `AllowDrop`, `CanDragItems`)
  persisting `sort_order`.
- Move task to another list (context menu + drag onto a nav-pane list).

**Gate**
- Create list → add 5 tasks → complete 2 → reorder → restart app → state exact.
- 10 000-task list scrolls at 60 fps (measure; do not assume).
- Every interactive element has `AutomationProperties.AutomationId` following
  `Todo.<Area>.<Element>` (e.g. `Todo.List.AddTaskBox`, `Todo.Row.CompleteToggle`).
- `docs/PERF.md` has its first real row.

---

### M4 — Task detail pane

**Goal:** everything you can do to a single task.

**Deliverables**
- `TaskDetailView`, `x:Load`-deferred, 360 px, slide-in per `docs/DESIGN.md` §7.
- Title (multi-line `TextBox`, `TwoWay`, **with
  `UpdateSourceTrigger=PropertyChanged`** — without it UIA `set-value` and
  programmatic edits never reach the ViewModel).
- Steps: add, edit, complete, delete, reorder. Stored as child tasks with
  `RELATED-TO;RELTYPE=PARENT` (this is the M11 foundation — build it right now).
- Action rows: Add to My Day · Remind me · Add due date · Repeat · Add file.
- Date/time pickers via `DropDownButton` + `MenuFlyout` quick options
  (Today / Tomorrow / Next week / Pick a date) opening a
  `CalendarDatePicker` + `TimePicker`.
- Repeat editor: Daily / Weekdays / Weekly / Monthly / Yearly / Custom
  (every N period, by weekday) → writes `RRULE`.
- Note field (multi-line, autosave on debounce).
- File attach: copy into `LocalState\attachments\<uid>\`, list with size, open,
  remove. Enforce the 25 MB per-task cap To Do uses.
- Footer: "Created on …" + delete.

**Gate**
- Every field round-trips through SQLite and survives restart.
- Recurrence: set "every 2 weeks on Tue", complete it, verify the due date
  advances exactly 14 days and `completion_history` gained a row.
- Detail pane XAML is not instantiated until first open — verify via the live
  visual tree or an instrumented counter.
- Keyboard: `Enter` in a step field creates the next step; `Esc` closes the pane.

---

### M5 — Smart lists, search, sort, group

**Goal:** the rest of the To Do information architecture.

**Deliverables**
- My Day (with midnight reset — a single timer, plus a check on resume/activate),
  Important, Planned (grouped Overdue / Today / Tomorrow / This week / Later),
  All, Completed.
- Search: FTS5-backed, results grouped by list, highlighting the match,
  `Esc` clears, results update within 60 ms of a keystroke (debounce 120 ms).
- Sort menu per list: Importance / Due date / Alphabetical / Creation date,
  ascending/descending, persisted per list.
- Show/hide completed; completed count in the section header.
- "Add to My Day" suggestions panel is **out of scope** — do not build it.

**Gate**
- My Day empties at local midnight while the app stays open (test by moving the
  clock or by injecting `IClock`).
- A task due yesterday shows in Planned → Overdue with
  `SystemFillColorCriticalBrush`.
- Search across 10 000 tasks returns in ≤ 60 ms.
- Sort choice survives restart, per list.

---

### M6 — iCalendar interop (`.ics` import/export)

**Goal:** prove the `VTODO` mapping before any network code exists. This is the
de-risking milestone for M13.

**Deliverables**
- `Wintodo.Core/Ical/IcalReader.cs` + `IcalWriter.cs` per ADR-007.
- Correct line folding/unfolding at 75 octets, `\n \, \;` escaping,
  parameter parsing, `DATE` vs `DATE-TIME` vs `TZID`, `VALARM`.
- **Verbatim passthrough**: any property we don't model is stored in
  `tasks.ical_unknown` and re-emitted byte-identical on export.
- Export: per-list `.ics`, and "export everything" as a zip of `.ics` files.
- Import: file picker → preview dialog (N tasks, M lists, conflicts) → apply.
- Fixture corpus in `tests/fixtures/ical/` — exports from Nextcloud Tasks,
  Tasks.org, Thunderbird, and Apple Reminders.

**Gate**
- **Round-trip test:** every fixture file, imported then exported, is
  semantically identical (property-set equality, ignoring `PRODID`,
  `DTSTAMP`, and property order).
- Unknown properties survive round-trip byte-for-byte.
- Export a list, import it into Nextcloud Tasks or Thunderbird by hand
  (human-verified once), tasks appear with correct dates, priority, subtasks,
  and recurrence.
- Malformed input produces a clear error dialog, never a crash and never a
  partial import (import is one transaction).

---

### M7 — Reminders and Windows integration

**Status:** IMPLEMENTED; GATE PARTIAL (resumed 2026-08-01). Debug and Release
x64 builds, the full Core/Data suite, and the isolated Windows-integration
preflight pass. Real app-closed Complete and Snooze toast actions also pass.
Reboot persistence and visible taskbar-badge confirmation remain, so the
milestone is not reported as fully complete.

**Goal:** the app behaves like a first-class Windows citizen.

**Deliverables**
- Reminder toasts. **Important:** the Windows App SDK `AppNotificationManager`
  has **no scheduling API**. The supported approach for a packaged app is to
  build the payload with `AppNotificationBuilder`, then schedule it via the
  UWP `ScheduledToastNotification` +
  `ToastNotificationManager.CreateToastNotifier().AddToSchedule(...)`, setting
  `Tag`/`Group` to the task `Uid` so it can be cancelled or rescheduled.
  Register the `AppNotificationManager` first, and declare the COM activator
  CLSID + executable in `Package.appxmanifest`
  (`desktop:Extension` + `com:Extension`).
- Toast actions: **Complete** and **Snooze** (10 min / 1 h / tomorrow), handled
  through `AppNotificationManager.NotificationInvoked` including the
  app-not-running activation path.
- Rescheduling sweep on launch and on resume (drift, missed reminders while the
  machine was off, and daylight-saving shifts).
- Taskbar badge = overdue + due-today count (`BadgeUpdateManager`).
- Jump list: "New task", plus the 3 most recent lists
  (`Windows.UI.StartScreen.JumpList`).
- Optional launch-at-login via `StartupTask` (off by default, a Settings toggle).
- Share target: accept text → create a task.

**Gate**
- Set a reminder 2 minutes out, close the app, toast fires, **Complete** from
  the toast marks the task done and the change is visible on next launch.
- Snooze reschedules correctly and cancels the old scheduled toast.
- Set a reminder, reboot, reminder still fires.
- Badge count matches the UI.

---

### M8 — Design polish and accessibility

**Goal:** it stops looking like a prototype.

**Deliverables**
- Full pass against `docs/DESIGN.md` — every spacing, brush, radius, type style.
- Motion pass: list-switch transition, detail-pane slide, row add/remove,
  completion animation, all respecting `UISettings.AnimationsEnabled`
  (reduce-motion).
- Empty states for every list and every smart list.
- Per-list theme colours applied to header, completion circles, and the nav-pane
  indicator.
- Per-list and fixed-category background images (the To Do "themes" feature) —
  decode at display size, never full resolution. Fixed-category glyphs use the
  selected theme colour, with a Settings preference to use neutral glyphs.
- Full keyboard map per `docs/DESIGN.md` §8, discoverable via a shortcuts dialog.
- Screen reader pass with Narrator: every row announces title, due date, and
  completion state; completing announces the state change via
  `AutomationPeer.RaiseNotificationEvent`.

**Skills:** `/winui-design`, then `/winui-ui-testing` (generate one batch script
covering assertions, interactions, and the accessibility audit — run it in a
single pass), then `/winui-code-review`.

**Gate**
- `winapp ui` accessibility audit: zero errors.
- Narrator can create, complete, and delete a task without touching the mouse.
- High-contrast screenshots reviewed; nothing invisible, nothing hardcoded.
- `/winui-code-review` returns no MVVM, `x:Bind`, theming, or accessibility
  findings above informational.

---

### M9 — Performance gate: Native AOT

**Goal:** hit the numbers in §7.

**Deliverables**
- `<PublishAot>true</PublishAot>`, `<SelfContained>true</SelfContained>`,
  `<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>` under Release.
- `scripts/Publish-Aot.ps1` — publish x64 + ARM64, report binary and package size.
- All trim/AOT warnings resolved. **If a dependency cannot be made AOT-clean,
  remove the dependency** — do not add `[DynamicDependency]` band-aids or
  suppress the warning.
- `scripts/Measure-Perf.ps1` results appended to `docs/PERF.md` for both
  framework-dependent and AOT builds, at 0 / 5 000 / 20 000 tasks.

**Gate**
- Every gated §7 number met, as re-baselined by ADR-008. If one is missed,
  profile and fix — do not relax the budget without a further ADR.
- The AOT build passes the entire `winapp ui` suite from M8 (AOT changes
  behaviour: anything reflective that survived will fail here, loudly).

---

### M10 — Packaging and release

Selected first production route: [ADR-019](docs/adr/ADR-019-store-first-source-available-release.md)
uses Store MSIX under Univera LLC, Microsoft-managed Store signing, public
source after review and free self-builds with commercial redistribution reserved.
Development test signatures remain groundwork; direct production signing and
downloads are a separate later route. The Store identity and `1.0.0.0` are in
the manifest. Rights, Store certification and customer/runtime gates remain pending.

**Deliverables**
- `winapp manifest` reviewed; identity, publisher, capabilities minimal
  (no `broadFileSystemAccess`).
- Obtain Microsoft-signed packages from the approved Store submission. A historical
  local test signature does not establish the customer signing path.
- GitHub Actions: build + test + x64/ARM64 AOT/package evidence, approved Store
  upload preparation and reviewed source-release metadata. Bind actual
  Store-signed delivery to those inputs before publication.
- App icons at every required size; Store-quality assets.

**Skills:** `/winui-packaging`.

**Gate**
- The actual Store-signed MSIX installs from an approved clean customer baseline
  (no Developer Mode or inherited test-certificate trust) and launches. Test
  x64 and native ARM64, plus updates preserving tasks/attachments/settings and
  credential access under the stable production family.
- Uninstall leaves nothing behind outside the package data folder.

---

### M11 — Deep subtasks and project trees *(post-parity #1)*

**Goal:** go beyond To Do's flat one-level "steps".

**Deliverables**
- Arbitrary nesting on the existing `RELATED-TO;RELTYPE=PARENT` foundation
  (built in M4 — no migration needed).
- Tree rendering in the list: indent guides, collapse/expand per node,
  persisted expansion state.
- Promote / demote via `Tab` / `Shift+Tab` and drag.
- Roll-up: parent shows `n/m` children complete; optional
  "complete children when parent completes" and
  "complete parent when last child completes" — both settings, both off by
  default.
- Cycle prevention on re-parent.
- Interop note surfaced in the UI: Apple Reminders flattens deep hierarchies.

**Gate**
- 5-level-deep tree renders, reorders, and round-trips through `.ics`.
- A cycle attempt is rejected with a clear message, never a hang.
- 10 000 tasks with 4 000 parent/child edges still meets the §7 query budget.

---

### M12 — Tags and saved smart filters *(post-parity #2)*

**Deliverables**
- Tags stored as `CATEGORIES`, edited inline in the composer (`#tag`) and in the
  detail pane; tag autocomplete; rename/merge/delete a tag globally.
- Filter query language:
  `list:Work tag:urgent due:<7d is:incomplete priority:>=high text:"invoice"`
  — parsed into a `QueryDescriptor` in `Wintodo.Core/Queries`, compiled to
  parameterised SQL. Never string-concatenate user input into SQL.
- Saved views: name it, pin it to the nav pane, reorder alongside lists.
- Tag chips in rows; a tag browser view.

**Gate**
- Every query operator has a unit test, including precedence and malformed input.
- A saved view survives restart and updates live as tasks change.
- SQL injection attempt through the query box is impossible by construction
  (test it).

---

### M13 — CalDAV sync *(post-parity #3)*

**Deliverables**
- `Wintodo.Sync` project: discovery, collection enumeration, `sync-collection`
  `REPORT` with token, ETag-based conditional `PUT`/`DELETE`, ctag fallback.
- Credentials in the Windows Credential Locker
  (`Windows.Security.Credentials.PasswordVault`) — **never** in the DB, never in
  a settings file.
- Local change log (already in the schema from M1) drives push; pull applies
  server state; conflicts surface a chooser (keep mine / keep theirs / keep both).
- Per-list sync toggle, sync status in the nav pane, manual "Sync now",
  background sync on a timer + on resume.
- Tested against **Radicale** (local, `pip install radicale`, fastest loop) and
  **Nextcloud** (Docker) at minimum; Baïkal and Fastmail if available.

**Gate**
- Create/edit/complete/delete on either side converges within one sync cycle.
- Offline edits queue and push on reconnect.
- Simultaneous edit on both sides produces a conflict prompt, never silent
  data loss.
- Unknown properties written by other clients survive our round-trip
  (re-verify the M6 corpus against live servers).

---

## 9. Testing strategy

| Layer | How |
|---|---|
| `Wintodo.Core` | xunit. Pure functions — recurrence, iCalendar, query parsing. Aim high on coverage; these are where the real bugs live. |
| `Wintodo.Data` | xunit against a temp-file SQLite DB per test. Includes perf assertions and a disposal-leak test. |
| ViewModels | xunit with a fake `ITaskStore` and a fake `IClock`. Never touch the dispatcher in tests. |
| UI | `winapp ui` batch scripts under `tests/ui/`, generated via `/winui-ui-testing`. One batch per feature area, run in a single pass. |
| Accessibility | `winapp ui` accessibility audit, gated at zero errors from M8 onward. |
| Perf | `scripts/Measure-Perf.ps1`, logged to `docs/PERF.md` every milestone from M3. |

Inject `IClock` everywhere a date matters. Nothing calls `DateTimeOffset.Now`
directly outside the clock implementation — otherwise My Day, recurrence, and
reminders become untestable.

---

## 10. Risks and open items

| Risk | Mitigation |
|---|---|
| ~~XAML compiler under `dotnet build` gives no diagnostic on malformed XAML~~ | Retired by ADR-010: fixed in Windows App SDK 2.1.3; we are on 2.5.1. |
| `win-dev-skills` is preview v0.x; skill names and tooling can change (0.7 removed `BuildAndRun.ps1` and the bundled analyzer) | Skills are vendored at a pinned tag; bump deliberately with `scripts/Update-AgentSkills.ps1` after reading the changelog. Map by description if a name doesn't resolve. |
| Native AOT surfaces a broken dependency late | `IsAotCompatible` + analyzers from M0; a weekly `dotnet publish -p:PublishAot=true` check from M1. |
| Scheduled toasts are a UWP-API workaround, not a first-class WinAppSDK feature | Isolate behind `IReminderScheduler`; if the SDK ships a real scheduling API, swap one implementation. |
| Hand-rolled iCalendar parser has edge cases | Real-world fixture corpus + strict round-trip test at M6, long before network sync. |
| Recurrence semantics differ per client | ADR-006 documents our choice; verify against live servers at M13, revisit only with evidence. |
| Per-list background images blow the memory budget | Decode at display size (`BitmapImage.DecodePixelWidth`), cap dimensions, one cached image at a time. |

**Open — needs the owner:** source/asset rights, effective repository protection,
controlled Store submission and customer acceptance. Wintodo's Store name and
[package family](docs/plans/deployment-store-first-preparation.md#owner-supplied-identity-handoff) are reserved.

---

## 11. Reference index

| Topic | Where |
|---|---|
| Windows App SDK 2.x release notes | https://learn.microsoft.com/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-2-0 |
| What's new for Windows developers | https://learn.microsoft.com/windows/apps/whats-new/whats-new-for-developers |
| `win-dev-skills` | https://github.com/microsoft/win-dev-skills |
| WinApp CLI | https://github.com/microsoft/winappcli |
| Microsoft Learn MCP server | https://learn.microsoft.com/api/mcp |
| WinUI title bar / `TitleBar` control | https://learn.microsoft.com/windows/apps/develop/title-bar |
| Materials (Mica / Acrylic) | https://learn.microsoft.com/windows/apps/develop/ui/materials |
| Windows app performance docs | https://learn.microsoft.com/windows/apps/develop/performance/ |
| Scheduled app notifications | https://learn.microsoft.com/windows/apps/develop/notifications/app-notifications/scheduled-toast |
| Segoe Fluent Icons | https://learn.microsoft.com/windows/apps/design/style/segoe-fluent-icons-font |
| RFC 5545 — iCalendar (`VTODO`) | https://www.rfc-editor.org/rfc/rfc5545 |
| RFC 4791 — CalDAV | https://www.rfc-editor.org/rfc/rfc4791 |
| RFC 6578 — WebDAV Sync | https://www.rfc-editor.org/rfc/rfc6578 |
| RFC 9074 — VALARM extensions | https://www.rfc-editor.org/rfc/rfc9074 |
| RFC 8984 — JSCalendar | https://www.rfc-editor.org/rfc/rfc8984 |
| JSCalendar ↔ iCalendar conversion (draft) | https://datatracker.ietf.org/doc/draft-ietf-calext-jscalendar-icalendar/ |
| Tasks.org CalDAV docs (interop conventions) | https://tasks.org/docs/caldav_intro.html |
| Nextcloud Tasks | https://github.com/nextcloud/tasks |
| Radicale (test server) | https://radicale.org |
