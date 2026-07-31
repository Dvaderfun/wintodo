# Progress log

One entry per milestone. Append, never rewrite history.

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

_No milestones completed yet. M0 has not started._

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
