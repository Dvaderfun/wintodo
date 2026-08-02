# Wintodo — Implementation Plan

> **Audience:** OpenAI Codex (GPT 5.6 Sol) acting as the implementing agent.
> **Working directory:** `the repository root`.
> **Status:** approved plan, not yet started. Nothing exists in the repo yet.

---

## 0. How to use this document

1. Read this file end to end **before** running any command.
2. Read `AGENTS.md` (operating rules — hard constraints, never violate).
3. Read `docs/DESIGN.md` (visual spec) before writing any XAML.
4. Read `docs/DATA-MODEL.md` before writing any storage or model code.
5. Work milestone by milestone (M0 → M13). **Do not skip ahead.** Each milestone
   has an acceptance gate. If the gate fails, fix before moving on.
6. At the end of every milestone: update `docs/PROGRESS.md` with what shipped,
   what deviated from this plan, and why.

`Wintodo` is a **placeholder name**. Final name is undecided. It appears as the
root namespace, the solution name, the MSIX identity, and the window title.
When the name is chosen, a single case-sensitive find-replace of `Wintodo` /
`wintodo` across the repo plus a regenerated MSIX identity is the whole rename.
**Do not scatter the name into strings** — put every user-visible occurrence in
`Strings/en-US/Resources.resw` from M2 onward.

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
| Lists | Create, rename, delete, reorder, per-list theme colour, list groups, one fixed default list ("Tasks") per account |
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

### ADR-001 — Canonical task format: iCalendar `VTODO` (RFC 5545)

`VTODO` is the only task format with real cross-vendor deployment. Everything we
would ever want to sync with speaks it: Nextcloud Tasks, Radicale, Baïkal, SOGo,
Fastmail, Thunderbird, Apple Reminders, Tasks.org (Android), DAVx⁵, todoman.

Our domain model is a superset of `VTODO` and every field maps to a property.
Anything we invent lives under an `X-WINTODO-*` property so it degrades to a
no-op on other clients instead of corrupting them.

### ADR-002 — Sync protocol (M13): CalDAV (RFC 4791) + WebDAV Sync (RFC 6578)

- Discovery: `.well-known/caldav` → `current-user-principal` → `calendar-home-set`.
- Collection enumeration: `PROPFIND` with `supported-calendar-component-set`,
  filter to collections advertising `VTODO`.
- Incremental sync: `sync-collection` `REPORT` (RFC 6578) with a stored
  `sync-token`. Fallback for servers without it: `getctag` + `calendar-query`
  `REPORT` returning `getetag`, then diff.
- Conflict detection: per-resource `ETag`, `If-Match` on `PUT`, `If-None-Match: *`
  on create.
- Auth: HTTP Basic over TLS (app passwords — what Nextcloud/Fastmail actually
  hand out), with an OAuth path deferred.
- Alarms: RFC 9074 (`VALARM` with `UID`, `RELATED-TO` snooze relationships).

### ADR-003 — JMAP is rejected for now

`draft-ietf-jmap-tasks` reached revision 06 (Sept 2023) and **expired**;
it is not an RFC. `draft-ietf-jmap-calendars` (rev 27) explicitly does **not**
cover `VTODO`. Building on JMAP today means building on a dead draft with
approximately zero server deployment for tasks. Revisit if it revives.

### ADR-004 — JSCalendar (RFC 8984) shapes the *internal* model, not the wire

RFC 8984 defines a `Task` object and is the modern, sane data model — nested
`participants`, `alerts`, `locations`, proper `duration`/`recurrenceRules`.
We shape our C# domain types after JSCalendar semantics (clearer than raw
iCalendar) but we **serialise to `VTODO`** because that is what servers accept.
The iCalendar↔JSCalendar conversion rules
(`draft-ietf-calext-jscalendar-icalendar`, rev 23, still a draft) are our
reference for the mapping table in `docs/DATA-MODEL.md`.

### ADR-005 — De-facto interop conventions we must honour

These are not in any RFC but every real client implements them. Getting them
wrong makes us the app that breaks everyone else's data.

| Convention | Meaning | Who uses it |
|---|---|---|
| `RELATED-TO;RELTYPE=PARENT:<uid>` | Subtask → parent link | Nextcloud Tasks, Tasks.org, Thunderbird, EM Task |
| `X-APPLE-SORT-ORDER:<int>` | Manual sort position | Apple Reminders, Nextcloud Tasks, Tasks.org |
| `CATEGORIES:a,b,c` | Tags | Nextcloud Tasks, Thunderbird, Tasks.org |
| `PRIORITY` 1–4 / 5 / 6–9 | high / medium / low | Everyone |
| `STATUS` + `PERCENT-COMPLETE` + `COMPLETED` | Completion state | Everyone |
| **Unknown-property preservation** | Round-trip every property you don't understand | Mandatory. Non-negotiable. |

Apple Reminders flattens `RELATED-TO` hierarchies to a single level — expected,
document it in the sync UI, do not work around it.

### ADR-006 — Recurrence handling on completion

Microsoft To Do's model: completing a recurring task advances it to the next
occurrence; the task itself never leaves the list.

The v1 implementation: keep **one** master `VTODO` carrying `RRULE`. On
completion, compute the next `DUE`/`DTSTART` from the rule, write the new dates,
reset `STATUS` to `NEEDS-ACTION`, and append a row to a local
`completion_history` table. This is what Tasks.org and Nextcloud Tasks do and is
the most interoperable simple behaviour.

Not doing: per-occurrence `RECURRENCE-ID` override instances. That is correct
`VTODO` recurrence but is poorly supported by clients and adds a large amount of
complexity for a v1. Revisit at M13 if server round-trips demand it.

### ADR-007 — No iCalendar library dependency in v1

`Ical.Net` v5 is the only serious .NET option; it is event-centric, its `VTODO`
support is thin, and its Native AOT / trimming posture is unverified (targets
listed as .NET 8 / .NET Standard). We do **not** take the dependency for v1.

M6 ships a small purpose-built iCalendar reader/writer in
`Wintodo.Core/Ical/` covering exactly the property set in `docs/DATA-MODEL.md`
plus verbatim passthrough of everything else. Line unfolding, `\`-escaping,
parameter parsing, `DATE` vs `DATE-TIME` vs `DATE-TIME;TZID`, and `RRULE`
round-trip must be unit-tested against real exports from Nextcloud Tasks,
Tasks.org and Apple Reminders (fixtures committed under `tests/fixtures/ical/`).
Budget: ~700 lines plus tests. Re-evaluate `Ical.Net` at M13 only if the
hand-rolled parser proves inadequate; the criteria are (a) it passes the AOT
publish gate warning-free and (b) it round-trips the fixture corpus losslessly.

---

## 3. Tech stack — locked

| Layer | Choice | Why |
|---|---|---|
| UI framework | **WinUI (WinUI 3)**, Windows App SDK **2.3.1+ stable** | Current native Windows stack. Microsoft now writes "WinUI" for what was WinUI 3. |
| Runtime | **.NET 10** (`net10.0-windows10.0.26100.0`) | Current LTS-class SDK on the machine (10.0.302 installed). Do not use .NET 11 previews. |
| Language | C# 14 | Partial properties (required by the modern `[ObservableProperty]` pattern), collection expressions. |
| Packaging | **Packaged MSIX**, x64 + ARM64 | Required for `winapp run`, toasts, jump lists, badges, Store. |
| MVVM | `CommunityToolkit.Mvvm` 8.4+ | Source-generated, zero reflection, AOT-clean. Use **partial properties**, not the legacy field-backed `[ObservableProperty]` — the WinUI analyzer flags the old form. |
| DI | `Microsoft.Extensions.DependencyInjection` | Explicit registration only. **No assembly scanning** (reflection = AOT break). |
| Storage | `Microsoft.Data.Sqlite` + `SQLitePCLRaw.bundle_e_sqlite3` | Raw ADO.NET. Trim/AOT compatibility fixed in the .NET 8 wave. |
| ORM | **None.** Hand-written mappers. | EF Core is reflection-heavy, costs startup time and tens of MB of working set. This app has ~8 tables. |
| Search | SQLite **FTS5** | Ships in `bundle_e_sqlite3`. No extra dependency, no in-memory index. |
| JSON | `System.Text.Json` **source-generated contexts only** | Settings + export only. `JsonSerializerContext`, never reflection-based overloads. |
| Extra controls | `CommunityToolkit.WinUI.Controls.*` (Segmented, SettingsControls) | v8.2+ is AOT-clean. Pull individual packages, never the metapackage. |
| Tests | `xunit` + `winapp ui` batch scripts | Unit tests for Core/Data; UI automation for the shell. |

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

### 4.1 Install `win-dev-skills` into Codex

The Microsoft `win-dev-skills` plugin is the source of truth for the WinUI inner
loop. Install it first — the skills change how everything below is done.

```powershell
# Prereqs for the plugin system
winget install --id Git.Git --exact --silent --accept-package-agreements --accept-source-agreements
```

Then, per the repo's Codex instructions: **add the `microsoft/win-dev-skills`
marketplace, then enable the `winui` plugin from the plugin directory.** Codex
has no "agents" concept, so the `winui-dev` orchestrator agent is not exposed —
invoke the skills by name instead (`/winui-setup`, `/winui-design`,
`/winui-dev-workflow`, …).

If the marketplace flow is unavailable in the installed Codex build, fall back to
a manual install — clone the repo and copy the skill folders into the Codex
skills directory:

```powershell
git clone --depth 1 https://github.com/microsoft/win-dev-skills $env:TEMP\win-dev-skills
# skills live at: $env:TEMP\win-dev-skills\plugins\winui\skills\*
# copy each <skill-name>\ folder (SKILL.md + references\ + payload binaries)
# into the Codex skills directory, preserving folder structure and the
# bundled winui-search.exe / BuildAndRun.ps1 / analyzer DLL payloads.
```

Pin to a release tag if `main` proves unstable:
`https://github.com/microsoft/win-dev-skills.git#v0.3.0`.

The plugin is **preview / v0.x with no SemVer commitment** — skill names and
layout can change under you. If a skill name in this plan does not resolve, list
the installed skills and map by description rather than guessing.

### 4.2 Run `/winui-setup`

It is idempotent and checks before installing. It handles:

| Tool | Minimum | Target |
|---|---|---|
| .NET SDK | 8.0 | 10.0 (**already present: 10.0.302**) |
| WinApp CLI | 0.3 | latest (`winget install Microsoft.WinAppCLI`, then `winget upgrade`) — **not currently installed** |
| WinUI 3 templates | any | latest (`dotnet new install Microsoft.WindowsAppSDK.WinUI.CSharp.Templates`) |
| Developer Mode | enabled | enabled — **ask the human before triggering UAC** |

Optional but recommended for the human to install (do **not** install it
yourself — multi-GB): Visual Studio 2026 with the WinUI workload, which gives
real XAML compiler diagnostics that `dotnet build` currently swallows.

```powershell
winget install Microsoft.VisualStudio.Community --override "--add Microsoft.VisualStudio.Workload.Universal"
```

### 4.3 Verify

```powershell
dotnet --list-sdks
winapp --version                      # must parse >= 0.3
dotnet new list winui                 # must include winui-mvvm
(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -Name AllowDevelopmentWithoutDevLicense).AllowDevelopmentWithoutDevLicense  # 1
```

---

## 5. Repository layout

```
wintodo/
  Wintodo.sln
  Directory.Build.props            # shared: TFM, nullable, AOT analyzers, warnings-as-errors
  Directory.Packages.props         # central package version management
  global.json                      # pin SDK 10.0.3xx, rollForward latestFeature
  .editorconfig                    # style + analyzer severities
  AGENTS.md                        # operating rules for Codex — read every session
  PLAN.md                          # this file
  README.md
  docs/
    DESIGN.md                      # visual spec — read before any XAML
    DATA-MODEL.md                  # domain model, SQLite schema, VTODO mapping
    PROGRESS.md                    # milestone log, written by the agent
    PERF.md                        # measurement log: every run's numbers
    adr/                           # architecture decision records, ADR-00N-*.md
  src/
    Wintodo.Core/                  # netstandard-free: net10.0, no UI references
      Models/                      # TodoTask, TodoList, Step, Alarm, RecurrenceRule, ...
      Recurrence/                  # RRULE parse + next-occurrence computation
      Ical/                        # M6: iCalendar reader/writer
      Queries/                     # smart-list + filter query descriptors
      Services/                    # interfaces only (ITaskStore, IReminderScheduler, IClock)
    Wintodo.Data/                  # net10.0, SQLite only, no UI references
      Migrations/                  # 001_init.sql, 002_*.sql — embedded resources
      SqliteTaskStore.cs
      Mapping/                     # hand-written row <-> model mappers
    Wintodo.App/                   # the WinUI 3 app — the only project with XAML
      App.xaml(.cs)
      MainWindow.xaml(.cs)
      Views/
        ShellPage.xaml             # NavigationView host
        TaskListView.xaml          # centre pane
        TaskDetailView.xaml        # right pane (x:Load deferred)
        SettingsPage.xaml
        Controls/                  # TaskRow, CompletionCircle, AddTaskComposer, DateChip, ...
      ViewModels/
      Themes/
        Generic.xaml               # control styles
        Colors.xaml                # Light / Dark / HighContrast dictionaries
      Strings/en-US/Resources.resw
      Assets/
      Package.appxmanifest
  tests/
    Wintodo.Core.Tests/
    Wintodo.Data.Tests/
    fixtures/ical/                 # real exports from Nextcloud, Tasks.org, Apple
    ui/                            # winapp ui batch scripts
  scripts/
    Measure-Perf.ps1               # working set + cold start, appends to docs/PERF.md
    Publish-Aot.ps1                # release AOT publish + size report
```

Rules: `Wintodo.Core` references nothing but the BCL. `Wintodo.Data` references
`Wintodo.Core` + `Microsoft.Data.Sqlite`. `Wintodo.App` references both. No
project references point backwards. UI types never appear below `Wintodo.App`.

---

## 6. Architecture

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

| Metric | Framework-dependent | Native AOT (M9+) | Hard fail |
|---|---|---|---|
| Cold start → first interactive frame | ≤ 900 ms | ≤ 500 ms | > 1500 ms |
| Warm start | ≤ 400 ms | ≤ 250 ms | > 700 ms |
| Idle working set, empty DB | ≤ 95 MB | ≤ 65 MB | > 150 MB |
| Idle working set, 5 000 tasks / 20 lists | ≤ 120 MB | ≤ 85 MB | > 180 MB |
| Working set after 10 min idle | must not exceed launch + 10 MB | same | any monotonic growth |
| List query, 10 000 tasks | ≤ 5 ms | ≤ 5 ms | > 20 ms |
| Search keystroke → results | ≤ 60 ms | ≤ 60 ms | > 150 ms |
| Scroll 10 000 tasks | 60 fps, zero per-frame allocations | same | any dropped frames |
| Installed size (framework pkg) | — | ≤ 260 MB self-contained | — |

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
  per-list theme colour picker, list groups (create, drag lists into/out of).
- A default list ("Tasks", `lists.is_default`) created on first run: pinned
  directly below the smart lists and above the divider, never renameable,
  deletable, groupable, or reorderable. It is the composer target on every smart list, because
  `tasks.list_id` is `NOT NULL` and a listless task has no CalDAV collection to
  live in. Picking another list in the composer applies to that one task.
- `TaskListView`: header (title + count + list menu), `AddTaskComposer`,
  `ListView` of `TaskRow`, completed section.
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
- Per-list background images (the To Do "themes" feature) — decode at display
  size, never full resolution.
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
- Every §7 AOT-column number met. If one is missed, profile and fix — do not
  relax the budget without an ADR.
- The AOT build passes the entire `winapp ui` suite from M8 (AOT changes
  behaviour: anything reflective that survived will fail here, loudly).

---

### M10 — Packaging and release

**Deliverables**
- `winapp manifest` reviewed; identity, publisher, capabilities minimal
  (no `broadFileSystemAccess`).
- `winapp cert generate` + trust for local install; `winapp sign` for the MSIX.
- GitHub Actions: build + test + AOT publish + sign + attach MSIX to a release,
  for x64 and ARM64.
- App icons at every required size; Store-quality assets.

**Skills:** `/winui-packaging`.

**Gate**
- MSIX installs from a clean VM (no dev mode, no dev cert pre-trusted, after
  installing the cert) and launches.
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
| XAML compiler under `dotnet build` gives no diagnostic on malformed XAML | Always use `BuildAndRun.ps1` (prefers MSBuild). Ask the human to install VS 2026 + WinUI workload. When a build fails with no error text, suspect the last-edited `.xaml`, not the C#. |
| `win-dev-skills` is preview v0.x, skill names can change | Pin to a tag if `main` breaks. Map by description if a name doesn't resolve. |
| Native AOT surfaces a broken dependency late | `IsAotCompatible` + analyzers from M0; a weekly `dotnet publish -p:PublishAot=true` check from M1. |
| Scheduled toasts are a UWP-API workaround, not a first-class WinAppSDK feature | Isolate behind `IReminderScheduler`; if the SDK ships a real scheduling API, swap one implementation. |
| Hand-rolled iCalendar parser has edge cases | Real-world fixture corpus + strict round-trip test at M6, long before network sync. |
| Recurrence semantics differ per client | ADR-006 documents our choice; verify against live servers at M13, revisit only with evidence. |
| Per-list background images blow the memory budget | Decode at display size (`BitmapImage.DecodePixelWidth`), cap dimensions, one cached image at a time. |

**Open — needs the human:** final app name and MSIX identity/publisher.
Everything else is decided.

---

## 11. Reference index

| Topic | Where |
|---|---|
| Windows App SDK 2.3.1 release notes | https://learn.microsoft.com/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-2-0 |
| What's new for Windows developers | https://learn.microsoft.com/windows/apps/whats-new/whats-new-for-developers |
| `win-dev-skills` | https://github.com/microsoft/win-dev-skills |
| WinApp CLI | https://github.com/microsoft/winappcli |
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
