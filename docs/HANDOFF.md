# Session handoff — UX-10 and UX-18 (2026-10-18)

The user hit `Exceeded maximum number of images (50) allowed in the request`
and chose to consider a fresh session. Continue from these edits; do not restart
the investigation or discard the working tree. Avoid loading many separate
screenshots into the conversation; create contact sheets/crops for visual review.

## Current authorization and scope

- User: "UX-10 fix and verify. ADR approved."
- Finish UX-10, including the unfinished row-completion undo, then implement
  UX-18 under accepted `docs/adr/ADR-015-share-target.md`. Approval is recorded
  in commit `a27eed8`; do not ask for ADR approval again.
- UX-16 and UX-17 are skipped for now by the owner. UX-12 is skipped; My Day
  resets at midnight. Completion viewport drift remains a separate parking-lot
  fix; preserve the existing 54 px tolerance and do not claim stability passed.
- No delegation authorized. All UI input/automation/capture belongs in
  WintodoTest via Invoke-VmGate. Never use host UI automation as a fallback.

## Git and validation

- Branch main, origin/main at `259f710` (UX-15 complete and pushed).
- Three local commits ahead: `5a1ec47` undo harness corrections;
  `9280734` UX-18 proposal/review; `a27eed8` ADR-015 acceptance and UX-10 scope.
- Uncommitted: TaskListViewModel.Tasks.cs, Resources.resw, tests/ui/undo.ps1.
  This handoff is also new. Preserve them.
- Current Debug x64 warnings-as-errors build passes (0 warnings/errors), after
  the initial row-completion edit. No unit/UI rerun after that edit yet.
- Prior full unit suite: 288/288 pass. Latest pre-edit undo VM run:
  `tests/ui/artifacts/undo/run-20261018-160833`, 6/7, 0 skips; bulk check
  misses the five-second snackbar after slow intermediate row assertions.
  Log: `tests/ui/artifacts/vm/run-undo-gate-20261018-160625.log`.
- No app/VM/background tool session is running at handoff.

## UX-10 edits and required follow-through

- TaskListViewModel.Tasks.cs: in ToggleTaskCompletionAsync, capture
  TaskUndoFields.Completion before CompleteTaskAsync, then RecordTaskUndo with
  `Undo_TaskCompleted` after the successful write. Reuses existing field restore,
  including recurrence dates, shifted alarms and completion count. Uncompletion
  and detail completion paths currently unchanged.
- Resources.resw: added localized Undo_TaskCompleted = "Completed" at the end.
  DESIGN section 7 now authorizes five-second row-completion feedback, including
  recurring occurrence completion; Ctrl+Z retains history after expiry, detail
  completion stays quiet.
- undo.ps1: bulk importance and due-date checks now use `Undo -Keyboard` after
  their slow row-value assertions. My Day and completion already did so.
  Other cases still test the actual snackbar. Preserve the five-second duration.
- Prior committed harness fixes: expected absence uses wait-for --gone;
  retained older history uses Ctrl+Z; redundant wait before snackbar invoke
  removed. These corrected harness defects, not product undo behavior.
- Next: add meaningful row-completion tests (ordinary and recurring, snackbar
  and Ctrl+Z after expiry, detail quiet). Short-lived snackbar assertions should
  execute in one Windows PowerShell UIA process, as existing measure-completion
  and selection helpers do; independent winapp CLI startups consume the lifetime.
- Review capture/record timing, failures and latest-edit handling. Run build,
  full units, corrected undo VM gate and affected M3/M4/M13/M8 checks. Update
  PROGRESS and UX-PLAN only with actual results. Separate UX-10 commit.

## UX-18 approved implementation plan

- Read ADR-015 for the full accepted behavior. It extends existing M7 text-share
  groundwork, rather than inventing an entirely new activation mechanism.
- Existing Package.appxmanifest declares Text only. App.xaml.cs
  CreateTaskFromShareAsync accepts Text, uses first line as title/rest as note,
  starts full main shell/integration/sync before saving and opens saved task.
- Add WebLink and Core BCL-only title/note mapping: source title for WebLink,
  text fallback, URL fallback; preserve accompanying text and URLs in notes.
  Text-only HTTP(S) URL also appears in note; do not fetch/execute URLs.
- Route ShareTarget before notifications/MainWindow/integration/sync creation.
  Minimal native share window for progress/errors; save once to local default
  Tasks, report completion after commit, dispose store/close/exit share process.
  Keep operation on UI DispatcherQueue. No list picker/general single-instance
  redesign/new dependencies/schema.
- Already-open main window currently RefreshForActivationAsync only refreshes
  dates/integration, not external SQLite rows. Add narrow reload with selection
  preservation and protection of pending editor changes.
- Real Windows share-source fixture and broker-driven VM gate needed; direct
  helper invocation does not prove activation. Check cold/open/repeated shares,
  failures, exact persisted title/note/list and one save per operation.
- Native AOT x64 real-share runtime proof required; ARM64 publish separately,
  runtime unclaimed on x64 VM. Release tests use guest package LocalState;
  any scratch override for broker activation must be DEBUG-only.
- Platform APIs already checked via project-scoped winapp find-api:
  ShareTargetActivatedEventArgs, DataPackageView.GetWebLinkAsync,
  ShareOperation.ReportStarted/ReportDataRetrieved/ReportCompleted/ReportError.
  Windows docs mention UWP CoreWindow lifecycle; WinUI HWND behavior still
  requires real broker/AOT validation. No host automation.

## Instructions and skills

Read AGENTS.md, ARCHITECTURE.md, PROGRESS Current state, DESIGN before XAML,
DATA-MODEL before domain/storage, testing/build/packaging guides as relevant.
CodeGraph exists: use codegraph explore before locating/reading code.
Vendored .agents/skills: winui-design, winui-dev-workflow, winui-ui-testing,
winui-code-review, winui-packaging. Surgical-patch for UX-10, lean-build for
UX-18. Follow the repository's commit conventions. No new skill installs/toolchain
enablement or ADR approval request needed for the authorized work.

## Completion update — 2026-10-18

This handoff's original edits were preserved. UX-10 finished in commit 6cce524:
undo 12/12, M3/M4 + restart, M13 11/11, M8 17/17. UX-18 implemented under
accepted ADR-015: real x64 Native AOT Share gate 10/10 in run-20261018-180938,
final M0/M7/AOT-M13 startup regression passes, 307 units pass and both AOT
architectures publish. Program captures activation before WinUI; share uses
explicit IShareTargetActivatedEventArgs. All UI stayed in WintodoTest; contact
sheets used. Current details and evidence are in PROGRESS/UX-PLAN. Viewport drift,
ARM64 runtime, manual Narrator/toast/reboot and M10 release gates remain open.
