# Open at the saved size and avoid unnecessary work when minimized

Signed x64 DEV `1.0.1.249` (`f94416fa`) opened at the saved size in **12/12** normal and maximized restarts, without an observed resize after the window became visible. The one local task became observable in a median **620 ms** (range **566–1,090 ms**), versus **801 ms** across four DEV `.245` launches. See the [candidate measurements](../PERF.md#2026-11-25--first-visible-placement-and-task-row-after-the-startup-fix) and the [baseline diagnosis](../PERF.md#2026-11-24--startup-and-minimized-idle-on-signed-dev). These are returning-user VM launches with warm OS caches.

## Why the old preview appeared to open in stages

The preview activated an empty window at the default 1180 × 820 DIPs and waited for a rendering callback before starting SQLite. It restored the normal bounds only after both the placement settings and the shell had loaded. In four fresh VM processes, the default window was visible for **315–375 ms** before it changed size. The task row became observable **61–94 ms** after that change.

Maximized sessions had a second problem. The code deferred maximizing until after Windows integration and launch routing. In the signed AOT binary, the restore method actually read the presenter and returned without calling `Maximize`, even for a known saved value of `true`. Closing the app also wrote `false` unconditionally. The missing calls match the [CsWinRT presenter-cast issue](https://github.com/microsoft/CsWinRT/issues/1930). A Debug launch cannot establish that maximization works in the signed app.

## How startup now works

1. [Startup](../../src/Wintodo.App/App.xaml.cs) opens and validates SQLite while constructing the window. After validation, one [settings query](../../src/Wintodo.App/Services/ShellStateService.cs) reads the saved position, size, maximized flag, and app theme. Interrupted-import recovery can continue while the shell is visible; user commands wait until it is safe to access the data.
2. The [window](../../src/Wintodo.App/MainWindow.xaml.cs) applies the saved theme, attaches the shell, and sets the normal bounds **before first show**. It uses the destination display's scale, fits the rectangle within its work area, and falls back to the default size when no usable placement exists. For a maximized session it maximizes and activates in the same UI turn. It accesses the overlapped presenter through an explicit WinRT projection, retains the normal bounds for Restore Down, and saves the live placement before close.
3. The [shell](../../src/Wintodo.App/Pages/ShellPage.Lifecycle.cs) reads local preferences and list data in batches, then loads the saved selection alongside local sync-account initialization. It doesn't present an initial My Day title or an empty-list message before the selection is known. The selected list, including any jump-list or notification target, is ready before navigation and search accept input. A Quick Add launch focuses the composer after it is enabled. The selected rows do not have to wait for the reminder, badge, and jump-list refresh to finish.

The [signed startup gate](../../tests/ui/run-startup-placement-gate.ps1) creates a task and saves a distinct normal rectangle through an ordinary close. On reopen it checks the first observed visible window, any subsequent bounds changes, and the exact onscreen row. It also checks maximized close/reopen and the Restore Down rectangle. This is native window and accessibility evidence, not a timestamp for pixels on screen or for the first accepted keystroke. [ADR-008](../adr/ADR-008-perf-budget-rebaseline.md) still defines the performance budgets.

### The blank interval after the window appeared

The [guest desktop recordings](../PERF.md#2026-11-25--what-the-installed-app-actually-paints-during-launch) showed that restoring placement did not fix the first paint. Windows could show the empty native client before WinUI had drawn anything. In the [private preview trial](../adr/ADR-026-branded-startup-preview.md), the window decodes the local Glide artwork while SQLite opens, applies the saved theme and bounds, and lets Windows compose a branded frame. The shell then takes over when the local view is ready. There is no fixed splash duration. On a cold first install the desktop can still remain visible before the app has a window; the saved placement and theme cannot be known until SQLite has opened.

## What happens when minimized

| Task Manager status | Meaning |
|---|---|
| Yellow pause | The group contains a suspended UWP process. Windows stops scheduling its threads and can reclaim its memory. A packaged [WinUI 3 desktop app does not have this UWP lifecycle](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/applifecycle/applifecycle). |
| Green leaf | The group contains a process in [Efficiency Mode](https://devblogs.microsoft.com/performance-diagnostics/reduce-process-interference-with-task-manager-efficiency-mode/). Lower CPU scheduling priority and EcoQoS give foreground apps priority and favor efficient execution. Work still runs and may take longer; a smaller working set is not guaranteed. |

Windows [already assigns a lower quality of service](https://learn.microsoft.com/en-us/windows/win32/procthread/quality-of-service/) to minimized or fully obscured windows under its normal policy. Wintodo leaves that policy in place; its changes control when the app needs to do work.

| App state | Sync and date refresh in the development source |
|---|---|
| No CalDAV account | No startup sync attempt or 15-minute sync timer. Compared with the baseline, this removes four no-op timer callbacks per hour that the app stays open. |
| Account connected | Connecting arms the 15-minute timer; disconnecting removes it. A minimized connected app can still sync, so remote changes need not wait until it is visible again. |
| Window minimized | The local-midnight display timer stops. Restoring the window checks whether the date changed, refreshes My Day, row labels and counts if needed, and rearms the timer. Merely switching focus to another window does not stop it. |

Scheduled reminders remain with Windows; minimizing doesn't suspend an edit, import or sync. The changes do not request Efficiency Mode or force memory out of the process. Wintodo need not show either Task Manager icon to benefit from fewer callbacks.

In the baseline's 30-second local-only minimized sample, process counters showed **no measurable CPU time or process I/O**. The sample didn't cross midnight or the 15-minute timer and didn't measure energy. The four eliminated callbacks are a property of the old schedule, not a measured battery or CPU saving.

## Questions for further measurement

Measure a connected account across multiple 15-minute intervals before deciding whether to defer more work, such as refreshing the visible list after a background sync. If profiling shows substantial background CPU use, consider [EcoQoS for that work](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-setprocessinformation), and measure sync completion and responsiveness on restore. Assess battery use on physical hardware. If memory is the concern, profile a populated list with photos and the cost of decoding them again before changing what the app retains.
