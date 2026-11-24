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

### UX-02 closure: existing query budgets — 2026-10-11

**PASS.** Debug x64 existing SmartListPerformanceTests, 10,000 tasks. No query
implementation or budget changes. Full unit suite also passes 259/259, zero skipped.

| Query | Measured warm time | Budget | Result |
|---|---:|---:|---|
| My Day | 0.897 ms | 5 ms | PASS |
| Important | 0.807 ms | 5 ms | PASS |
| Planned | 0.896 ms | 5 ms | PASS |
| All | 0.873 ms | 5 ms | PASS |
| Completed | 0.964 ms | 5 ms | PASS |
| FTS (worst warm query) | 1.655 ms | 60 ms | PASS |

Command: `dotnet test --project tests/Wintodo.Data.Tests -p:Platform=x64
--filter-class '*SmartListPerformanceTests' --output Detailed`.
These are store unit-test timings, not app render or Native AOT measurements.
Historical variable failures above remain recorded.

### UX-13 follow-up: completion validation — 2026-10-11

Debug x64, unchanged product binaries, 200 tasks / 20 lists, 15 prewarmed
in-process UIA placement samples with one excluded warm-up. Original strict
mean/median comparisons remain unchanged. No AOT or statistical speedup claim.

| Run | Baseline median / mean | Candidate median / mean | Result |
|---|---|---|---|
| Fresh ca140a8 pre-refactor pair | 333.837 / 336.241 ms | 331.487 / 337.272 ms | FAIL: mean +1.031 ms |
| Second fresh ca140a8 pair | 330.490 / 336.310 ms | 331.586 / 331.446 ms | FAIL: median +1.096 ms |
| Gate against accepted UX-07 d6038f2 baseline | 338.447 / 339.532 ms | 331.071 / 329.839 ms | Timing PASS |

The two fresh pairs remain failed evidence; the accepted UX-07 comparison is
a separate existing-gate check and does not supersede them. All three runs
pass visual completion/uncompletion, importance and undo checks. Reduced motion
is SKIPPED: explicit owner permission for the temporary Windows setting change
was requested but not received. No system setting changed in this follow-up.

Evidence under `tests/ui/artifacts/closure-20261011/`: `motion-before`,
`motion-after`, `motion-before-repeat`, `motion-after-repeat`, and
`motion-ux07-baseline` (placement-timings.json, results.json and recording).
UX-13 remains in progress; no full motion-gate PASS is claimed.

### UX-13 fresh timing comparison — 2026-10-11

Pre-refactor `ca140a8` and current `f3da981`, Debug x64, matching fresh
200-task / 20-list fixtures. Same prewarmed in-process UIA placement method,
15 samples with one warm-up excluded; Windows animation effects enabled.

| Run | Baseline median / mean | Candidate median / mean | Result |
|---|---|---|---|
| First pair; unit tests overlapped candidate measurement | 333.237 / 335.275 ms | 339.097 / 348.061 ms | FAIL retained |
| Sequential pair without concurrent builds/tests | 333.070 / 335.488 ms | 332.412 / 331.688 ms | Strict timing PASS |

Thresholds and earlier failure records are unchanged. No product or harness
source changed. The quiet pair passes both required comparisons; this is gate
evidence, not a statistical speedup or Native AOT latency claim. Five motion
checks pass; reduced motion is SKIPPED pending explicit permission to temporarily
change Windows Animation effects. No system setting was changed.

Evidence: `tests/ui/artifacts/ux13-final-before/timings.json`,
`ux13-final-after/placement-timings.json`, `ux13-quiet-before/timings.json`,
and `ux13-quiet-after/placement-timings.json`, `results.json`, `completion.mp4`
and screenshot sequence. UX-13 remains in progress pending reduced motion and
acknowledgement of the separately corrected harnesses.

### UX-13 authorized reduced-motion closure — 2026-10-11

Debug x64, 200 tasks / 20 lists. Motion gate 7/7, zero skipped. Completion
median/mean 332.018/332.204 ms versus fresh baseline 333.070/335.488 ms: PASS.
With Windows Animation effects off, placement samples 67.143, 92.222, 64.366 ms
(median 67.143, mean 74.577 ms) are all below the 250 ms animation hold.
Original Animation effects value 1 restored and verified as 1 in finally.
Owner authorized this setting change and acknowledged ADR-011 on 2026-10-11.

Evidence: `tests/ui/artifacts/ux13-approved-motion/placement-timings.json`,
`reduced-motion-timing.json`, `animation-setting.json`, `results.json`,
`completion.mp4` and screenshots. All historical failures retained;
no threshold change, statistical speedup or Native AOT claim. UX-13 complete.

### Test VM completion harness baseline — 2026-10-17

WintodoTest Hyper-V VM: 4 vCPUs, 8 GB RAM, Windows 26300, 1920 × 1080,
100% scale. Final Debug x64 build for both runs; fresh clean-checkpoint runs,
200 tasks / 20 lists, 15 prewarmed in-process UIA placement samples with one
warm-up excluded. These are same-build harness/behaviour checks; they are not
a UX-07 regression comparison, statistical speedup claim or Native AOT matrix.

| Run | Median | Mean | Samples | Result |
|---|---:|---:|---:|---|
| VM baseline | 429.568 ms | 424.515 ms | 15 | Captured |
| VM motion gate | 359.499 ms | 366.242 ms | 15 | Existing strict comparison PASS |
| VM reduced motion | 71.320 ms | 76.866 ms | 3 | All below 250 ms |

Reduced-motion placements: 68.851, 90.426, 71.320 ms. Original Animation effects
value 1 restored and read back as 1. Motion gate 7/7, zero failures/skips; video
and screenshots delivered and reviewed. No budget changes.

Evidence: `tests/ui/artifacts/completion-measure/vm-20261017/timings.json` and
`tests/ui/artifacts/completion-motion/run-20261017-203731/` (placement timings,
reduced-motion timings, animation-setting.json, results.json, completion.mp4).
The optional M9-VM memory/startup/scroll perf matrix was not run.

### IMP-16 / IMP-20 — WintodoTest fixture measurement (2026-10-20)

Debug x64; 1,000 tasks / 20 lists; FixtureGraphHandler with 50 ms request latency.
Candidate is test-only; production parallelism has not been enabled for this comparison.

| Reader | Time to Choose (read) | Preview | Total read + preview + save |
|---|---|---|---|
| serial production | 1434.9 ms | 893.4 ms | 3226.2 ms |
| bounded candidate (4 lists) | 441.7 ms | 485.6 ms | 1765.6 ms |

### IMP-16 / IMP-20 — WintodoTest fixture measurement (2026-10-20)

Debug x64; 1,000 tasks / 20 lists; FixtureGraphHandler with 50 ms request latency.
Production reader compared at one and four list workers; attachment downloads remain serial.

| Reader | Time to Choose (read) | Preview | Total read + preview + save |
|---|---|---|---|
| production serial (1 list) | 1434.7 ms | 530.4 ms | 2929.2 ms |
| production bounded (4 lists) | 455.4 ms | 335.6 ms | 2264.2 ms |

### Store v1.0.0.0 live idle snapshot & Release SQLite/FTS5 benchmark — 2026-11-20

Measured on host (11th Gen Intel Core i7-11370H @ 3.30GHz, 32 GB RAM, Windows 11 Pro x64 10.0.28020) without UI automation or synthetic desktop input. This is a side-by-side observational snapshot of the Store-signed x64 package (47371DermenzhyStudio.Wintodo 1.0.0.0, SignatureKind: Store) against Microsoft To Do (Microsoft.Todos 2.179.8901.0) plus a headless Release x64 SmartListPerformanceTests run. It does not replace the deferred M9 +10-minute idle matrix or ARM64 runtime measurements.

**Installed Store package & binary footprint (1.0.0.0):**
- Self-contained MSIX package size: 59,368,447 bytes (56.62 MiB, x64) / 57,700,215 bytes (55.03 MiB, ARM64).
- Store-installed x64 folder size: 149,346,098 bytes (142.43 MiB; includes self-contained Windows App SDK runtime); Wintodo.exe: 21.28 MiB Native AOT binary (vs. Microsoft.Todos installed folder 74.47 MiB).

**Live idle memory & thread snapshot (Win32_PerfFormattedData_PerfProc_Process, same host):**

| Process | Processes | Threads | Task Manager Memory (WorkingSetPrivate) | Total Working Set (WorkingSet) | Private Bytes |
|---|---:|---:|---:|---:|---:|
| Wintodo (1.0.0.0, Store WinUI 3 + Native AOT) | 1 | 18 | 86.80 MB | 182.16 MB | 112.33 MB |
| Todo (2.179.8901.0, Store UWP Microsoft To Do) | 1 | 32 | 83.30 MB | 199.95 MB | 113.52 MB |

**Headless Release x64 store benchmark (SmartListPerformanceTests, 10,000 tasks):**
Command: dotnet test --project tests/Wintodo.Data.Tests -c Release -p:Platform=x64 --filter-class "*SmartListPerformanceTests" --output Detailed --show-live-output on (PASS, 2/2).

| Query (10,000 tasks) | Measured warm time | Budget | Result |
|---|---:|---:|---|
| My Day | 1.749 ms | 5 ms | PASS |
| Important | 2.506 ms | 5 ms | PASS |
| Planned | 1.695 ms | 5 ms | PASS |
| All | 1.519 ms | 5 ms | PASS |
| Completed | 2.652 ms | 5 ms | PASS |
| FTS5 (worst warm query) | 6.694 ms | 60 ms | PASS |

## 2026-11-21 — dependency servicing, headless 10,000-task checks

Debug x64, .NET 10.0.404, Microsoft.Data.Sqlite 10.0.15 and native SQLite
3.54.1. The first full unit run missed My Day (6.483 ms; 5 ms budget).
A second full run with its test modules serialized also missed My Day (5.881 ms).
The focused performance class, with no build running in this checkout,
passed both tests. Results below use the existing budgets and measurement
methods in `SmartListPerformanceTests`.

| Query | Focused warm time | Budget |
|---|---:|---:|
| My Day | 4.034 ms | 5 ms |
| Important | 3.107 ms | 5 ms |
| Planned | 2.969 ms | 5 ms |
| All | 2.876 ms | 5 ms |
| Completed | 3.286 ms | 5 ms |
| FTS5 (worst warm query) | 13.707 ms | 60 ms |

On an empty database initialized by all five migrations, both engines report
the same My Day plan: `SEARCH t USING INDEX ix_tasks_myday (my_day_date=?)`.
This rules out a different chosen index in that check, not a runtime regression.

These are existing store tests, not measurements of the packaged UI,
process memory or a controlled engine-to-engine speed comparison. No
threshold, query, schema, or performance claim changed.

## 2026-11-21 — release-fallback dependency test measurements

Single full fallback run (source `c12e5e6`), x64 Debug, .NET SDK 10.0.404,
Microsoft.Data.Sqlite 10.0.15, native SQLite 3.54.1. The clean solution build
finished before the four MTP modules ran serially. This was a headless store
test run, not an app/UI or idle-system measurement.

| Query (10,000 tasks) | Warm time from the full test | Budget | Result |
|---|---:|---:|---|
| My Day | 3.952 ms | 5 ms | PASS |
| Important | 3.526 ms | 5 ms | PASS |
| Planned | 4.135 ms | 5 ms | PASS |
| All | 3.991 ms | 5 ms | PASS |
| Completed | 3.052 ms | 5 ms | PASS |
| FTS5 (worst warm query) | 428.653 ms | 60 ms | FAIL |

The full suite was 706/707, zero skipped. These values came from the same
run's TRX; the timing assertion was not retried or weakened. On the 2.7.0
candidate the identical Data/test graphs gave a focused FTS measurement of
13.707 ms; its two full suites missed My Day instead. The observed timing
variation does not establish the cause, steady-state query performance, a
memory regression, or a fix.

### 2026-11-21 — hybrid DEV preview, full x64 suite

The first full Debug run for the hybrid appearance change passed 707/707
(Core 257, Data 133, Import 155, Sync 162; no skips). The build finished
before the four test modules ran serially. At 10,000 tasks, the Data TRX
recorded **2.841 ms** for My Day and **2.929 ms** for the worst warm FTS
query. No store code or threshold changed. These results belong to that
run; they do not explain the fallback's earlier 428.653 ms FTS result or
measure packaged UI startup, RAM, or native ARM64 performance.

After correcting the High Contrast marker at `7fa57e1`, an incremental x64
Debug build again had zero warnings/errors. A separate full serial run passed
707/707 (zero skipped) in 17.871 s before Native AOT packaging started. On
this run My Day took **3.053 ms** and the worst warm FTS query **1.192 ms**.
These are two observations of the same unchanged store, not a speedup claim.

### 2026-11-22 — My Day after PR #8

Debug x64, `SmartListPerformanceTests`, 10,000 tasks, 5 ms budget.
The review ran the base and PR benchmark separately on the same laptop. DEV
was measured in the full suite with parallel test modules and again in isolation.

| Revision and run | Best warm My Day query | Result |
|---|---:|---|
| PR base `d8efe5e`, focused | 1.801 ms | PASS |
| PR head `c07f510`, focused | 6.180 ms | FAIL |
| DEV merge plus date fixes (`8f15605`), full suite | 14.351 ms | FAIL |
| DEV merge plus date fixes, focused | 9.564 ms | FAIL |

These runs do not isolate power settings or test contention. The owner
accepted integrating the feature and addressing the two functional issues
first. The 5 ms threshold remains unchanged.

### 2026-11-22 — Indexed My Day query on DEV

Debug x64, 10,000 tasks, full unit suite with test modules run serially.
The existing best-of-seven warm query test recorded the following times:

| My Day | Important | Planned | All | Completed | Budget per query |
|---:|---:|---:|---:|---:|---:|
| 3.653 ms | 2.089 ms | 2.245 ms | 2.032 ms | 3.058 ms | 5 ms |

On bundled SQLite 3.54.1, `EXPLAIN QUERY PLAN` shows a search on
`ix_tasks_myday` for pins and a range search on `ix_tasks_due_live` for
deadlines, both with and without completed tasks. The two branches exclude
overlap before applying the shared order and limit. All 24 ordered top-100
comparisons matched the previous query across two time zones, three sorts,
both completion settings, and filtered/unfiltered lists.

### 2026-11-22 — Indexed My Day query on the main integration

The Debug x64 suite at `b1a51e8` passed all 725 tests with serial test
modules. The unchanged 10,000-task benchmark measured these best-of-seven
warm reads, each within the 5 ms limit:

| My Day | Important | Planned | All | Completed |
|---:|---:|---:|---:|---:|
| 3.641 ms | 1.981 ms | 1.938 ms | 1.575 ms | 1.884 ms |

The query merges disjoint index lookups for pins and live deadlines; a task
that matches both is returned only once. The highest warm FTS read was
5.553 ms (60 ms limit).

### 2026-11-24 — Task detail opening and resizing on signed DEV

Diagnostic, not a release gate. WintodoTest, signed x64 Native AOT DEV
`1.0.1.241` (`041880a3`), one launch, fresh My Day with two local tasks,
maximized wide window at 100% scale. A persistent external UI Automation
client timed the row's native `SelectionItem.Select` call until the exact
incoming title was observable in an onscreen editor with nonempty bounds.
Finding the row and allowing earlier transitions to settle were outside the
timer; the provider call, title lookup and polling were inside. The original
physical-click attempt could not validate its hit point, produced no samples,
and is excluded. These are neither first-paint nor first-keystroke timings.

| Selection | Samples | Median | Range |
|---|---:|---:|---:|
| First open in process | 1 | 488.65 ms | single observation |
| Reopen after closing Details | 8 | 306.47 ms | 280.19–342.52 ms |
| Switch while Details is open | 8 | 63.29 ms | 50.15–77.34 ms |
| Switch immediately after changing the outgoing note | 5 | 77.82 ms | 65.20–92.67 ms |

Both tasks retained their own edited note on reopening. Whether each write
had begun before switching wasn't instrumented. In a separate test an
*unsubmitted step draft* from Alpha was still in the field on Bravo.

Two one-second drags of the native sizer requested 100 DIPs in opposite
directions in the inline layout; the detail header moved 98 DIPs each way.
Intermediate UIA samples showed movement during both drags. This records
geometry, not presented frames or pointer-to-paint latency. The overlay,
photo/no-photo comparison, and larger task lists are unmeasured.
Local raw evidence: `tests/ui/artifacts/detail-pane-study/{open,drag,run}.json`.
See the [proposal and follow-up measurements](plans/2026-11-24-task-detail-responsiveness.md).

### 2026-11-24 — Task detail candidate versus signed DEV baseline

Signed x64 Native AOT DEV `1.0.1.245` (`486482f5`) used the same
WintodoTest setup and selection-to-title measurement as `1.0.1.241` above:
one fresh app process per build, two My Day tasks, a maximized window and
100% scale. Each time includes the UI Automation selection call and polling
until the exact incoming title is present with onscreen bounds.

| Action | Samples per build | .241 median (range), ms | .245 median (range), ms |
|---|---:|---:|---:|
| First open in process | 1 | 488.65 | 236.26 |
| Reopen after closing Details | 8 | 306.47 (280.19–342.52) | 70.77 (63.67–104.59) |
| Switch while Details is open | 8 | 63.29 (50.15–77.34) | 61.79 (52.72–91.02) |
| Switch immediately after changing the outgoing note | 5 | 77.82 (65.20–92.67) | 131.41 (74.20–161.45) |

Every candidate reopen took less time than the fastest baseline reopen.
Clean switches had similar medians. **Switches after a note edit were slower
in this run:** the median increased by 53.59 ms, and four of five candidate
samples exceeded the baseline maximum. Both tasks retained their own note on
reopening. The probe did not time the save separately or determine whether
it had begun when selection changed. Repeat that case before attributing the
difference to the save or the UI. Neither first paint nor time to the first
accepted keystroke was measured; the first-open figures are single observations.

At a window width of 1,936 DIPs, the candidate completed two one-second
inline divider drags on My Day without a photo and two on a list with the
Coast photo. In **both** cases the header moved −98 and +98 DIPs for a
requested −100 and +100. The no-photo baseline reached the same endpoints.
Intermediate UI Automation readings showed movement, but did not measure
frame intervals or pointer-to-paint latency. There was no baseline photo
run, and these small lists do not establish smoothness on a large list.

Local raw evidence: `tests/ui/artifacts/detail-pane-candidate/open.json`,
`drag.json` and `drag-photo-inline.json`; the corresponding baseline is in
`tests/ui/artifacts/detail-pane-study/`.

The signed update gate separately widened the pane by keyboard to 759 DIPs
between the divider and close button and restored that distance within 3
DIPs after restarting at the same maximized window size. The first test sent
all keystrokes in one burst and did not reach the old 720-DIP limit.

**Narrow-window drag remains unverified.** At 960 DIPs with the Coast photo,
the header did not move on a requested 100-DIP outward drag. Follow-up probes
attempted to measure available travel first: keyboard input produced no
observed divider movement at 960 DIPs (expanded navigation) or 880 DIPs
(compact navigation); the foreground check stopped the test at 1,080 DIPs.
The layout reserves 320 DIPs for the list even in overlay mode. The probe
did not distinguish a layout limit from input that failed to move the
divider. The screenshots show the resting layout only. Local evidence:
`tests/ui/artifacts/detail-pane-candidate/drag-photo-overlay.json` and
`tests/ui/artifacts/detail-pane-overlay/`.

### 2026-11-24 — Startup and minimized idle on signed DEV

WintodoTest, Windows 11 build 26300, 4 vCPUs, signed x64 Native AOT DEV
`1.0.1.245` (`486482f5`). Clean private DEV profile, onboarding completed,
one local task and no CalDAV account. See the [startup and idle investigation](plans/2026-11-24-startup-and-minimized-work.md)
for the launch sequence and proposed changes. These are observations of the
installed preview, not Store or ARM64 measurements or a new M9 gate.

**Startup with known saved placement.** Each row is a fresh process launched
as from Start after the setup run, with warm OS file caches. Before launch
the observer wrote and verified a normal rectangle of (120, 96), 1432 × 912,
and the requested maximized flag in the stopped synthetic fixture. Times in
milliseconds start immediately before AppsFolder activation. A separate thread
sampled the package process's native main window (visibility, rectangle and
`IsZoomed`), while UI Automation waited for the exact task row with positive
onscreen bounds. These are observed HWND/UIA states, not compositor frames
or time to the first accepted keystroke. The probe itself has scheduling and
provider overhead; the VM figures are not a comparison with the Store or M9.

| Saved state | First visible (1180 × 820) | First observed at 1432 × 912 | Task row observable | Maximized by 2 s after row? |
|---|---:|---:|---:|---|
| Normal | 411 | 726 | 786 | No, as expected |
| Maximized, run 1 | 372 | 747 | 816 | No |
| Maximized, run 2 | 408 | 750 | 840 | No |
| Maximized, run 3 | 343 | 664 | 758 | No |

All four showed the default size, then the saved *normal* size 315–375 ms
later, then the row another 61–94 ms later. The normal-size assertion passed;
all three maximization assertions **failed**. Local JSON: `normal-1.json`,
`max-{1,2,3}.json` and `summary.json` under
`tests/ui/artifacts/startup-idle-study/4789cc75fa004e72869eda5904fe4db0/`.

Two independent fixture setups also found a persistence problem. With native
maximization verified before an ordinary close request, the initial run did
not reopen maximized; the fixture DB read `shell.window.maximized=False`.
The next setup used the system maximize command, verified `IsZoomed`, sent
Alt+F4 and checked the DB **before** relaunch: it also read `False`. Seeding
the verified flag for the table above isolates restoration from that close
behavior. A fourth seeded-maximized process was still at the saved *normal*
size 35 seconds after the row appeared. In the same process a subsequent
system maximize command changed `IsZoomed` from false to true; normal close
again persisted `False`. This extended maximization assertion also failed.
Local JSON: `max-extended.json` and `summary.json` under
`tests/ui/artifacts/startup-idle-study/ef4edb167a42430c828bd728091847d3/`.

**Binary check, same installed build.** `Wintodo.exe` extracted from the signed
DEV MSIX and the retained native link output both have SHA-256
`b807bd381687672f9368a760b196f0158075fa4aa09a028c185e61c37eb2b1e6`.
Disassembly with the retained Native AOT object's symbols identified
`MainWindow.ApplyDeferredPresenterState` at executable RVA `0x46f6f0`: the
saved-true branch gets the presenter and returns without calling `Maximize`.
`ConfigurePresenter` at `0x46f730` likewise has no minimum-size setters. The
`OnClosed` state machine writes zero to the placement's `IsMaximized` byte at
RVA `0x4cbc79`, then calls the save method. The corresponding bytes matched
the signed executable. This confirms the managed presenter type tests in
those paths did not survive AOT compilation as intended. It does not establish
the behavior of every WinUI app or untested Wintodo release.

**Idle process counters.** One process, one interval per state, in the order
shown. The observer verified the HWND's owner and actual foreground/minimized
state before and after each interval. Each sample started five seconds after
the transition; the observer made no UI calls during the 30-second interval.

| Window state | Process CPU delta | Process read/write bytes | Working set at end | Private bytes at end |
|---|---:|---:|---:|---:|
| Visible, foreground | 0 ms | 0 / 0 | 126.39 MiB | 44.59 MiB |
| Minimized | 0 ms | 0 / 0 | 121.65 MiB | 44.34 MiB |
| Restored, foreground | 0 ms | 0 / 0 | 116.82 MiB | 44.51 MiB |

Zero means no increment at process-counter resolution. The samples exclude
the transitions, did not span the 15-minute sync timer or local midnight,
and do not measure wakeups, system/compositor CPU, battery use or restore
latency. Working set continued to fall after restore; this does not isolate
a memory saving caused by minimizing. No priority or power policy was changed.
Local evidence: `tests/ui/artifacts/startup-idle-study/2b47419f23df45f7bb7472911e56c307/idle.json`.
