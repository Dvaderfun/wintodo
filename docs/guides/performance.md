# Performance

Budgets are gates, not goals. The table lives in `PLAN.md` §7, re-baselined by
[ADR-008](../adr/ADR-008-perf-budget-rebaseline.md). Every measured row goes in
[docs/PERF.md](../PERF.md). No estimates, no "should be faster".

## Measure

```powershell
.\scripts\Measure-Perf.ps1 -Milestone M14                       # FD + AOT, 0 / 5k / 20k tasks
.\scripts\Measure-Perf.ps1 -Builds AOT -TaskCounts 5000 -Milestone M14 -SkipTenMinuteSample
```

| Parameter | Default | Meaning |
|---|---|---|
| `-Builds` | `FD`, `AOT` | Framework-dependent and/or Native AOT. Only AOT is gated. |
| `-TaskCounts` | `0, 5000, 20000` | Seeded database sizes |
| `-SkipBuild` | off | Reuse existing build/publish output |
| `-SkipTenMinuteSample` | off | Skip the +10 min sample. Also skips appending rows to `docs/PERF.md`. |
| `-SkipScrollGate` | off | Skip the 10 000-task scroll check |
| `-Milestone` | `M9` | Label written into the `PERF.md` row |

The script builds (or AOT-publishes through `scripts/Publish-Aot.ps1`), launches
through `winapp run`, samples working set, reads the startup stage trace, and
appends dated rows to `docs/PERF.md`. Run it on the reference machine recorded
in ADR-008, plugged in, with nothing else heavy running.

If a row misses a budget, add a note under the table explaining the cause and
the fix. Do not relax a budget without a new ADR.

## Rules that keep the numbers

The full list is `PLAN.md` §7 "Rules that produce those numbers". The ones most
often broken:

- `{x:Bind}` only; no `IValueConverter`; no `string.Format` in templates.
- `x:Load` on the detail pane, dialogs, flyout bodies, the completed section,
  empty states, and the settings view.
- `ListView` virtualisation stays on: no wrapping `ScrollViewer`, no nested
  scrolling. `TaskRow` stays ≤ 12 elements.
- `FontIcon` glyphs, not bitmaps, in rows.
- No LINQ in per-item render or per-keystroke paths.
- One `DispatcherQueueTimer` in the app; reminders are OS-scheduled.
- No reflection anywhere — it breaks Native AOT.

Known gap: the task list clears and refills its bound collection instead of
diff-updating it (`ARCHITECTURE.md`, Known divergences). Measure before and
after if you change it.
