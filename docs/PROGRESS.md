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
