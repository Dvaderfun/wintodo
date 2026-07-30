# Performance log

Every row is a real measurement from `scripts/Measure-Perf.ps1`. No estimates.
Budgets live in `PLAN.md` §7.

| Date | Milestone | Build | Tasks in DB | Cold start | Warm start | WS @2s | WS @30s | WS @10min | List query | Search |
|---|---|---|---|---|---|---|---|---|---|---|
| _(empty)_ | | | | | | | | | | |

**Build** is `FD` (framework-dependent) or `AOT`.
**WS** is `Process.WorkingSet64` in MB.

Any row that misses a budget needs a note below it explaining the cause and the
fix, or an ADR relaxing the budget.

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
