# Performance log

Every row is a real measurement from `scripts/Measure-Perf.ps1`. No estimates.
Budgets live in `PLAN.md` §7, as re-baselined by
[ADR-008](adr/ADR-008-perf-budget-rebaseline.md).

| Date | Milestone | Build | Tasks in DB | Cold start | Warm start | Cold app work | Warm app work | WS @2s | WS @30s | WS @10min | List query | Search |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 2026-07-30 | M3 | FD | 5,000 / 20 lists | 1,690 ms | 1,020 ms | not recorded | not recorded | 128.22 MB | 203.37 MB | deferred | 0.961 ms | n/a (M5) |
| 2026-07-31 | M4 | FD | 5,000 / 20 lists | 1429 ms | 909 ms | not recorded | not recorded | 201.43 MB | 205.02 MB | 199.64 MB | 0.961 ms | n/a (M5) |
| 2026-08-04 | M9 | FD | 0 / 1 list | 2,642 ms | 1,968 ms | not recorded | not recorded | 181.15 MB | 184.42 MB | not run | 1.893 ms | 2.309 ms |
| 2026-08-04 | M9 | FD | 5,000 / 20 lists | 1,913 ms | 2,544 ms | not recorded | not recorded | 181.15 MB | 218.95 MB | not run | 1.893 ms | 2.309 ms |
| 2026-08-04 | M9 | FD | 20,000 / 20 lists | 2,115 ms | 2,558 ms | not recorded | not recorded | 199.96 MB | 224.68 MB | not run | 1.893 ms | 2.309 ms |
| 2026-08-04 | M9 | AOT | 0 / 1 list | 2,509 ms | 1,486 ms | not recorded | not recorded | 144.21 MB | 142.70 MB | not run | 1.893 ms | 2.309 ms |
| 2026-08-04 | M9 | AOT | 5,000 / 20 lists | 1,809 ms | 1,306 ms | not recorded | not recorded | 142.19 MB | 175.88 MB | not run | 1.893 ms | 2.309 ms |
| 2026-08-04 | M9 | AOT | 20,000 / 20 lists | 2,224 ms | 1,993 ms | not recorded | not recorded | 160.91 MB | 180.47 MB | not run | 1.893 ms | 2.309 ms |

**Build** is `FD` (framework-dependent) or `AOT`.
**WS** is `Process.WorkingSet64` in MB.
**App work** is first rendered frame → first interactive frame, the app-owned
segment of startup. The harness started emitting it after the 2026-08-04 M9
matrix; earlier rows read "not recorded" rather than being back-filled from a
different run.

Any row that misses a budget needs a note below it explaining the cause and the
fix, or an ADR relaxing the budget.

### M3 acceptance note

The owner accepted a relaxed M3 performance gate to close the core-loop
milestone. The 30-second working set exceeds the framework-dependent budget and
the 10-minute sample was deferred. Startup hydration was moved behind the first
render after these baseline timings; M4 must remeasure startup and memory before
adding its row.

The 10,000-task scroll probe measured 107.18-119.64 fps with zero callback
allocations. Repeated runs reported zero or one frame above 25 ms; the isolated
hitch occurred while the external UI automation driver was issuing wheel
commands and is accepted for M3.

### M4 acceptance note

The framework-dependent build still misses the §7 launch and loaded-working-set
budgets. Cold start was 1,429 ms against 900 ms, warm start was 909 ms against
400 ms, and loaded working set remained between 199.64 MB and 205.02 MB against
120 MB. Warm start and loaded working set also exceed the current hard-fail
lines.

This is not a budget relaxation. Working set decreased from 201.43 MB at two
seconds to 199.64 MB at ten minutes, and private memory decreased from 127.50 MB
to 124.33 MB, so the idle sample does not indicate monotonic growth. The
10,000-task scroll gate passed at 119.86 fps with zero dropped frames and zero
per-frame allocations. Startup/task hydration and framework shared-page costs
remain M9 profiling work before the Native AOT gate; no ADR relaxes the budget.

### M9 gate result

M9 is **not complete**, but only one gate is now outstanding, and it is
outstanding by choice rather than by failure.

The matrix above was measured against the originally locked §7 budgets and
missed them by 1.7×–5.2×. A startup stage trace attributed 76–78% of startup to
reaching the first rendered frame, most of it inside first-use XAML runtime and
window construction — cost the app cannot move. Safe startup overlap work was
taken first: Windows integration now finishes after the first interactive
task-list frame, window placement loads alongside shell initialization, and the
settings-only StartupTask state no longer blocks the task list. That improved the
timings but not the ratio, because it acts on the 22–24% of startup that follows
the first frame. The owner accepted an explicit re-baseline;
[ADR-008](adr/ADR-008-perf-budget-rebaseline.md) records the evidence, the
revised numbers, and the follow-ups.

Re-evaluated against ADR-008, using the same measurements, every AOT row passes:

| Row | Cold (≤ 3,000 ms) | Warm (≤ 2,300 ms) | WS @30 s | Budget |
|---|---:|---:|---:|---:|
| 0 / 1 list | 2,509 ms ✅ | 1,486 ms ✅ | 142.70 MB ✅ | ≤ 165 MB |
| 5,000 / 20 lists | 1,809 ms ✅ | 1,306 ms ✅ | 175.88 MB ✅ | ≤ 200 MB |
| 20,000 / 20 lists | 2,224 ms ✅ | 1,993 ms ✅ | 180.47 MB ✅ | ≤ 205 MB |

App-owned startup work — first rendered frame to first interactive frame —
measured 585.6 ms cold against a 700 ms budget and 423.4 ms warm against 550 ms.
Both pass, but both come from the focused profile runs
(`tests/ui/artifacts/perf/profile-aot-20260803-231142` and
`profile-aot-warm-20260803-231204`), not from a matrix row: the harness only
started emitting these columns after the matrix was measured, which is why the
matrix rows above read "not recorded". A matrix run will fill them.

The other gates pass on measurements already recorded: 1.893 ms maximum
list-query time over 10,000 tasks, 2.309 ms search, and an AOT scroll probe at
119.37 fps with zero dropped frames and zero callback allocations. The x64
self-contained AOT layout is 188.13 MB and the ARM64 layout is 186.97 MB, both
under the 260 MB installed-size budget.

**Outstanding: the +10-minute idle sample.** It has never been measured on any
build. It is the leak gate — the rule is that the +10-minute working set must
not exceed the +30 s sample by more than 10 MB, and must not grow monotonically
across all three samples. An AOT-only run of that sample was started and stopped
partway on 2026-08-04 at the owner's request; it is deferred, not waived, and M9
does not close until it is measured. Command:

```powershell
pwsh -File scripts/Measure-Perf.ps1 -Milestone M9 -Builds AOT -SkipBuild
```

That run takes roughly 35 minutes, most of it idle waiting, and it also fills in
the two app-work columns for every AOT row.

### ARM64 Native AOT

ARM64 publishing was unblocked on 2026-08-04 by installing the Visual Studio
C++ ARM64 build tools with the owner's approval. `win-arm64` publishes clean
under `-warnaserror`: a 12.61 MB executable and a 186.97 MB layout.

There are no ARM64 runtime numbers and there will not be any from this
machine — the reference host is x64 (i7-11370H). ARM64 cold start, working set,
and the M8 UI suite need ARM64 hardware or a validated emulation baseline. M9
can therefore close on ARM64 *publishing* only; ARM64 *performance* is untested
and must not be reported otherwise.

## M1 data-query gate

Measured by
`SmartListPerformanceTests.TenThousandTasks_EachSmartListQueryCompletesWithinFiveMilliseconds`
on 2026-07-30. The x64 Debug store was warm, contained 10,000 tasks, returned
up to 100 materialized tasks per query, and recorded the best of seven runs.
The 5 ms assertion includes opening the pooled read connection and mapping the
result rows.

| Smart list | My Day | Important | Planned | All | Completed |
|---|---:|---:|---:|---:|---:|
| Query time | 0.689 ms | 0.961 ms | 0.660 ms | 0.634 ms | 0.904 ms |

## 2026-10-10 — M9 idle run blocked before runtime measurements

The requested full AOT command stopped in data-access setup because the old
VSTest filter selected zero tests under MTP. The harness was repaired and its
seeding/timing parsing verified. No new matrix row exists for this attempt.
Historical rows above are retained unchanged.

The host was plugged in and matched ADR-008's CPU, but repeated CPU samples
showed 76–100% use with Zed, Rust compilation, ingest tests and Windows Search
active. An idle window was requested but not established. For **each** of
0 / 5,000 / 20,000 tasks, startup, +2 s, +30 s and +10 min working set are
**not measured** in this attempt; scroll is **not measured**. Budget gating
and leak diagnosis are pending the full idle run. No sample is passed or
waived and no budget is relaxed. ARM64 runtime perf is **not measured** on x64.

A separate AOT startup check exposed an unsupported ItemsRepeater vector and
was fixed; the published x64 M8 rerun passed 14/14 automated checks. The
republished x64 layout is 220.17 MB (260 MB size ceiling), with a 16.69 MB
executable. These are size/build checks, not replacement memory measurements.


### UX-02 unit-test timing checks — 2026-10-10

These are Debug x64 unit-test measurements, separate from the app performance
matrix above. The existing smart-list budget stays at 5 ms.

| Run | 10,000-task query | Best warm time | Result |
|---|---|---|---|
| Initial full suite during build | My Day | 5.743 ms | FAIL |
| Focused rerun during build | Planned | 6.506 ms | FAIL |
| Full suite during UI gate | My Day | 5.811 ms | FAIL |
| Full suite without build/UI | All five queries | within 5 ms | PASS, 130/130 suite |
| Post-composer-fix suite during UI gate | Completed | 5.963 ms | FAIL |
| Final full suite without UI | My Day | 5.041 ms | FAIL |

Failures varied across query types while the query implementation was unchanged.
No budget adjustment or query-performance claim is made by UX-02.

### UX-07 baseline — 2026-10-10

Measured before product edits at commit 680ada9. The existing Measure-Perf
harness ran with -Builds FD -TaskCounts 5000 -SkipBuild -SkipTenMinuteSample;
the short-run results are recorded here because that switch disables its main
table append. Native AOT runtime and the ten-minute sample remain deferred.

| Build | Tasks / lists | Cold interactive | Warm interactive | Cold app work | Warm app work | WS +2s | WS +30s | Query max | Search | 10k scroll | Dropped | Callback bytes |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| FD Release | 5,000 / 20 | 2517.653 ms | 1780.171 ms | 1273.081 ms | 848.551 ms | 212.62 MB | 239.12 MB | 1.346 ms | 1.977 ms | 99.584 fps | 0 | 0 |

Evidence: tests/ui/artifacts/perf/20261010-163503/summary.json.
FD startup/memory are recorded, not AOT-gated. Query/search and scroll pass.

A separate UI Automation round-trip measurement used Debug x64, 200 tasks
across 20 lists: list switches 387.712 ms and 419.613 ms; completing the first
row through its checkbox to the Completed header 899.328 ms. Timings include
winapp command startup, UIA dispatch and waits; they are not isolated render
latency or an AOT performance claim. Evidence:
tests/ui/artifacts/ux07-baseline-20261010-163907/timings.json. Initial Release
interaction attempts failed to reach detail/completed state and were discarded;
Debug before/after uses the same scratch fixture and gesture.

### UX-07 after — 2026-10-10

| Build | Tasks / lists | Cold interactive | Warm interactive | Cold app work | Warm app work | WS +2s | WS +30s | Query max | Search | 10k scroll | Dropped | Callback bytes |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| FD Release | 5,000 / 20 | 2820.088 ms | 2486.231 ms | 1276.309 ms | 1299.840 ms | 214.26 MB | 238.46 MB | 1.071 ms | 2.550 ms | 99.559 fps | 0 | 0 |

Evidence: tests/ui/artifacts/perf/20261010-172322/summary.json. Same short-run
arguments as baseline. Queries/search and scroll meet PLAN §7. Working set is
0.66 MB lower at +30 s. FD total and warm startup work are slower in this run;
FD startup is not gated per ADR-008 and this is not an AOT performance claim.
Native AOT and +10 minute samples remain under the owner's existing M9 deferral.
Initial after attempts aborted at the observable CollectionViewSource ABI;
they are not measurements. An explicit source-generated IObservableVector
fixed Release startup before these rows were taken.

Matched Debug interaction round trips (same 200 tasks / 20 lists and checkbox
gesture): list switches 454.109 ms and 466.064 ms; completion to Completed header
830.239 ms. Evidence: tests/ui/artifacts/ux07-after-20261010-173834/timings.json.
Compared with baseline, completion decreased by 69.089 ms; list round trips
increased by 66.397/46.451 ms. CLI/UIA overhead is included; no isolated UI
latency or overall speedup claim. The correctness gate separately proves
middle-of-list selection, focus and one-row scroll tolerance.

### UX-08 completion motion — 2026-10-10

Debug x64, 200 tasks / 20 lists, checkbox click to Completed header. These are
CLI/UI Automation round trips, not render latency or Native AOT measurements.
The strict UX-08 no-regression row remains **FAIL / open**; no budget changed.

| Comparison | Before | After | Result |
|---|---|---|---|
| UX-07 historical single sample vs initial UX-08 (M8 also running) | 830.239 ms | 929.242 ms | FAIL |
| UX-07 historical single sample vs isolated UX-08 | 830.239 ms | 905.278 ms | FAIL |
| Fresh matched five-run median (UX-07 d6038f2 vs UX-08) | 1071.996 ms | 1074.956 ms | +2.960 ms, strict acceptance remains open |
| Fresh matched five-run mean | 1067.830 ms | 1097.890 ms | +30.060 ms |

Fresh baseline samples: 1106.208, 1071.996, 1046.719, 1085.724, 1028.504 ms.
After samples: 1182.320, 1118.564, 1042.619, 1070.993, 1074.956 ms.
Fresh comparisons add identical focus before the timed click, then expand,
uncomplete and repeat five times. All fixtures are fresh scratch databases.
Baseline source was built in an isolated worktree without changing product code.
Evidence: tests/ui/artifacts/ux08-baseline-timings-4/timings.json and
ux08-after-timings/timings.json; harness ux08-measure-completion.ps1 in the same
artifacts directory. Earlier aborted baseline runs (collapsed header / lost
foreground focus) are not timing measurements. A subsequent screen-capture
attempt measured 1280.704 ms and failed to start recording; it is not a passed gate.
No new AOT or memory claim; M9 remains owner-deferred.

### UX-08 closure: completion placement and reduced motion — 2026-10-10

**PASS.** Matched Debug x64 UIA completion measurements at UX-07 d6038f2 and the
UX-08 closure build use 200 tasks / 20 lists, an expanded Completed section and
15 repetitions after one excluded warm-up. Measure from a prewarmed UIA toggle
until the realized task is below Completed; allow transitions to settle between
completion/uncompletion. CLI process startup is outside these timings. Both mean
and median must not increase; no tolerance or motion-duration change was applied.

| Run | Median placement | Mean placement | Result |
|---|---|---|---|
| UX-07 d6038f2 baseline | 338.447 ms | 339.532 ms | Baseline |
| UX-08 fixed build, investigation | 336.355 ms | 335.017 ms | PASS |
| UX-08 final motion gate | 333.307 ms | 336.008 ms | PASS |
| Animation effects off, 3 samples | 67.188 ms | 74.277 ms | PASS; all below 250 ms |

Reduced-motion samples: 62.785, 92.858, 67.188 ms. State feedback arrives in the
same observation as placement (61.348, 91.813, 66.187 ms). Windows setting read-back
is original=1, restored=1. A live setting query removed the stale cached flag that
kept the 250 ms completion hold after Animation effects was turned off. Initial
owner-authorized trial failed at 352.070 ms and restored its setting; it remains
failed evidence, followed by the fix and passing runs.

Evidence: tests/ui/artifacts/ux08-closure-before/timings.json,
ux08-closure-after/timings.json and completion-motion/run-20261010-190628/
placement-timings.json, reduced-motion-timing.json, animation-setting.json.
Reusable harnesses: tests/ui/measure-completion.ps1 and run-completion-measure.ps1.
The baseline was built in an isolated d6038f2 worktree; no baseline product edits.

Historical CLI/header-only failures above remain recorded. The final diagnostic
is 877.751 ms against the historical 830.239 ms, but that comparison includes CLI
startup and stops on header existence, not completed row placement. Its few-ms
increase was not evidence of a render regression. The repeatable matched placement
check replaces that unsuitable acceptance proxy while preserving strict mean and
median comparisons. No AOT/memory or statistical speedup claim; M9 remains deferred.

Final artifact after viewport/hit-target fixes:
completion-motion/run-20261010-192117. Median placement 331.759 ms, mean 335.079 ms;
both below baseline 338.447/339.532 ms, PASS. Reduced-motion placements 63.434,
88.270, 67.703 ms (median 67.703, mean 73.135), setting original=1/restored=1, PASS.
CLI diagnostic 882.989 ms is retained separately. Final motion gate 7/7, M8 17/17
and stability 3/3; 10 px viewport anchor shift versus one-row tolerance. Native
row transitions remain suppressed when preserving selected-task focus to avoid
viewport movement toward a virtualized Completed row. The checkbox stays 32x32
while its decorative circle springs. No AOT/memory measurement was made.
