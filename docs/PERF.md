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
