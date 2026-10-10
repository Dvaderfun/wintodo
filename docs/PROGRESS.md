# Progress log

## Current state

_Maintained summary — update it whenever an entry below changes it. Everything
under "History" is append-only._

As of 2026-10-10:

| Milestone | Status | Open gate rows |
|---|---|---|
| M0–M5 | PASS | — (M3 passed with an owner-approved perf tolerance) |
| M6 iCalendar interop | AUTOMATED PASS | One-time hand import into Nextcloud Tasks or Thunderbird |
| M7 Reminders / Windows integration | AUTOMATED PASS | Reminder survives reboot; visible taskbar badge check |
| M8 Design / accessibility | AUTOMATED PASS | Narrator listening check |
| M9 Native AOT | DEFERRED (owner) | Full idle perf matrix pending; AOT startup crash fixed and M8 automation passed; ARM64 runtime perf not measured |
| M10 Packaging and release | GROUNDWORK | Unsigned tag CI added; final identity/signing, Store assets and clean-VM install/uninstall gates pending |
| M11 Deep subtasks | not started | — |
| M12 Tags and saved filters | not started | — |
| M13 CalDAV sync | AUTOMATED PASS | Live Radicale + Nextcloud convergence; conflict dialog via UI Automation |

- **Build:** Debug and Release x64, zero warnings with `WinUIAnalyzer` 0.7.1.
  Tests (xunit v3 on Microsoft Testing Platform): Core 74, Data 47, Sync 9 — 130/130
  on one isolated full run; latest run 129/130 (My Day 5.041 ms against 5 ms).
- **Toolchain:** .NET SDK 10.0.401 (runtime 10.0.12), Windows App SDK 2.5.1, WinApp CLI 0.7.1,
  win-dev-skills v0.7.1 vendored (ADR-010).
- **Known gaps:** the task list clears and refills its bound collection instead
  of diff-updating (`ARCHITECTURE.md`, Known divergences). Final app name and
  MSIX publisher are still undecided. UX-01 display culture and the M4 step-drag
  blocker are fixed; M4 passes all 9 phase-one checks and restart persistence.
- **UX-02:** Tasks settings implemented; dedicated gate passes 9/9. Composer
  regression gate remains partial (1/3), so the package is still in progress.
- **Next:** close the UX-02 composer regression, then continue app polish via
  [UX-PLAN.md](UX-PLAN.md). M9 remains owner-deferred;
  final name/publisher, signing and M10 clean-VM gates remain pending.

---

## History

One entry per milestone or change. Append, never rewrite history.

Template:

```
## M<n> — <name>          <YYYY-MM-DD>

**Gate:** PASS / FAIL (which criteria)

**Shipped**
- ...

**Deviations from PLAN.md**
- <what> — <why> — <ADR if the deviation is permanent>

**Known issues carried forward**
- ...
```

---


## M0 — Setup and scaffold          2026-07-30

**Gate:** PASS — `BuildAndRun.ps1` built and launched the packaged x64 app
without an app exception; the M0 UI smoke suite passed 4/4; and
`dotnet build Wintodo.sln -p:Platform=x64 -warnaserror` completed with zero
warnings and zero errors across all five projects.

**Shipped**
- Initialized CodeGraph and Git with the `main` branch.
- Installed and enabled `winui@microsoft-winui` 0.3.0.
- Verified .NET SDK 10.0.302, WinApp CLI 0.5.0, current WinUI templates, and
  Developer Mode.
- Scaffolded the packaged WinUI app, Core and Data libraries, and both xUnit
  test projects with the required one-way project references.
- Added central SDK, package, analyzer, architecture, formatting, and ignore
  configuration.
- Added localized app identity resources and an empty, rule-compliant template
  window.
- Added the attached build/run workflow and repeatable M0 UI smoke test.
- Completed the WinUI code review with no actionable findings.

**Deviations from PLAN.md**
- `BuildAndRun.ps1` searches parent directories for an existing
  `Directory.Build.props` — the bundled script otherwise created a temporary
  project-level props file that shadowed the repository-wide target framework.

**Known issues carried forward**
- None.

## M1 — Domain model and SQLite store          2026-07-30

**Gate:** PASS — 41/41 tests passed; merged line coverage is 81.64% for
`Wintodo.Core` and 96.82% for `Wintodo.Data`; every 10,000-task smart-list
query completed in 0.961 ms or less against a 5 ms budget; migration
initialization and reopen both held `schema_version` at 1; and Release x64 and
ARM64 builds completed with zero trim/AOT analyzer warnings.

**Shipped**
- Added the complete domain model, query shapes, injected clock, store contract,
  coalesced change notifications, and RFC 5545 recurrence parser/serializer.
- Implemented daily, weekly, monthly, and yearly recurrence advancement,
  including finite rules, date filters, leap years, and DST-aware wall time.
- Added the embedded SQLite v1 migration with WAL, partial indexes, FTS5,
  synchronization metadata, unknown iCalendar preservation, and change logging.
- Implemented serialized writes, bounded concurrent reads, list/group/task/step
  operations, soft deletion, movement and ordering, smart lists, FTS search,
  alarms, recurring completion history, vacuum, and store statistics.
- Added deterministic recurrence tests and SQLite integration tests for schema
  idempotency, CRUD, collection round-trips, change notifications, soft delete,
  FTS, smart lists, alarms, recurrence completion, connection cleanup, coverage,
  and the 10,000-task performance budget.
- Completed the WinUI code review with no actionable findings.

**Deviations from PLAN.md**
- Added `ix_tasks_completed` and `ix_tasks_created`, and extended
  `ix_tasks_myday` with `sort_order`, because the original index set could not
  reliably meet the explicit 5 ms smart-list gate. `docs/DATA-MODEL.md` now
  records the shipped schema.
- Added an explicit `SQLitePCLRaw.bundle_e_sqlite3` reference so the store uses
  SQLite 3.53.4 instead of the vulnerable transitive 3.40.1 native bundle
  initially selected by `Microsoft.Data.Sqlite`.

**Known issues carried forward**
- None.

## M2 — Shell: window, title bar, Mica, navigation          2026-07-30

**Gate:** PASS — the packaged x64 app built and launched with zero warnings
or errors; the shell UI suite passed 10/10 from 1180 down to the 640 minimum;
Light, Dark, and High Contrast updated live in one process with visible focus
and the original OS appearance restored; and all 42 Core/Data tests passed.
Static review found no hardcoded colors, corner radii, or font sizes, and the
High Contrast dictionary references only permitted `SystemColor*` brushes.

**Shipped**
- Replaced the scaffold page with a Mica-backed extended title bar and
  responsive title search, plus the three-column shell and deferred detail
  surface.
- Added smart-list navigation, generated user-list entries with stable
  AutomationIds, localized footer and Settings controls, and persisted the
  last selected list.
- Added DPI-aware default/minimum sizing and persisted/restored window
  position, normal size, and maximized state through the SQLite settings
  table.
- Added Light, Dark, and High Contrast theme dictionaries and resource-backed
  shell brushes.
- Added repeatable resize, navigation, search-flyout, keyboard, AutomationId,
  live theme, High Contrast, and focus gates with visual artifacts.
- Completed the WinUI code review; its generated user-list AutomationId
  finding was fixed before commit.

**Deviations from PLAN.md**
- The responsive search is a `UserControl` over the extended title-bar row
  instead of `TitleBar.Content` because that content is measured at its own
  fixed width and cannot observe the window breakpoints. It remains centered,
  interactive, and entirely VisualStateManager-driven.

**Known issues carried forward**
- None.

## M3 — Lists and the task list          2026-07-30

**Gate:** PASS with owner-approved performance tolerance — a clean M3 UI run
passed 10/10, the exact `5,3,4` active order and two completed tasks survived a
packaged restart, 44/44 Core/Data tests passed, and Release x64 and ARM64 builds
completed with zero warnings. The 10,000-task probe measured 107.18-119.64 fps
with zero callback allocations and zero or one frame above 25 ms.

**Shipped**
- Added inline list and group creation, rename, delete confirmation, drag
  ordering/grouping, list color themes, and persisted list display options.
- Added the virtualized task list, pinned composer, task rows, collapsible
  completed section, completion/importance controls, drag ordering, task
  movement, keyboard commands, and five-second delete undo.
- Added repository-local packaged launch arguments, deterministic M3 and
  performance fixtures, restart-state assertions, and repeatable UI gates.
- Deferred task-list hydration until after the first rendered shell frame and
  kept Release framework-dependent until the planned M9 Native AOT switch.
- Completed the WinUI code review; literal transparent fills and a post-
  `WhenAll` blocking access were corrected.

**Deviations from PLAN.md**
- The owner accepted the M3 performance gate with a 203.37 MB working set at
  30 seconds, no 10-minute sample, and up to one isolated scroll hitch because
  continuing to tune the harness was disproportionate for this milestone.
- Completion retains the 250 ms state-change delay and row transition but omits
  the spring scale effect because applying `Scale` through the custom checkbox
  visual states caused a native WinUI fail-fast during pointer activation.

**Known issues carried forward**
- Remeasure startup, 30-second/10-minute memory, and scrolling at M4. The first
  M3 performance row records the accepted baseline rather than a budget pass.
- Coordinate-driven task drag automation can occasionally miss the insertion
  target; an earlier clean run and the restart database assertion verified the
  persisted order.

## M4 — Task detail pane          2026-07-31

**Gate:** PASS — the M4 UI suite passed 7/7 and the packaged restart
round-trip passed; the detail view remained uninstantiated until first open;
every field, step, alarm, attachment, and recurrence setting survived SQLite
restart; completing the biweekly Tuesday task advanced its due date exactly 14
days and added `completion_history`; 45/45 Core/Data tests passed; and Release
x64 and ARM64 builds completed with zero warnings.

**Shipped**
- Added the responsive, `x:Load`-deferred task detail pane with debounced title
  and note editing, completion, importance, My Day, due date, reminder,
  recurrence, footer, delete, Escape close, and Enter-to-add-step behavior.
- Added child-task steps with add, edit, complete, delete, drag reorder, stable
  AutomationIds, and direct persisted ordering for the future project-tree
  model.
- Added quick and custom date/time controls plus daily, weekday, weekly,
  monthly, yearly, and custom every-N/by-weekday recurrence editing.
- Added local attachments with generated collision-safe names, a cumulative
  25 MB cap, constrained LocalState paths, open/remove commands, and the
  Windows App SDK desktop picker.
- Added deterministic M4 persistence, recurrence-completion, deferred-load,
  restart, keyboard, attachment, and responsive UI gates with visual artifacts.
- Fixed startup navigation restoration so the XAML-default My Day selection
  cannot overwrite persisted state before initialization completes.
- Reworked the performance scroll driver to use sustained wheel input from one
  process; the final 10,000-task sample passed at 119.86 fps with zero dropped
  frames and zero callback allocations.
- Completed the WinUI code review; legacy picker usage, raw exception text,
  attachment path confinement, size-overflow handling, and startup navigation
  initialization were corrected before commit.

**Deviations from PLAN.md**
- Custom `CalendarDatePicker` and `TimePicker` controls use attached flyouts
  launched from the action rows because `MenuFlyout` cannot host arbitrary
  picker controls; the quick choices remain `MenuFlyout` commands.

**Known issues carried forward**
- The framework-dependent M4 performance row still misses the §7 startup and
  loaded-working-set budgets, including the warm-start and loaded-memory
  hard-fail lines. The ten-minute trend is flat/decreasing rather than a leak,
  and no budget relaxation was accepted. Startup hydration, framework shared
  pages, and the Native AOT result must be profiled and resolved by M9.

## M5 — Smart lists, search, sort, group          2026-07-31

**Gate:** PASS — the deterministic M5 UI suite passed 6/6 in one live process
and both per-list sort assertions passed after packaged restart; advancing an
injected clock across local midnight and reactivating the still-running app
emptied My Day; Planned rendered yesterday under a critical-colour Overdue
header; the 10,000-task FTS search measured 1.971 ms worst warm query against
the 60 ms budget; all 47 Core/Data tests passed; and Debug x64 plus Release x64
and ARM64 builds completed with zero warnings.

**Shipped**
- Completed the five smart-list surfaces and added culture-aware Planned
  buckets with sticky group headers for Overdue, Today, Tomorrow, This week,
  Later, and No date.
- Wired the responsive title search to FTS5 with a 120 ms debounce, stale-query
  cancellation, per-list result groups, inline match highlighting, compact and
  wide search synchronization, and Escape restoration of the selected view.
- Added one-shot local-midnight refresh scheduling plus an activation check so
  My Day and relative due labels refresh without restarting the app.
- Finished sort labels and ascending/descending behavior for smart lists,
  retained persisted per-user-list sort choices, and disabled manual drag
  ordering while a non-manual sort is active.
- Migrated the task list to a grouped `CollectionViewSource`, preserved
  virtualization, added the completed count to its accessible header, and
  rendered overdue metadata with `SystemFillColorCriticalBrush`.
- Added the deterministic clock/database launch hooks, the 10,000-task FTS
  performance test, and repeatable M5 live/restart UI gates with visual
  artifacts.
- Completed the WinUI code review with no actionable findings.

**Deviations from PLAN.md**
- The midnight gate advances a file-backed injected `IClock` and exercises the
  required activation check instead of waiting for the one-shot timer in real
  time. Production still schedules exactly one timer for the next local
  midnight and reschedules it after each tick or activation.
- Search displays at most the store contract's top 1,000 ranked matches while
  still querying the complete FTS5 index; this keeps the live grouped view
  bounded and virtualized.

**Known issues carried forward**
- The framework-dependent startup and loaded-working-set misses recorded in M4
  remain open for profiling and the Native AOT gate in M9. M5 introduced no
  additional known issues.

## M6 — iCalendar interop (`.ics` import/export)          2026-07-31

**Gate:** AUTOMATED PASS; MANUAL INTEROP PENDING — all four fixture families
round-trip with equal VTODO/VALARM property sets while ignoring only `DTSTAMP`
and property order; unknown physical property lines, folding, and terminators
round-trip byte-for-byte; 70/70 Core/Data tests and the 7/7 M6 UI suite passed;
malformed input showed a line-specific dialog without a crash or partial
write; and Debug x64 plus Release x64 and ARM64 builds completed with zero
warnings. The required one-time hand import into a current Nextcloud Tasks or
Thunderbird installation has not been run in this environment, so M6 is not
reported as fully complete.

**Shipped**
- Added the purpose-built RFC 5545 `IcalReader` and `IcalWriter` with strict
  UTF-8, 75-octet folding, physical-line unfolding, TEXT and RFC 6868
  escaping, quoted parameters, DATE/DATE-TIME/TZID handling, RFC duration
  alarms, recurrence passthrough, local attachment URIs, and documented custom
  properties.
- Preserved unmodelled VTODO and VALARM properties verbatim in
  `tasks.ical_unknown`, together with internal presence and DTSTART timezone
  metadata needed to retain the original property set without freezing edited
  values.
- Added transactional SQLite preview/import, UID conflict replacement,
  deleted-task revival, collection replacement, child-before-parent support,
  cycle/cross-list hierarchy rejection, coalesced notifications, and complete
  per-list export queries including steps, alarms, tags, and attachments.
- Added localized Settings data commands, current desktop open/save pickers,
  task/list/conflict preview, line-specific import errors, per-list `.ics`
  export, all-list ZIP export, atomic destination replacement, progress/status
  feedback, responsive layout, and stable M6 AutomationIds.
- Added a sanitized Nextcloud, Tasks.org, Thunderbird, and Apple Reminders
  fixture corpus with source provenance, exact unknown-line assertions,
  canonical property-set comparisons, malformed data, storage rollback, and
  deterministic picker-driven UI coverage.
- Completed the WinUI code review; stale transfer feedback, UI types in the
  view model, generic generated-dialog IDs, temp-file cleanup failures,
  attachment path confinement, non-finite ordering, RFC duration edge cases,
  and hierarchy integrity findings were fixed before this entry.

**Deviations from PLAN.md**
- The checked-in client corpus is sanitized and minimized from public exports
  and documented client conventions rather than copied directly from personal
  live-client accounts. `tests/fixtures/ical/README.md` records provenance and
  explicitly does not treat those fixtures as the manual interop gate.

**Known issues carried forward**
- A human must import a Wintodo-exported list into current Nextcloud Tasks or
  Thunderbird and verify dates, priority, steps, and recurrence before M6 can
  be marked fully complete.
- The framework-dependent startup and loaded-working-set misses recorded in M4
  remain open for profiling and the Native AOT gate in M9.

## M7 — Reminders and Windows integration          2026-07-31

**Gate:** SKIPPED — not attempted. The owner deferred this milestone because
reminder delivery and the broader Windows integration set are not required for
the current MVP.

**Shipped**
- No M7 functionality was implemented in this milestone slot.

**Deviations from PLAN.md**
- M8 began before M7. This is an explicit sequencing decision, not a claim that
M7 passed; M7 remains available for implementation after the MVP priorities.

**Known issues carried forward**
- Reminder toasts, toast actions, rescheduling, taskbar badges, jump lists,
launch at login, and the share target remain unavailable until M7 is resumed.

## M8 — Design polish and accessibility          2026-07-31

**Gate:** AUTOMATED PASS; MANUAL NARRATOR LISTENING CHECK PENDING — the single
disposable-database `winapp ui` batch passed 14/14, including the zero-error
authored-control accessibility audit, keyboard-only create/complete/delete and
undo flow, every smart-list empty state, adaptive Settings layout, keyboard
map, list themes, and an OS High Contrast transition with state restoration.
The reviewed High Contrast screenshot hides raster backgrounds and uses system
colours for authored controls. Debug x64 and Release x64/ARM64 builds completed
with zero warnings. Core passed 40/40 and Data passed 30/30 in isolated runs.
An audible Narrator session was not run in this environment, so M8 is not
reported as fully complete.

**Shipped**
- Applied the DESIGN spacing, typography, resource-brush, hit-target, adaptive
  layout, Light/Dark/HighContrast, and reduced-motion rules across the shell,
  task rows, task detail, composer, dialogs, and Settings surface.
- Added Normal/Compact density and System/Light/Dark appearance settings, plus
  eight per-list raster themes decoded at the displayed physical-pixel width;
  appearance and background choices persist in SQLite.
- Added list-switch, row, importance, and completion motion; complete empty
  states; inline task rename; selection/task/list keyboard commands; and a
  localized, scrollable shortcuts dialog reachable from the keyboard and menu.
- Added explicit row accessibility sentences, live UI Automation Name updates,
  completion notifications through `AutomationPeer.RaiseNotificationEvent`,
  stable AutomationIds, 32x32 row actions, and deterministic High Contrast
  colour/background refresh behavior.
- Added `tests/ui/run-m8-gate.ps1` and its one-pass M8 scenario. Final artifacts
  are under `tests/ui/artifacts/m8/run-20260731-212054` (ignored by Git).

**Deviations from PLAN.md**
- M7-dependent notification and default-reminder settings remain absent because
  M7 was explicitly deferred for the current MVP.
- The screen-reader workflow is covered by keyboard-only UI Automation,
  accessible names, property-change events, and notification events, but the
  required human Narrator listening check still must be completed.

**Known issues carried forward**
- A human must run Narrator and confirm the create, complete, and delete flow is
  understandable without a mouse before M8 can be marked fully complete.
- The existing 5 ms smart-list microbenchmark is timing-sensitive: one loaded
  full-suite run measured My Day at 7.559 ms, while the isolated rerun passed.
- M7 remains deferred, and the framework-dependent startup/working-set misses
  remain assigned to the Native AOT performance gate in M9.

## Post-M8 — Smart-list task creation and composer rework          2026-08-01

**Gate:** PARTIAL — M8 gate passed 14/14 and the M5 gate passed 6/6 with the
change in place. The M3 suite passed 9/10 and the M4 gate passed 4/7; both
failures reproduce identically on the unmodified `a864b98` build (the M3
drag-reorder assertion and the M4 file-picker trio), so neither is a regression
from this work. Manual `winapp ui` verification covered My Day, Important,
Planned, All, Completed, the list picker, and a first-run database.

**Shipped**
- The add-task composer now works on every smart list, not only user lists. The
  task is created in a real user list — the composer's target-list picker
  chooses which, defaulting to the first list and remembering an explicit choice
  for the session — and the current smart list prefills the attribute that makes
  the task belong there: My Day sets `MyDayDate`, Important sets
  `Priority = High`, Planned preselects a due date of today. Completed hides the
  composer.
- A first-run database is seeded with one list named `Tasks`, and deleting the
  last remaining list recreates it, so the smart-list composer always has a
  target.
- Reworked the composer to a single 48 px row (DESIGN §3.2 updated). Chips moved
  inline to the right, show icon-only until a value is set, and are preceded by
  the list picker; a divider separates them from `Add`. The leading `+` glyph
  cross-fades to the task-row completion ring while composing.
- Fixed the composer text baseline. `VerticalContentAlignment` is inert on a
  `TextBox` — the template top-aligns text and only binds `Padding` — so the box
  now uses `MinHeight="0"` with symmetric `Padding="4"` and
  `VerticalAlignment="Center"`. The focused border/background overrides make the
  field read as part of the card instead of a nested control.
- Removed the `ListViewHeaderItem` divider rule, which drew a stray line above
  the first task row in ungrouped lists and under every Planned/Completed group
  header. The task list now uses its own header container template.

**Deviations from PLAN.md**
- DESIGN §3.2 previously specified an 88 px two-row focused composer. The
  single-row layout replaces it after a direct comparison with Microsoft To Do;
  §3.2 and the motion table were rewritten to match what ships.

**Known issues carried forward**
- Everything carried into M8 remains open (Narrator listening check, M7, and the
  Native AOT startup/working-set gate).
- The WinUI `TextBox` clear button is template-scoped (`DeleteButtonStyle` is an
  `x:Name` inside the control template) and cannot be suppressed per instance,
  so the composer keeps the standard `×` affordance while text is present.

## Post-M8 — Detail pane sizing and clipped icon buttons          2026-08-01

**Gate:** M8 gate passed 14/14 after the change (the responsive, High Contrast,
and accessibility scenarios are the ones this work could break). Other gates
were skipped at the owner's request for this session.

**Shipped**
- The detail pane is user-resizable. A 12 px `ContentSizer` from
  `CommunityToolkit.WinUI.Controls.Sizers` sits on its left edge (pointer drag or
  keyboard arrows), the width is clamped to 320–720 and further capped per
  responsive state, and the value persists in `appearance.detail_pane_width`
  behind a 500 ms debounce.
- Fixed the detail pane collapsing to roughly 200 px in the wide layout. The
  `Wide` state set `DetailHost.Width="Auto"` inside an `Auto` column, so the pane
  shrank to its content. Width is now one two-way `x:Bind` on `DetailHost` and no
  responsive state sets it.
- Fixed a whole class of clipped icons: a 32 × 32 `Button` with
  `SubtleButtonStyle` keeps the theme's 11 px horizontal padding, leaving a 10 px
  content box that slices a 20 px glyph. `Padding="0"` added to the new-group
  button (the reported clip), the list `⋯` menu button (rendering as a single
  dot in every screenshot so far), the compact title-bar search button, the
  detail importance star, and the seven custom-recurrence weekday toggles.
  DESIGN §0 now carries this as rule 9.
- Aligned the nav pane footer with the nav items (`Margin="4,0,4,4"`); it was
  4 px wider on each side, which pushed the new-group button past the pane clip.
- The detail title `TextBox` is now transparent and borderless in every state,
  matching the composer, so it no longer reads as a nested white box crowding the
  star and close buttons.

**Deviations from PLAN.md**
- New dependency: `CommunityToolkit.WinUI.Controls.Sizers` 8.2.251219. PLAN §3
  permits individual `CommunityToolkit.WinUI.Controls.*` packages at 8.2+; the
  alternative was a hand-rolled pointer-manipulation splitter with no keyboard
  or cursor support.

**Known issues carried forward**
- Everything from the previous entry stands, including the Narrator listening
  check, M7, and the Native AOT startup/working-set gate.

## Post-M8 — Detail pane chrome and icon columns          2026-08-01

**Gate:** Not run this round at the owner's request. Debug x64 builds with zero
warnings and the surfaces were verified manually with `winapp ui`.

**Shipped**
- Removed the list pane's 1 px right border. It drew a full-height rule
  immediately left of the detail card, so the pane read as a container inside a
  container. DESIGN §2 already forbade a rule there; the code disagreed.
  `ShellDividerBrush` is now unused but stays defined in all three theme
  dictionaries.
- Widened every detail-pane icon column from 16 to 20. `FontIcon`/`SymbolIcon`
  render at 20 px, so the My Day, reminder, due-date, repeat, attachment, and
  footer glyphs were sliced on both sides. DESIGN §0 rule 9 records this.
- Made the "Next step" field transparent and borderless like the detail title, so
  the card has no nested filled boxes other than the multi-line note field.

## Post-M8 — Row cards, pane limits, option states, delete confirmation          2026-08-01

**Gate:** Not run at the owner's request. Debug x64 builds with zero warnings;
every change was verified manually with `winapp ui` and pixel crops.

**Shipped**
- Task rows are rounded again and match the composer. `ListViewItemPresenter`
  draws its background, border, and selection fill square no matter what the
  item's `CornerRadius` says, and overriding `ListViewItemCornerRadius` at app
  level does not reach it either (verified with a 12 px probe). The row card is
  now a custom `ListViewItem` `ControlTemplate` — card `Border` + state overlay +
  `ContentPresenter`, with `CommonStates`, `DisabledStates`, and `DragStates`.
  The selection accent bar moved 4 px inward so it no longer straddles the
  rounded edge.
- The detail pane's drag limit is now `min(content width - 320, 960)`, recomputed
  on `ContentGrid` size changes, replacing the fixed 400/520/720 per-state caps.
  It grows with the window and always leaves 320 px of task list.
- Detail option rows are 4 px apart and colour-coded: glyph and label are
  `TextFillColorSecondaryBrush` when unset and `ListAccentBrush` when set, driven
  by a `VisualStateGroup` per row so the brushes survive a theme change. Added
  `HasDue`, `HasReminder`, and `HasRepeat` to the detail view model for the
  triggers.
- Deleting a task now confirms first, from all three entry points (row context
  menu, `Delete` key, detail footer). `TaskListViewModel.DeleteTaskAsync` and the
  detail delete command raise a request; `ShellViewModel` holds the pending
  target and `ShellPage` shows `Todo.Dialog.DeleteTask`. The undo snackbar still
  follows the confirmed delete. Both delete dialogs now default to **Cancel**.

**Deviations from PLAN.md**
- Second custom `ControlTemplate` for a standard control (DESIGN §0 rule 7). A
  `Style` cannot round `ListViewItemPresenter`; the alternative was leaving rows
  visibly inconsistent with the composer.
- The detail pane's `MaxWidth` is set from code-behind on size changes rather
  than by a visual state, because the limit depends on available width rather
  than on which state is active.

**Known issues carried forward**
- Everything from the previous entries stands: the Narrator listening check, M7,
  and the Native AOT startup/working-set gate.

## Post-M8 — Importance star checked appearance          2026-08-01

**Gate:** Not run at the owner's request. Debug x64 built with zero warnings,
and the active row star was verified in a targeted runtime screenshot.

**Shipped**
- Removed the stock blue checked-state fill from the row and detail importance
  buttons. An important task now shows only the filled `FavoriteStarFill` glyph
  in `ListAccentBrush`; pointer-over and pressed states retain subtle feedback.

**Deviations from PLAN.md**
- None. The change brings the implementation back in line with DESIGN §3.5.

**Known issues carried forward**
- Everything from the previous entries stands: the Narrator listening check, M7,
  and the Native AOT startup/working-set gate.

## Post-M8 — Completed-section visual polish          2026-08-01

**Gate:** Not run at the owner's request. A focused Debug x64 build and manual
collapsed/expanded screenshot review were used for validation only.

**Shipped**
- Reworked the completed disclosure into a compact, content-width 40 px card
  with balanced horizontal padding, neutral checked/hover/pressed states, and
  vertically aligned label and count.
- Corrected the collapsed disclosure glyph from `ChevronLeft` (`E76B`) to
  `ChevronRight` (`E76C`) and reduced it to the platform control-content size.
- Completed rows now mute and strike only their text content, leaving the
  completion and importance controls crisp and independently interactive.

**Deviations from PLAN.md**
- None. This is post-M8 visual polish within the approved Microsoft To Do parity
  surface.

**Known issues carried forward**
- Everything from the previous entries stands: the Narrator listening check, M7,
  and the Native AOT startup/working-set gate.

## Post-M8 — Opaque detail-pane overlay          2026-08-01

**Gate:** Not run at the owner's request. A focused Debug x64 build was used
for compile validation only.

**Shipped**
- Replaced the translucent detail-pane layer fill with
  `SolidBackgroundFillColorTertiaryBrush` in Light and Dark. This is the opaque
  Fluent surface matching the layer's composited colour, so compact and overlay
  layouts no longer show task-list content through the detail pane.
- Kept the High Contrast pane background on `SystemColorWindowColorBrush`.

**Deviations from PLAN.md**
- None. The existing overlay layout remains unchanged; only its persistent
  surface is now opaque.

**Known issues carried forward**
- Everything from the previous entries stands: the Narrator listening check, M7,
  and the Native AOT startup/working-set gate.

## Post-M8 — Opaque detail-pane transitions          2026-08-01

**Gate:** Not run at the owner's request. A focused Debug x64 build and a
targeted open-pane screenshot were used for validation only.

**Shipped**
- Kept the detail pane's bordered background at full opacity whenever the pane
  is visible. Open and close visual states now animate only an inner content
  layer, so the task list cannot bleed through during either transition.
- Preserved the 32 px content translation and the existing 200 ms open / 150 ms
  close durations.

**Deviations from PLAN.md**
- None. This corrects the motion implementation to preserve the specified
  opaque persistent surface.

**Known issues carried forward**
- Everything from the previous entries stands: the Narrator listening check, M7,
  and the Native AOT startup/working-set gate.

## Post-M8 — Task-row detail toggle          2026-08-01

**Gate:** Not run at the owner's request. A focused Debug x64 build and
targeted row-click checks were used for validation only.

**Shipped**
- Task rows now toggle their active state on every row click. Clicking an
  inactive row selects it and opens detail; clicking that row again deselects
  it and closes detail.
- Selecting another row transfers the single active state directly, despite the
  toggle-capable `ListView` selection mode.
- Closing detail through another route also clears the active row so the next
  row click opens it consistently.

**Deviations from PLAN.md**
- None. This makes the implemented pointer interaction match the selected-row
  and detail-pane state model.

**Known issues carried forward**
- Everything from the previous entries stands: the Narrator listening check, M7,
  and the Native AOT startup/working-set gate.

## M7 resumed — Reminders and Windows integration          2026-08-01

**Gate:** AUTOMATED PASS; REBOOT AND VISIBLE BADGE CHECKS PENDING — Debug and
Release x64 built with zero warnings. Core passed 40/40 and Data passed 36/36.
The isolated M7 gate seeded disposable databases, launched with `winapp run`,
verified the Settings surface, persisted reminder schedule identity, and
validated the overdue-plus-today badge input. With the app closed, real
two-minute Windows toasts fired; UI Automation chose **Complete** and **Snooze**
in separate runs, the broker-launched processes exited without showing a
window, and both resulting database states passed verification. A visible
taskbar badge and reminder survival across a reboot have not been observed, so
M7 is not reported as fully complete.

**Shipped**
- Added OS-scheduled reminder toasts with open, Complete, and Snooze actions;
  snooze supports 10 minutes, 1 hour, and tomorrow at 09:00. Scheduling is
  reconciled on launch, activation/resume, and store changes, including missed
  reminders and recurring-task advancement.
- Added packaged app-notification activation and COM manifest declarations so
  toast actions can update SQLite when the app is not already running.
- Added overdue-plus-today taskbar badge updates, a jump list with New task and
  the three most recent lists, an off-by-default launch-at-login setting, and a
  text share target that creates a task.
- Added task-aware pending-reminder and due-count store queries, focused data
  tests, and `tests/ui/run-m7-gate.ps1` with reproducible artifacts. The latest
  Settings and Snooze artifacts are under
  `tests/ui/artifacts/m7/run-20260801-194155`; the verified Complete run is
  under `tests/ui/artifacts/m7/run-20260801-193814`.

**Deviations from PLAN.md**
- Scheduled toasts use the alarm UID as `Tag` and the task UID as `Group`,
  rather than using the task UID for both. This preserves independent identity
  for multiple alarms while retaining task-wide cancellation.
- Scheduling uses the legacy `Windows.UI.Notifications` API because Windows App
  SDK app notifications still provide payload construction and activation but
  no scheduling API. The Windows integration service contains that deliberate
  UWP-era exception.
- Share-target activation necessarily uses
  `Windows.ApplicationModel.Activation.ShareTargetActivatedEventArgs`; it is
  the platform activation contract rather than a XAML namespace leak.
- Debug builds add a guarded `m7-gate.db` path to toast action arguments so a
  broker-launched gate process reopens the disposable database. The code is
  excluded from Release builds and cannot redirect production activation.

**Known issues carried forward**
- The visible taskbar badge and reboot reminder-persistence checks remain
  outstanding. The M8 Narrator listening check and M9 Native AOT
  startup/working-set gate also remain open.

## Post-M8 — Completed-section pointer crash          2026-08-01

**Gate:** FOCUSED PASS — Debug x64 built with zero warnings. Twelve consecutive
pointer expand/collapse cycles kept the packaged process alive, and the visible
row count returned 1 collapsed, 2 expanded, 1 collapsed again. Milestone gate
scripts were not re-run this session.

**Root cause.** The fail-fast was never a collection-mutation problem. The
completed-group header `ToggleButton` overrode its `ToggleButtonForeground*`
and `ToggleButtonBackground*` state keys with `<StaticResource ResourceKey=…/>`
alias entries. A header container is recycled on every re-expansion, and the
`ThemeResource` update pass cannot dereference an alias: it resolves to
`Unknown` and XAML raises `COMException 0x800F1001`, "Invalid attribute value
Unknown for property Foreground", which surfaces as the `0xC000027B` stowed
exception. The app had no `UnhandledException` handler, so the managed message
was never visible — only the native fail-fast stack. Two expansions were needed
to reproduce; a single expand/collapse always survived.

**Shipped**
- The group-header `ToggleButton` state overrides are concrete `SolidColorBrush`
  elements with `Color="{ThemeResource …}"`, matching the pattern already used
  in `TaskRow.xaml` and `TaskDetailView.xaml` — the two override sites that
  never crashed.
- `App` now traces unhandled exceptions as `WINTODO_UNHANDLED_EXCEPTION=…` and,
  in Debug, enables binding tracing with a `WINTODO_BINDING_FAILED=…` trace.
  The handler does not mark the error handled, so fail-fast behavior is
  unchanged.
- The earlier incremental-group-mutation workaround was reverted: it targeted
  the wrong cause and crashed identically. `ToggleCompletedAsync` calls
  `RebuildVisibleItems()` directly again, and the
  `CompletedExpansionChangeRequested` event, its deferred `DispatcherQueue`
  callback, and `ApplyCompletedExpansion` are gone.
- The M3 Completed regression keeps the real pointer click instead of
  `TogglePattern`, covering the input route that produced the fail-fast.

**Deviations from PLAN.md**
- None. The grouped `ListView`, sticky headers, built-in transitions, and the
  completed-section visual and persistence behavior remain unchanged.

**Known issues carried forward**
- The existing M3 harness still assumes expanded navigation and retains its
  coordinate-sensitive drag assertion.
- Milestone gates were not re-run after this fix, so the M3/M5/M8 suites remain
  unverified against the current tree.
- The visible taskbar badge and reboot reminder-persistence checks, the M8
  Narrator listening check, and the M9 Native AOT startup/working-set gate
  remain open.

## Post-M8 — Default "Tasks" list                      2026-08-02

**Gate:** PARTIAL — Debug x64 solution build with zero warnings, and the
`Wintodo.Data.Tests` suite passes at 39/39 (three new default-list tests).
Milestone UI gate scripts were not re-run this session; the local development
database was wiped so the next gate run starts on the new schema.

**Why.** Adding a task from a smart list targeted whichever list happened to
sort first, so a throwaway gate list ("M3 Gate") captured new tasks. The
requested model — tasks that belong to no list — is not expressible: `VTODO`
only exists inside a collection, so a listless task could never sync over
CalDAV. Microsoft To Do solves this the same way we now do, with a real default
list named "Tasks".

**Shipped**
- `lists.is_default` plus `ux_lists_default`, a unique partial index keyed on
  `COALESCE(account_id, 0)`, so each account has exactly one default list. The
  schema was edited in place rather than migrated; the app is pre-release and no
  installed database needs preserving.
- `ITaskStore.EnsureDefaultListAsync` creates or returns the default list and is
  idempotent. `GetListsAsync` orders `is_default DESC, sort_order, id`.
  `DeleteListAsync` throws for the default list, so import and future sync paths
  cannot remove it either.
- The composer resolves its target as session override, then default list, then
  first list. Choosing another list from the composer flyout now applies to that
  one task only and resets afterwards, matching Microsoft To Do.
- The default list renders as a pinned navigation entry below Completed and
  above the list separator, outside "Lists". It cannot be deleted, dragged, or
  used as a list-reorder drop target; it still accepts task drops and can be
  renamed, which maps cleanly to a CalDAV collection `DISPLAYNAME`.
- Share-target activation and the jump-list "new task" verb use the default list
  instead of the first list, and the recreate-a-list-after-deleting-the-last-one
  workaround in `ShellViewModel` is gone.

**Deviations from PLAN.md**
- PLAN.md described lists without a default; the default-list concept is new.
  `tasks.list_id` stays `NOT NULL` exactly as planned.

**Known issues carried forward**
- Milestone gates remain unverified against the current tree, and their
  navigation snapshots now contain the extra pinned item.
- The visible taskbar badge and reboot reminder-persistence checks, the M8
  Narrator listening check, and the M9 Native AOT startup/working-set gate
  remain open.

## Post-M8 — Flat navigation pane                      2026-08-02

**Gate:** FOCUSED PASS — Debug x64 built with zero warnings and deployed. On a
wiped database the pane renders My Day / Important / Planned / All / Completed,
then the pinned "Tasks" row, then the divider, then each user list as its own
top-level row. Creating "Groceries" placed it at the top level, and the My Day
composer still targeted "Tasks" with that second list present. Milestone gate
scripts were not re-run.

**Shipped**
- The "Lists" parent `NavigationViewItem` is gone. `PopulateUserListNavigationItems`
  now appends list and group items straight into `ShellNavigation.MenuItems`
  after a named `ListsSeparator`, which is what `docs/DESIGN.md` §5 always
  described.
- The default list renders with the `Home` glyph and no colour dot, matching the
  way Microsoft To Do presents its own "Tasks" entry.
- Dragging a list out of a group used the "Lists" header as its drop target.
  That path is replaced by dropping the list onto any ungrouped list — which
  already adopts the target's group — plus a "Remove from group" context item on
  grouped lists.
- The five gate scripts that invoked `Todo.Nav.UserLists` to expand the pane no
  longer do; every list is visible without expanding.
- The default list is no longer renameable. `CanDeleteSelectedList` became
  `CanEditSelectedList` and now gates the header title button, the rename and
  delete menu items, and F2; the pinned pane entry has no context menu at all.
  `BeginRenameList`, `CanCommitRenameList`, and `SelectListAndBeginRenameAsync`
  refuse it as well. `ITaskStore.RenameListAsync` deliberately still allows it,
  because a CalDAV server may rename the collection remotely.
- The detail-pane completion toggle and the step-row toggle no longer paint the
  default accent-filled `ToggleButton` box when checked. Both now carry the same
  transparent `ToggleButtonBackgroundChecked*` overrides the importance toggle
  already used, leaving just the filled circle and check.
- The list-title editor matches the title it replaces: same 28 px SemiBold ramp
  and accent foreground, so the name no longer shrinks to body text mid-rename.
  The title button now hides while `IsRenamingList` is set — both shared column 0
  and drew on top of each other before. `TitleEditBox` is a `TextBox` subclass
  that neutralises the built-in clear button in `OnApplyTemplate`; the control
  exposes no property for it, and at title size the glyph landed mid-header.
  Clicking outside the editor now leaves it — committing the new name, or
  reverting when the field was cleared — matching the inline task title editor.
  Enter and Escape are unchanged.
- The Important pane glyph is the outlined `FavoriteStar` (`E734`). The filled
  star stays reserved for a task row whose importance is set.
- "New group" uses a drawn list-card-with-plus mark instead of `NewFolder`;
  Segoe Fluent has no add-group glyph, and the folder and circled-plus
  alternatives both read wrong.
- Creating a list or group no longer pushes the whole pane down. NavigationView
  caps its menu `ScrollViewer` with a `MaxHeight` derived from the pane footer;
  showing the inline name box shrinks that cap, and the cap outlives the box. A
  stretched element clamped by `MaxHeight` centers in its slot, so the menu
  drifted down by half the difference. `PinMenuItemsToPaneTop` clears the cap and
  pins the ScrollViewer to the top once the inline editor closes.
- Task titles are vertically centered again. The `Active` and `Completed` visual
  states set `MetadataText.Visibility` and `CompletionText.Visibility`
  directly, and a visual-state setter outranks the `x:Bind` local value — so an
  active task with no metadata still reserved an empty 24 px caption line and
  pushed its title above center. Both setters are gone; the two captions now
  bind to `SecondLineVisibility` and `CompletionLineVisibility`, which check the
  completion state and the string together.

**Deviations from PLAN.md**
- None. PLAN.md and `docs/DESIGN.md` are updated to describe the flat pane and
  the pinned default list.

**Known issues carried forward**
- Per-list task counts in the pane are still not implemented.
- Milestone gates remain unverified against the current tree.
- The visible taskbar badge and reboot reminder-persistence checks, the M8
  Narrator listening check, and the M9 Native AOT startup/working-set gate
  remain open.

## M9 — Performance gate: Native AOT          2026-08-04

**Gate:** OPEN on one row. x64 and ARM64 Native AOT both publish with zero
trim/AOT warnings, the x64 AOT build passes the complete M8 UI/accessibility
script (14/14), and every gated §7 number passes as re-baselined by ADR-008.
The +10-minute idle sample — the leak gate — has still never been measured on
any build, so M9 does not close. ARM64 runtime performance is untestable on this
x64 host and is not claimed.

Against the originally locked budgets this gate read FAIL: every AOT startup row
and both required working-set rows missed by 1.7×–5.2×. A startup stage trace
attributed 76–78% of startup to reaching the first rendered frame, dominated by
first-use XAML runtime and window construction, which app code cannot move. The
owner reviewed the gap and chose an explicit re-baseline over further
optimisation against numbers that were locked before any WinUI 3 code existed.
`docs/adr/ADR-008-perf-budget-rebaseline.md` records the evidence, the revised
budgets, the reference machine, and three follow-ups.

**Shipped**
- Release now enables Native AOT, trimming, self-contained .NET, and
  self-contained Windows App SDK deployment. `scripts/Publish-Aot.ps1`
  publishes a runnable package layout, reports executable/layout sizes, and
  guards recursive output replacement. The x64 executable is 12.19 MB and the
  full layout is 188.13 MB.
- Native AOT exposed WinRT collection-marshalling failures that the managed
  build hid. Task groups, shortcut rows, steps, attachments, and option lists
  now expose exact `List<object>`-backed sources; mutable detail collections
  publish fresh snapshots so compiled `x:Bind` receives change notification.
  Group header metadata is kept separately and resolved by static x:Bind
  functions without reflection or converters.
- The M8 gate now matches the post-M8 row-toggle and delete-confirmation flows,
  uses an automation-visible detail control as its pane marker, and confirms
  deletion through authored AutomationIds. The shortcuts accelerator ignores
  the documented WinUI "another ContentDialog is open" HRESULT instead of
  terminating an AOT process. The shortcuts dialog is deferred with `x:Load`.
- Startup tracing now records first frame, first interactive frame, and elapsed
  stages. Windows integration overlaps shell hydration, placement loads in
  parallel with the shell, selected-navigation state joins the initial store
  reads, and the settings-only StartupTask state finishes after the list is
  interactive.
- `scripts/Measure-Perf.ps1` now supports FD/AOT builds and 0/5,000/20,000-task
  datasets, first-frame/interactive timing, working/private memory at +2s,
  +30s, and +10min, budget evaluation, query/search benchmarks, per-build
  scrolling probes, structured JSON output, and full-run `docs/PERF.md` rows.
  The seed fixture accepts a true empty DB and resolves list foreign keys by
  UID so the default Tasks list cannot skew the dataset.

**Shipped after the re-baseline decision**
- `docs/adr/ADR-008-perf-budget-rebaseline.md`. The startup gate is split into a
  total ceiling — which is dominated by framework cost and only catches gross
  regressions — plus a gated app-owned segment measured from the first rendered
  frame to the first interactive frame, which is where a real regression shows.
  Working-set gates move from the +2 s sample to the settled +30 s sample,
  because +2 s lands before the first list virtualisation pass and before the GC
  heap settles. The +10-minute rule now compares against +30 s rather than
  against launch, so it measures a leak instead of measuring startup. The
  framework-dependent build stops being a gate; it is a development-loop build
  from M9 onward and stays in `docs/PERF.md` for comparison. No budget that
  passed was touched, and no §7 rule was relaxed.
- `PLAN.md` §7 rewritten to the ADR-008 numbers, ADR-008 summarised in §2, and
  the M9 gate wording updated to point at it.
- `scripts/Measure-Perf.ps1` evaluates the ADR-008 budgets and emits
  `ColdAppWorkMs` / `WarmAppWorkMs` into `docs/PERF.md`, the summary JSON, and
  the console table.
- `scripts/Publish-Aot.ps1` puts the Visual Studio Installer directory on PATH
  before publishing. ILCompiler locates the MSVC linker by shelling out to
  `vswhere.exe` and taking stdout verbatim; with `vswhere` off PATH the shell's
  "not recognized" text landed in the linker command line and the ARM64 publish
  died with MSB3073 / exit 123.

**Measured result**
- Matrix artifact: `tests/ui/artifacts/perf/20260803-232609/summary.json`.
  All three AOT rows pass every gated ADR-008 number:

  | Row | Cold (≤ 3,000 ms) | Warm (≤ 2,300 ms) | WS @30 s | Budget |
  |---|---:|---:|---:|---:|
  | 0 / 1 list | 2,509 ms | 1,486 ms | 142.70 MB | ≤ 165 MB |
  | 5,000 / 20 | 1,809 ms | 1,306 ms | 175.88 MB | ≤ 200 MB |
  | 20,000 / 20 | 2,224 ms | 1,993 ms | 180.47 MB | ≤ 205 MB |

- App-owned startup work: 585.6 ms cold against 700 ms, 423.4 ms warm against
  550 ms. Both from the focused profile runs, not from a matrix row — the
  harness began emitting those columns after the matrix was measured.
- Data access passed at 1.893 ms maximum query and 2.309 ms search. AOT scroll
  passed at 119.37 fps, zero dropped frames, and zero callback allocations.
- x64 AOT M8 artifact:
  `tests/ui/artifacts/m9/m8-aot-exact-20260803-233642` (14 passed, 0 failed).
- ARM64 Native AOT publishes clean under `-warnaserror`: 12.61 MB executable,
  186.97 MB layout. x64 is 12.19 MB and 188.13 MB. Both are under the 260 MB
  installed-size budget.

**Deviations from PLAN.md**
- The §7 startup and working-set budgets were re-baselined by ADR-008 with owner
  approval. This is a real relaxation of those rows and is recorded as one.
- The +10-minute idle samples are still unmeasured. A run was started and
  stopped partway on 2026-08-04 at the owner's request, who chose to treat it as
  optional later work. It is deferred, not waived: M9 does not close until it is
  measured, and it is the only gate still without data.
- ARM64 performance is unmeasured and unmeasurable on this machine, which is an
  x64 host. Only ARM64 publishing is verified.
- UI-facing collections that cross the AOT WinRT projection use replaceable
  `IList<object>` snapshots instead of `ObservableCollection<T>`. This is
  required because the projected controls rejected the typed/custom vectors at
  runtime; the M8 AOT suite covers the resulting behavior.

**Known issues carried forward**
- M9 remains open on the +10-minute idle sample. Re-run with
  `pwsh -File scripts/Measure-Perf.ps1 -Milestone M9 -Builds AOT -SkipBuild`
  (~35 minutes, mostly idle); it also fills the two app-work columns.
- ADR-008 follow-ups for M10: isolate the true framework floor with a bare
  Native AOT WinUI window; fix cold-start measurement, which currently runs cold
  and warm back-to-back so "cold" shares a warm file cache (visible in the M9
  data as framework-dependent warm measuring slower than cold at both 5,000 and
  20,000 tasks); and decide the self-contained versus framework-dependent
  Windows App SDK trade deliberately.
- ARM64 runtime verification needs ARM64 hardware.
- The visible taskbar badge and reboot reminder-persistence checks, plus the M8
  Narrator listening check, remain open from earlier milestones.

## M13 — CalDAV sync          2026-08-04

**Gate:** AUTOMATED PASS; LIVE INTEROP OPEN. The production implementation is
present and the deterministic protocol, persistence, packaged UI, restart, and
Native AOT checks pass. Radicale and Docker/Nextcloud are not installed on this
machine, so the PLAN.md requirement to exercise both live servers has not run
and M13 is not reported closed.

**Shipped**
- Added the `Wintodo.Sync` project. `CalDavClient` performs principal and
  calendar-home discovery, enumerates VTODO collections, uses
  `sync-collection` REPORT tokens, falls back to ctag plus a full Depth-1
  PROPFIND when a token is invalid, and sends conditional task PUT/DELETE
  requests with ETags. Collection creation, rename, and deletion use
  MKCALENDAR, PROPPATCH, and DELETE.
- Added `CalDavSyncService` with pull-before-push cycles, per-account
  serialization, offline queueing, timer/manual/resume entry points, and
  explicit keep-mine/keep-theirs/keep-both conflict resolution. Server-provided
  URLs are restricted to the configured origin before Basic credentials are
  attached; non-loopback HTTP, URL credentials/fragments, DTD-bearing XML, and
  oversized XML/calendar responses are rejected.
- Extended `SqliteTaskStore` over the M1 sync columns and `change_log`: account
  and discovery state, per-list metadata/toggles, remote task snapshots,
  transactional pull application, race-aware change acknowledgements, remote
  versions, and conflict copies. Pulls do not generate outbound changes,
  disabled lists do not push, and discovery preserves pending local list
  deletes. No credential is written to SQLite.
- Added the app credential adapter over `Windows.Security.Credentials.PasswordVault`.
  Settings now provides server/user/password connection, account state, manual
  sync, disconnect, and per-list toggles. The navigation footer shows sync
  state. Sync runs every 15 minutes, immediately after restart, and after app
  resume. Conflicts use a deferred localized ContentDialog with three choices.
- Added automation IDs for every new interactive control and kept all authored
  UI strings in `Strings/en-US/Resources.resw`. Raw server exception text is
  traced but not exposed as unlocalized UI.

**Verification**
- `dotnet test Wintodo.sln -p:Platform=x64 -warnaserror --no-restore`: 88
  passed (Core 40, Data 39, Sync 9). Sync coverage includes discovery,
  VTODO-only collection enumeration, token deltas and invalid-token fallback,
  conditional writes, a stateful two-client create/edit/complete/delete
  convergence cycle, concurrent-edit detection and resolution, offline queue
  retention, write-acknowledgement races, pending list deletion, keep-both, and
  unknown iCalendar property preservation.
- `BuildAndRun.ps1 -SkipRun`: x64 Debug build passed with zero warnings and zero
  errors.
- `tests/ui/run-m13-gate.ps1`: 9/9 passed against the loopback CalDAV harness,
  including account discovery, per-list toggle, manual sync, real PasswordVault
  persistence, process restart with immediate resume sync, automation IDs, and
  disconnect. Artifact:
  `tests/ui/artifacts/m13/run-20260804-013505`.
- `scripts/Publish-Aot.ps1`: x64 and ARM64 Native AOT both published cleanly.
  x64 is 16.41 MB executable / 210.17 MB layout; ARM64 is 16.98 MB / 215.14 MB.
  Summary: `publish/aot-m13/summary.json`.
- `/winui-code-review` checks found no legacy Binding, missing explicit x:Bind
  modes, converter/reflection/legacy-dispatcher usage, or missing IDs on the new
  surface. The connected Settings screenshot was reviewed for clipping and
  overlap.

**Deviations from PLAN.md**
- The owner explicitly directed M13 implementation while M9 still has one open
  measurement row and M10-M12 have not shipped. This work therefore advances
  out of milestone order; the status of those milestones is unchanged.
- The deterministic loopback CalDAV harness supplements but does not replace
  the required live-server row. `radicale` is absent, the Python module is not
  installed, and Docker is absent. Installing either would modify the machine
  outside this repository and requires owner approval under AGENTS.md.

**Known issues carried forward**
- Run the convergence and unknown-property corpus against live Radicale and
  Nextcloud, and exercise the conflict ContentDialog through UI Automation.
  Those are the remaining M13 gate rows.
- Baikal and Fastmail remain optional interop targets if endpoints become
  available.

## Post-M13 — Custom emoji list icons          2026-08-04

**Gate:** PASS — 100/100 Core/Data/Sync tests passed; the focused list-icon UI
gate passed 6/6 across a packaged restart; the analyzer-backed Debug x64 build
completed with zero warnings; and x64 plus ARM64 Native AOT published cleanly.

**Shipped**
- Added optional custom emoji icons for non-default user lists. `Change icon`
  opens a localized, keyboard-focusable editor; invalid text stays open with an
  inline error; and `Remove icon` restores the existing theme-colour dot.
- Added shared extended-grapheme validation for joined, skin-tone, flag, and
  keycap emoji sequences. The fixed default Tasks list rejects icon changes at
  both the UI and store boundaries and continues to render Home.
- Added forward schema migration 002 with a pre-upgrade SQLite backup, nullable
  `lists.icon_emoji` persistence, ViewModel propagation, stable 20 px nav icon
  layout, and Segoe UI Emoji rendering.
- Added `X-WINTODO-LIST-ICON` whole-list `.ics` import/export support. The list
  colour remains independent and continues to tint the task surface.
- Added ADR-009 and updated the plan, visual design, and data-model contracts to
  make user-selected list emoji the sole exception to the product-icon emoji
  prohibition.

**Verification**
- `dotnet test Wintodo.sln -p:Platform=x64 -warnaserror --no-restore`: 100 passed
  (Core 50, Data 41, Sync 9).
- `BuildAndRun.ps1 -SkipRun`: Debug x64 passed with zero warnings and errors.
- `tests/ui/run-list-icon-gate.ps1`: 6/6 passed for create, edit, rendered icon,
  invalid-input feedback, packaged restart persistence, and removal. Visual QA
  found no clipping, overlap, or alignment issue. Artifacts:
  `tests/ui/artifacts/list-icon/run-20260804-091103`.
- `scripts/Publish-Aot.ps1 -OutputDirectory publish/aot-list-icon`: x64 is
  16.49 MB executable / 210.82 MB layout; ARM64 is 17.07 MB / 215.78 MB. Both
  completed without trim/AOT warnings; summary is
  `publish/aot-list-icon/summary.json`.
- `/winui-code-review` checks found no new legacy Binding, converter,
  code-behind Click/Tapped handler, raw colour/radius/type value, missing
  explicit x:Bind mode, or missing AutomationId on the new interactive surface.

**Deviations from PLAN.md**
- The owner explicitly added emoji list icons to the parity target. ADR-009
  records that direction and narrows the earlier design-wide emoji ban because
  it contradicted the requested Microsoft To Do behavior.
- CalDAV collection metadata currently writes display names only. Emoji icons,
  like list colours and backgrounds, remain local to this client; manual `.ics`
  export/import preserves them through the open extension property.

**Known issues carried forward**
- M13 live Radicale/Nextcloud interop, the conflict dialog UI row, M9's open
  10-minute idle sample, the visible taskbar badge/reboot reminder checks, the
  M8 Narrator listening check, and ARM64 runtime verification remain unchanged.

## Post-M13 — Title emoji picker and navigation counts          2026-08-04

**Gate:** PASS — 101/101 Core/Data/Sync tests passed; the focused list-icon UI
gate passed 10/10 across a packaged restart; the analyzer-backed Debug x64
build completed with zero warnings; and x64 plus ARM64 Native AOT published
cleanly.

**Shipped**
- Added a Microsoft To Do-style emoji button directly beside editable user-list
  titles. Its localized flyout exposes 36 preset emoji in a stable six-column
  grid, keeps rename mode active while open, updates the header and navigation
  icon together, and includes a clear action. The existing custom-emoji dialog
  remains available for emoji outside the preset catalog.
- Added a derived incomplete-task count for every persisted list. Counts include
  active root tasks only, exclude child steps, completed tasks, and soft-deleted
  tasks, and appear in zero-hidden badges for both user lists and the fixed
  default Tasks list.
- Refreshes navigation counts after create, complete, uncomplete, delete, move,
  import, sync, and restart paths. Stale overlapping refreshes are discarded.
- Added localized picker names/tooltips, count-aware navigation accessibility
  names, stable automation IDs, focused data coverage, and packaged UI coverage.
- Updated PLAN.md, the visual and data-model contracts, and ADR-009 for the
  title-adjacent picker and query-derived count semantics.

**Verification**
- `dotnet test Wintodo.sln -p:Platform=x64 -warnaserror --no-restore`: 101 passed
  (Core 50, Data 42, Sync 9).
- `BuildAndRun.ps1 -SkipRun`: Debug x64 passed with zero warnings and errors.
- `tests/ui/run-list-icon-gate.ps1`: 10/10 passed, including the default Tasks
  badge, user-list two/zero/one transitions, title picker selection, header/nav
  propagation, custom validation, restart persistence, and removal. Visual QA
  found no clipping or overlap. Artifacts:
  `tests/ui/artifacts/list-icon/run-20260804-094855`.
- `scripts/Publish-Aot.ps1 -OutputDirectory publish/aot-title-icon-counts`: x64
  is 16.56 MB executable / 211.24 MB layout; ARM64 is 17.14 MB / 216.20 MB.
  Both completed without trim/AOT warnings; summary is
  `publish/aot-title-icon-counts/summary.json`.
- Static review found no new legacy Binding, converter, reflection,
  code-behind Click/Tapped handler, UWP XAML namespace, missing explicit x:Bind
  mode, or missing AutomationId on the new interactive surface.

**Deviations from PLAN.md**
- None beyond the already accepted owner-directed emoji parity extension in
  ADR-009. The picker and counts follow the existing Microsoft To Do navigation
  and inline-title patterns.

**Known issues carried forward**
- M13 live Radicale/Nextcloud interop, the conflict dialog UI row, M9's open
  10-minute idle sample, the visible taskbar badge/reboot reminder checks, the
  M8 Narrator listening check, and ARM64 runtime verification remain unchanged.

## Post-M13 — Startup navigation pane repair          2026-08-04

**Gate:** PASS — the production-data startup reproduction renders the expanded
pane correctly; the shell responsive gate passed 10/10; the list-icon restart
gate passed 10/10; and the analyzer-backed Debug x64 build completed with zero
warnings.

**Shipped**
- Replaced competing manual `PaneDisplayMode`/`IsPaneOpen` visual-state setters
  with NavigationView's built-in `Auto` mode at the existing 640/900 DIP
  thresholds. This prevents a restored list selection from leaving compact
  item templates inside the 300 DIP expanded-pane reservation during startup.
- Retained the existing visual states for shell padding, detail placement,
  footer commands, and the minimal-mode toggle button.
- Extended the persisted emoji-list restart gate to require a visible,
  nonzero-sized navigation label, not merely a discoverable list item.

**Verification**
- `BuildAndRun.ps1 -SkipRun`: Debug x64 passed with zero warnings and errors.
- `tests/ui/m2-shell.ps1`: 10/10 passed at wide, 656 DIP compact, and 640 DIP
  minimum widths. Screenshots in `tests/ui/artifacts/m2` show correct content
  bounds and pane behavior.
- `tests/ui/run-list-icon-gate.ps1`: 10/10 passed across restart with the new
  visible-label assertion. Artifacts:
  `tests/ui/artifacts/list-icon/run-20260804-102531`.
- Direct production-data startup capture confirms the original failure no
  longer reproduces: `tests/ui/artifacts/m2/09-real-startup-auto-pane.png`.

**Deviations from PLAN.md**
- None. The repair uses NavigationView's supported adaptive display mode and
  preserves the approved navigation structure and breakpoints.

## Post-M13 — Fixed-category header icons          2026-08-04

**Gate:** PASS — the analyzer-backed Debug x64 build completed with zero
warnings, and the responsive shell UI gate passed 10/10.

**Shipped**
- Added the matching Fluent navigation glyph beside the page title for My Day,
  Important, Planned, All, Completed, and the pinned default Tasks list.
- Reused the existing 44 DIP header icon slot so fixed glyphs and editable-list
  emoji align identically. Search and Settings remain text-only, and the task
  count stays directly adjacent to the title.
- Kept fixed glyphs decorative in the accessibility tree; the localized list
  title remains the single accessible page heading.
- Updated the header design contract with fixed-category icon behavior.

**Verification**
- `BuildAndRun.ps1 -SkipRun`: Debug x64 passed with zero warnings and errors.
- `tests/ui/m2-shell.ps1`: 10/10 passed at wide, compact, and minimum widths.
- Visual QA confirmed Home, Brightness, and FavoriteStar headers plus the
  unchanged custom-emoji header/count layout. Artifacts:
  `tests/ui/artifacts/m2/15-fixed-header-my-day.png` through
  `tests/ui/artifacts/m2/18-custom-header-preserved.png`.

**Deviations from PLAN.md**
- None. The change makes the centre-pane header follow the fixed navigation
  categories already specified by the Microsoft To Do parity design.

## Post-M13 — Fixed-category counts and appearance          2026-08-04

**Gate:** PASS — the full x64 test suite, analyzer-backed Debug build, clean
restart persistence check, and responsive shell UI gate all passed.

**Shipped**
- Removed task counts from every page header and removed the My Day header
  glyph. Navigation now shows zero-hidden counts for My Day, Important,
  Planned, All, Completed, Tasks, and user lists.
- Replaced the All list glyph with an infinity symbol in both navigation and
  the page header.
- Enabled the existing background picker for fixed categories and persisted
  each selection in settings.
- Tinted fixed-category navigation and header glyphs with their semantic or
  selected background colour. Added an Appearance toggle to return these
  glyphs to the neutral foreground.
- Added a single aggregate smart-count query, refresh propagation, localized
  count-aware accessibility names, automation IDs, and focused data coverage.

**Verification**
- Focused smart-list and persisted-list count tests: 2/2 passed.
- `dotnet test Wintodo.sln -p:Platform=x64 -warnaserror --no-restore`: 102 passed
  (Core 50, Data 43, Sync 9).
- `BuildAndRun.ps1 -SkipRun`: Debug x64 passed with zero warnings and errors.
- Clean packaged restart preserved the enabled icon-tint preference and restored
  fixed-category counts and glyph colours.
- `tests/ui/m2-shell.ps1`: 10/10 passed at wide, compact, and minimum widths.
- Direct UI Automation verified My Day without a header glyph/count, zero-hidden
  fixed-category badges, the All infinity glyph, background-driven tinting, and
  the fixed-category tint preference. Screenshots:
  `tests/ui/artifacts/m2/21-fixed-category-header-tint.png` through
  `tests/ui/artifacts/m2/26-restart-my-day.png`.
- `git diff --check`: passed.

**Deviations from PLAN.md**
- None. This extends the existing list-theme and navigation-count behavior to
  the fixed categories requested by the owner.

## Post-M13 — Unified navigation count badges          2026-08-04

**Gate:** PASS — the analyzer-backed Debug build, full x64 test suite, list-icon
interaction gate, and responsive shell gate all passed.

**Shipped**
- Replaced the smart-category default circular badges and the persisted-list
  custom badge implementation with one shared neutral rectangular `InfoBadge`
  style for every navigation count.
- Reverted All to the original Fluent List glyph after visual review so its
  weight, bounds, and rendering match the other fixed-category icons.
- Applied one 8-DIP fixed-category end-inset token so smart, default, and user-list
  count badges share the same right-edge alignment.
- Updated the navigation design contract to require one count-badge treatment.

**Verification**
- `BuildAndRun.ps1 -SkipRun`: Debug x64 passed with zero warnings and errors.
- `dotnet test Wintodo.sln -p:Platform=x64 -warnaserror --no-restore`: 102 passed
  (Core 50, Data 43, Sync 9).
- `tests/ui/run-list-icon-gate.ps1`: 10/10 passed, including default/user-list
  count transitions and their preserved automation IDs.
- `tests/ui/m2-shell.ps1`: 10/10 passed at wide, compact, and minimum widths.
- Visual QA confirmed one aligned expanded-pane count column, no compact-pane
  badge/icon overlap, and matching Fluent List glyphs in navigation and header.
  Screenshots: `tests/ui/artifacts/m2/31-aligned-badges-final.png` and
  `tests/ui/artifacts/m2/32-all-list-glyph.png`.

**Deviations from PLAN.md**
- None. This corrects visual inconsistency in the existing count presentation.

## Post-M13 — Detail editor Enter-key isolation          2026-08-04

**Gate:** FOCUSED PASS — the analyzer-backed Debug x64 build completed with zero
warnings, all 102 Core/Data/Sync tests passed, and the new Enter regression plus
the following title/note/importance/My Day flow passed in the packaged app.

**Shipped**
- Scoped the task-list Enter accelerator away from focused text editors so
  list-level Enter handling cannot claim input from the detail editors.
- Suppressed `NavigationView.SelectionChanged` while navigation items are
  rebuilt, preserving the selected list and open detail pane during count
  refreshes.
- Limited count refreshes to completion, importance, due-date, and My Day field
  changes. Title, note, reminder, recurrence, attachment, and step edits no
  longer rebuild navigation because they cannot affect a navigation count.
- Extended the M4 UI gate to verify Enter preserves both the open detail pane
  and unsaved title/note text, while the existing next-step flow still verifies
  Enter adds a step without dismissing the pane.

**Deviations from PLAN.md**
- None. This is a focused keyboard interaction repair for the existing M4
  detail-pane contract.

**Verification**
- `BuildAndRun.ps1 -SkipRun`: Debug x64 passed with zero warnings and errors.
- `BuildAndRun.ps1 ... /p:Configuration=Release -SkipRun`: Release x64 passed
  with zero warnings and errors.
- `dotnet test Wintodo.sln -p:Platform=x64 -warnaserror --no-restore`: 102
  passed (Core 50, Data 43, Sync 9).
- `tests/ui/run-m4-gate.ps1`: the new Enter regression, detail editing/actions,
  recurrence, and all next-step additions passed. The broader historical gate
  remains red on unrelated drag-reorder and file-picker automation; the open
  picker then contaminated the final accessibility and Escape checks. Artifacts:
  `tests/ui/artifacts/m4/run-20260804-131305`.

## Post-M13 — Pane footer list and group composer          2026-08-06

**Gate:** NOT RUN — UI gates were deliberately skipped for this change at the
requester's instruction. The analyzer-backed Debug x64 build completed with zero
warnings and every flow below was verified by hand in the packaged app.

**Shipped**
- Replaced the stacked name box with an in-place composer: choosing New list or
  New group turns the footer's action row into a 40px card carrying a leading
  icon and a chrome-free text box, so the pane keeps one action row in every
  state instead of growing a detached field above the buttons.
- Hid the action buttons through a wrapper element while a composer is open. The
  responsive visual states already own each button's own `Visibility`, and two
  owners on one property fight.
- Added commit-or-revert on losing focus, matching the inline list rename.
  Leaving the window is excluded: focus lands on nothing inside the app there, so
  a half-typed name survives an alt-tab.
- Returned focus to the originating button once a composer closes. Without a
  landing spot focus flew to the next tab stop, where the key-up half of the
  committing Enter press opened Settings.
- Swapped the hand-drawn group mark for the `NewFolder` Segoe Fluent glyph, and
  the New list mark from a 20px `SymbolIcon` to a 16px `FontIcon`, so both footer
  icons share one weight and follow the button foreground in every visual state.
- Dropped `TextControlThemeMinHeight` to zero for the composer boxes. A text box
  lays its single line out at the top of whatever height it gets, so the theme's
  32px floor left the text and placeholder riding about 3px above the centre of
  the row.

**Deviations from PLAN.md**
- None. This is presentation and keyboard-focus work on the existing navigation
  pane contract.

**Known gap**
- Groups can still only be created. `ITaskStore.DeleteGroupAsync` exists but
  nothing in the UI calls it, and there is no rename either, so a group made by
  mistake cannot be removed.

**Verification**
- `BuildAndRun.ps1 -SkipRun`: Debug x64 passed with zero warnings and errors.
- By hand in the packaged app: create by Enter, revert by Escape, commit by
  clicking away, group creation, the composer surviving window deactivation, and
  focus returning to the button afterwards. Alignment was measured from window
  captures rather than judged by eye — the caret centre moved from 3px above the
  card centre to within half a pixel of it.

## Post-M13 — AI-friendly repo docs and toolchain 0.7          2026-10-10

**Gate:** PASS for this change. `dotnet build Wintodo.sln` Debug and Release x64
with `-warnaserror` completed with zero warnings; `dotnet test` passed 102/102
(Core 50, Data 43, Sync 9); `BuildAndRun.ps1` built and launched the packaged
app through WinApp CLI 0.7.1 project mode against a scratch database, and the M0
smoke suite passed 4/4 against it; and `scripts/Publish-Aot.ps1 -Platforms x64`
published Native AOT with zero warnings (16.68 MB binary, 212.20 MB publish
folder). Feature UI gates (M4–M8, M13) were not re-run:
the only app-code change is a behaviour-preserving extraction in
`WindowsIntegrationService`.

**Shipped** ([ADR-010](adr/ADR-010-ai-friendly-repo-docs.md))
- `AGENTS.md` rewritten as a short, agent-neutral entry point (read order,
  commands, definition of done, hard rules, where-things-are); `CLAUDE.md`
  imports it. Mandatory per-session reading drops from about 110 KB to
  `AGENTS.md` + `ARCHITECTURE.md` + this summary.
- New `ARCHITECTURE.md` describing the code as built, with a known-divergences
  table against `PLAN.md` §3/§5/§6.
- `docs/README.md` index; `docs/adr/README.md` index and template; ADR-001…007
  moved verbatim out of `PLAN.md` §2; `docs/guides/` with build-and-run,
  testing, migrations, performance and ai-tooling guides.
- `PLAN.md` keeps scope, §7 budgets and milestones; §0, §2, §4, §5 and the §10
  XAML risk now point to the new docs. Section numbers unchanged.
- This file gained the "Current state" summary above; the stale "no milestones"
  placeholder was removed.
- win-dev-skills v0.7.1 vendored into `.agents/skills/` and mirrored into
  `.claude/skills/` by the new `scripts/Update-AgentSkills.ps1`.
- WinApp CLI upgraded 0.5.0 → 0.7.1 on the owner's machine (owner approved).
  `Microsoft.Windows.SDK.BuildTools.WinApp` 0.5.0 → 0.7.1.
- The copied `analyzer/` DLL and its `Directory.Build.props` wiring were
  replaced by the `Microsoft.Windows.SDK.BuildTools.WinUIAnalyzer` 0.7.1 NuGet
  package, so plain `dotnet build`, `winapp run`, the IDE and CI all run it.
- `BuildAndRun.ps1` is now a thin wrapper over project-mode `winapp run`
  (upstream 0.7 removed the old wrapper). Old `-SkipRun` / `-UseMSBuild` flags
  are gone; use `dotnet build` or `--no-launch`.
- `.mcp.json` registers the Microsoft Learn MCP server.
- `.github/workflows/ci.yml`: x64 build with warnings as errors plus the three
  xunit projects on `windows-latest`.
- Removed an empty stray `Microsoft.PowerShell.Core/` folder from the repo root.

**Deviations from PLAN.md**
- **WUI1001 suppressed** — the newer analyzer flags
  `ToastNotificationManager.CreateToastNotifier()` and recommends
  `AppNotificationManager`, which has no scheduling API. Scheduled reminder
  toasts are the deliberate M7 design (`PLAN.md` M7, `AGENTS.md` allows
  `Windows.UI.Notifications`). Both call sites now go through one
  `CreateScheduledToastNotifier()` helper with a scoped `#pragma` and comment.
  Remove the suppression if the Windows App SDK ships a scheduling API.
- The repo-wide rule "suspect the last-edited `.xaml` on a silent build failure"
  was retired: the underlying XAML-compiler bug is fixed in Windows App SDK
  2.1.3 and later.

**Known issues carried forward**
- ~~CI has not run on GitHub yet.~~ First run (commit `7fec1b6`, run 38045772106)
  passed: build with warnings as errors and 102/102 tests on `windows-latest`.
- Unchanged: M6 hand import, M7 reboot/badge checks, M8 Narrator check, M9
  +10 min sample and ARM64 runtime, M13 live-server rows.

## Post-M13 — Stack and library upgrades          2026-10-10

**Gate:** PASS. Debug and Release x64 builds with `-warnaserror`: zero warnings.
`dotnet test` 102/102 on xunit v3 / Microsoft Testing Platform, with TRX and
coverage reports produced. `scripts/Publish-Aot.ps1 -Platforms x64`: Native AOT,
zero warnings, 16.69 MB binary, 220.18 MB publish folder (was 212.20 MB; the
Windows App SDK 2.5 runtime is larger; budget is ≤ 260 MB). M8 UI gate 14/14 on
the Debug x64 build. Other UI gates and `Measure-Perf.ps1` were not re-run.

**Shipped**
- .NET SDK 10.0.302 → 10.0.401 (runtime 10.0.12), installed on the owner's
  machine and pinned in `global.json`. .NET 11 is RC and STS, so the app stays on
  the .NET 10 LTS line.
- `Microsoft.WindowsAppSDK` 2.3.1 → 2.5.1 (bug fixes behind runtime-compatibility
  switches; no breaking changes in the 2.4 / 2.5 release notes).
- `Microsoft.Windows.SDK.BuildTools` 10.0.28000.2526 → .2705;
  `Microsoft.Data.Sqlite` 10.0.10 → 10.0.12.
- Tests moved from xunit 2.9.3 (final v2 release) to `xunit.v3` 4.0.2 on
  Microsoft Testing Platform. `global.json` opts `dotnet test` into MTP; test
  projects are `OutputType=Exe`. `Microsoft.NET.Test.Sdk`,
  `xunit.runner.visualstudio` and `coverlet.collector` (VSTest-only) are replaced
  by `Microsoft.Testing.Extensions.CodeCoverage` 18.12.0 and
  `Microsoft.Testing.Extensions.TrxReport` 2.5.1. CI now runs
  `dotnet test --solution … --report-trx --coverage`.
- xunit v3's analyzer rule xUnit1051 (pass `TestContext.Current.CancellationToken`)
  is fixed at all 307 call sites, not suppressed; 37 of them use a named
  `cancellationToken:` argument because optional parameters precede it.
- Already current, unchanged: `CommunityToolkit.Mvvm` 8.4.2,
  `CommunityToolkit.WinUI.Controls.*` 8.2.251219 (8.3 is preview only),
  `SQLitePCLRaw.bundle_e_sqlite3` 3.0.5, WinApp CLI / build tools / analyzer 0.7.1.

**Deviations from PLAN.md**
- None. PLAN §3 already allowed "2.3.1+ stable"; the stack table now records the
  current versions and the test runner.

**Known issues carried forward**
- The .NET SDK installer asked for a reboot; builds and tests already run on
  10.0.401.
- Unchanged: M6 hand import, M7 reboot/badge checks, M8 Narrator check, M9
  +10 min sample and ARM64 runtime, M13 live-server rows.

## 2026-10-10 — M9 harness repair, AOT crash fix, idle gate blocked

**Gate: BLOCKED, not closed.** The exact requested command,
`./scripts/Measure-Perf.ps1 -Milestone M9 -Builds AOT`, published x64 cleanly,
then stopped before any runtime matrix sample: the old VSTest filter selected
zero tests under Microsoft Testing Platform. Artifact:
`tests/ui/artifacts/perf/20261010-104850/data-access.log`.

The harness now uses MTP project/filter arguments and live output, appends
rather than overwrites previous PERF rows, and returns failure after preserving
a complete measured matrix when a budget fails. Verified the actual 5,000-task
seed, parsed query/search output, monotonic-growth and +10 MB rejection checks,
and repeat appends against an ignored copy of PERF.md. No budget changed.

The idle precondition could not be met: repeated host CPU observations were
76–100%, with Zed, ingest tests, rustc, Windows Search and other workloads
active. The owner was asked to pause workloads; no idle-ready reply arrived.
No +2 s, +30 s, +10 min or scroll sample was taken, no new matrix row was
fabricated, and no leak conclusion is possible. All three datasets remain
pending; rerun the full command on an otherwise idle host. ARM64 runtime perf
is **not measured** on this x64 machine.

The published x64 AOT M8 attempt failed 13/14 checks after a startup crash
(`tests/ui/artifacts/m9-aot/run-20261010-105453`). Attached `winapp run
--debug-output` diagnosed `Argument 'source' is not a supported vector` at
`ItemsRepeater.ItemsSource` for `TaskListViewModel.ListIconChoices`. Changed
that source to the existing `IList<object>` vector pattern and made its item
class partial for CsWinRT. No XAML or user choices changed. Clean AOT republish:
16.69 MB executable, 220.17 MB layout (under the 260 MB size ceiling).
The AOT M8 rerun passed **14/14 automated checks**:
`tests/ui/artifacts/m9-aot/run-20261010-110036/results.json`.

Validation: requested x64 warnings-as-errors build clean; unit tests **102/102**
(Core 50, Data 43, Sync 9). Debug M8 automation also passed 14/14 before the
fix; final Debug gate is recorded with the M10 entry. Reviewed both suites'
six screenshots: settings capture shows the shell, and the background capture
does not visually prove the chosen raster. These visual coverage gaps and the
manual Narrator check remain open; 14/14 automation is not full visual signoff.
`winui-code-review` of the targeted collection change found no issue above
informational. Existing uncommitted settings edits were preserved and excluded
from this milestone commit.

## 2026-10-10 — M10 unsigned packaging groundwork

**Scope: groundwork shipped; release gate OPEN.** Started after M9 was recorded
as blocked on the required idle window, as authorized by the owner.

Reviewed `Package.appxmanifest`: only `runFullTrust`, no
`broadFileSystemAccess`. Toast COM and optional startup extensions retained.
Name `5826186B-4750-4C22-BC50-B5DD1EED9CA5`, publisher `CN=AppPublisher`, app
name and version unchanged. Final name/publisher remain the owner's decision.

CI now accepts `v*` tag pushes and has a separate x64/ARM64 release matrix,
after build/test succeeds. It verifies target MSVC tools, AOT-publishes through
`Publish-Aot.ps1`, builds unsigned MSIX using the centrally pinned restored
WinApp CLI, and uploads architecture-specific packages and size summaries.
No signing credentials, certificate trust, app installation or GitHub Release
publication in CI. Tag workflow execution is **not measured/not run** locally;
no tag was created. Validated YAML trigger/condition/matrix and PowerShell
syntax, and ran the real local publish/package commands for both architectures.

CLI 0.7.1 packaging without explicit AOT properties hit CsWinRT IL2081 trim
errors. Explicit `PublishAot=true`, `SelfContained=true`, `PublishTrimmed=true`,
`PublishReadyToRun=false` and warnings-as-errors packaged cleanly; those are
recorded in CI and `docs/guides/packaging.md`, with no warning suppression.
Both package payloads were inspected: matching PE architecture, no managed CLR
header, PRI present, unchanged placeholder identity, only `runFullTrust`, no
signature. Unsigned x64 MSIX **54.07 MB**, ARM64 **52.56 MB**. AOT layouts:
x64 **220.17 MB**, ARM64 **225.69 MB**, both below the 260 MB size ceiling;
ARM64 runtime performance is **not measured**.

Generated an ignored local test certificate matching the placeholder publisher
and signed a separate x64 package copy successfully (**54.09 MB**). Certificate
was not installed/trusted; no install/uninstall gate attempted. Production
signing, final Store assets, clean-VM install/launch and uninstall cleanup are
**pending**, not passed. No accepted ADR or budget was changed.

Final requested checks: `dotnet build Wintodo.sln -p:Platform=x64 -warnaserror`
passed with zero warnings/errors; `dotnet test Wintodo.sln -p:Platform=x64`
passed **102/102**, zero skipped; `tests/ui/run-m8-gate.ps1` passed **14/14
automated checks** (`tests/ui/artifacts/m8/run-20261010-110304/results.json`).
All six final screenshots reviewed; the existing settings/background visual
coverage gaps recorded in M9 persist, so no full visual gate claimed.
`winui-code-review` of the targeted fix found no issue above informational.
Existing settings/CodeGraph working-tree changes excluded from both commits.

## 2026-10-10 — M8 follow-up: composer stays usable with detail open

Opening the inline detail pane left only 112 px for the task title in the
expanded composer. Added composer-width visual states: below 880 DIPs, the
inline list/date/reminder/repeat chips move to one localized Task options
button with native submenus, while the title and Add remain on the same
48 px row. Inline chips return at wider widths; value labels are capped at
72 DIPs. Drafts and option selections survive resizing. Existing commands,
quick choices and Clear actions are reused; no dependency or model change.
SizeChanged only selects a VisualState; visual properties stay in XAML.
Updated DESIGN §3.2 and the testing guide. The M9 performance task remains
owner-deferred and was not resumed.

Validation: x64 warnings-as-errors build clean; 102/102 unit tests, zero skips;
x64 Native AOT publish clean. The new `run-composer-layout-gate.ps1` passed
3/3 checks in Debug (`artifacts/composer/run-20261010-112948`) and AOT
(`artifacts/composer/run-20261010-113025`): usable input with detail open,
compact menu reachability and scheduled task creation, narrow/wide resize
with draft preservation. All eight focused screenshots reviewed: no clipping
or overlapping composer controls at the tested widths. Initial focused runs
found test setup issues (detail already closed after commit, focus remaining
in a collapsed composer, popup foreground targeting, startup window timing);
the harness now captures the main HWND, waits for startup and changes focus
before reopening the composer. Those failed artifacts are retained.

Full M8 first attempt failed 9/14 checks, beginning with keyboard row
activation (`artifacts/m8/run-20261010-112733`). Repeated with one explicit
WINAPP_UI_WORKFLOW_ID after the other run completed, without changing app
code: **14/14 automated checks passed** (`artifacts/m8/run-20261010-113116`).
This supports desktop-input contention as the cause but does not prove it.
Manual Narrator listening and the existing settings/background screenshot
coverage gaps remain open; no complete manual gate is claimed.
`winui-code-review` of the composer change found no issue above informational:
explicit x:Bind modes, PropertyChanged text binding, localized accessible
names, theme resources, command actions, 4 px spacing and no reflection.
Existing uncommitted settings/CodeGraph edits were preserved. The new composer
strings are added alongside the owner's existing Resources.resw edits.

## 2026-10-10 — M8 follow-up: clear stale My Day startup selection

Reproduced the owner's double-highlight report after restoring Completed:
UI Automation reported **My Day IsSelected=True and Completed IsSelected=True**,
with **HasKeyboardFocus=False** on both, even after moving the pointer away.
It was a stale selection flag, not keyboard focus or an ordinary hover.
My Day had `IsSelected="True"` locally in XAML; startup then assigned the saved
item through `NavigationView.SelectedItem`. Removed the local flag so
`ShellPage.InitializeAsync` owns initial selection. The existing fresh-database
fallback to My Day remains unchanged. No control/template/dependency changes.

Added `run-navigation-startup-gate.ps1`: fresh My Day selection, changing to
Completed, restoring Completed across process restart, and hover/leave all
assert exactly one selected fixed category. **4/4 passed**, with selection and
focus properties saved alongside screenshots under
`tests/ui/artifacts/navigation/run-20261010-114120`. All four screenshots
reviewed: the extra My Day highlight is gone. Initial harness attempts used
the editable-title selector for smart lists and encountered foreground/focus
safeguards; corrected to the static page title and captured main HWND. Their
failed artifacts remain available. Product fix stayed one XAML-line removal.

Validation: x64 warnings-as-errors build clean; **102/102** unit tests with
zero skipped; M8 **14/14 automated checks** passed at
`tests/ui/artifacts/m8/run-20261010-114153`. All six M8 screenshots reviewed;
existing settings/background capture coverage and manual Narrator listening
remain open. `winui-code-review` found no issue above informational in the
one-line change. Docs describe startup selection ownership. Existing composer
and owner settings/CodeGraph changes preserved; no commit or push performed.
M9 performance remains owner-deferred.

## 2026-10-10 — M8/M13 polish batch reviewed for commit

Owner authorized committing and pushing all pending work, including the
composer responsive layout, single startup navigation selection, CalDAV
Configure dialog/settings action sizing, and CodeGraph ignore-all policy.
Generated databases, certificates, builds and test artifacts stay ignored.

Final review corrected the pending CalDAV dialog to use a RelayCommand for
Configure, explicit two-way credential bindings (PropertyChanged on TextBox),
view-model busy state, password clearing on dismissal, and a guaranteed async
deferral completion. Native dialog buttons receive stable AutomationIds via
the popup tree. Removed raw FontSize from the touched settings action icons.
The connection command and PasswordVault storage remain unchanged.

Updated M13 UI coverage for Configure, cancel/reopen draft preservation,
password clearing, failed connection/retry and optional `-Aot` runs. The first
attempt failed 7/11 because native dialog button IDs were not assigned from
the popup tree (`artifacts/m13/run-20261010-114700`); the error-path check in
that older harness could also mask a prior CLI failure. Corrected the ID
lookup and enabled native-command failure propagation within each check.
Rerun **11/11 passed** in Debug (`artifacts/m13/run-20261010-115017`) and AOT
(`artifacts/m13/run-20261010-115221`). All ten M13 screenshots reviewed, including
credential and error states. No visual clipping in the tested dialog states.

Final validation: requested x64 warnings-as-errors build clean, **102/102** unit
tests with no skips, x64 AOT publish clean, M8 **14/14 automated checks**
(`artifacts/m8/run-20261010-115059`, all six screenshots reviewed). Prior focused
composer **3/3 Debug + 3/3 AOT** and navigation startup **4/4** results remain
recorded above. `winui-code-review` found no issue above informational in the
final diff. Manual Narrator, live-server convergence, ARM64 runtime perf and
M10 release gates remain open; M9 perf is explicitly owner-deferred.

## 2026-10-10 — UX-01 — Dates and times in the UI language

Implemented language-matched formatting: `App_FormattingCulture` in the
MRT-resolved en-US resources sets `DisplayCulture.Current` before view models
and Windows integration are created, including background/toast activation.
Missing or invalid tags fall back to `CurrentUICulture`. All 39 user-visible
`CurrentCulture` uses now read this culture; the sole remaining use is the
commented regional week-start calculation reserved for UX-02. Invariant parsing,
storage and notification payload serialisation are unchanged. Current toast
text already comes from resources and has no date formatter; the jump-list
formatting now uses display culture too. DESIGN §11 records the rule.

Extracted pure Created/list-due/detail-due helpers and culture resolution into
Core Presentation, keeping translated strings in App. Added 17 regression
cases covering Ukrainian/German regional formats, relative-date boundaries,
weekday/month labels, translated tags and missing/invalid fallback. Debug and
Release x64 warnings-as-errors builds passed with zero warnings; all 119 unit
tests passed (Core 67, Data 43, Sync 9; zero skipped). `winui-code-review` of
the changed code found no issue above informational; UX-01's explicit display
culture policy takes precedence over the skill's generic CurrentCulture advice.
No XAML, model, storage or dependency changes.

M4's new English Created assertion passed on a uk-UA regional/UI-culture host.
Updated M4 date/recurrence expectations to en-US and repaired calendar lookup
(result cap could hide day numbers behind uid matches) and file-picker lookup
(the picker can be hosted by the app rather than PickerHost). Assertions were
preserved. Initial run: 4/9; first retry: 8/9; final retry: 8/9. The remaining
step drag failed to reorder the rows in each run; experimental coordinates were
removed, with before/after UIA evidence retained in artifacts. Per the UI skill's
two-retry limit, no further full runs were attempted. Restart phase was not
reached. Latest evidence: `tests/ui/artifacts/m4/run-20261010-122129`; screenshot
review confirms English Created, reminder and recurrence labels. The unrelated
step-drag issue is in UX-PLAN's parking lot; UX-01 stays in progress until the
full required M4 gate passes.

M8 passed 14/14 automated checks in
`tests/ui/artifacts/m8/run-20261010-121746`. All six PNGs were reviewed: relevant
app layout/text states showed no new clipping; the settings image captured a
transition and an external desktop overlay partly covered the dialog captures,
so those existing capture limitations are retained. High-contrast issues already
scheduled as UX-04 remain. Windows Sandbox is disabled on this machine, so gates
ran locally with scratch databases. No Windows regional/language setting was
changed. Native AOT runtime, ARM64 and German desktop UI were not rerun; German
formatting is covered by the new unit tests. M9 remains owner-deferred.

## 2026-10-10 — UX-01 — M4 blocker resolved and package completed

Owner authorized investigating/fixing the remaining M4 step drag, closing UX-01
and committing the combined work. The focused probe reproduced the unchanged
UI and database order using M4's original gesture. Temporary event traces proved
that container-level `CanDrag=true` started a standalone element drag: the
ListView's native reorder path received no DragEnter/DragOver and completion
returned None. Removing the per-container drag/drop overrides let the ListView
own the operation; traces then showed accepted Move and SQLite persisted the new
sort order. A plain `List<object>` still left visible rows stale. The final fix
uses `ObservableCollection<object>` for Steps, adds/removes in place, and leaves
container AutomationIds/names intact. Loading another task replaces its source
as before. Temporary traces and trial changes were removed. DESIGN §5 records
this native ListView ownership requirement; no new interaction pattern or ADR.

The full M4 gate passed on the first final run: 9/9 phase-one checks plus the
restart round-trip, including completed-step order/status, title/note, due date,
biweekly recurrence, reminder, My Day, attachment persistence/removal and pane
closure. Evidence: `tests/ui/artifacts/m4/run-20261010-124453`. Both screenshots
reviewed: completed Second step stays first before and after restart; English
Created, due and recurrence labels remain legible without new clipping.
The original drag coordinates and behavior assertions were preserved.

Final Debug and Release x64 warnings-as-errors builds: zero warnings/errors.
All 119 unit tests passed with zero skipped (Core 67, Data 43, Sync 9), including
17 new UX-01 culture/date cases and the existing M4 store persistence test.
M8 rerun: 14/14 automated checks passed in
`tests/ui/artifacts/m8/run-20261010-124559`. All six PNGs reviewed: dialog and
high-contrast layout remain legible; the existing Settings capture still shows
the transition state, and UX-04's already-known contrast issues remain scheduled.
`winui-code-review` found no issue above informational in the final changed code.
Only the documented regional FirstDayOfWeek use of CurrentCulture remains;
invariant parsing/serialization, credentials and database schema are unchanged.
No XAML edits, dependencies, Windows feature enablement or perf claims.

The previous UX-01 partial-gate history is retained above. Its parking-lot
blocker is removed and UX-01 is done. Next package: UX-02 Tasks settings.
M9 performance, ARM64 runtime and owner-run manual gates remain deferred/open.


## UX-02 — Settings infrastructure + Tasks          2026-10-10

**Gate:** PARTIAL — Debug x64 warnings-as-errors build clean. Dedicated Settings
gate PASS 9/9 (`tests/ui/artifacts/settings/run-20261010-142634`), including
all-five restart persistence, immediate Delete/Undo, confirmation/cancel,
row/detail starring, configured reminder time, Planned grouping, calendar week
start and zero-error authored-control accessibility audit (27 controls).
All ten Settings PNGs reviewed: new cards, focus, reminder/calendar and dialog
states are legible. Existing top-of-scroll partial Appearance row and tooltip
captures are scroll/capture states, not new layout defects.

**Shipped**
- Shared Core `TaskPreferences`: typed defaults/validation, notifications,
  ordered setting saves and visible persistence errors.
- Tasks SettingsCards for confirm before delete (true), default reminder time
  (09:00), regional/explicit week start, add new to top and move starred to top
  (both false); values live in the existing settings table.
- Task delete retains Undo with confirmation off. Reminder picker and
  tomorrow/next-week shortcuts consume the configured time. Planned and both
  calendar pickers consume week start.
- Atomic store ordering options: root tasks in manual lists, both directions;
  no setting reads inside the store. Row/detail updates reposition immediately.
- Eleven new unit cases (Core 7, Data 4). Isolated full unit run passed 130/130,
  zero skipped. Other runs hit variable existing 5-ms performance assertions,
  and one hit an existing SQLite pool-disposal race; timings recorded in PERF.
- DESIGN §8/§12, architecture and test index updated; no new dependency,
  migration, credential change or ADR.
- Crash investigation found a composer startup/detail-resize COM exception:
  VSM tried to target unloaded `InlineOptions`. Added a narrow guard for the
  deferred setter targets. Attached debug evidence:
  `debug-25796-20261010-172423.log` (winapp dump directory).

**Deviations and open verification**
- Supporting composer crash fix is included because it blocked reminder/restart
  verification. No navigation or design decision changed.
- Composer regression gate remains PARTIAL 1/3: compact options disappear after
  adding a task, then draft/resize checks cannot proceed. Latest evidence:
  `tests/ui/artifacts/composer/run-20261010-143746`. Experimental harness changes
  were reverted; package remains in progress and finding is in UX-PLAN parking lot.
- Changed-code `winui-code-review`: no remaining binding, localization, MVVM,
  SQL or AOT-pattern findings; completion is held by the named UI regression.
- Final unit rerun: 129/130, zero skipped; only My Day timing failed at
  5.041 ms against 5 ms. The earlier isolated 130/130 pass is retained as evidence,
  not substituted for the final result. Native AOT, ARM64 runtime and owner-only
  manual gates were not run.
