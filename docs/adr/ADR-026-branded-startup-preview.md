# ADR-026 — Try the Glide logo during startup

- **Status:** proposed; Debug and private DEV trial only
- **Date:** 2026-11-25
- **Supersedes:** nothing
- **Owner request:** investigate the blank startup and try the logo-to-app transition seen in Microsoft To Do. The owner has not selected this behavior for the Store.

## Context

DEV .249 restores the window before showing it, but the owner still sees a blank surface before the list appears. The package already declares the Glide splash artwork. [WinUI desktop does not provide UWP's automatic splash](https://github.com/microsoft/microsoft-ui-xaml/issues/4055). On .251 an app-owned logo alone still left plain window frames on screen in the [signed VM recordings](../PERF.md#2026-11-25--what-the-installed-app-actually-paints-during-launch).

## Proposed trial

Center the existing Glide artwork on a theme-aware surface in the real, correctly placed main window. Keep the native caption buttons usable. Reveal the initial view as soon as it is ready, whether that is Welcome, the user's list, or an activation target. Do not wait for network sync or delay the reveal to make the logo visible. There is no separate splash window, spinner, minimum display time, or new timer.

Following the owner's review of DEV .253's abrupt handoff, fade the logo and its background together over the ready view for 240 ms. The shell accepts input from the start of the fade. With Windows animations disabled, or when there is no visible logo to transition from, reveal the view immediately.

Respect Light, Dark and High Contrast. The logo must not receive keyboard focus or obscure an error or recovery dialog. Headless activations remain headless, and sharing keeps its own window.

To cover the native window's empty paint as well as the XAML load, try withholding the main window from desktop composition until WinUI commits a usable frame. Windows can continue rendering and the app can continue loading. The cost is that Windows may skip its normal window-open animation. Once the shell is ready, missing compositor notifications must not leave an otherwise usable app hidden.

## Review and rollback

Review captured first-visible frames and the time to usable content in the signed private preview, including saved normal and maximized launches. The owner will decide whether the transition looks smooth enough to adopt. Until then, Store and other non-preview Release builds keep showing the shell first. Removing the trial restores that path without changing any stored data. The [ADR-008 performance budgets](ADR-008-perf-budget-rebaseline.md) still apply.
