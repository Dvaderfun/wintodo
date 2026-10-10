# UX improvement plan

Work packages from the 2026-10-10 UX review (baseline commit `d6e8192`). Each
package is sized for one agent and one branch/PR. Line numbers are as of the
baseline; they drift, so find code by the **symbol** named next to them.

## How to use this plan

1. Pick a package whose status is `ready` and whose dependencies are `done`.
   Set its status to `in progress (<agent/branch>)` in the table below, in its
   own commit, before starting work.
2. Read in order: `AGENTS.md` → `ARCHITECTURE.md` → the package below → the
   spec sections it names (DESIGN before any XAML, DATA-MODEL before any
   store code).
3. Load the skills the package names **before** writing code.
4. Stay inside the package scope. If you find something else, add it to
   **Parking lot** at the bottom; don't fix it in the same PR.
5. Done means every item in the package's **Acceptance** list plus the
   definition of done in `AGENTS.md`: a clean `-warnaserror` build, `dotnet test`
   green, the affected UI gates passing, `/winui-code-review` with nothing above
   informational, updated docs, and a `docs/PROGRESS.md` entry headed
   `UX-xx — <title>`. Then set the status to `done (<commit>)`.
6. **Packages marked 🔒 need the owner's go-ahead first.** Write the ADR (or
   the DESIGN change) first, set the status to `awaiting owner`, and stop.
   Don't write code until the owner has acknowledged it.

### Rules for working in parallel

These files are shared hot spots. Before two agents edit the same one at the
same time, check the **Touches** column:

- `Strings/en-US/Resources.resw`: add new strings in a block at the **end** of
  the file, starting with a `<!-- UX-xx -->` comment. Never reorder existing entries.
- `Pages/ShellPage.xaml.cs`, `ViewModels/TaskListViewModel.cs` and
  `Controls/SettingsView.xaml`: only one package that touches each file should
  be `in progress` at a time.
- New setting keys go in `ISettingsStore`, named `<area>.<name>` in snake_case
  (the pattern already used by `appearance.density`). Never add a new table for a
  preference.
- New AutomationIds follow `Todo.<Area>.<Element>`. Existing ones are API: never
  rename them.

## Status

Owner acknowledged proceeding to UX-03 on 2026-10-10 with UX-02's dedicated
settings gate passing. UX-02's composer/performance gates remain open and tracked;
this dependency exception does not mark those gates passed.


| ID | Title | Phase | Size | Depends on | Touches (hot files) | Status |
|---|---|---|---|---|---|---|
| UX-01 | Dates and times in the UI language | A | S | — | TaskDetailVM, TaskListVM | done (252f8e6) |
| UX-02 | Settings infrastructure + **Tasks** section (confirm before delete) | A | M | — | SettingsView, ShellVM, ShellPage | done (40a1a34; settings 9/9, composer 3/3, 259 tests) |
| UX-03 | Settings: **Notifications**, **Data** and **About** sections | A | M | UX-02 | SettingsView, WindowsIntegrationService | done (09aa070) |
| UX-04 | High-contrast fixes | A | S | — | ShellPage | done (71f6434) |
| UX-05 | Hide the sync status until sync is configured | A | S | — | ShellPage.xaml, ShellVM.Sync | done (07fbcdc) |
| UX-06 | Move list presentation logic into Core, with unit tests | B | M | — | TaskListVM | done (8a319ad; M3/M5/M8 PASS) |
| UX-07 | Update the task list in place instead of rebuilding it | B | L | UX-06 | TaskListVM, TaskListView | done (d6038f2; stability/M3/M5/M8 PASS) |
| UX-08 | Completion and row motion per DESIGN §7 | B | S | UX-07 | TaskRow, TaskListView | done (9fa4a86; motion 7/7, stability 3/3, M8 17/17) |
| UX-09 | Real multi-select and bulk actions | B | L | UX-07 | TaskListView, TaskListVM, TaskRow | done (b327d85; multiselect 7/7, M3/M8/stability PASS) |
| UX-10 | Undo for every destructive action | B | M | UX-02 | TaskListVM, ShellVM, Data | ready |
| UX-11 | Drop tasks on My Day and Important | B | S | — | ShellPage | done (41b1946; M5 9/9 + restart, M8 17/17) |
| UX-12 | My Day reset time | B | M | UX-02 | Core (clock/queries), Data | ready |
| UX-13 | Split oversized code-behind and view models | C | M | UX-07, UX-09 | ShellPage, all VMs | in progress (efa09c2; split implemented, full gate closure open) |
| UX-14 | 🔒 Show the sort indicator only when the sort isn't the default | B | S | — | TaskListView | awaiting owner |
| UX-15 | 🔒 Quieter placeholder when a list has no icon | B | S | — | TaskListView | awaiting owner |
| UX-16 | 🔒 Natural-language quick add | D | L | UX-06 | Core, AddTaskComposer | awaiting owner |
| UX-17 | 🔒 Global quick-add hotkey | D | L | UX-16 recommended | App, new window | awaiting owner |
| UX-18 | 🔒 Share target (text/URL → task) | D | M | — | Manifest, App | awaiting owner |
| UX-19 | 🔒 My Day carry-over and suggestions, rule-based | D | M | UX-12 | Core, TaskListView | awaiting owner |
| UX-20 | Close the open manual gate rows (owner-run) | C | — | — | — | owner |

Phases: **A** quick fixes and spec gaps (can run in parallel) · **B** interaction
quality · **C** engineering health · **D** beyond parity (each needs an ADR).

Suggested lanes for three agents in parallel:
- **Lane 1:** UX-01 → UX-04 → UX-05 → UX-11
- **Lane 2:** UX-02 → UX-03 → UX-10 → UX-12
- **Lane 3:** UX-06 → UX-07 → UX-08 → UX-09 → UX-13

---

## Phase A — quick fixes and spec gaps

### UX-01 — Dates and times in the UI language

**Problem.** The UI is English (only `Strings/en-US` exists; the manifest uses
`x-generate`), but dates are formatted with `CultureInfo.CurrentCulture`, which
follows the Windows regional format. On a machine with Ukrainian regional
settings, the detail pane shows "Created сб, 10 жовт." beside English labels
(evidence: `tests/ui/artifacts/composer/run-20261010-113025/01-detail-open.png`).
There are 40 `CultureInfo.CurrentCulture` uses across these files:
`TaskDetailViewModel`, `TaskListViewModel`, `TaskDetailView.xaml.cs`,
`StepRow.xaml.cs`, `SettingsView.xaml.cs`, `ShellPage.xaml.cs`,
`AttachmentItemViewModel`, `WindowsIntegrationService`.

**Approach.**
1. Add a resw string `App_FormattingCulture` = `en-US` to
   `Strings/en-US/Resources.resw`, with a comment saying every future language
   folder must set it to its own tag. Reading it through the resource loader
   gives the language MRT actually resolved, so text and dates always match.
2. Add `Services/DisplayCulture.cs`: `internal static CultureInfo Current`.
   Resolve it once at startup in `App.OnLaunched`, before any view model is
   built. If the tag is missing or invalid, fall back to `CultureInfo.CurrentUICulture`.
3. Replace every `CultureInfo.CurrentCulture` used for **user-visible text
   formatting** with `DisplayCulture.Current`. Leave invariant-culture parsing
   and serialisation alone.
4. **Exception:** the start of the week for Planned → "This week"
   (`TaskListViewModel.GetPlannedBucket`, ~line 1463) keeps the user's regional
   `FirstDayOfWeek` until UX-02 adds the "Week starts on" setting.
5. Toast text (`WindowsIntegrationService`) is user-visible, so it uses
   `DisplayCulture.Current` too.

**Tests.** Move the string-building helpers that are pure into Core, or make
them testable static functions. Assert that "Created" and due-date labels come
out in en-US when `CurrentCulture` is `uk-UA`, by setting
`CultureInfo.CurrentCulture` inside the test.

**Acceptance.**
- With the Windows regional format set to Ukrainian or German, every date,
  weekday and month name in the list, the detail pane, step rows, attachments
  and toasts is English.
- `grep -rn "CultureInfo.CurrentCulture" src/Wintodo.App` shows only the
  remaining week-start use, with a comment.
- The M4 and M8 gates pass.

**Docs.** Add a short note to DESIGN §11 or §9.2 on how display culture is
resolved.

### UX-02 — Settings infrastructure + Tasks section

**Problem.** DESIGN §12 specifies a **Tasks** section (default reminder time,
week starts on, confirm before delete) that doesn't exist. As a result:
- deleting a task always shows a dialog *and then* an undo snackbar
  (`ShellPage.OnDeleteTaskRequested` ~line 731, then `TaskListViewModel.BeginUndo`);
- the default reminder time is hard-coded to 9:00 (`TaskDetailViewModel`, ~lines 529 and 764).

**Approach.**
1. Add a `Services/AppPreferences.cs` (or a `ShellViewModel.Preferences.cs`
   partial; follow whichever pattern the `appearance.*` settings in
   `ShellViewModel` already use). It holds typed properties loaded from and
   saved to `ISettingsStore`, and raises a change event. It is the one place
   preferences are read from. Keys:
   - `tasks.confirm_before_delete` (bool, default `true`, which matches DESIGN §8)
   - `tasks.default_reminder_time` (`HH:mm` invariant, default `09:00`)
   - `tasks.week_start` (`system` | `0`–`6`, default `system`)
   - `tasks.add_new_to_top` (bool, default `false`). This is To Do parity: new
     tasks go to the top of a manually sorted list.
   - `tasks.move_starred_to_top` (bool, default `false`). To Do parity: in
     manual sort, starring moves the task to the top.
2. In `SettingsView.xaml`, add a **Tasks** header between Appearance and
   Windows. Use one `SettingsCard` per key: a `ToggleSwitch`, a `TimePicker`,
   and a `ComboBox` with "Use system setting" plus the seven days in
   `DisplayCulture` names. Add AutomationIds `Todo.Settings.Tasks.<Name>`.
3. Wire them up:
   - confirm before delete → `TaskListViewModel` / `ShellPage` skips the dialog
     when the setting is false. The undo snackbar always shows.
   - default reminder time → `TaskDetailViewModel`'s picker default and the
     composer's reminder chip.
   - week start → `GetPlannedBucket` (resolve `system` through the regional
     `FirstDayOfWeek`) and any calendar pickers (`CalendarDatePicker.FirstDayOfWeek`).
   - add new to top / move starred to top → the store-side ordering. Find where
     new tasks get their `SortOrder` in `SqliteTaskStore.Tasks.cs`. Pass the
     preference in as a parameter; don't read settings inside the store.
4. DESIGN §12's "move completed to bottom" is redundant: completed tasks
   already sit in their own section (§3.6). Remove it from DESIGN §12 with a
   one-line note, rather than building a no-op setting.

**Tests.** Data tests for add-to-top and starred-to-top ordering. If the
preferences class lives in a testable layer, unit-test that it parses defaults
and rejects malformed values.

**Acceptance.**
- With confirm off, pressing Delete removes the task right away and shows the
  undo snackbar. With it on, behaviour is unchanged.
- The reminder picker defaults to the configured time.
- Week start changes Planned → "This week" grouping.
- Every setting survives a restart.
- Extend `tests/ui/m8-design-accessibility.ps1` or add
  `tests/ui/run-settings-gate.ps1` to cover: delete without confirmation,
  restart persistence, and accessibility audit at zero errors.

**Docs.** DESIGN §12 (the actual list of keys), DESIGN §8 (Delete row: "dialog
if *Confirm before deleting* is on").

### UX-03 — Settings: Notifications, Data, About

**Problem.** The rest of DESIGN §12 is missing: enable reminders, default
snooze, database location and size, a GitHub link, licences.

**Approach.**
1. **Notifications** section. Move the existing launch-at-login card here,
   matching DESIGN §12. Add:
   - `notifications.enabled` (bool, default `true`). When off,
     `WindowsIntegrationService` cancels every scheduled toast. When turned
     back on, it runs the existing rescheduling sweep.
   - `notifications.default_snooze` (`10` | `60` | `tomorrow`, default `10`).
     This preselects the toast's snooze selection box (the `.AddItem("10", …)`
     block ~line 352).
2. **Data** section. Add read-only cards for the database path (with a "Show in
   Explorer" command) and the database size. Get the size from the existing
   `StoreStats` / maintenance APIs; check `ITaskStore` before adding a method.
3. **About** section. Keep the version card. Add a repository link and a
   licences expander listing the third-party packages from
   `Directory.Packages.props`. The licence text goes in resw or a bundled
   text asset, never fetched over the network.

**Acceptance.**
- Turning reminders off removes pending scheduled toasts (verify via the M7
  gate's toast inspection) and turning them on restores them.
- The default snooze is preselected in a test toast.
- The M7 and M8 gates pass.

**Docs.** DESIGN §12, ARCHITECTURE "Windows integration" (the enabled flag).

### UX-04 — High-contrast fixes

**Problem** (evidence: `tests/ui/artifacts/m8/run-20261010-115059/05-high-contrast.png`):
1. A selected custom list's colour dot disappears. Root cause:
   `ShellPage.RefreshUserListAccentBrushes` (~line 1872) fills the dot with
   `SystemColorHighlightColorBrush`, which is also the selected-row
   background.
2. The title-bar search box has no visible border in high contrast.
3. The list header's emoji button and title get heavy individual borders. Check
   this against DESIGN §11 and decide whether it's intended.

**Approach.**
- Fill the dot with `SystemColorWindowTextColorBrush` normally and
  `SystemColorHighlightTextColorBrush` when its item is selected. Drive this
  from the `NavigationViewItem` selection state, not a one-off refresh. Stay
  within the 8 permitted `SystemColor*` brushes.
- Search box: give it a `HighContrast` theme dictionary entry that sets a
  `SystemColorWindowTextColorBrush` border (in `Themes/Colors.xaml`, or
  wherever the title-bar resources live).
- Header borders: if they aren't a requirement, remove the extra borders in
  high contrast only.

**Acceptance.**
- In each of the four Windows contrast themes, the dot is visible both
  selected and unselected, and the search box border is visible.
- Extend `m2-theme.ps1` / the M8 high-contrast check to take a screenshot with
  a custom list selected.
- The accessibility audit stays at zero errors.

### UX-05 — Hide the sync status until sync is configured

**Problem.** `Todo.Nav.SyncStatus` (`ShellPage.xaml` ~line 274) always shows
"Not connected" in the nav footer, even for users who will never sync.

**Approach.** Bind the footer item's visibility to "an account exists" in
`ShellViewModel.Sync.cs`. When no account exists, collapse it. Settings →
Sync stays the entry point. Leave connected, syncing and error states as they
are.

**Acceptance.**
- On a fresh database there's no sync item. After connecting (M13 gate's
  loopback server) it appears. After disconnecting it disappears.
- Update the M13 gate. Any gate that asserts `Todo.Nav.SyncStatus` on a fresh
  database must be updated in the same PR.

---

## Phase B — interaction quality

### UX-06 — Move list presentation logic into Core, with unit tests

**Why first.** UX-07 rewrites how the list updates. Today the logic that
decides *what* to show (grouping, Planned buckets, sort, the metadata line
"Tasks · Tomorrow · Repeats") lives inside the 1,800-line
`TaskListViewModel` and is only covered by UI gates.

**Approach.**
1. Extract pure functions into `Wintodo.Core/Presentation/` (a new folder,
   still no UI types):
   - `TaskGrouping.Build(query, active, completed, showCompleted, searchText, clock, weekStart)`,
     returning an ordered list of groups, each with a key and an ordered list
     of task uids (from `RebuildVisibleItems`, `BuildPlannedGroups`,
     `BuildSearchGroups`, `GetPlannedBucket`).
   - `TaskSorting.Compare(sortKind, reverse)` (whatever `TaskListViewModel`
     sorts with today).
   - `TaskMetadataLine.Build(task, context, clock, culture)`, returning the
     parts of the row subtitle (from `UpdateItemDisplay`). Localised strings
     stay in the App; Core returns part kinds and values, and the App formats
     them.
2. `TaskListViewModel` calls these functions. Don't change behaviour in this
   package.

**Tests.** Add `tests/Wintodo.Core.Tests/Presentation/*`. Cover:
- every Planned bucket boundary (overdue / today / tomorrow / this week / later)
  across week starts and midnight, using a fake clock;
- every sort kind with ties and reverse;
- the search grouping;
- the metadata-line part order per DESIGN §3.3.

**Acceptance.**
- No behaviour change: the M3, M5 and M8 gates pass without edits to
  assertions.
- At least 30 new Core tests.
- `TaskListViewModel` is smaller by the moved code.

### UX-07 — Update the task list in place instead of rebuilding it

**Problem.** `TaskListViewModel.RebuildVisibleItems` (~line 1259) clears and
refills `VisibleItems` and `TaskGroups`, and it's called from 17 sites,
including complete, star and rename. This breaks PLAN §6.4. ARCHITECTURE's
"Known divergences" lists it as a perf item, but users feel it directly:
- the scroll position can jump;
- keyboard focus and selection can be lost after Space or Ctrl+I;
- Narrator re-reads the list;
- `AddDeleteThemeTransition` fires for every row;
- the DESIGN §7 completion motion can't play.

**Approach.**
1. Before touching code, measure the current numbers with
   `scripts/Measure-Perf.ps1` (list switch, completion, the 10,000-task
   fixture) and record the baseline rows in `docs/PERF.md`.
2. Using UX-06's `TaskGrouping.Build` output, compute the target sequence and
   apply a minimal diff to the bound collections: `Insert` / `RemoveAt` /
   `Move` per item, reusing the existing `TaskItemViewModel` instances by uid.
   A simple keyed diff is enough: longest-increasing-subsequence for moves, or
   remove+insert when a row changes group. Put the diff algorithm in Core
   (`Presentation/SequenceDiff`) with unit tests. The App only applies the
   operations.
3. A whole-list rebuild is still allowed for list switches and search
   changes, where the list's identity changes. Single-task mutations
   (complete, star, rename, date change, My Day toggle, move, delete) must
   apply a diff.
4. Keep `SelectedTask`, keyboard focus and scroll offset across a diff. A
   completed row stays selected and focused as it moves into Completed.
5. Remove the row from ARCHITECTURE "Known divergences" and the PROGRESS
   "Known gaps" line.

**Tests.**
- Core: diff tests (property-style: applying the operations to the old
  sequence gives the new one; moves are minimal for single-item changes).
- UI: a new `tests/ui/run-list-stability-gate.ps1`:
  - 200 tasks, scroll to the middle, complete a visible task: scroll offset
    stays within ±1 row and focus stays on the row;
  - Ctrl+I on the selected row: selection stays;
  - Alt+↓: the row moves and stays selected.

**Acceptance.**
- The stability gate passes.
- M3, M5 and M8 pass.
- Perf rows are recorded after the change, and nothing regresses against
  PLAN §7. If something does regress, stop and report it; don't tune silently.

### UX-08 — Completion and row motion per DESIGN §7

**Depends on UX-07**: the motion needs stable row containers.

**Approach.** First audit what's already implemented: there is a 250 ms
`Task.Delay` in `TaskListViewModel` (~line 1140) and transitions in
`TaskRow.xaml`. Then implement the missing rows of DESIGN §7:
- circle spring;
- check glyph fade;
- row fades to 0.6 opacity with a strikethrough;
- after 250 ms, the row moves into Completed;
- star bounce;
- the undo snackbar slides up and fades.

Every explicit animation checks `UISettings.AnimationsEnabled`. Use
composition or Storyboard animations in XAML resources, driven by visual
states, not code-behind timers.

**Acceptance.**
- A video or screenshot sequence of a completion from the gate, attached to
  the PROGRESS entry.
- With "Animation effects" off in Windows, state changes are instant.
- The M8 gate passes and the perf completion row doesn't regress.

### UX-09 — Real multi-select and bulk actions

**Problem.** `TasksList` declares `SelectionMode="Multiple"`
(`TaskListView.xaml:728`), but `TaskListView.OnTasksSelectionChanged`
(~lines 300–330) deselects everything except the last item. Microsoft To Do for
Windows supports Ctrl/Shift multi-select with bulk actions in the context menu.

**Approach.**
1. Switch to `SelectionMode="Extended"`: click selects one, Ctrl+click toggles,
   Shift+click selects a range. Remove the strip-to-one logic.
2. `TaskListViewModel` gets `SelectedTasks` (read-only) and `SelectionCount`.
   The detail pane opens only when exactly one task is selected. With more than
   one selected, the detail pane closes.
3. The context menu acts on the whole selection when the right-clicked row is
   part of it: complete/uncomplete, mark important, add to/remove from My Day,
   set due date (Today / Tomorrow / Pick), move to list, delete. Labels show the
   count, e.g. "Delete 3 tasks", using resw format strings.
4. Keyboard: Shift+↑/↓ extends the selection, Ctrl+A selects all in the
   current group, Space / Ctrl+I / Ctrl+T / Delete apply to the selection,
   Esc clears it.
5. Store: wrap bulk operations in **one write transaction** with one
   `change_log` row per task, so sync and undo see one consistent change. Add
   `ITaskStore` bulk methods only if looping inside one transaction isn't
   possible through the existing API. If you add methods, add Data tests too.
6. Bulk delete respects UX-02's confirm-before-delete and produces **one**
   undo entry (UX-10). If UX-10 isn't done yet, restore with the existing
   `RestoreTaskAsync` per uid.
7. Drag a multi-selection onto a nav list: move every selected task.

**DESIGN changes.** Add a "Multi-select" subsection to DESIGN §3, update the
keyboard table in §8, and add the shortcuts to the shortcuts dialog
(`ShellPage.CreateKeyboardShortcuts` builds 19 entries from
`ShortcutNN_Key`/`_Action`; extend the count and the resw entries).

**Acceptance.**
- A new `tests/ui/run-multiselect-gate.ps1`:
  - Ctrl-select 3, complete all;
  - Shift-range 5, move to another list;
  - select 2, delete, undo restores both;
  - the detail pane is hidden with more than one selected.
- Narrator announces the selection count (UI Automation `SelectionPattern`).
- M3 and M8 pass.

### UX-10 — Undo for every destructive action

**Problem.** DESIGN §8 says Ctrl+Z undoes "the last destructive action". Today
only task delete can be undone (`TaskListViewModel.UndoDeleteAsync`,
`_lastDeletedTaskUid`). Deleting a list, deleting a step, moving a task to
another list, clearing a due date or reminder, and completing a task by
mistake can't be reversed.

**Approach.**
1. Replace the single-uid field with an `UndoService`: a stack of at most 20
   entries, each holding a label and an async revert delegate. It is owned by
   `ShellViewModel` and shared by the list and detail view models. The snackbar
   always shows the newest entry, and Ctrl+Z pops one entry. Changing lists
   doesn't clear the stack, but a sync pull that touches a task with a pending
   undo entry drops that entry.
2. Record an entry for:
   - task delete (existing);
   - bulk actions (UX-09);
   - move to list;
   - step delete;
   - due date / reminder / repeat cleared;
   - **list delete**;
   - complete. For complete, only a snackbar "Completed · Undo" is shown, and
     only when the completion came from the list row. This is new behaviour
     compared with To Do, so it needs a one-line note in DESIGN §7 and the
     owner's OK. Skip it if the owner declines.
3. List delete needs `ITaskStore.RestoreListAsync(listId)`. It reverses
   `DeleteListAsync` (`SqliteTaskStore.Lists.cs:415`) including the tasks that
   delete soft-deleted with the list, and writes `change_log` rows so CalDAV
   push stays correct. Read DATA-MODEL and the sync change-log rules first. If
   the list is CalDAV-backed and the delete was already pushed, the restore
   must queue a re-create. If that turns out to be complex, reject undo for
   synced lists and record that as a deviation.

**Tests.**
- Data: `RestoreListAsync` round trip, including tasks and the change log.
- Sync: a restored synced list pushes correctly (fake HTTP handler).
- Core or App-free: `UndoService` stack behaviour, if it's placed in Core.

**Acceptance.**
- Each action above can be undone via the snackbar and via Ctrl+Z.
- The M3, M4 and M13 gates pass.

### UX-11 — Drop tasks on My Day and Important

**Problem.** Tasks can be dropped on user lists, groups and the default list
(`ShellPage` ~lines 97, 417, 461), but not on My Day or Important. To Do adds
the task to My Day, or stars it, when dropped there.

**Approach.** Wire `MyDayNavigationItem` and `ImportantNavigationItem` with
`AllowDrop`, `DragOver` and `Drop`. Show the drag-over caption "Add to My Day"
/ "Mark as important" (in resw). On drop, call the existing toggle commands
with the value set, not toggled. This works for multi-select too once UX-09
lands.

**Acceptance.** Extend the M3 or M5 gate: drag a task onto My Day and it
appears there, drag onto Important and it gets starred. The accessibility audit
stays clean.

### UX-12 — My Day reset time

**Problem.** DESIGN §12 specifies a My Day reset time (default midnight). Today
My Day membership is `MyDayDate = IClock.Today` (`SqliteTaskStore.Lists.cs:122`,
`SqliteTaskStore.Tasks.cs:437`). People who work past midnight lose their day
at 00:00.

**Approach.**
1. Core: add `MyDayCalendar.CurrentDay(IClock clock, TimeOnly resetTime)`,
   which is `Today` shifted back one day when the local time is before
   `resetTime`. Don't change `IClock.Today` itself; due dates still use the
   real calendar day.
2. Data: the queries that compare `MyDayDate` against today, and the code that
   sets `MyDayDate` when adding to My Day, take the My Day date as a parameter
   computed by the caller. The store still reads no settings.
3. App: setting `myday.reset_time` (`HH:mm`, default `00:00`) as a
   `TimePicker` in a **My Day** settings section. The existing rollover
   timer/refresh must fire at the reset time, not at midnight. Find it by
   searching for where My Day refreshes on day change.

**Tests.**
- Core: boundary tests around the reset time and DST changes (FileBacked/fake
  clock).
- Data: a task added at 01:00 with a 04:00 reset stays in My Day until 04:00.

**Acceptance.**
- Using `--clock-file`, a UI gate moves the clock across midnight and across
  the reset time, and My Day membership changes at the reset time only.
- The M5 gate passes.

### UX-14 — 🔒 Show the sort indicator only when the sort isn't the default

**Proposal.** The header always shows the sort mode ("Creation date,
ascending"), as DESIGN §3.1 specifies. To Do shows a sort chip only when the
sort isn't the default, with a × to reset. **Owner decision:** change DESIGN
§3.1 or keep it. If the owner approves: show a `[Sort ▾]` icon button by
default, and a "Sorted by X ↑ ×" chip below the header when the sort is
non-default. The AutomationIds of the existing sort button are preserved.

### UX-15 — 🔒 Quieter placeholder when a list has no icon

**Proposal.** DESIGN §3.1 (~line 135) shows an outlined smiley in the header
when a list has no emoji. It reads as if the list's icon *is* a smiley, and in
high contrast it gets a heavy border. Option: show the list's theme-colour dot
(the same thing the nav pane shows), and reveal the emoji-picker affordance
only on hover or keyboard focus. **Owner decision** required, and ADR-009 must
be amended.

---

## Phase C — engineering health

### UX-13 — Split oversized code-behind and view models

**Blocked until UX-07 and UX-09 are merged**, so it doesn't conflict with them.

**Problem.** `ShellPage.xaml.cs` (~2,000 lines), `TaskListViewModel` (~1,800)
and `TaskDetailViewModel` (~1,200) are hard to change safely. A lot of shell
behaviour sits in code-behind, where it can't be tested.

**Approach.**
- Split `ShellPage.xaml.cs` by area into partial files:
  `ShellPage.Navigation.cs` (nav item construction, drag/drop),
  `ShellPage.Dialogs.cs`, `ShellPage.Accelerators.cs`, `ShellPage.Theme.cs`.
- Split the view models by area the same way `ShellViewModel.Sync.cs` already
  does.
- Move any decision logic found in code-behind into the view models.
- This is a move-only refactor: no behaviour change, no renamed AutomationIds.

**Acceptance.**
- No file over ~600 lines in `Pages/` or `ViewModels/`.
- All UI gates pass unchanged.
- `/simplify` and `/winui-code-review` are clean.

### UX-20 — Close the open manual gate rows (owner-run)

These are in PROGRESS **Current state**:
- M6: hand-import into Nextcloud Tasks or Thunderbird.
- M7: a reminder survives a reboot; a visible taskbar badge.
- M8: Narrator listening check.
- M13: live Radicale and Nextcloud convergence; the conflict dialog via UI
  Automation.

Agents can prepare checklists and scripts. Only the owner can mark them passed.

---

## Phase D — beyond parity (🔒 each needs an ADR and the owner's acknowledgement)

These go past "v1 is a Microsoft To Do clone" (PLAN §1). Each package's first
deliverable is an ADR in `docs/adr/`, using the template in
`docs/adr/README.md`. No code until the owner acknowledges it.

### UX-16 — Natural-language quick add

When you type "Pay rent tomorrow 9am !" in the add-task box, the app shows
removable chips (due date tomorrow, reminder 09:00, important) and the title
"Pay rent".
- The parser is a pure, deterministic `Wintodo.Core/Parsing/QuickAddParser`:
  `IClock` plus `DisplayCulture`, no AI, no network, English grammar first.
  Tokens: today/tomorrow/weekday names/"next week"/ISO and locale dates, times,
  `!` for important, "every day/week/month" for repeat (mapped to
  `RecurrenceRule`), and optionally `#list`.
- Chips use the existing composer chip style. Backspace on an empty title
  removes the last chip. A setting turns parsing off.
- The ADR must cover: when parsing is ambiguous, keep the text in the title.
- Heavy unit tests: at least 100 phrase cases, plus DST and week-start
  boundaries.

### UX-17 — Global quick-add hotkey

A system-wide shortcut (default proposal: Win+Alt+T, configurable, off by
default) opens a small always-on-top quick-add window that works while the main
window is closed or minimised. The ADR must cover:
- the hotkey API (`RegisterHotKey` through CsWin32 or the existing interop;
  confirm it's AOT-clean and check it against the PLAN §3 banned list);
- what "app closed" means (startup task vs. background process; the RAM budget
  in PLAN §7);
- how this sits with the "do not invent new navigation patterns" rule.

### UX-18 — Share target

Register as a Windows share target so text and URLs shared from other apps
become a task in the default list, with the URL in the note. The ADR covers
the manifest change (coordinate with M10 packaging), the activation path in
`App.OnLaunched` (which must initialise only what it needs, like toast
activation), and AOT.

### UX-19 — My Day carry-over and suggestions, rule-based

PLAN §1 rules out a "suggestions panel". The ADR proposes a rule-based
alternative and asks the owner to amend PLAN §1:
- on the first launch after the reset time (UX-12), offer "Carry over N
  unfinished tasks from yesterday";
- a flyout, opened from the My Day header, listing overdue, due-today and
  yesterday's My Day tasks, each with a one-click "Add to My Day".

No AI, deterministic, `IClock`-driven, with Core-tested selection rules.

---

## Parking lot

Add findings that are outside a package's scope here, one line each, with a
file reference.

- Placeholder app icon in the title bar and assets: belongs to M10 (final
  name/identity, PLAN §10 open item).

- UX-02 validation resolved 2026-10-11: composer 3/3 and Tasks settings 9/9.
  Explicit detail closure and time-picker SelectionItemPattern fix harness
  preconditions/targeting; original evidence remains in PROGRESS History.
- UX-06 prerequisite: M3 task-row drag reorder still fails on the unchanged
  baseline and after extraction (`Controls/TaskListView.xaml.cs` container drag
  path). Removing container drag/drop overrides did not resolve it; reverted.
- `TaskListViewModel.UpdateItemDisplay`: DESIGN §3.3 reminder, steps, note and
  attachment subtitle parts are not implemented. UX-06 preserves the shipped
  list/due/repeat/My Day parts; adding the missing parts is separate work.
- `tests/Wintodo.Data.Tests/StoreFixture.cs`: intermittent SQLite disposed-handle
  during another fixture's `InitializeAsync` on a UX-09 full-suite run. Fixtures
  call process-wide `SqliteConnection.ClearAllPools` during parallel tests;
  investigate pool cleanup isolation separately. Fresh full suite passes 257/257.
- UX-13 baseline harness audit resolved separately 2026-10-11: M6 accepts
  Open Button/SplitButton (7/7), list counts 10/10, Tasks settings 9/9 and
  composer 3/3. No product behavior or assertion removed; motion remains open.
