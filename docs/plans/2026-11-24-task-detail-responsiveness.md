# Make task details faster to open, resize and edit

Proposal, 24 November 2026. Applies to the app code in signed x64 DEV
`1.0.1.241`. No product changes are part of this investigation.

Start with opening from the closed state. In the [VM diagnostic](../PERF.md#2026-11-24--task-detail-opening-and-resizing-on-signed-dev),
the correct title was observable after 489 ms on the first open (one sample)
and 306 ms on a typical reopen (median of eight), compared with 63 ms for a
switch while Details remained open. Resizing deserves a separate change and
frame trace: the live editor moved during both test drags, but observing the
final width doesn't establish whether the drag looked smooth.

## Open the task the user picked

Suggestions has a useful property: once an outgoing draft is safe, its existing
surface becomes visible and then the list fills in. Details does more work
before it acknowledges the selection. [OpenDetailAsync](../../src/Wintodo.App/ViewModels/ShellViewModel.Dialogs.cs)
waits for [LoadAsync](../../src/Wintodo.App/ViewModels/TaskDetailViewModel.Persistence.cs)
to flush edits, read the full task and its children in parallel, bind the model,
and build the steps and attachment rows. Only then does the shell request the
editor and show it. First open also constructs the view. On later opens the
view already exists. Switching while it is visible doesn't replay the 200 ms
entrance; that duration isn't a measurement of the wait. The notes debounce
isn't a fixed wait either: leaving a task flushes a pending save immediately.

1. For a clean selection, show the pane and the title, completion and star
   state from the selected row immediately. For a dirty editor, protect the old
   draft first; on save failure keep it visible and recoverable. Keep the new
   header read-only until commands can target the verified new task. Never
   display A's note, steps or reminder beneath B's title.
2. Keep the header stationary and legible when switching. Enable title/note
   editing as soon as the active task can safely receive input. Populate larger
   step and attachment lists separately if traces justify it. Keep the existing
   single editor and defer its first construction; don't move full editor cost
   to application startup. Check construction cost of the calendar, time and
   custom-repeat flyout content; create heavy bodies on first use if that is
   where the first-open time goes.
3. Preserve the current latest-selection checks; cancel or skip superseded
   **reads** if rapid selection builds a backlog. Keep saves in flight. A short
   or absent content fade can be tested separately if the title still takes
   too long to read. The [design](../DESIGN.md#7-motion) currently specifies
   200 ms open and 150 ms close; changing those values is a design choice.

## Make the divider track the pointer

The [sizer](../../src/Wintodo.App/Pages/ShellPage.xaml) changes the real pane
width during a drag. At window widths of 1,100 DIPs and up, this also resizes
the task list. Below that breakpoint the editor overlays the list. When the
list has a visible background photo, [its resize handler](../../src/Wintodo.App/Pages/ShellPage.Theme.cs)
discards the current bitmap and sets a new decode width after differences of
64 physical pixels. [Width persistence](../../src/Wintodo.App/ViewModels/ShellViewModel.Appearance.cs)
already waits 500 ms before writing; it cancels and schedules a new delay on
each width change.

- First retain and stretch the existing photo during the gesture, then decode
  once at the settled size. Keep transient width local to the gesture; persist
  the final value on release or after keyboard resizing settles. Keep the live
  320 DIP minimum, room for the list, and the existing focusable, arrow-key
  operated handle.
- Fix a separate width bug: live resize permits up to 960 DIPs when space
  allows; restoring the saved preference clamps it to 720. Restore the
  preferred width subject to the *current* window constraints.
- Measure live layout before introducing a custom drag handle. If multiple
  expensive updates occur within one display frame, use just the latest width
  for that frame. A divider that moves alone until release would lose the
  useful live preview of wrapped titles and notes; try it only if live layout
  still misses the responsiveness target.

## Reduce friction while editing

- **Keep “Next step” drafts with their task.** On the signed build, unsubmitted
  text in Alpha's field remained in the field after selecting Bravo. The
  [add command](../../src/Wintodo.App/ViewModels/TaskDetailViewModel.Tasks.cs)
  uses the current task UID. Key the draft to its task; capture that UID when
  submitting and apply the asynchronous result only to the matching editor.
  The [file picker already captures its owning task](../../src/Wintodo.App/ViewModels/TaskDetailViewModel.Attachments.cs).
  Returning to Alpha should restore its unsubmitted draft.
- **Use the open editor for an immediate action.** The existing
  [due and reminder shortcuts](../../src/Wintodo.App/Pages/ShellPage.Accelerators.cs)
  request a load even when the correct task is already open. Open the picker
  directly in that case; otherwise verify the requested UID is still active
  before showing it. Keep row focus during keyboard browsing.
- **Give writing room where it helps.** If notes are a frequent workflow,
  let a focused, multiline note use the spare height above the pinned Created /
  Delete footer on tall windows. Keep an empty note compact and the current
  40-DIP action targets; keep a short window scrollable. This is a separate
  usability experiment, not a fix for the resize or opening path.
- A saved note currently updates its row and [rebuilds the list presentation](../../src/Wintodo.App/ViewModels/TaskListViewModel.Tasks.cs).
  Profile dirty switches on a large list before optimizing this. If it is
  material, update only the row when the changed fields cannot affect the
  current filter, group or sort. Keep the full update for affected views.

## How to decide whether the changes worked

Use the current signed build as baseline and the candidate on the same
`WintodoTest` guest, size, scale and animation setting. Measure the first open
after a fresh app launch, reopen, A→B with the pane open, and typing a note
then immediately switching. Run a simple task first, then a long note and
many steps in a large list. Record selection to **first frame showing the
right title**, **first accepted keystroke on the right task**, and full content
ready separately. UI Automation can check the title's value and ownership;
it doesn't measure those paint timings.

For resizing, record presented frame intervals and the distance between the
pointer and visible divider during the same drag in inline and overlay modes,
with photos on and off. Compare median and slow-tail timings on repeat runs.
On a 60 Hz display one frame is 16.7 ms; a proposed first target is a correct
visible title within 100 ms on warm selections and no repeated multi-frame
stalls while dragging. These are trial targets, not existing release gates.
Also verify keyboard resizing and persistence after restart, reduced motion,
rapid A→B→C selection, failed saves, and drafts/shortcuts never affecting
the wrong task.
