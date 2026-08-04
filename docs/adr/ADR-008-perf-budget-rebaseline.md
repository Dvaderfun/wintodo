# ADR-008 — M9 performance budgets re-baselined to the measured WinUI 3 floor

- **Status:** accepted
- **Date:** 2026-08-04
- **Supersedes:** the `PLAN.md` §7 startup and working-set rows as originally locked
- **Owner acknowledgement:** the repository owner reviewed the measured gap and
  chose an explicit ADR re-baseline over further optimisation against the
  original numbers.

## Context

The original §7 budgets were locked before any WinUI 3 code existed. They were
chosen as targets, not derived from a measurement of the framework the app was
committed to. The M9 Native AOT gate is the first point at which the real cost
of the shipping configuration — Native AOT, self-contained .NET, self-contained
Windows App SDK — could be measured, and the result misses the locked numbers by
a margin that app-side work cannot close.

Measured on the reference machine (below), x64 Native AOT, best of the M9 matrix:

| Metric | Locked budget | Measured | Ratio |
|---|---:|---:|---:|
| Cold start → first interactive frame | 500 ms | 1,809 ms | 3.6× |
| Warm start → first interactive frame | 250 ms | 1,306 ms | 5.2× |
| Idle working set, empty DB (+2 s) | 65 MB | 144.21 MB | 2.2× |
| Idle working set, 5,000 tasks (+2 s) | 85 MB | 142.19 MB | 1.7× |

The startup stage trace explains where the time goes. Two AOT profile runs,
stamped from the top of `App.OnLaunched`
(`tests/ui/artifacts/perf/profile-aot-20260803-231142` and
`profile-aot-warm-20260803-231204`):

| Stage | Cold | Warm |
|---|---:|---:|
| `notifications` (registration complete) | 26.2 ms | 18.3 ms |
| `window-created` (`new MainWindow(...)` returned) | 1,522.9 ms | 1,109.9 ms |
| `first-frame` | 1,867.3 ms | 1,480.1 ms |
| `store` (SQLite open + initialise) | 1,973.8 ms | 1,571.6 ms |
| `window` / first interactive frame | 2,452.8 ms | 1,903.5 ms |
| `integration` (Windows integration finished) | 2,516.2 ms | 1,970.2 ms |

Everything before `first-frame` — 1,867 ms cold, 1,480 ms warm, 76–78% of the
total — is spent reaching the first rendered pixel. The 1,497 ms between
`notifications` and `window-created` contains a handful of cheap object
constructions — activation arguments, the clock, database path resolution, and
three service constructors that do no I/O — plus `new MainWindow(...)`, which is
the first use of the XAML runtime in the process: loading `Microsoft.UI.Xaml`,
`generic.xaml`, the theme resource dictionaries and the compiled-binding
infrastructure, then building the window and shell tree. That segment alone
exceeds the entire original 500 ms cold budget by 3×, and it completes before any
wintodo data is read.

The M9 work already took every safe reduction available on the app side:
Windows integration now completes after the first interactive frame, window
placement loads in parallel with shell initialisation, selected-navigation state
joins the initial store reads, and the settings-only `StartupTask` activation
path no longer blocks the task list. These moved the measured numbers but left
the ratio essentially unchanged, because they act on the 22–24% of startup that
follows the first frame.

Working set has the same shape. The shipping configuration statically links the
.NET runtime and carries a private Windows App SDK, so there are no shared
framework pages to amortise; 142 MB is what an interactive XAML window costs in
this configuration before wintodo's own data is loaded. The 5,000-task and
empty-database working sets are within 2 MB of each other, which confirms the
cost is fixed, not data-driven.

## Decision

Re-baseline the startup and working-set budgets against the measured floor, and
restructure the startup gate so that it constrains what the app controls.

### 1. Split startup into framework cost and app cost

The gate that matters for regressions is the work between the first rendered
frame and the first interactive frame: store initialisation, shell hydration,
and the initial query. That segment is app-owned, and it is stable across runs
(585.6 ms cold, 423.4 ms warm). It becomes the primary gated number.

Total startup is retained as a ceiling. It catches gross regressions without
flaking on framework and OS-scheduler variance, which is large: cold totals
across the M9 matrix ranged 1,809–2,509 ms with no correlation to dataset size.

### 2. Gate the settled working-set sample, not the +2 s sample

The +2 s sample lands before the first list virtualisation pass and before the
GC heap settles. The +30 s sample is the steady state and is what a user's
machine actually holds. Working-set budgets move to the +30 s sample; the +2 s
sample stays in `docs/PERF.md` as a recorded figure with no gate.

### 3. Compare the 10-minute sample against the settled sample

The original rule — "the 10-minute working set must not exceed launch + 10 MB" —
compares a settled process against a still-initialising one, so it measures
startup rather than a leak. It becomes: the +10-minute sample must not exceed
the +30 s sample by more than 10 MB. The monotonic-growth hard fail
(+2 s < +30 s < +10 min) is unchanged, and remains the real leak detector.

### 4. The framework-dependent column stops being a gate

From M9 onward the shipping build is Native AOT and self-contained. The
framework-dependent build exists for the inner development loop only. Its
numbers stay in `docs/PERF.md` for comparison and are no longer gated.

### 5. Revised §7 numbers

| Metric | Native AOT budget | Hard fail |
|---|---:|---:|
| App startup work, first frame → first interactive frame, cold | ≤ 700 ms | > 1,000 ms |
| App startup work, first frame → first interactive frame, warm | ≤ 550 ms | > 800 ms |
| Cold start → first interactive frame (total) | ≤ 3,000 ms | > 3,500 ms |
| Warm start → first interactive frame (total) | ≤ 2,300 ms | > 2,800 ms |
| Idle working set at +30 s, empty DB | ≤ 165 MB | > 190 MB |
| Idle working set at +30 s, 5,000 tasks / 20 lists | ≤ 200 MB | > 225 MB |
| Idle working set at +30 s, 20,000 tasks / 20 lists | ≤ 205 MB | > 230 MB |
| Working set at +10 min | ≤ +30 s sample + 10 MB | monotonic growth across all three samples |

Headroom over the measured M9 figures is 14–30% on every row: enough that
scheduler noise does not flake the gate, tight enough that a real regression
trips it.

### 6. Unchanged

ADR-008 does not touch any budget that passed. These stay exactly as locked in
§7: list query over 10,000 tasks ≤ 5 ms (measured 1.893 ms), search keystroke to
results ≤ 60 ms (measured 2.309 ms), scrolling 10,000 tasks at 60 fps with zero
per-frame allocations (measured 119.37 fps, zero dropped frames, zero callback
allocations), and installed size ≤ 260 MB self-contained (measured 188.13 MB).
Every rule in §7 "Rules that produce those numbers" also stands unchanged — none
of them was the cause of the miss, and relaxing any of them would make the
revised budgets harder to hold, not easier.

## Reference machine

Budgets are machine-relative. The figures above were measured on:

- CPU: 11th Gen Intel Core i7-11370H @ 3.30 GHz (4 cores / 8 threads)
- RAM: 31.8 GB
- Storage: WD_BLACK SN7100 1 TB NVMe
- OS: Windows 11 Pro Insider Preview 10.0.28020
- Build: x64 Native AOT, self-contained .NET, self-contained Windows App SDK

A materially different machine needs its own baseline before these numbers mean
anything. Re-measure and note the machine when that happens.

## Consequences

- M9's gate becomes achievable and stays meaningful: the app-work rows are tight
  enough to catch a regression in hydration or query work, which is where a
  regression would realistically appear.
- The app is slower to first interactive frame than the original plan promised.
  This is a real product cost and is not hidden: a ~1.9–2.5 s cold start is
  ordinary for a self-contained WinUI 3 desktop app, but it is not the ~0.5 s the
  plan implied. If that becomes unacceptable, the lever is a splash or an
  earlier-rendering shell, not further micro-optimisation of hydration.
- Working set of ~142 MB idle is the price of self-contained deployment. The
  alternative is framework-dependent Windows App SDK, which trades ~40–60 MB of
  private working set for a runtime prerequisite on the user's machine. M10
  should make that trade deliberately rather than inheriting it.

## Follow-ups

1. **Isolate the true framework floor (M10).** Build a bare Native AOT WinUI 3
   window with no wintodo code and measure its first-frame time and working set.
   The attribution in this ADR is by stage trace, not by a controlled baseline;
   a bare-window number would confirm how much of the 1,497 ms pre-first-frame
   cost is the XAML runtime versus the shell tree, and would tell us whether
   trimming the shell is worth anything.
2. **Fix cold-start measurement (M10).** `Measure-Perf.ps1` runs cold and warm as
   back-to-back launches within one row, so the "cold" launch shares an already
   warm OS file cache with whatever preceded it. This shows in the M9 data:
   framework-dependent warm start measured *slower* than cold at both 5,000
   (2,544 ms vs 1,913 ms) and 20,000 tasks (2,558 ms vs 2,115 ms), which is not
   physically meaningful. A genuine cold measurement needs a cleared standby list
   between runs, and each figure needs repetition with a reported median rather
   than a single sample.
3. **Evaluate framework-dependent Windows App SDK at M10** against the working
   set and installed-size trade described above.
