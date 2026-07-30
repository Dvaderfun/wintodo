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
