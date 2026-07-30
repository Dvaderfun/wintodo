# Performance log

Every row is a real measurement from `scripts/Measure-Perf.ps1`. No estimates.
Budgets live in `PLAN.md` §7.

| Date | Milestone | Build | Tasks in DB | Cold start | Warm start | WS @2s | WS @30s | WS @10min | List query | Search |
|---|---|---|---|---|---|---|---|---|---|---|
| 2026-07-30 | M3 | FD | 5,000 / 20 lists | 1,690 ms | 1,020 ms | 128.22 MB | 203.37 MB | deferred | 0.961 ms | n/a (M5) |
| 2026-07-31 | M4 | FD | 5,000 / 20 lists | 1429 ms | 909 ms | 201.43 MB | 205.02 MB | 199.64 MB | 0.961 ms | n/a (M5) |

**Build** is `FD` (framework-dependent) or `AOT`.
**WS** is `Process.WorkingSet64` in MB.

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
