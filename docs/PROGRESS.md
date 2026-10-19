# Progress log

## Current state

_Maintained summary — update it whenever an entry below changes it. Everything
under "History" is append-only._

As of 2026-10-19:

| Milestone | Status | Open gate rows |
|---|---|---|
| M0–M5 | PASS | — (M3 passed with an owner-approved perf tolerance) |
| M6 iCalendar interop | AUTOMATED PASS | One-time hand import into Nextcloud Tasks or Thunderbird |
| M7 Reminders / Windows integration | AUTOMATED PASS | Reminder survives reboot; visible taskbar badge check |
| M8 Design / accessibility | AUTOMATED PASS | Narrator listening check |
| M9 Native AOT | DEFERRED (owner) | Full idle perf matrix pending; AOT startup crash fixed and M8 automation passed; ARM64 runtime perf not measured |
| M10 Packaging and release | GROUNDWORK | Fresh x64/ARM64 AOT MSIX and local test signatures prepared; unsigned tag CI added; final identity/signing, Store assets and clean-VM install/uninstall gates pending |
| M11 Deep subtasks | not started | — |
| M12 Tags and saved filters | not started | — |
| M13 CalDAV sync | AUTOMATED PASS | Live Radicale + Nextcloud convergence; conflict dialog via UI Automation |
| IMP Onboarding + Microsoft To Do import | AUTOMATED PASS | Manual gate with real personal + work accounts (live Graph sign-in) |

- **Build:** Debug and Release x64 clean on 2026-10-18 with `WinUIAnalyzer` 0.7.1
  after the completion viewport repair;
  Release/AOT x64 and ARM64 republished on 2026-10-18 after the viewport repair
  for M10 package preparation; fresh runtime execution is not claimed.
  Tests (xunit v3 on Microsoft Testing Platform): Core 202, Data 95, Sync 10 — 307/307,
  zero failed/skipped on the latest 2026-10-18 run. One intervening existing
  InvalidListMutation test failed transiently; focused and full reruns pass.
- **Toolchain:** .NET SDK 10.0.401 (runtime 10.0.12), Windows App SDK 2.5.1, WinApp CLI 0.7.1,
  win-dev-skills v0.7.1 vendored (ADR-010).
- **Known gaps:** Completion viewport drift is fixed separately from UX-14:
  strengthened list stability passes 3/3 twice, with 10 px drift on immediate
  and settled completion/uncompletion against the unchanged 54 px limit.
  Final app name and MSIX publisher are still undecided. UX-01 display culture and the M4 step-drag
  blocker are fixed; M4 passes all 9 phase-one checks and restart persistence.
- **Header alignment:** Fixed-category and user-list display title text now use tight line bounds;
  icon containers were already centered, but full font line metrics placed the
  visible Tasks letters 2 px lower than the glyph. Light/Dark pixel checks pass
  14/14; fixed-category headers within 1 px, unset dots optically lowered by
  an owner-selected 2 px. M8 17/17
  from the fixed-category change; list-icon regression 14/14, Debug/Release builds
  and 307/307 unit tests pass.
  Signed x64 MSIX version `1.0.0.3` now
  includes this fix; ARM64 and earlier test-signed copies predate it.
- **IMP (2026-10-19):** Onboarding Welcome + one-way Microsoft To Do import (ADR-016).
  Import gate 11/11, M8 17/17, settings 9/9 in WintodoTest; Debug/Release and AOT x64
  clean; 364/364 unit tests (Core 211, Data 95, Sync 10, Import 48). Entra app
  registered (client id `038f1bee-…a03e`); live sign-in awaits the manual gate.
- **UI execution:** UI gates run in the `WintodoTest` Hyper-V VM via
  `scripts\Invoke-VmGate.ps1` (ADR-013). The earlier sweep's 14 passing runs remain
  recorded. Follow-up: m8 17/17, settings 9/9 twice, list-stability 3/3 twice
  after fixing intermittent viewport drift, undo 7/7, completion motion 7/7
  including reduced motion. Completion uses a same-build VM baseline, not a
  UX-07 regression comparison. VM perf remains unmeasured. Host UI automation stays disabled.
- **UX-02:** Complete: Tasks settings 9/9, composer 3/3, clean Debug/Release
  builds and 259/259 tests. Baseline harness targeting/focus failures corrected;
  existing smart-list/FTS performance checks pass without budget changes.
- **UX-03:** Notifications/Data/About complete: reminder toggle and snooze,
  database location/size, repository and offline licences. M7 preflight and M8 pass.
- **UX-04:** High-contrast navigation dots, search borders and header chrome
  fixed. Expanded M8 passes 17/17 across all four Windows contrast palettes.
- **UX-05:** Sync footer hidden until an account exists; connect, failed-connect,
  restart and disconnect checks pass in M13 (11/11). M8 passes 17/17.
- **UX-06:** Grouping, sort policy and subtitle parts extracted into Core;
  70 extraction cases, 10 reorder cases and 16 SQLite parity cases pass.
  Grouped task drag fixed; M3 10/10 plus restart order, M5 8/8 and M8 17/17.
  Package complete.
- **UX-07:** Stable grouped collections with keyed diffs; exact task selection,
  completion focus and middle-of-list viewport checks pass (3/3). M3/M5/M8 pass;
  FD before/after measurements recorded; no AOT runtime claim.
- **UX-08:** Complete: motion 7/7, stability 3/3, M8 17/17 and 241/241 tests pass.
  Matched completion placement mean/median do not regress; animation-off commands
  skip the hold and Windows settings restoration is verified. Completion hit target
  stays 32 × 32; selected-task moves preserve viewport/focus. M3 remains passing.
- **UX-09:** Complete: Extended selection, bulk actions, atomic writes, one
  delete undo entry and multi-task navigation drag. Multi-select 7/7, M3 10/10
  plus restart, M8 17/17, stability 3/3; Debug/Release builds and 257 tests pass.
- **UX-11:** Complete: My Day and Important accept single/multi-task drops,
  set rather than toggle, and retain original lists. M5 9/9 plus three restart
  checks, M8 17/17 and 259 tests pass; Debug/Release builds are clean.
- **UX-13:** Source split implemented: all 39 Pages/ViewModels C# files are
  below 600 lines (largest 516); 587 declarations verified unchanged, with export
  filename policy and quick-add fallback moved into ShellViewModel. Build/tests,
  M3/M4/M5/M7/M8/M13, multi-select, navigation and notification settings pass.
  Stability passes on repeat. Baseline harness failures corrected separately:
  M6 7/7, list icons 10/10, Tasks settings 9/9 and composer 3/3 now pass.
  Complete: motion 7/7 with strict fresh timing PASS, reduced motion and Windows
  setting restoration verified; corrected harnesses acknowledged under ADR-011.
- **UX-15:** Complete: unset user-list header icons show a 16 px theme-colour
  dot without an idle border; hover and keyboard focus reveal the emoji picker.
  Existing emoji, accessible name/IDs and 44 px target preserved. List-icon
  14/14 and M8 17/17 pass in WintodoTest; Light/Dark, detail/narrow and four
  contrast palettes visually reviewed. DESIGN and ADR-009 amendment approved.
- **Next:** UX-12 skipped by the owner on 2026-10-18; keep My Day's midnight
  reset for To Do parity. The owner authorized committing/pushing UX-14 on
  2026-10-18 with its failed stability gate documented. Sort 13/13, M5 9/9 +
  restart 3/3 and M8 17/17 pass; list stability remains 2/3, including a
  previous-header comparison. The separate viewport repair now passes 3/3 twice,
  including exact completion focus and Space uncompletion, without changing
  the one-row tolerance. Owner selected UX-18 next and skipped
  UX-16/17 for now; ADR-015 is accepted. UX-18 is complete:
  Text/WebLink mapping, share-only startup/window and external SQLite refresh
  build and unit tests pass; real x64 AOT broker gate passes 10/10. Final
  startup/integration regression passes M0 4/4, M7 preflight and x64 AOT M13
  11/11; ARM64 runtime remains unclaimed.
  UX-10 expanded undo gate passes 12/12, M3 + restart and M4 + restart pass;
  M13 passes 11/11 and M8 17/17; UX-10 is complete. M9 remains owner-deferred;
  final name/publisher, signing and M10 clean-VM gates remain pending.

- **Release readiness:** Viewport repair committed as `23881eb`; Debug/Release
  builds and 307/307 tests rechecked. UX-20 owner-run procedures and M10 acceptance
  are in [the release checklist](guides/release-checklist.md). Fresh unsigned
  x64/ARM64 MSIX and existing-certificate test-signed copies are in `publish/msix`;
  package inspection and SHA-256 hashes are in `inspection.json`. Host signature
  verification stops at the untrusted development certificate; no trust was added.
  The regular VM checkpoint has Developer Mode and certificate trust already,
  so a separate clean-install baseline remains necessary. Final name, publisher,
  distribution route, production signing/assets and owner-run gates remain open.

- **Completion viewport repair:** Panel updates keep their scroll offset rather
  than competing with the view's anchor correction. Transition-free updates
  restore immediately; recycled anchors are realized before correction resumes.
  Stability 3/3 twice, M3 10/10 + restart and M8 17/17 pass in WintodoTest;
  all 307 tests pass. The strengthened gate also checks settled completion
  and uncompletion. WinUI code review and returned captures pass.

- **UX-10:** Row completion, including recurring occurrences, now records undo
  after its row update. The five-second snackbar and retained Ctrl+Z history
  pass for ordinary/recurring tasks; detail completion stays quiet. Expanded
  undo 12/12, M3 10/10 + restart, M4 9/9 + restart and M13 11/11 pass in
  WintodoTest on 2026-10-18. Recurring checkbox state refresh corrected;
  field restore retains later title/note edits and restores dates/alarms/history.
  M8 17/17 and contact-sheet visual review pass. M7 automated preflight and
  M0 4/4 pass on follow-up. UX-10 is complete; stability unclaimed.

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


## UX-03 — Settings: Notifications, Data, About          2026-10-10

**Gate:** PASS — Debug x64 warnings-as-errors build, zero warnings/errors;
137/137 unit tests (Core 79, Data 49, Sync 9), zero skipped. Dedicated notification
settings gate 6/6, M7 automated preflight PASS, M8 14/14.

**Shipped**
- Notifications contains reminder enablement (default true), default snooze
  (10 minutes / 1 hour / tomorrow) and the existing launch-at-login control.
  One Core `NotificationPreferences` instance is shared by Settings and Windows
  integration, loaded before scheduling on regular and background activations.
- Disabling removes scheduled toasts without deleting stored alarms. Re-enabling
  reschedules them. Snooze changes rebuild pending payloads with the selected
  `defaultInput`. Scheduling is queued off the UI callback and serialized by
  the existing refresh gate; setting/scheduling failures show an InfoBar.
- Data shows the resolved database path and file size through `StoreStats`;
  Show in Explorer uses a command with separately passed arguments.
- About retains version, adds the repository link, a package/licence expander
  and a command opening bundled `Assets/ThirdPartyNotices.txt`. No network is
  needed to read notices. Licences come from installed package metadata and
  bundled terms; build/test tools are identified as such.
- Seven new test cases cover notification defaults, parsing, persistence and
  database stats. M7 runner now uses the repository's current MTP test flags.
- DESIGN §12, architecture and testing index updated. No schema migration,
  dependency, credential change or accepted-decision change.

**Evidence and review**
- `tests/ui/artifacts/notification-settings/run-20261010-151409`: six passing
  checks; actual scheduled-toast XML proves removal/restoration and all snooze
  defaults, including restart. Accessibility audit: 33 controls, zero errors.
- `tests/ui/artifacts/m7/run-20261010-151102`: UI + schedule-tag/badge preflight.
- `tests/ui/artifacts/m8/run-20261010-151445`: 14/14, all six PNGs reviewed.
  Existing high-contrast colours remain UX-04 work; Settings screenshot captures
  a navigation transition, so dedicated screenshots provide section evidence.
- Dedicated images reviewed, including actual licence list visibility. The
  list extends below the viewport in its scrolling page; Data/About controls
  and list text are legible. Earlier header-only expander implementation was
  replaced with the working ItemsSource/ItemTemplate pattern.
- `winui-code-review`: no findings above informational in changed code.

**Deviations / carried forward**
- Owner explicitly approved proceeding despite UX-02's open broader gates;
  its composer regression remains tracked and is not reported as passed.
- WinApp UIA toggle/scroll invocation stalled on this machine; the dedicated
  gate uses clicks and wheel input with value, visibility and schedule assertions.
  The final gate passes without skipping those assertions.
- Full licence texts open from a bundled file, keeping the in-page list compact.
- M7 reboot and toast-action owner checks, Native AOT runtime and ARM64 runtime
  were not run. No performance claim or budget change.

## UX-04 — High-contrast fixes          2026-10-10

**Gate:** PASS — Debug and Release x64 builds with `-warnaserror`, zero warnings
and errors; 137/137 unit tests, zero skipped; expanded M8 17/17.

**Shipped**
- Custom-list dots use WindowText when unselected and HighlightText when
  selected in High Contrast. A callback on each navigation item's IsSelected
  dependency property updates the brush immediately, including rebuilt items.
  Light/Dark retain the selected list palette; emoji icons are unchanged.
- Wide and compact search boxes use a theme-resource border: zero thickness
  in Light/Dark, 1 px WindowText in High Contrast.
- Header icon/title buttons have zero resting border thickness in High
  Contrast. Native focus visuals and contrast adjustments remain enabled.
- M8 now switches all four shipped Windows contrast schemes using their
  localised names, verifies the applied system colours, captures custom lists
  selected/unselected and both header focus states, and restores and verifies
  original flags, scheme and system colours in cleanup. Empty accessibility
  trees and failed captures are errors.
- DESIGN §§1.1, 9.5 and 11 and the testing guide document the changes.

**Evidence and review**
- `tests/ui/artifacts/m8/run-20261010-153423`: 17/17. All 16 contrast images
  reviewed: dots and search borders visible in Aquatic, Desert, Dusk and
  Night sky; header controls borderless at rest with clear focus outlines.
- Authored-control accessibility audits pass with zero missing AutomationIds,
  including selected/unselected contrast states. Existing minimum-hit-target
  checks also pass.
- `winui-code-review`: no findings above informational in changed code;
  selection callback APIs checked against the restored app project. No new
  dependencies, runtime bindings, reflection or user-facing strings.

**Deviations / carried forward**
- Earlier expanded gate attempts failed while the test harness was being
  corrected: SetSysColors alone did not refresh WinUI, a shipped Dusk file
  has an unrelated malformed MenuText value, and legacy scheme names differ
  from current theme-file names. The final run uses actual theme switches and
  validates Window, WindowText, Highlight and HighlightText without skips.
- Native AOT/ARM64 runtime and performance gates were not run for this UI
  patch. UX-02 composer/performance checks and earlier owner-run rows remain
  open; no performance claim or accepted-decision change.

## UX-05 — Hide sync status until configured          2026-10-10

**Gate:** PASS — Debug and Release x64 builds with `-warnaserror`, zero warnings
and errors; 137/137 unit tests, zero skipped; M13 11/11; M8 17/17.

**Shipped**
- The sync footer's Visibility uses an explicit OneWay x:Bind to the existing
  IsSyncConnected account-presence property through ShellPage.BoolToVisibility.
  It collapses before configuration and after disconnect, appears after
  connecting and restores on restart. Settings → Sync remains available.
- M13 asserts footer absence on a fresh database and after a failed connection,
  visibility and Up to date after connecting, restoration on restart and
  absence of both the footer item and its text after disconnect.
- DESIGN §12 and the testing guide document the visibility lifecycle.

**Evidence and review**
- `tests/ui/artifacts/m13/run-20261010-154756`: 11/11, loopback CalDAV;
  disconnected/connected/disconnected-again screenshots reviewed.
- `tests/ui/artifacts/m8/run-20261010-154852`: 17/17; accessibility audits at
  zero missing AutomationIds, all four contrast palettes and original
  appearance restoration pass. Selected-list screenshots reviewed in every
  contrast palette; Settings stays visible with the sync footer collapsed.
- `winui-code-review`: no findings above informational. No new view-model
  state, commands, dependencies, strings, converters or reflection. Existing
  AutomationIds and connected/syncing/error status rendering remain intact.

**Deviations / carried forward**
- Native AOT/ARM64 runtime, live Radicale/Nextcloud convergence and performance
  gates were not run for this visibility patch. UX-02 composer/performance
  checks and earlier owner-run rows remain open. No accepted-decision change.

## UX-06 — Core list presentation extraction          2026-10-10

**Gate:** PARTIAL — Debug and Release x64 builds with `-warnaserror`, zero
warnings/errors; 224/224 tests (Core 149, Data 66, Sync 9), zero skipped;
M5 6/6 plus restart 2/2; M8 17/17. M3 9/10: task drag reorder fails on the
unchanged baseline too. UX-06 remains in progress pending that gate or an
explicit owner exception; no failed row is recorded as passed.

**Implemented**
- Core `TaskGrouping.Build` returns ordered group descriptors and task UIDs
  from already ordered query results. Search list order, active-before-completed
  order, Planned bucket order, completed count/expansion and empty groups match
  the prior implementation. Local date and week boundaries take IClock and the
  resolved week-start preference.
- Core `TaskSorting` owns sort terms and pure comparisons. Data translates
  those closed enum terms into the same SQL ORDER BY expressions, preserving
  SQLite ordering and indexes. Undefined priority/null due terms keep their
  original reverse semantics; completed defaults newest first; persisted id
  ties reverse, while in-place manual insertion keeps equal-order input order.
  SQLite ASCII-only NOCASE and stored date-only ordering have explicit tests.
- Core `TaskMetadataLine.Build` returns unformatted list/due/repeat/My Day parts
  and due-state flags. App keeps resource formatting, accessible names, command
  wiring, cached row objects, selection and completed expansion. TaskListViewModel
  shrank from 1,845 to 1,685 lines (160 fewer).
- 70 Core cases cover bucket/week/local-midnight boundaries, grouping and
  empty/collapsed cases, every sort mode/direction/tie, and current metadata
  order/contexts. Sixteen Data cases compare Core sorting to actual SQLite
  query results. No schema or dependency changes.
- Architecture and testing guide updated. A repeatable M3 runner creates a
  scratch database with confirmation off to exercise its immediate-delete loop.
  M3 assertions remain intact; startup, row-body drag coordinates, focus and
  state waits improve the test's input sequencing.

**Evidence and review**
- Baseline unit suite 137/137; valid baseline M5 under
  `tests/ui/artifacts/m5/run-20261010-155338` passed 6/6 + restart 2/2.
- Post-extraction M5 under `tests/ui/artifacts/m5/run-20261010-155819` passed
  6/6 + restart 2/2; search and Planned screenshots reviewed.
- M8 under `tests/ui/artifacts/m8/run-20261010-160504`: 17/17 with unchanged
  assertions, zero missing AutomationIds and original appearance restored.
  List metadata screenshot reviewed.
- M3 under `tests/ui/artifacts/ux06-m3/run-20261010-160203`: 9/10; only
  reorder fails. Baseline under `tests/ui/artifacts/ux06-baseline-m3/
  run-20261010-155906` also fails reorder (plus older input-sequencing failures).
  Historical post-M8 entry already documented the same reorder failure.
- `winui-code-review`: nothing above informational in extraction code. No new
  XAML, runtime bindings, reflection, UI types in Core or user-visible strings.

**Deviations / carried forward**
- Task sorting was in Data's query builder rather than the App. Core now owns
  the policy; Data retains SQL execution. Localization stays entirely in App,
  so Core returns metadata kinds/dates rather than accepting a culture.
- DESIGN §3.3 reminder/steps/note/attachment subtitle parts were absent before
  this package. Preserve the shipped subset; missing parts are in Parking lot.
- M3 row drag overrides were removed experimentally, without resolving reorder;
  experiment reverted. An initial pair of overlapping M5 baseline runs had
  invalid input interference and were discarded; the serial runs above are
  the evidence. Existing M3 UIA/input and Release settings crashes remain outside
  this behavior-preserving extraction.
- No Native AOT/ARM64 runtime or performance claims. UX-02 composer/performance
  checks and earlier owner-run rows remain open. Unrelated CLAUDE.md edit left
  untouched.

## UX-06 — M3 drag blocker resolved and package completed          2026-10-10

**Gate:** PASS — Debug and Release x64 warnings-as-errors builds, zero warnings
and errors; 234/234 unit tests (Core 159, Data 66, Sync 9), zero skipped;
M3 10/10 plus new restart-order check; M5 6/6 plus restart 2/2; M8 17/17.

**Shipped**
- Fixed grouped task drag reorder. WinUI documents that built-in
  CanReorderItems cannot reorder grouped sources. Custom row containers now
  provide task payloads; the ListView accepts active-row insertion boundaries
  and delegates ordering/persistence to the view model after the drop finishes.
  Completed rows, non-manual lists and external payloads are rejected.
- Added the DESIGN insertion indicator using a theme brush and visual states.
  Added Core TaskReordering boundary logic with 10 cases covering both directions,
  end positions, same-row drops and adjacent no-ops. Existing keyboard ordering
  remains on its prior path; collection stability remains UX-07.
- Fixed M3's drag coordinates: integer conversion prevents Ukrainian decimal
  commas from producing invalid x,y pairs, and native drag failures now throw.
  Clear selection before list F2, which correctly edits a selected task otherwise.
  Original reorder and rename behavior assertions remain intact.
- Added m3-restart.ps1 to prove task 5 stays before task 3 after stopping and
  relaunching the same scratch database. Updated DESIGN and testing guide.

**Evidence and review**
- Initial baseline M3: 8/10 (reorder and selected-task F2 setup). A focused probe
  then showed no drag events for the malformed gesture. Integer coordinates
  and explicit container payloads produced DragStarting, accepted Move, Drop,
  and updated row order. Temporary file traces were removed before final builds.
- Final M3: tests/ui/artifacts/m3/run-20261010-162336; all three screenshots
  reviewed, including restarted order and intact Completed section.
- Final M5: tests/ui/artifacts/m5/run-20261010-162628; all three screenshots
  reviewed. The tracked runner still uses obsolete VSTest flags; an untracked
  copy adapted only its test invocation to --project/--filter-class and x64.
- First M8: tests/ui/artifacts/m8/run-20261010-162430, 16/17; one screenshot
  command failed for the shortcuts dialog bottom. Unchanged rerun:
  tests/ui/artifacts/m8/run-20261010-162703, 17/17. All 21 PNGs reviewed via
  contact sheet with dialog detail reviewed at full size; no new row clipping
  or contrast regressions. The Settings image retains its existing transition
  capture limitation and is not visual proof of Settings content.
- winui-code-review: nothing above informational in the final diff; event
  adapters own pointer geometry/payloads, the view model owns ordering, and
  Core remains free of UI types. No dependencies, schema or new strings.

**Deviations / carried forward**
- The pre-existing grouped drag defect needed a product fix to close UX-06's
  mandatory M3 gate. This restores the specified interaction; no new design
  decision or ADR. The extraction itself retains existing presentation policy.
- Local gates used scratch databases because Windows Sandbox is disabled.
  No Native AOT/ARM64 runtime or performance claims. UX-02 composer/performance,
  M9 owner deferral and manual gate rows remain open. UX-07 is now unblocked.

## UX-07 — In-place task list updates          2026-10-10

**Gate:** PASS — Debug and Release x64 warnings-as-errors builds, zero warnings
and errors; 241/241 unit tests (Core 165, Data 67, Sync 9), zero skipped;
stability 3/3; M3 10/10 plus restart; M5 6/6 plus restart 2/2; M8 17/17.

**Shipped**
- Core SequenceDiff computes insert/remove/minimal retained-key moves via a
  longest increasing subsequence. Six tests exercise 600 generated pairs,
  minimal single-item moves in 10,000 rows, unchanged input and linear append
  of 10,000 initial rows. App applies operations to cached row objects and
  stable group collections; VisibleItems/TaskGroups no longer Clear/refill.
- Observable group metadata and TaskGroupHeader preserve counts, titles,
  accessible names and expansion without resetting CollectionViewSource.
  Hidden headers are collapsed; completed selection expands its group.
- TaskGroupCollection explicitly implements IObservableVector so optimized
  Release accepts grouped sources. Native move notifications are removal then
  insertion after each real mutation. AllowUnsafeBlocks is required only for
  CsWinRT-generated vtables; no handwritten unsafe code, reflection, dependency
  or schema change. Existing IList<object> boundary is retained.
- View coordinates collection mutation with selection/focus preservation and
  bounded viewport restoration, cancelling on new keyboard/pointer input.
  Capture precedes completion state/height changes. Restore first returns the
  saved offset, then corrects the realized unchanged anchor after layout settles.
  Failure paths release synchronization. List switches reset their viewport;
  same-query refreshes preserve it. Alt+Up/Down preview handling avoids native
  ListView navigation consuming the reorder gesture.
- Stability gate seeds 200 tasks, verifies selection near task 100, checks
  Ctrl+I and Alt+Down, completes with Space, verifies scroll within one row,
  queries UIA FocusedElement for the exact completed task ID, then uses Space
  again to prove selection still targets that same task.
- Architecture, DESIGN, testing/performance guides and PERF rows updated.

**Evidence and review**
- Final stability: tests/ui/artifacts/list-stability/run-20261010-174852,
  3/3; anchor delta 10 physical pixels versus a 54-pixel row tolerance;
  completion-focus.json matches the completed task 099. All four screenshots
  reviewed. Earlier failed trials and intermediate passes do not constitute
  final evidence; temporary file diagnostics were removed.
- M3: tests/ui/artifacts/m3/run-20261010-174456, 10/10 plus restart; three
  screenshots reviewed. M5: tests/ui/artifacts/m5/run-20261010-174600, 8/8;
  three screenshots reviewed. Its untracked runner copy adapts the existing
  obsolete VSTest invocation to MTP; behavior assertions are unchanged.
- M8: tests/ui/artifacts/m8/run-20261010-174636, 17/17; all 21 PNGs reviewed
  via contact sheet. No new clipping or contrast regression. Existing Settings
  transition screenshot limitation remains; it is not content visual proof.
- PERF baseline: tests/ui/artifacts/perf/20261010-163503. After:
  tests/ui/artifacts/perf/20261010-172322. 10k scroll 99.584 -> 99.559 fps,
  zero drops/callback allocations in both; queries 1.346 -> 1.071 ms;
  search 1.977 -> 2.550 ms. FD startup is slower and is recorded without a
  speedup claim. AOT runtime and +10 minute memory remain M9 owner-deferred.
- Debug UIA interaction baseline/after: 387.712/419.613 -> 454.109/466.064 ms
  list round trips; checkbox completion 899.328 -> 830.239 ms. CLI overhead
  included. No isolated render latency claim.
- One full-suite attempt hit an existing SQLite disposed pooled-handle failure;
  unnecessary global pool clearing was removed from the new seed fixture.
  Subsequent full suites passed 241/241.
- winui-code-review: nothing above informational in final changed code. Correct
  WinRT notification sequencing and completion failure cleanup were addressed
  before the final gates. No new user-facing strings or navigation pattern.

**Deviations / carried forward**
- The briefly requested list-focus exception is unnecessary: FocusedElement
  proved that the original exact completed-row focus requirement is met even
  when the normal CLI search cannot find its virtualized container. No plan
  exception or ADR was made.
- Native vectors lack Move; App translates Core's move to remove/insert while
  retaining the cached task object. Completion motion remains UX-08.
- Local scratch databases were used because Windows Sandbox is disabled.
  UX-02 composer/performance rows and owner-run manual gates remain open.

## UX-08 — Completion and row motion          2026-10-10

**Gate:** PARTIAL — Debug and Release x64 warnings-as-errors builds clean; 241/241 unit tests
(Core 165, Data 67, Sync 9), zero skipped; stability 3/3; M3 10/10 plus restart;
M8 17/17. Completion performance acceptance FAIL; reduced-motion Windows test
SKIPPED pending owner authorization for a temporary OS setting change. UX-08
remains in progress; no open row is reported as passed.

**Shipped**
- Completion/check fades use explicit visual-state transitions (100/200 ms),
  with immediate strikethrough; the circle spring keeps damping 0.6 / 40 ms.
  Star bounce follows visual-state changes. Row recycling resets scale,
  opacity and glyph state without replaying completion for a different task.
- Completion/uncompletion restores built-in row add/delete/reorder transitions
  specifically at the grouped move, retaining UX-07 selection/viewport capture.
  The existing 250 ms completion hold is skipped when animations are disabled;
  the list now observes UISettings.AnimationsEnabledChanged while open and also
  skips delayed viewport restoration when effects are off.
- Undo snackbar slides up 24 px and fades over 200 ms, stays loaded for its
  200 ms exit after the existing five-second lifetime, then unloads. Explicit
  storyboards and composition motion honor the Windows animation setting;
  setting changes marshal through DispatcherQueue, with loaded/unloaded cleanup.
- Dedicated motion gate seeds 200 tasks / 20 lists, checks timing/placement,
  uncompletion, importance and snackbar auto-dismissal, and writes screenshots
  and an MP4/frame bundle. Its optional -TestReducedMotion requires owner
  permission, toggles client-area animation only temporarily, restores in finally,
  and measures placement via UIA in one process to expose a retained 250 ms delay.

**Evidence and review**
- Motion functional checks 3/3 in
  tests/ui/artifacts/completion-motion/run-20261010-184420/results.json.
  Screenshot sequence reviewed: [active](../tests/ui/artifacts/completion-motion/run-20261010-184420/01-active.png),
  [completed](../tests/ui/artifacts/completion-motion/run-20261010-184420/02-completed.png),
  [placement](../tests/ui/artifacts/completion-motion/run-20261010-184420/03-completion-placement.png),
  [important](../tests/ui/artifacts/completion-motion/run-20261010-184420/04-important.png),
  [undo](../tests/ui/artifacts/completion-motion/run-20261010-184420/05-undo.png).
  The [MP4](../tests/ui/artifacts/completion-motion/run-20261010-184420/completion.mp4)
  is delivered/playable (360 frames), but WGC held stale images during motion;
  screenshot sequence is the accepted evidence format, not that video's timing.
  A screen-capture trial failed to start; earlier recorder stderr handling was fixed.
- M3: tests/ui/artifacts/m3/run-20261010-184608, 10/10 plus restart.
  Initial stability/M8: list-stability/run-20261010-184006 (3/3, 10 px anchor
  shift), m8/run-20261010-184056 (17/17), under tests/ui/artifacts. Screenshot
  contact sheets reviewed for layout, focus and all four contrast palettes.
- /winui-code-review: no remaining Error/Warning findings after fixing recycled
  spring replay and stopping in-flight explicit motion on an OS setting change.
  App-scoped find-api verified API shapes; WinUIAnalyzer ran with zero warnings.
  No new dependencies, reflection, strings, schema, theme tokens or AutomationId
  changes. AOT runtime validation remains under the existing M9 owner deferral.

**Open rows / deviations**
- PERF.md retains failed historical comparisons (905 ms vs 830 ms isolated).
  Fresh matched medians 1074.956 ms after vs 1071.996 ms before (+2.960 ms)
  still do not satisfy strict no-regression acceptance; no tolerance was invented.
- Reduced-motion functional verification awaits permission to change a Windows
  setting outside the repo, as AGENTS.md requires. The gate is implemented but
  its optional path has not run; Windows Animation effects were left unchanged.
- Local scratch databases used because Windows Sandbox is unavailable.
  UX-02 composer/performance rows and earlier owner-run manual gates remain open.

**Final verification**
- Final stability run: tests/ui/artifacts/list-stability/run-20261010-185209,
  3/3, zero viewport-anchor shift, exact focused completed task 101; screenshots
  reviewed. Final M8: tests/ui/artifacts/m8/run-20261010-185248, 17/17;
  all screenshot states reviewed. Debug/Release x64 both zero warnings/errors.
- The committed recorder readiness handshake now waits for recording-started
  output; this last harness-only fix is syntax-checked, not a newly passed motion
  gate. The existing functional screenshot evidence remains from run-184420.

## UX-08 — Completion motion closure          2026-10-10

**Fixes and proof**
- Owner authorized the temporary Windows Animation effects toggle and restoration
  by instructing completion of both remaining checks. First run exposed a real
  stale cached setting: completed state arrived immediately but the grouped move
  retained its 250 ms hold. TaskListView now supplies a live setting function to
  the completion command. The three-sample reduced-motion proof checks actual
  row placement and reads back the original Windows setting after restoration.
- Replaced the historical CLI/header-only acceptance proxy with 15 prewarmed,
  single-process UIA samples through actual completed row placement. Baseline
  d6038f2: median 338.447 ms, mean 339.532 ms. First fixed comparison: median
  336.355 ms, mean 335.017 ms. A full motion gate passes 7/7 at run-20261010-190628;
  earlier failed CLI results remain diagnostics in PERF.md. Strict mean/median
  comparisons remain, with no motion-duration or budget tolerance change.
- Regression runs exposed native move transitions pulling a selected completed
  task's viewport toward its virtualized container (96/172 px anchor shifts).
  Preserve UX-07's transition suppression when selection is preserved; visible
  unselected checkbox moves animate, with circle/text feedback on both paths.
  Final stability run-20261010-191627 passes 3/3 with a 10 px anchor shift and
  exact focus on completed task 099. Failed experimental scroll-restoration
  changes and temporary trace code were removed; no extra restoration machinery
  ships. This tradeoff is documented in DESIGN §7.
- M8 exposed the completion spring shrinking its interactive checkbox below
  32x32. Apply the spring only to the decorative 20 px circle; checkbox dimensions
  remain fixed. No new visual element, token, dependency, string or AutomationId.
- WinUI code review: no remaining Error/Warning findings; app-scoped API lookup,
  AOT analyzers and WinUIAnalyzer run. Source changes preserve command/MVVM
  boundaries; the live setting function keeps Windows settings in the view.
  AOT runtime and earlier owner-run manual gates remain deferred as before.

**Gate: PASS — closure**
- Debug/Release x64 warnings-as-errors builds: zero warnings/errors. Unit tests
  241/241, zero skipped. Final M8 run-20261010-191924: 17/17; contrast screenshots
  reviewed. Stability run-20261010-191627: 3/3, exact focus, 10 px anchor shift;
  reviewed. Existing M3 10/10 plus restart remains the task-loop regression proof.
- Final motion run-20261010-192117: 7/7. Placement median 331.759 ms and mean
  335.079 ms both below the matched baseline 338.447 / 339.532 ms. Animation-off
  placements 63.434, 88.270, 67.703 ms, with no 250 ms hold; original=1/restored=1.
  Original Windows setting verified after testing. No budget relaxation, motion
  duration change or AOT performance claim. Previous failed runs remain recorded.
- Reviewed [active](../tests/ui/artifacts/completion-motion/run-20261010-192117/01-active.png),
  [completed](../tests/ui/artifacts/completion-motion/run-20261010-192117/02-completed.png),
  [placement](../tests/ui/artifacts/completion-motion/run-20261010-192117/03-completion-placement.png),
  [star](../tests/ui/artifacts/completion-motion/run-20261010-192117/04-important.png),
  [undo](../tests/ui/artifacts/completion-motion/run-20261010-192117/05-undo.png) and
  [reduced motion](../tests/ui/artifacts/completion-motion/run-20261010-192117/06-reduced-motion.png).
  [Completion video](../tests/ui/artifacts/completion-motion/run-20261010-192117/completion.mp4)
  and frame bundle are delivered (360 samples, 68 changing images).
- UX-08 is complete. UX-09 is next; UX-02 and earlier manual/AOT deferrals are
  unchanged. Local testing used scratch databases because Sandbox is unavailable.

## UX-09 — Real multi-select and bulk actions          2026-10-10

**Gate: PASS**
- Debug and Release x64 warnings-as-errors builds: zero warnings/errors.
  Unit tests 257/257, zero skipped; 15 new Data batch cases plus one UI fixture.
- Multi-select run-20261010-200259: 7/7. Ctrl-select three, detail hides,
  native SelectionPattern reports three items and CanSelectMultiple; bulk
  importance/My Day/due date, completion/uncompletion, Shift-click five and
  context move, Shift-arrow two/delete/one undo, Ctrl+A/Esc, two-task nav drag.
- M3 run-20261010-195821: 10/10 plus restart order persistence.
  M8 run-20261010-195916: 17/17, accessibility zero errors and all four contrast
  palettes. Stability run-20261010-200208: 3/3, exact focus, 10 px anchor shift.
- WinUI code review: no remaining Error/Warning findings. Selection and shortcut
  screenshots reviewed; project-scoped APIs, WinUIAnalyzer and AOT analyzers run.
  Native AOT runtime and Narrator listening gates remain owner-deferred.

**Shipped**
- Extended native selection with a read-only selected-task snapshot/count,
  native selection announcements, group-scoped Ctrl+A, Shift-arrow ranges, Esc.
  Detail opens only for one task; asynchronous detail loads cannot reopen it
  after selection changes. Stable diffs restore all surviving selected tasks
  without reloading unchanged detail or moving its viewport.
- Count-aware context actions for completion, importance, My Day, due date,
  move and delete. Pick a date uses a localized CalendarDatePicker flyout.
  Keyboard bulk commands preserve text-input behavior; shortcuts dialog extended.
- ApplyTaskBatchAsync performs field-level changes in one writer transaction,
  with one change-log row per changed UID and one committed notification.
  Duplicate UIDs are deduplicated; failures roll back; recurrence/alarms and
  unrelated metadata/collections are preserved. No dependency or schema change.
- Bulk delete snapshots the tasks for confirmation and produces one undo entry
  restoring the whole selection. Multi-task drag snapshots selection before the
  row press changes it; single-task manual reorder retains its existing path.
- Architecture, data model, DESIGN and testing guide updated.

**Validation notes / deviations**
- Initial stability run exposed a 96 px completion scroll regression; retaining
  unchanged detail and selection fixed it (final shift 10 px). Failed evidence
  is retained. No accepted design or architecture decision changed.
- WinApp CLI click releases held modifier keys. The gate injects Ctrl/Shift
  and mouse together; virtual-key codes avoid the active regional layout's
  letter-key lookup. Nav selectors are refreshed after task-count rebuilds.
- One full-suite run hit an intermittent SQLite disposed handle during an
  existing fixture's initialization; fresh full suite passes 257/257. Pool
  cleanup isolation is tracked in UX-PLAN's Parking lot for separate work.
- UX-02 composer/performance and earlier manual/AOT deferrals remain open.

## UX-11 — Drop tasks on My Day and Important          2026-10-10

**Gate: PASS**
- Debug/Release x64 warnings-as-errors builds: zero warnings/errors.
  Core 165, Data 85, Sync 9: 259/259 tests, zero skipped. Two new batch cases
  prove repeated drops retain membership/importance, original lists and
  unrelated tasks without extra changes or notifications.
- Expanded M5 run-20261010-200852: 9/9 plus 3/3 restart checks. Single and
  repeated drops work for both targets; a two-task selection gains both
  properties, stays in Beta, and survives restart. Interactive-control audit
  remains clean. Existing search, clock rollover and sort checks pass.
- M8 run-20261010-201030: 17/17, accessibility zero errors and four contrast
  palettes. WinUI code review: no remaining Error/Warning findings.

**Shipped**
- My Day/Important accept only task drags; localized platform drag captions
  describe the target action and clear on leaving. Drop handlers hold an async
  deferral while reading the UID payload and applying the action.
- TaskListViewModel reuses ApplyTaskBatchAsync to set MyDayDate from IClock
  or mark importance, honoring starred-to-top preferences. Repeating a drop
  keeps the property set. Existing multi-task payloads are supported atomically.
- M5 coverage extended for drops, original-list retention and restart.
  Updated its stale VSTest invocation to the repository's MTP command.
  DESIGN, architecture and testing guide document the shipped behavior.

**Validation notes / deviations**
- First M5 run loaded the old phase-one suite before the bulk check was added;
  new restart expectations then failed. Complete final rerun passes all checks.
- Reviewed post-drop and restart screenshots. Screen/record capture omitted
  the Windows drag overlay, so those captures are not visual caption proof;
  caption properties were checked against the restored project API and review.
- No new dependency, migration, control, AutomationId or accepted decision.
  Earlier owner-run Narrator/AOT and UX-02 composer/performance gates remain open.

## UX-13 — Split oversized code-behind and view models          2026-10-10

**Gate: PARTIAL — source refactor implemented; full UI acceptance open**
- Before/after Debug x64 warnings-as-errors builds and 259/259 unit tests pass,
  zero skipped. Release x64 build: zero warnings/errors. No XAML, resource,
  dependency, schema, binding name or AutomationId changes.
- All 39 C# files under Pages/ViewModels are below 600 lines; largest is
  ShellPage.Navigation.cs at 516. Roslyn comparison verifies 587 declarations
  unchanged, with no lost/duplicate members. Three forwarding call sites change
  only to delegate filename policy and quick-add fallback to ShellViewModel.
- Original UI scripts were run unchanged: M3 10/10 plus restart; M4 9/9 plus
  restart; M5 9/9 plus three restart checks; M7 automated preflight; M8 17/17;
  M13 11/11; multi-select 7/7; navigation startup 4/4; notification settings 5/5.
  Stability first failed (96 px anchor shift), then passed 3/3 on an unchanged
  repeat (39 px shift); baseline also passes 3/3. Results are retained under
  tests/ui/artifacts/ux13-gates.
- M6 fails 2/7 on baseline and refactor: original script requires SplitButton
  for the current picker Open button. A temporary diagnostic changing only that
  type match to accept Button passes all 7 import/export/error checks. Original
  gate unchanged and still open.
- List-icon 9/10 (zero-count/new-task row), Tasks settings 8/9 (reminder time
  after restart), and composer 2/3 (resize back to inline options) fail identically
  in the pre-refactor checkout. Tracked in UX-PLAN Parking lot, outside this
  move-only package. No acceptance exception or unrelated fix was introduced.
- Motion visuals pass: completion/uncompletion, star and undo. Timing comparison
  fails: baseline median/mean 333.895/335.080 ms; refactor 338.667/339.298 ms.
  Reduced motion skipped; no Windows animation-setting change or performance
  claim. This is not a full motion-gate PASS.
- Manual simplify and WinUI review: no remaining Error/Warning findings in the
  structural changes; imports narrowed, no extra abstractions, existing command,
  theme, accessibility and AOT behavior retained. Reviewed M4 restart and M8
  shortcuts screenshots. Native AOT runtime and owner-run manual rows not rerun.

**Shipped**
- ShellPage area partials: Navigation, Dialogs, Accelerators, Theme, Transfers
  and Lifecycle. The main code-behind contains composition and view wiring.
- ShellViewModel area partials: Navigation, Dialogs, Appearance and Transfers,
  retaining existing Settings/Sync. Export filename normalization/disambiguation
  and quick-add fallback choice moved from page to view model unchanged.
- TaskListViewModel partials: Loading, Lists, Composer, Tasks and Presentation,
  retaining Selection. TaskDetailViewModel partials: Tasks, Dates, Recurrence,
  Attachments and Persistence. Shared state and initialization remain in base
  files to preserve field initialization order and generated property behavior.
- ARCHITECTURE and AGENTS navigation pointers updated. Temporary Roslyn tooling
  and diagnostic copies stay under ignored test artifacts, not in the app.

**Deviations / remaining work**
- UX-13 stays in progress: its strict "all UI gates pass unchanged" acceptance
  is not satisfied. Baseline failures and motion validation remain explicit;
  the refactor does not silently weaken gates or change accepted decisions.

## UX-02 — Close Tasks settings validation          2026-10-11

**Gate: PASS** — Debug and Release x64 warnings-as-errors builds clean;
259/259 unit tests, zero skipped. Tasks settings 9/9, composer layout 3/3,
list icons 10/10 and M6 import/export 7/7 pass. Existing M3/M4/M5/M7/M8/M13,
multi-select, stability, navigation and notification-settings results from the
UX-13 run remain current: no product code changed in this follow-up.

**Shipped**
- Corrected baseline UI harness failures without changing app behavior:
  M6 accepts the picker Open Button or SplitButton with AutomationId 1;
  the composer gate closes detail explicitly before requiring inline options;
  the list-icon gate selects real task rows, completes through TogglePattern,
  and focuses the collapsed composer before adding the next task.
- Tasks settings selects hour 10 through the time flyout's SelectionItemPattern
  and invokes Accept in its own PopupHost window. Unscoped keyboard/focus calls
  could leave the time unchanged and prevent the restart test from running.
- Verified restart restores all five settings and the detail reminder defaults
  to 10:00. Reviewed restored settings and wide composer screenshots.
- Focused existing performance tests pass: My Day 0.897, Important 0.807,
  Planned 0.896, All 0.873, Completed 0.964 ms against 5 ms; FTS 1.655 ms
  against 60 ms. Measurements recorded in PERF.md; budgets unchanged.
- PowerShell syntax checks and manual simplify/WinUI review clean; no app,
  XAML, resources, dependencies, schema or AutomationId changes.

**Deviations / remaining work**
- Owner requested closure after the baseline-harness follow-up was proposed.
  Corrections are a separate follow-up to the move-only UX-13 refactor; all
  user-visible assertions remain. Original failed evidence stays in History
  and ignored artifacts. UX-13 motion validation remains open separately.
- Native AOT runtime, ARM64 and owner-run manual gates were not rerun.

## UX-13 — Resolve baseline UI harness failures          2026-10-11

**Gate: PARTIAL** — all four formerly failing functional suites pass after the
separate harness corrections recorded under UX-02: M6 7/7, list icons 10/10,
Tasks settings 9/9 and composer 3/3. Debug/Release builds and 259/259 tests pass.
The 39 source files remain below 600 lines (maximum 516); no product code changed.
Prior passing M3/M4/M5/M7/M8/M13, multi-select, stability, navigation and
notification-settings proof remains applicable to identical app source.

**Motion evidence**
- Fresh pre-refactor/candidate pair: baseline median/mean 333.837/336.241 ms,
  candidate 331.487/337.272 ms. FAIL on mean (+1.031 ms).
- Second fresh pair: baseline 330.490/336.310 ms, candidate 331.586/331.446 ms.
  FAIL on median (+1.096 ms). Both failed runs retained; threshold unchanged.
- Existing gate with the accepted UX-07 d6038f2 baseline: candidate
  331.071/329.839 ms versus 338.447/339.532 ms, timing PASS. Completion and
  uncompletion placement, importance and undo visuals pass in all three runs.
- Reduced motion SKIPPED pending explicit owner permission requested this turn.
  No Windows setting changed. No full motion-gate or perf acceptance claimed.
- Detailed rows and evidence paths are in PERF.md. Structural manual simplify
  and WinUI review remain clean; PowerShell parser and diff checks pass.

**Remaining work**
- Run reduced-motion check with permission and verify system setting restoration.
- Resolve strict fresh-comparison acceptance before marking UX-13 done. Also
  explicitly settle its literal "all UI gates pass unchanged" criterion: the
  owner requested these separate baseline-harness fixes, but corrected scripts
  are not unchanged scripts. No acceptance criterion silently rewritten.
- UX-02 is closed. UX-10 undo is the next feature; its optional completion
  snackbar requires a DESIGN note and owner acknowledgement before implementation.

## UX-13 — Fresh completion timing validation          2026-10-11

**Gate: PARTIAL** — strict fresh timing comparison PASS, all five motion checks
run with animation effects enabled PASS, Debug warnings-as-errors build clean,
259/259 unit tests PASS with zero skipped. Reduced motion SKIPPED pending explicit
owner permission; the runner's printed PASS includes this skip and does not
establish full package acceptance. No app or test harness source changed.

**Evidence**
- Sequential pre-refactor `ca140a8` / current `f3da981` Debug x64 runs, matching
  200-task / 20-list fixtures, 15 prewarmed UIA placement samples and one excluded
  warm-up: baseline median/mean 333.070/335.488 ms, candidate 332.412/331.688 ms.
  Both candidate values are below baseline; the strict threshold is unchanged.
- Completion/uncompletion placement, importance feedback, and undo snackbar
  entrance/automatic dismissal pass. Screenshots, recording and results are in
  `tests/ui/artifacts/ux13-quiet-after/`; the matching baseline is in
  `tests/ui/artifacts/ux13-quiet-before/timings.json`.
- The preceding candidate run failed at 339.097/348.061 ms versus baseline
  333.237/335.275 ms. Unit tests ran concurrently with part of that candidate
  measurement. That failure is retained under `ux13-final-before/` and
  `ux13-final-after/`; the subsequent comparison ran without concurrent builds
  or tests. Earlier failed comparisons remain in the history and PERF.md.
- Existing functional and structural/review proof remains applicable to unchanged
  product source. No new Native AOT or statistical speedup claim.

**Remaining work**
- Run the reduced-motion gate after permission for temporarily disabling Windows
  Animation effects; verify and record restoration of the original setting.
- Obtain acknowledgement that UX-13's "all UI gates pass unchanged" means
  unchanged product behavior, accepting the separately corrected harnesses in
  `40a1a34`: [ADR-011](adr/ADR-011-ux13-validation-harnesses.md). This clarification
  is proposed only; the package remains in progress.
- UX-10 has not been claimed or implemented while UX-13 closure remains pending.

## UX-13 — Close motion validation          2026-10-11

**Gate: PASS** — motion 7/7, zero skipped. Candidate completion median/mean
332.018/332.204 ms remain below the fresh pre-refactor baseline 333.070/335.488 ms.
Animation-off placement samples 67.143, 92.222, 64.366 ms all omit the 250 ms hold.
Windows Animation effects restored and read back as original=1, restored=1.

**Shipped**
- Owner acknowledged ADR-011 and authorized the temporary Windows setting change
  with "Yes" on 2026-10-11. Accepted the separate harness corrections in `40a1a34`
  while preserving functional assertions and strict timing thresholds.
- Source split, all functional gates, 259/259 tests, Debug/Release builds and
  clean structural/WinUI review proof remain current for unchanged product source.
- Evidence: `tests/ui/artifacts/ux13-approved-motion/` contains results, timing,
  restoration JSON, screenshots and recording. No AOT runtime claim.

**Deviations**
- Literal unchanged-script criterion clarified by accepted ADR-011. Earlier
  failures remain recorded. UX-13 is complete; UX-10 is next.

## UX-10 — Shared destructive-action undo          2026-10-11

**Gate: PARTIAL** — implemented actions pass dedicated undo 7/7, multi-select
7/7, M3 10/10 + restart, M4 9/9 + restart, M13 11/11, M8 17/17. Debug/Release
x64 warnings-as-errors builds clean; 270/270 unit tests pass, zero skipped.
Optional single-row completion snackbar awaits the owner's DESIGN §7 answer;
it has not been implemented or silently excluded from package acceptance.

**Shipped**
- Shared Core UndoService, bounded to 20 session entries. Snackbar expiry hides
  feedback while Ctrl+Z retains history across navigation. Reverts are serialized;
  failure retains the entry for retry. Text editors retain native Ctrl+Z.
- Task/list/step deletion, moves, bulk actions and due/reminder/repeat clears
  record field-specific undo snapshots. Pending detail edits flush before undo;
  newer titles/notes, unknown iCalendar properties and remote versions survive.
- Atomic store restores log changes and notify once. Recurring bulk completion
  restores dates, shifted reminders and occurrence count; ordinary completion
  restore retains later dates/reminders. Remote store changes invalidate entries.
- Migration 003 records tasks actually removed by list deletion. List restore
  retains earlier deletions, restores steps and lists together, and recreates a
  pushed CalDAV collection before uploading its tasks. ADR-012 records the design.
- Stable list IDs replace stale instance comparisons after navigation refresh.
- Unit additions: stack limit/order/retry/concurrency/invalidation, list restore
  membership/idempotence, field isolation/rollback, recurrence/alarms, remote
  notification provenance, version-two migration preservation/backup and CalDAV
  MKCALENDAR-before-PUT behavior.
- WinUI review: no findings above informational. No new dependency, reflection,
  renamed AutomationId or product XAML added; Pages/ViewModels remain below 600 lines.

**Validation and deviations**
- Evidence: `tests/ui/artifacts/undo/run-20261010-231852/`,
  `multiselect/run-20261010-233217/`, `m3/run-20261010-232001/`,
  `m4/run-20261010-233439/`, `m13/run-20261010-232137/`,
  `m8/run-20261010-233730/`. Each includes its gate results/screenshots.
- M4 baseline harness needed explicit enumeration of Windows PowerShell JSON
  window arrays and typed picker searches; no attachment behavior changed.
  Earlier attempts failed 6/9 because a mis-targeted picker remained open.
- M8 twice failed 14/17 because SPI retained customised colours; the harness now
  applies/verifies colours from shipped Windows theme files and restores/verifies
  originals in finally. The corrected suite passes 17/17. No theme rules changed.
- One 269/270 unit run hit the pre-existing SQLite pool-disposal race in unrelated
  iCalendar setup; subsequent full run passes 270/270. Earlier failures retained.
- No Native AOT/runtime performance or live-server convergence claim.

**Remaining**
- Owner answer to optional "Completed · Undo" proposal in DESIGN §7. Include and
  validate it if accepted; record its explicit omission if declined. UX-10 stays
  in progress until this scope decision is resolved.

Final dedicated undo rerun also passes 7/7 with no title-save settling delay:
`tests/ui/artifacts/undo/run-20261010-233937/`. Immediate title edits are preserved
through undo; all previously listed required gates remain passing.

## Tooling — Protect the owner's working desktop          2026-10-11

**Gate: PASS (guard verification)** — all 36 UI/performance scripts reject execution
at their first statement without `WINTODO_ALLOW_LOCAL_UI=1`. Verified with
PowerShell 7; 35 UI scripts also verified with Windows PowerShell 5.1 (the existing
performance script uses PowerShell 7 syntax). No fixture setup, app launch, UI
input or system-setting change ran during verification. Parser/diff checks pass.

**Shipped**
- Owner reported cursor/focus disruption from local visual gates. Recorded the
  main-machine policy in AGENTS.md and the testing guide: prefer an isolated VM
  or Sandbox; leave UI gates pending if isolation is unavailable; never silently
  fall back to host automation or set the opt-in without session permission.
- Guarded all 35 UI entry scripts (including direct suites and UIA measurement
  helpers) plus `scripts/Measure-Perf.ps1`. Loopback CalDAV server excluded because
  it does not drive desktop input. Builds and unit tests remain available.
- Testing guide explains process-only opt-in/restoration, guest PID/HWND routing,
  direct UIA/native helper execution inside the guest, fixture/artifact transfer,
  and guest process cleanup. It explicitly states legacy gates are local-only.

**Remaining**
- Sandbox snapshot reports no running/input-ready target; WindowsSandbox.exe is
  absent on this machine. No Windows feature was enabled. Enablement requires
  owner action/permission (UAC and potentially restart).
- Migrate/validate gates for an isolated desktop, or run the current suite inside
  a dedicated Windows VM. Adding `--on sandbox` only to launch is insufficient
  because direct UIA/input, paths, server helpers and cleanup still use the host.
- Prior UI gate results remain recorded; no isolated gate PASS is claimed.

## Tooling — Hyper-V test VM for UI gates          2026-10-14

**Gate: PASS (m0 smoke in the VM, 4/4)**. `.\scripts\Invoke-VmGate.ps1 -Gate
run-m0-gate.ps1` restored `clean`, copied the Debug x64 build and tests, ran the
gate in the guest's signed-in desktop session and returned exit code 0. Checks:
process responding, main window visible and titled, interactive controls have
AutomationIds, screenshot captured. Results and screenshot were copied back to
`tests/ui/artifacts/` (log `tests/ui/artifacts/vm/run-m0-gate-20261013-234132.log`).
A deliberately failing earlier run returned exit code 1 through the same path.
Nothing ran on the host desktop; `WINTODO_ALLOW_LOCAL_UI` is unset on the host
at process, user and machine level. Build `-warnaserror` clean; 270/270 tests pass.

**Shipped** ([ADR-013](adr/ADR-013-hyperv-test-vm.md))
- `scripts/New-TestVm.ps1` creates `WintodoTest`: Gen 2, 4 vCPUs, 8 GB static,
  80 GB dynamic VHDX, Default Switch, Secure Boot, vTPM, standard checkpoints.
  Runs unelevated for Hyper-V Administrators.
- `scripts/Install-TestVmWindows.ps1` installs Windows 11 Pro (build 26300)
  unattended:
  - an answer file on a temporary virtual DVD;
  - the boot prompt answered through the VM's `Msvm_Keyboard`;
  - a generated password, DPAPI-saved to
    `%LOCALAPPDATA%\Wintodo\WintodoTest.credential.xml`;
  - host locale `uk-UA` and FLE time zone in the guest.
- `scripts/Initialize-TestVm.ps1` prepares the guest over PowerShell Direct and
  takes the `clean` checkpoint:
  - autologon via LSA secret;
  - no lock screen or timeouts, and no update restarts;
  - 1920 × 1080 display and Developer Mode;
  - PowerShell 7.6.7, Windows App Runtime 2.5.1.0 and WinApp CLI 0.7.1, the
    latter two pinned from `Directory.Packages.props`;
  - the M10 test certificate in TrustedPeople.
- `scripts/Invoke-VmGate.ps1` runs gates in the VM. It uses a one-shot interactive
  scheduled task for the desktop session, a single-run mutex and artifact copy-back,
  and turns the VM off afterwards. `scripts/Set-TestVmCredential.ps1` re-saves the
  credential. `scripts/TestVm.Common.ps1` holds the shared helpers, including
  `Save-TestVmScreen` (guest screen via Hyper-V WMI).
- `tests/ui/run-m0-gate.ps1`: new runner for `m0-smoke.ps1` (which only takes a
  PID). It waits up to 60 s for `Todo.List.AddTaskBox` before the smoke checks.
- Host Hyper-V enhanced session mode turned off (owner-approved; no other VMs).
- AGENTS.md hard rule, testing guide ("Running gates in the test VM") and ADR index
  updated.

**Deviations**
- The owner asked to install Windows themselves; on 2026-10-14 they asked the
  agent to do it, so the install became unattended (`Install-TestVmWindows.ps1`).
- The payload omits `Wintodo.build.appxrecipe` and `AppX\`. The recipe lists
  absolute host paths (`obj\`, NuGet cache), so `winapp run <folder>` failed in the
  guest with "The build did not produce 275 file(s)". Without the recipe, winapp
  builds the layout from the folder.
- The first VM m0 run failed 2/4 (window not yet visible, screenshot failed). The
  first launch in a fresh guest is cold, and `m0-smoke.ps1` checks immediately.
  The runner now waits for the main window; `m0-smoke.ps1` itself is unchanged.
  Its AutomationId check passed vacuously in that run, before any window existed.

**Known issues carried forward**
- Gates that seed databases with `dotnet test` are still pending in the VM: m3,
  m5, m7, undo, multiselect, list-stability, completion-motion/measure and
  notification-settings. They need the .NET SDK and sources in `clean`. `-Aot`
  variants and `Measure-Perf.ps1` aren't wired up yet.
- Other gates are unvalidated in the VM (m8, m13, settings, list-icon,
  navigation-startup, composer-layout, m6). They may need the same window-ready
  wait on a cold first launch.
- The guest isn't activated (generic Pro install key); an activation watermark
  may appear in screenshots.

## Tooling — All automated gates wired into the test VM          2026-10-14

**Gate: PARTIAL**. Swept in `WintodoTest` via `Invoke-VmGate.ps1`; logs are in
`tests/ui/artifacts/vm/`.

| Result | Gates |
|---|---|
| PASS | m0 4/4 · m3 · m4 9/9 + restart · m5 9/9 + restart · m6 · m7 preflight · m13 · m13 `-Aot` · composer-layout · composer-layout `-Aot` · navigation-startup · notification-settings · list-icon · multiselect |
| FAIL | m8 16/17 · settings 8/9 · list-stability 2/3 |
| Not run | undo (sweep stopped by owner request) · completion-measure/motion · `Measure-Perf.ps1` |

The three failures are open; none is diagnosed as an app defect yet:
- **m8:** "Original High Contrast state is restored" fails in the fresh guest.
  It probably comes from restoring an empty default-scheme name; unconfirmed.
  The check now reports which flags, scheme or colours differ. Rerun m8 to read it.
- **settings:** "Add new and star … move tasks to top" failed because a
  `winapp ui inspect --depth 16` call failed with no output. Not yet rerun, so
  it's unknown whether this is flaky.
- **list-stability:** Space completion moved the scroll anchor 178 px (limit one
  row, 50 px). The VM runs at 100% scaling and 1920 × 1080. It needs a rerun to
  separate flakiness (PROGRESS already notes "passes on repeat") from a real
  layout bug.

**Shipped**
- `clean` now has .NET SDK 10.0.401 (official installer, SHA-512 checked against
  release metadata) and a NuGet cache warmed for `tests\Wintodo.Data.Tests`.
  `Initialize-TestVm.ps1 -Force` restores the old `clean` first.
- `Invoke-VmGate.ps1` changes:
  - It copies all tracked and untracked-not-ignored sources plus the host-built
    outputs, through the shared `New-TestVmPayload`/`Copy-TestVmPayload` helpers.
  - It builds `tests\Wintodo.Data.Tests` in the guest when the gate calls
    `dotnet test`.
  - `-Aot` copies `publish\aot\win-x64`. `-Gate Measure-Perf.ps1` copies the
    Release FD build and AOT output, adds `-SkipBuild` and copies a changed
    `docs/PERF.md` back. `-Include` adds extra files, such as a baseline.
  - A warm-up launch runs before UI gates, then `Reset-AppxPackage`; `-NoWarmup`
    skips it. A fresh guest's first launch takes 11–12 s, longer than the suites'
    window waits (m5 failed on it). Measure-Perf never warms up.
  - It recovers the gate lock if a previous run was killed.
- `m4-detail.ps1`: the file dialog's Open control (id 1) is a SplitButton on host
  build 28020 but a Button on guest build 26300. The test accepts both.
- `m8-design-accessibility.ps1`: the restoration failure detail names what differs.
- Host: `Publish-Aot.ps1 -Platforms x64` and the FD Release build refreshed.
  Build is clean; no C# changes since the 270/270 unit test run.

**Remaining**
- Rerun m8, settings and list-stability; diagnose any that fail again.
- Run the undo and completion-measure/motion gates (motion compares against a
  same-environment baseline, so it isn't a UX-07 regression comparison) and a
  `Measure-Perf.ps1` VM baseline labelled `-VM`.
- m7 toast Complete/Snooze and reboot persistence remain manual.

## Tooling — VM follow-up gates and viewport restoration          2026-10-17

**Gate:** PASS for the required automated follow-up; optional VM perf not run.
Host Debug x64 build: zero warnings/errors; Core/Data/Sync tests 270/270,
zero failures/skips. All UI execution stayed in WintodoTest (Windows 26300,
1920 × 1080, 100% scale); no host app launch, automation or UI opt-in.

| Gate | Results this session |
|---|---|
| M8 | Initial 16/17 (1 failure); corrected harness 17/17; final app 17/17 (0 failures on both corrected runs) |
| Settings | Unchanged 9/9 twice (0 failures on both runs) |
| List stability | Before fix: 3/3, then 2/3 (1 failure); after fix: 3/3 twice (0 failures) |
| Undo | Initial 5/7 (2 failures); intermediate harness 0/7 (7 failures); corrected harness 7/7 (0 failures) |
| Completion measure | 15/15 placement samples captured; exit 0 |
| Completion motion | 7/7 (0 failures/skips), including reduced motion and setting restoration |
| M9-VM perf matrix | Not run (optional) |

**Shipped**
- M8 failure reproduced exactly: flags and all colours restored, but SPI scheme
  changed from empty to `High Contrast Black`. Both a null pointer and an explicit
  empty string retain Windows' cached name; clearing the preference, broadcasting
  settings change and refreshing per-user parameters do not clear that cache.
  Restore the saved registry preference for the empty-scheme case. Only an
  originally empty, inactive scheme checks the preference instead of the cached
  name; named/active schemes, flags and every system colour remain exact checks.
  Original/restored values are saved in `contrast-restoration.json`.
- Settings' earlier empty-output `inspect --depth 16` failure did not recur in
  two unchanged clean-checkpoint runs. Classified as transient CLI/UIA failure;
  no app change or retry that hides failures.
- Stability reproduced a real intermittent app layout defect: 93 px drift after
  a 10 px passing run, versus the 50 px limit (previous sweep: 178 px).
  Grouped virtualization can revise offsets after ChangeView's first correction.
  Viewport restoration now rechecks/corrects the realized anchor, at most four
  times, stopping within 1 DIP and cancelling on newer updates or user input.
  Two clean-checkpoint runs passed with 10 px drift each; exact completion focus
  and Space restoration also passed. This is WinUI layout code, not Core logic;
  the existing real-VM gate supplies regression coverage instead of a mock unit test.
- Undo's initial bulk check failed CLI focus confirmation on the ListView; its
  list-deletion check missed the expiring snackbar. Keyboard undo now focuses
  the list menu button, multi-selection focuses a selected row, and list deletion
  uses Ctrl+Z. An intermediate row-search approach failed on the empty target
  list and cascaded to 0/7; that approach was removed. Final run passes 7/7.
- Same final Debug build and VM environment: baseline completion median/mean
  429.568/424.515 ms; motion run 359.499/366.242 ms (15 samples each, one warm-up
  excluded). Original strict thresholds unchanged. Reduced-motion placements:
  68.851, 90.426, 71.320 ms; animation setting 1 restored/verified as 1.
  Motion 7/7 is a harness/behaviour check, not a UX-07 regression comparison,
  statistical speedup claim or AOT/perf-matrix result. Timings recorded in PERF.md.
- Used `-KeepRunning` and Hyper-V `Save-TestVmScreen` for guest diagnostics;
  reviewed returned screenshots. WinUI code review: no findings above informational.
  No ADR or accepted UX decision changed.

**Evidence**
- Logs: `tests/ui/artifacts/vm/` (run names dated 20261017).
- M8: `m8/run-20261017-194839` (failure), `m8/run-20261017-200331` (fixed).
  Final app recheck: `m8/run-20261017-205004`, 17/17.
- Settings: `settings/run-20261017-195402`, `settings/run-20261017-200810`.
- Stability: `list-stability/run-20261017-195848`, `run-20261017-201236`
  (93 px failure), `run-20261017-201833`, `run-20261017-202347` (fixed).
- Undo: `undo/run-20261017-204501` (final); earlier failed logs retained.
- Completion: `completion-measure/vm-20261017/timings.json`,
  `completion-motion/run-20261017-203731` (results, timings, video and screenshots).

**Remaining**
- Optional `Measure-Perf.ps1 -Milestone M9-VM` baseline not run.
- M7 toast actions/reboot, Narrator, live CalDAV and release/manual rows remain open.

## Planning — Skip configurable My Day reset          2026-10-18

**Gate:** Not run; planning-only change.

- Owner declined UX-12 to preserve Microsoft To Do parity: My Day continues
  resetting at midnight, without a configurable reset time.
- UX-PLAN status and Current state updated. UX-14 is the next proposal; its
  DESIGN acknowledgement is still required before implementation. The optional
  UX-10 completion snackbar has not been approved or declined by this decision.
- No app code or existing gate result changed.

## UX-14 — Design acknowledgement and claim          2026-10-18

**Gate:** Not run; acknowledgement recorded before implementation.

- Owner approved a compact Sort button at default, a Sorted by … chip with
  direction/reset otherwise, and reset to each list's actual default.
- Updated DESIGN §3.1 and recorded ADR-014; claimed UX-14 in UX-PLAN.
- Existing sort AutomationIds, choices and persistence are required to survive.
- UX-12 remains skipped, UX-10 completion snackbar undecided, M9 deferred.

## UX-14 — Conditional sort indicator          2026-10-18

**Gate:** UX-14 behavior PASS; required list-stability gate FAIL. Owner scope
decision pending before final implementation commit/push.

**Shipped in the working tree**
- Compact localized Sort dropdown at default; Sorted by … chip with direction
  and command-driven reset otherwise. Existing sort/menu AutomationIds retained.
- TaskSorting owns actual list-kind defaults and compares effective sort terms.
  Completed retains its fixed completion-date ordering and ignores mode choices.
- Reset saves both mode/direction through existing user-list persistence; smart
  sorts retain their previous navigation/session lifetime. Search/Settings hide
  the indicator. Async saves retain the initiating query/list across navigation.
- Added 16 Core default/effective-sort cases and two Data persistence/order cases.
- Preserved and included the owner's UX-12 skip planning edits in approval commit
  `185cb95`. DESIGN and ADR-014 acknowledgement were written before implementation.

**Validation**
- Final Debug x64 build with `-warnaserror`: 0 warnings, 0 errors. Unit tests:
  Core 185, Data 93, Sync 10; 288 PASS, 0 FAIL, 0 SKIP.
- WintodoTest sort gate `run-20261017-220208`: initial 10/10, saved-sort restart
  2/2, reset restart 1/1; total 13 PASS, 0 FAIL, 0 SKIP. Includes five smart
  lists, Tasks, user lists, task order, navigation/search, keyboard reset,
  Light/Dark, detail-open and minimum-width layout with overlay detail closed.
- M5 `run-20261017-220825`: phase one 9/9, restart 3/3; 12 PASS, 0 FAIL, 0 SKIP.
- M8 `run-20261017-215419`: 17 PASS, 0 FAIL, 0 SKIP; chip/keyboard reset audited
  in Aquatic, Desert, Dusk and Night sky; original Windows contrast state restored.
- List stability with final UX-14: two runs each 2 PASS, 1 FAIL, 0 SKIP;
  completion anchor drift +65 px against a 54 px limit. Exact completion-focus
  and uncompletion assertions after the drift check were not reached.
- Controlled comparison using only the previous header XAML (`0e17d5a`) with the
  same completion code/fixture: 2 PASS, 1 FAIL, 0 SKIP; +93 px anchor drift.
  UX-14 XAML restored and final build clean afterward.
- A two-second observation experiment on final UX-14 also failed: 2 PASS,
  1 FAIL, 0 SKIP; -350 px anchor drift. Timing experiment reverted; no viewport
  implementation or tolerance change remains in this package.
- All launches/input/captures ran exclusively through Invoke-VmGate in
  WintodoTest. Guest logs/artifacts copied back; credentials never printed.

**Harness corrections and review**
- Initial sort attempts: 0/8, 0/10, 0/10, 9/10 and 9/10 (each 0 skips;
  three restart checks not reached per attempt). Corrected nested-content name
  assertion, expected-absence checks, PowerShell Sort alias collision, search
  editor targeting via Ctrl+F, and visible-header check when detail overlays.
- Initial M5: phase one 9/9; restart runner failed before its three assertions.
  Added a bounded 15-second app-readiness wait; corrected full rerun passes.
- Vendored winui-code-review completed: no outstanding Error/Warning in UX-14
  changes. Localized accessible dropdown/reset names, explicit x:Bind modes,
  commands, theme resources, 32px reset target, no dependencies/schema changes,
  AOT-safe Core policy and persistence verified. Reviewed VM screenshots in
  Light/Dark, narrow layout and contrast. Existing viewport failure tracked above.
- M9 remains deferred; no AOT runtime/performance validation claimed. UX-10's
  optional completion snackbar remains undecided; My Day still resets at midnight.

## UX-14 — Commit/push disposition          2026-10-18

**Gate:** Sort 13/13, M5 12/12 and M8 17/17 PASS; list stability remains FAIL
(2/3). This disposition does not mark the failed or unreached assertions passed.

- Owner explicitly chose commit/push with the failed stability gate documented.
  UX-14 ships within its accepted sort-indicator scope; viewport repair stays
  separate. The preceding implementation entry records the validation evidence
  and the scope decision that was pending at the end of that session.
- Pre-commit verification: fresh Debug x64 `-warnaserror` build passed with
  0 warnings/errors; all 288 unit tests passed with 0 failures/skips. Reviewed
  the final UX-14 diff under vendored winui-code-review: no outstanding
  Error/Warning. VM gates were not rerun during this commit/push handoff;
  their exact prior results remain recorded above.
- Existing UX-12 skip edits are preserved. UX-10's optional completion snackbar,
  M9 deferral and owner-controlled release decisions remain unchanged.
- Recommended next: diagnose completion anchor drift, fix the responsible layer
  and pass all three list-stability checks in WintodoTest with the existing
  54 px limit, including exact completion focus and Space uncompletion.

## UX-15 — Quieter placeholder when a list has no icon          2026-10-18

**Gate:** PASS — Debug x64 build clean, 288/288 unit tests, list-icon 14/14,
M8 17/17. Completion viewport drift remains separately open; stability was
not rerun or marked passed by this package.

**Shipped**
- Owner approval recorded before implementation in claim commit `df3aaa1`:
  UX-PLAN acceptance, DESIGN §3.1 and an appended ADR-009 amendment preserve
  the original decision while recording the approved header change.
- The existing 44 px semantic Button shows a 16 px colour dot without an idle
  border when no emoji is assigned. VisualStateManager reveals the Fluent Emoji
  affordance on hover or keyboard/programmatic focus, with native focus chrome.
  A selected emoji stays visible; presets, custom emoji and clear commands reuse
  their existing paths. No new product strings, dependencies or schema changes.
- Placeholder colour updates immediately after list-colour changes and uses
  the app's actual Light/Dark palette; High Contrast uses system window text.
  Existing accessible name and AutomationIds are retained.

**Validation**
- Final `dotnet build Wintodo.sln -p:Platform=x64 -warnaserror`: 0 warnings/errors.
  `dotnet test Wintodo.sln -p:Platform=x64`: Core 185, Data 93, Sync 10;
  288 passed, 0 failed, 0 skipped. No new domain logic requires unit cases.
- Expanded list-icon VM gate `run-20261018-151837`: 14 passed, 0 failed/skipped.
  Covers real Shift+Tab focus and Enter activation, hover, icon selection/custom
  validation, clearing, saved emoji and cleared-icon restart persistence,
  colour changes, Light/Dark and detail-open/minimum-width layout. Screenshots
  confirm the new dot, hover affordance and focus states without clipping.
- M8 VM gate `run-20261018-152234`: 17 passed, 0 failed/skipped. Added idle/hover
  captures and keyboard picker activation in Aquatic, Desert, Dusk and Night sky.
  Header states visually reviewed; saved contrast flags, colours and registry
  preference restored using the existing documented SPI-cache exception.
- Initial icon run `run-20261018-151155`: 13 automated checks passed, but visual
  review found the placeholder retained a stale colour and system-theme brush.
  Corrected only the placeholder refresh/palette lookup, rebuilt and reran the
  complete gate with the extra cleared-icon restart check before acceptance.
- Vendored winui-code-review completed with no outstanding Error/Warning in
  UX-15: explicit x:Bind modes, localized accessible name, preserved commands,
  semantic button/tab stop, native focus, theme brushes and AOT-safe APIs.

**Scope and remaining work**
- Existing wider accent resolution can retain stale colours/use the system
  palette during app-only theme switches. Recorded separately in UX-PLAN's
  parking lot; this package fixes the new placeholder only.
- Release/AOT, performance and manual owner gates were not rerun. M9 stays
  deferred; UX-10's optional completion snackbar is still undecided.
- All UI input and capture ran in WintodoTest through Invoke-VmGate, with no
  host desktop automation. Next work remains the completion viewport repair.

## UX-10 — Owner-requested completion review          2026-10-18

**Gate:** PARTIAL — Debug x64 warnings-as-errors build clean; 288 unit tests
passed, 0 failed/skipped. Fresh undo VM gate 6/7; full UX-10 not approved.

- Owner requested "Check it and approve if done right." Reviewed row/bulk/detail
  completion, shared undo history, field restoration, recurrence/alarm restoration
  and current tests under vendored winui-code-review.
- Optional list-row "Completed · Undo" is not implemented: ToggleTaskCompletionAsync
  does not capture/record undo. DESIGN §7 is still a proposal comment. Bulk
  completion records field undo; detail completion stays quiet. No optional
  behavior was approved as shipped, or silently skipped.
- First VM run `run-20261018-155559`: 6/7, 0 skips. Expected absence of Bulk task
  01 used search, whose nonzero exit was treated as test failure. Replaced it
  with explicit wait-for --gone, preserving the stack-order assertion.
- Second run `run-20261018-160116`: 5/7, 0 skips. Older history check and deleted
  step check missed the five-second snackbar between CLI calls. Older history
  now uses Ctrl+Z, appropriate after expiry. Removed redundant wait before
  visible-button invoke; other checks still exercise the actual snackbar.
- Final rerun: 6/7, 0 skips; log `vm/run-undo-gate-20261018-160625.log`.
  Retained history, newer title, reminder/repeat clears, step restore, move and
  list restore pass. The combined bulk check misses UndoButton after intermediate
  row-value reads consume its lifetime; later assertions in that check are not
  reported passed. No product or five-second duration change. Stopped after the
  two permitted fix/rerun cycles; remaining harness timing requires focused work.
- Existing Core/Data tests prove bounded history/retry/serialization/invalidation,
  recurring occurrence/alarm/count restore and newer-field preservation. These
  do not replace the incomplete UI gate. No AOT or stability validation claimed.
- All input and captures ran inside WintodoTest; no host UI automation.

## UX-18 — Share-target proposal and UX-16/17 disposition          2026-10-18

**Gate:** PROPOSAL ONLY — no share implementation or integration pass claimed.

- Owner chose UX-18 and skipped UX-16/17 for now. Status table and package
  dispositions updated; completion viewport drift remains open separately.
- Prepared ADR-015 and indexed it as proposed. Existing M7 shares accept Text
  only, initialize the full shell/integration/sync graph, then create a task and
  open it. No current real-share gate proves URL or broker/AOT handling.
- Proposed extension: Text/WebLink to local default Tasks, preserve URLs in notes,
  minimal native share window, one committed save, return to source application,
  localized failures, immediate cleanup and narrow already-open-shell refresh.
- Checked exact referenced DataPackageView/ShareOperation/AppInstance APIs via
  winapp find-api and current Microsoft Learn activation/manifest/lifecycle docs.
  UWP CoreWindow lifecycle wording is not proof for WinUI HWND activation; real
  broker and x64 AOT share tests are required. M10 identity/signing stays separate.
- Asked for acknowledgement of the concrete ADR before code, as UX-PLAN Phase D
  explicitly requires. UX-18 stays awaiting owner; no product files were changed.

## UX-10 — Row-completion undo finished and verified          2026-10-18

- Preserved the handoff edits and finished list-row completion undo. Captures
  completion state before writing; the five-second snackbar starts after the
  row update, so an immediate undo cannot race the delayed completed-row move.
  Detail completion stays quiet; Ctrl+Z retains history after snackbar expiry.
- Recurring occurrence completion now explicitly refreshes the native checkbox
  even when its bool stays false. Undo restores due/start, shifted alarms and
  completion history while preserving newer title/note edits.
- Expanded undo VM gate: 12/12, no skips (`undo/run-20261018-164742`;
  `vm/run-undo-gate-20261018-164543.log`). Ordinary/recurring row completion
  each pass snackbar and keyboard-after-expiry checks; detail quiet passes.
  Initial expanded run was 10/12 with the recurring native-toggle defect;
  the product fix and rerun passed. Bulk checks retain the handoff Ctrl+Z fix.
- M3 10/10 plus restart (`m3/run-20261018-164016`); M4 9/9 plus restart
  (`m4/run-20261018-170055`); M13 11/11 (`m13/run-20261018-170317`);
  M8 17/17 (`m8/run-20261018-170420`). VM-only input and capture. Contact
  sheets reviewed for undo/M3 and all 29 M8 captures, including four contrast
  palettes. No visual regression found. Narrator/manual rows stay pending.
- Debug x64 warnings-as-errors build clean. Full units pass 307/307 during
  combined UX-10/18 work; recurrence regression strengthened. One intervening
  existing InvalidListMutation test failure did not reproduce in its focused
  or full rerun. Vendored winui-code-review: no issues above informational.
- No viewport stability or performance claim; the existing 54 px tolerance and
  separate completion drift issue remain unchanged. Separate UX-10 commit.

## UX-10/18 — Activation regression follow-up          2026-10-18

- Combined VM regression batch passed M4 9/9 plus restart, M13 11/11, M8
  17/17 and M0 4/4; M7 initially missed Launch at login. The fresh standalone
  M7 rerun passed UI, scheduled-tag and badge preflight (`m7/run-20261018-171549`,
  `vm/run-m7-gate-20261018-171403.log`). No product or tolerance change for M7.
  Captures reviewed through the integration contact sheet; manual toast/reboot
  rows remain pending. Added missing-control diagnostics for future M7 failures.
- UX-10 completion commit: 6cce524. UX-18 remains in progress. Real Windows
  Share discovers Wintodo, but the cold x64 AOT target is initially treated as
  a normal launch; source never receives ShareCompleted. Focused diagnostics
  (`vm/run-share-gate-20261018-171124.log`) prove normal-shell startup rather
  than share-only startup. Mapping/unit/manifest passes do not replace broker
  evidence. Testing the direct platform activation query used by Microsoft's
  official WinUI ShareTarget sample; no ADR reapproval or host UI fallback.

## UX-18 — Windows Text/WebLink share target complete          2026-10-18

- Implemented accepted ADR-015. Selecting Wintodo in Windows Share saves one
  root task in local default Tasks. Core maps first nonempty text line/title,
  preserves Unicode/note lines, source WebLink title and exact URL note lines;
  HTTP(S) URLs stay local, without fetch/launch/date inference or truncation.
  No dependency, migration, credential or package identity change.
- Share activation precedes notifications, MainWindow, reminder and CalDAV
  construction. Minimal native progress/error window keeps ShareOperation on
  the UI dispatcher. One save attempt; ReportCompleted follows commit. Store
  disposal and process exit verified. Empty/provider/database failures display
  localized Close-only errors and save no task. Committed-report failure has
  separate wording and never retries a write.
- Real AOT exposed two activation defects hidden by helper tests: WinUI startup
  consumes the platform payload, and the AOT IInspectable does not match the
  concrete ShareTargetActivatedEventArgs type check. Program now captures
  AppLifecycle activation before Application.Start; App explicitly queries
  IShareTargetActivatedEventArgs via CsWinRT As. No single-instance redesign.
- Main window observes external commits on return via the writer connection's
  SQLite data_version; routine local activations avoid reloads. Stable row
  selection and open title/note editor survive. Inline title drafts are retained.
  Data unit proof distinguishes local versus other-connection commits.
- Real source fixture uses native DataTransferManager HWND interop and a
  button-driven Windows Share UI. All activation/input/capture stayed in
  WintodoTest. Published x64 gate 10/10, no skips:
  `share/run-20261018-180938`, log `vm/run-share-gate-20261018-180754.log`.
  Text, WebLink, text URL, combined/repeated open-main shares, empty and failed
  provider, unavailable database, unsupported discovery and restart all pass.
  Exact title/note/default-list/task count verified after each scenario; every
  successful operation gets one task, source ShareCompleted and target exit.
- Corrected harness defects: host C++ runtime dependency (static fixture build),
  hidden source startup, wrong 1x1 shell helper HWND, picker-ready waits, real
  source button, duplicate-title wait (search/count two rows), PowerShell IntPtr,
  error Close click. The OS picker sometimes stalls before target selection;
  the harness allows one reopen before any activation/save, and preserves failed
  run evidence. No committed write retry or duration/budget relaxation.
- Final Debug x64 warnings-as-errors build: 0 warnings/errors. Full units:
  Core 202 + Data 95 + Sync 10 = 307/307, no failures/skips. x64 and ARM64
  Native AOT publish clean; generated manifests retain identity/permissions and
  advertise Text/WebLink. x64 real-share runtime proven; ARM64 runtime unclaimed.
- Final startup regression log `vm/run-share-regression-gate-20261018-181444.log`:
  M0 4/4, M7 automated UI/schedule/badge preflight, x64 AOT M13 11/11.
  Earlier affected M3/M4/M8 pass with UX-10 evidence. Manual toast/reboot,
  Narrator, M10 signing/Store and deferred perf rows stay open.
- Vendored winui-code-review completed: no issues above informational. Compiled
  bindings have explicit modes, partial observable properties/commands, theme
  resources, localized strings and IDs; no reflection/new dependency. Final
  error/main-return and integration captures reviewed through contact sheets.
  No clipping/overlap found; no share Dark/HighContrast runtime claim.
- UX-10 remains separately committed as 6cce524. Completion viewport drift
  remains separate, with the existing 54 px tolerance unchanged. No perf claim.

## UX-14 follow-up — Completion viewport restoration          2026-10-18

**Gate:** PASS — strengthened stability 3/3 twice, M3 10/10 plus restart
order, M8 17/17; Debug and Release x64 warnings-as-errors builds clean;
Core 202 + Data 95 + Sync 10 = 307/307, no failures/skips.

**Cause and shipped fix**
- The panel's item-preserving scroll updates competed with the view's delayed
  ChangeView corrections. Diagnostic tracing showed requested corrections
  followed by offsets moving in the opposite direction. During uncompletion,
  a recycled anchor made the old loop exit before restoration; a trace later
  placed that anchor about 998 px above its expected position.
- ItemsStackPanel now uses KeepScrollOffset so the existing view owns the
  anchor correction. Updates with no item transitions skip the 500 ms wait;
  an unrealized surviving anchor is scrolled into realization before correction
  resumes. Existing four-attempt bound and cancellation on new input remain.
- The gate retains its original 900 ms observation, 54 px tolerance, exact
  completed-row focus and Space restoration checks. It additionally checks
  settled completion and uncompletion positions and captures both. It focuses
  the existing menu button before wheel input to establish the foreground app.
- Final clean-checkpoint runs each pass 3/3: immediate completion, settled
  completion and settled uncompletion all have 10 px anchor drift. Selected
  completed task 099 retains its exact AutomationId as keyboard focus.
- Vendored winui-design/API checks and winui-code-review completed with no
  issues above informational. Stability, M3 and all M8 captures reviewed;
  no new clipping/overlap found. DESIGN and UX-PLAN record the resolved issue.

**Evidence and limits**
- Final stability: list-stability/run-20261018-190759 and run-20261018-191318
  (scroll-anchor.json, settled-scroll-anchor.json, uncompleted-scroll-anchor.json,
  completion-focus.json, results.json and six screenshots per run).
- M3: m3/run-20261018-191744; restart-results.json verifies persisted task order.
- M8: m8/run-20261018-192046; 17/17 and original contrast state restoration
  verified by the existing accepted harness.
- Unchanged baseline passed 3/3 this session; historical 65/93/350 px failures
  remain in History. An instrumented run also passed. A three-launch diagnostic
  batch passed its first run; the other two stopped before product assertions
  on the CLI foreground-window guard. Those failures remain in
  vm/run-viewport-investigation-20261018-185834.log; the temporary batch and
  product tracing were removed. No original assertion or tolerance was relaxed.
- All launches, synthetic input and captures stayed inside WintodoTest through
  Invoke-VmGate. No host UI automation, new dependency, ADR change or performance
  claim. Release build is verified; fresh Native AOT runtime and ARM64 execution
  were not part of this focused repair. M9 and owner-run manual gates remain open.

## UX-20 / M10 — Manual gate and release preparation          2026-10-18

**Gate:** PREPARATION PASS; owner-run UX-20 and M10 deployment gates PENDING.
Debug/Release x64 warnings-as-errors builds clean; unit tests 307/307 with no
failures/skips. Native AOT publish and unsigned MSIX packaging succeed for x64
and ARM64. Local test signing succeeds; full trust verification does not pass.

**Shipped**
- Committed the already-verified completion viewport repair as `23881eb` after
  reviewing the six-file diff, restored-project ItemsStackPanel API, existing VM
  stability/M3/M8 evidence and returned screenshot. No review findings above
  informational. Existing UI evidence remains the preceding entry; no new UI
  gate or ARM64 runtime execution is claimed in this preparation session.
- Added `docs/guides/release-checklist.md`: M6 external import, M7 reboot reminder
  and visible badge, M8 Narrator, real Radicale/Nextcloud convergence, offline
  edits, conflicts and unknown-property retention. Each row remains pending
  owner observation. The checklist explains how to retain guest state through
  reboot without restoring the standard gate checkpoint.
- Prepared fresh unsigned and test-signed x64/ARM64 packages in the ignored
  `publish/msix` directory, using the existing test PFX/public certificate.
  Packaged manifest inspection confirms the existing identity/version,
  `runFullTrust` only, toast/COM/startup/share extensions, and Text/WebLink.
  Native app, WinUI runtime, Windows App Runtime and SQLite binaries match each
  package's architecture and have no CLR directory. Required assets/PRI exist.
- `publish/msix/inspection.json` records SHA-256 hashes, source commit, package
  sizes and signature metadata. Unsigned x64: 57,071,649 bytes; ARM64:
  55,456,420 bytes. Test-signed x64: 57,082,950 bytes; ARM64: 55,467,733 bytes.
  Signing certificate matches the existing `CN=AppPublisher` public certificate.
- SignTool verification exits 1 for both test-signed packages because the
  self-signed development certificate is not trusted on the host. Logs are
  retained as `*.verify.log`; signature presence and signer matching do not
  replace a passed trust/deployment gate. No host certificate trust was added.

**Open decisions and limits**
- Final app name, exact identity/publisher and distribution route were requested
  from the owner; current placeholders remain. Production signing, release
  assets/listing, signing/release CI and Store submission remain pending those
  choices. Existing CI still uploads unsigned artifacts only.
- M10's clean-install premise requires a separate guest baseline: the normal
  `clean` checkpoint already has Developer Mode and test certificate trust.
  The checklist records this and the install/uninstall acceptance; no VM
  provisioning, certificate trust, installation or owner desktop automation
  was performed. Guest changes outside the repo require owner authorization.
- No ADR or accepted product decision changed. M9 remains owner-deferred.

## Header alignment — Center fixed-category title lettering  2026-10-18

**Gate:** PASS — header alignment 10/10, M8 17/17 in WintodoTest;
Debug/Release x64 warnings-as-errors builds clean, 307/307 tests pass without
failures/skips. WinUI code review has no findings above informational.

**Cause and fix**
- The fixed icon and title already share the center of a 44 px horizontal row.
  TextBlock's full font line metrics nevertheless put visible Tasks lettering
  2 px below the icon's ink center (115 vs 117 in the baseline capture).
- Set `TextLineBounds="Tight"` only on `SmartListTitle` so the visible letters
  center beside the fixed icon. Existing glyph size, 44 px slot, spacing, theme
  resources and AutomationIds remain intact. No arbitrary offset or new control.
  My Day/search use this title too; editable user-list titles are unchanged.
- Grounded the change with the vendored design skill, TextBlock Gallery sample,
  and restored-project TextLineBounds/API checks. DESIGN and the testing guide
  now describe the alignment and focused regression gate.

**Proof and limits**
- Header run `header-alignment/run-20261018-203731`: Tasks, Important, Planned,
  All and Completed in Light/Dark pass 10/10. Tasks differs by -0.5 px in Light
  and -1 px in Dark; every measured header is within 1 px. Returned captures and
  the `headers.png` contact sheet reviewed with no clipping/overlap.
- The first diagnostic could not locate the decorative icon in CLI control-view
  search. A raw UIA probe also found no FontIcon peer and was stopped. A later
  diagnostic captured a labeled multi-window composite, which could not map to
  UIA coordinates. Final measurement crops the known XAML icon slot relative to
  the adjacent title and uses an actual screen-region capture; it requires visible
  ink and retains measurements/screenshots. Original diagnostic failures remain
  in ignored artifacts; no product gate is claimed from them.
- The final pixel gate initially checked a 2 px ceiling; saved measurements were
  re-evaluated against the committed stricter 1 px limit, all 10 passing. The
  diagnostic-only bypass was removed. M8 `m8/run-20261018-204001` passes 17/17,
  including all four contrast palettes and accepted state restoration.
- All input, captures and UI execution stayed inside WintodoTest. No host UI
  automation or ADR change. Earlier MSIX packages are unchanged; fresh AOT/MSIX
  runtime execution and ARM64 were not part of this layout repair.

## M10 follow-up — Signed x64 header-alignment update         2026-10-18

**Gate:** PACKAGE PASS — self-contained x64 Native AOT MSIX builds with warnings
as errors, signature verified as Valid on the host. New package runtime/install
gate not executed in this session; preceding header/M8 evidence remains.

- Built the current header-alignment source into `publish/msix/Wintodo-x64.msix`
  and signed that exact filename with the existing development PFX, as requested.
- Incremented manifest version to `1.0.0.1` so it can update the prior `1.0.0.0`
  installation with the same identity/publisher. No identity or certificate change.
- Inspected packaged version, x64 PE architecture, absence of a CLR directory,
  signature presence and signer `CN=AppPublisher`. Get-AuthenticodeSignature
  returns Valid / "Signature verified." No certificate trust was added by the agent.
- Artifact: 57,082,493 bytes; SHA-256
  `072143AA0DF06AFB4AA6C7416E4E363AFA53976A18BCD436503208AFE36A3224`.
  Metadata is saved to `publish/msix/Wintodo-x64.details.json`; earlier
  `inspection.json` remains historical and describes the previous packages.
- ARM64 and the separately named test-signed packages were not rebuilt. No app
  installation, launch or host UI automation was performed. M10 production
  signing/clean-install gates and M9 remain open.

## Header alignment follow-up — Newly created user lists      2026-10-19

**Gate:** Header alignment PASS 14/14 in WintodoTest; Debug/Release x64 builds
clean, 307/307 tests pass. Signed Native AOT x64 MSIX package creation succeeds;
Get-AuthenticodeSignature returns Valid. List-icon regression PASS 14/14.

- Owner's new-list screenshot showed the same optical offset in the separate
  user-list display TextBlock. The previous correction only covered fixed
  categories. Applied `TextLineBounds="Tight"` to that display TextBlock too,
  retaining the icon picker target, bindings, title-edit command and editor.
- Expanded `run-header-alignment-gate.ps1` with freshly created lowercase
  `adwawd` and descender-bearing `Project gy` lists in Light/Dark. The new-list
  dot/title centers differ by 0.5 px and 0 px respectively in both themes.
  Fixed-category checks continue to pass; all 14 measurements meet the 1 px limit.
  Evidence: `header-alignment/run-20261018-205709`, measurements and screenshots.
  Light/Dark new-list captures visually reviewed without clipping/overlap.
- WinUI code review of the additional XAML change has no findings above
  informational. No new API, dependency, theme brush or ADR change.
- Incremented package version to `1.0.0.2`, retaining identity and the existing
  developer certificate. Rebuilt/signed the requested
  `publish/msix/Wintodo-x64.msix`: 57,082,508 bytes; SHA-256
  `BFB9A85D4FADF17734D7217D4A7F0D5C73072599C44300EA1FD045DA27D6C0EE`.
  `Wintodo-x64.details.json` records current metadata. Earlier ARM64 and
  separately named test-signed packages remain unchanged.
- All synthetic input and captures stayed in WintodoTest. No host app launch,
  install or certificate trust change. New MSIX runtime/install gate remains
  unclaimed; M9 and M10 production/clean-install rows stay open.
- Existing list-icon gate `list-icon/run-20261018-210145` passes 14/14, including
  title editing, emoji picker, persisted icon/count, placeholder hover/focus,
  Light/Dark and narrow/detail-open cases. No original assertion was relaxed.

## Placeholder dot — Owner-selected optical lowering         2026-10-19

**Gate:** PASS — header 14/14 in WintodoTest, Debug warnings-as-errors build
clean, 307/307 tests pass; Release Native AOT signed MSIX builds successfully
with warnings as errors and verifies Valid. No new MSIX runtime claim.

- Owner still perceived the dot as high while confirming emoji placement was
  good. Lowered only `ListIconPlaceholder` by 2 px using a 4 px top margin in
  its centered slot. The emoji, hover affordance and title retain their positions;
  16 px dot size and 44 px picker target remain unchanged.
- This is an explicit optical preference beyond geometric ink centering.
  Updated DESIGN and the focused pixel gate: fixed glyphs retain the 1 px
  center check; the dot now targets a 2 px lower ink center, with 1 px tolerance.
  This changes the assertion to the owner's requested position, rather than
  claiming the previous centered-dot criterion still passes.
- `header-alignment/run-20261018-210942` passes 14/14. Dot centers are 2.5 px
  below lowercase `adwawd` and 2 px below `Project gy` in Light and Dark.
  Returned dark new-list capture reviewed. WinUI diff review finds no issues
  above informational; emoji XAML is untouched. Prior list-icon gate 14/14
  remains preceding evidence and was not rerun for this dot-only change.
- Rebuilt exact requested filename `publish/msix/Wintodo-x64.msix` as version
  `1.0.0.3`, signed with the same developer certificate. Size 57,082,571 bytes;
  SHA-256 `A3499B9F92FA9D1DA440C6D746485F2F4C8888C8E81A84850EFECB3468A46CD7`.
  Current metadata is in `Wintodo-x64.details.json`. No host install, launch,
  input automation, certificate change or ARM64 rebuild. M9/M10 open rows remain.

## IMP — Onboarding and Microsoft To Do import          2026-10-19

**Gate:** AUTOMATED PASS — import gate 11/11 in WintodoTest
(`import/run-20261019-140532`, strict UIA invoke, no retries or keyboard
fallback); M8 17/17 (`m8/run-20261019-140802`) and Tasks settings 9/9
(`settings/run-20261018-221730`) after the shell/Settings changes. Debug and
Release x64 warnings-as-errors builds clean; Native AOT x64 publish clean with
no trim/AOT warnings (binary 20.11 MB). Unit tests 364/364 (Core 211, Data 95,
Sync 10, Import 48), zero failed/skipped. WinUI code review: no findings above
informational after the fixes below. **Open:** IMP-00 Entra app registration
and the owner's manual gate with real personal and work accounts.

**Shipped** (ADR-016, IMPORT-PLAN IMP-01…IMP-07)
- `Wintodo.Import`: read-only Graph client (paging, `$expand` with automatic
  fallback, 429/503 retry honouring `Retry-After`), reader, mapper, recurrence/
  date/HTML converters, recorded fixture `tests/fixtures/mstodo/basic`.
- Core `ImportSnapshot`/`IImportStore`/`ImportIdentity`; migration
  `004_import_links.sql`; store preview (rolled-back transaction), apply,
  `RemoveImportRunAsync`, dedupe by link → fingerprint → list name.
- App: `OAuth2Manager` sign-in with a loopback redirect (token kept in memory
  only), import wizard, first-run Welcome surface, Settings → Data card,
  launch flags `--show-onboarding` and Debug-only `--import-fixture <dir>`.
- Debug-only `--trace-file <path>` writes the app's trace lines for UI gates;
  the import gate uses it.

**Fixed while closing the gate**
- Reopening the wizard in one session failed: the second "Import from To Do"
  press threw inside the command (WinRT swallowed it; UIA reported "no invoke
  pattern" and the button stayed drawn as pressed). The x:Load-discarded
  `ImportWizardView` still observed the shared view model's collections, and
  `Reset()` cleared them. The view now stops its bindings and drops its
  repeaters' `ItemsSource` on `Unloaded`. A trace from `--trace-file` showed
  the command running without the wizard opening. The earlier note calling
  this harness-only was wrong; the gate's retry helper and keyboard fallback
  were removed, not kept as workarounds.
- Code review: the loopback listener no longer stops on a malformed, stalled
  (10 s) or state-mismatched request, so a local process cannot end sign-in
  early; HTTP timeouts are reported as offline instead of as a cancel;
  `GraphTodoClient` takes `IClock` for `Retry-After` dates (new unit test).
  The snapshot was already moved off the UI thread earlier in this package.
- `dotnet sln add` had added `Any CPU`/`x86` solution platforms and mapped
  every project to `Any CPU`; the solution now only adds the two new projects
  with x64/ARM64 mappings, like the rest.

**Deviations from IMPORT-PLAN**
- Redirect: loopback `http://localhost` listener chosen over a protocol
  redirect (decision 4); no manifest change.
- Welcome cards use a `SettingsCard` with an action button; a clickable card
  exposed no Invoke pattern.
- §7.3 idempotency suite lives in `Wintodo.Import.Tests/ImportStoreTests.cs`
  (it needs the fixture reader), not in `Wintodo.Data.Tests`.
- §7.4 "offline error" is not a UI gate row; offline/timeout mapping is
  covered by code review and unit tests of the client only.
- Other as-built details (counts, step change detection, possible duplicates)
  are in IMPORT-PLAN §10.

**Known issues carried forward**
- Not verified against live Graph: emoji-prefixed list names, `$expand` on
  list tasks, raw attachment `$value`, and the `OAuth2Manager` token exchange.
  All need the Entra client id in `MicrosoftTodoRegistration.ClientId` and the
  owner's manual gate.
- No ARM64 runtime or AOT runtime gate claimed for the import surfaces.

## IMP-00 — Entra app registration          2026-10-19

**Gate:** registration verified through Microsoft Graph; Debug/Release x64
warnings-as-errors builds clean, 364/364 unit tests. No UI gate rerun: the
client id is read only by the real connector, which the fixture-driven import
gate does not use. Live sign-in remains the owner's manual gate.

- On the owner's request and with their sign-in to the owner's Entra
  directory, created app "Wintodo" via the Azure CLI and Graph: client id
  `038f1bee-9c89-4f6c-9061-15210945a03e` (public by design), audience
  `AzureADandPersonalMicrosoftAccount`, v2 access tokens, public-client
  redirect `http://localhost`, delegated Graph `Tasks.Read` + `User.Read`
  (both user-consentable). No secret or certificate. Not publisher-verified,
  so consent shows "unverified"; some work tenants may need admin consent.
- `MicrosoftTodoRegistration.ClientId` now holds the client id.
