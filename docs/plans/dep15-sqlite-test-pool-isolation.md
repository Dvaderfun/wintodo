# DEP-15 — Isolate SQLite test pool cleanup

Recorded 2026-11-09 during the ML servicing integration after `703757b`.
**Test isolation corrected; three regressions and the full Data suite PASS.**
Production provider/pooling behavior and its separate runtime acceptance are
outside this correction.

## Observed failure and investigation

The initial integration run passed 703/704 tests. The failing Delete case in
`TaskBatchTests.Batch_CommitsOnceAndPreservesUnrelatedData` reached
`ChangeCount` with an open connection whose native handle had been disposed.
The same class passed alone, 17/17. Preserve the initial failure rather than
reclassifying it from the later result.

The exact `Microsoft.Data.Sqlite.Core/10.0.12` package identifies vendor source
commit `95017c711e6afc1085133d440e42b4bd78155701`.
[Activate](https://github.com/dotnet/dotnet/blob/95017c711e6afc1085133d440e42b4bd78155701/src/efcore/src/Microsoft.Data.Sqlite.Core/SqliteConnectionInternal.cs#L161)
sets `_active` before publishing its weak outer owner. The
[leaked predicate](https://github.com/dotnet/dotnet/blob/95017c711e6afc1085133d440e42b4bd78155701/src/efcore/src/Microsoft.Data.Sqlite.Core/SqliteConnectionInternal.cs#L149)
and concurrent
[pool cleanup](https://github.com/dotnet/dotnet/blob/95017c711e6afc1085133d440e42b4bd78155701/src/efcore/src/Microsoft.Data.Sqlite.Core/SqliteConnectionPool.cs#L102)
can treat that publication window as a leaked connection and dispose its handle.

A public-API stress probe in an ignored checkout reproduced eight query-stage
`ObjectDisposedException` failures alongside 81,564 successful pooled queries.
The unpooled control completed 110,130 queries with zero failures. Both phases
used concurrent process-wide pool clearing. The original full-run interleaving
was not captured; this establishes a reachable mechanism matching its symptom.

A deterministic check then failed the intended fixture-isolation assertion:
disposing actual fixture A removed A's directory and closed an unrelated idle
pooled handle belonging to B. That cleanup interference is directly observed.

## Correction and proof

[TestSqliteConnections](../../tests/Wintodo.Data.Tests/TestSqliteConnections.cs)
creates helper connections with `Pooling=false`, escaped paths and the prior
open mode/default cache. Fourteen previously pooled direct helper sites use it.
Five process-global `ClearAllPools` calls now clear only the owned database's
production writer/read-only pool keys, after the fixture store is disposed.

Microsoft.Data.Sqlite keys pools by exact connection-string text. The scoped
cleanup explicitly matches the production builder's order, Shared cache and
enabled pooling. Keep those keys aligned when the production builder changes;
owned-directory deletion and handle-isolation checks must still pass.

[Three deterministic regressions](../../tests/Wintodo.Data.Tests/TestSqliteConnectionsTests.cs)
verify owned handle closure/directory deletion, unrelated idle handle survival
and reuse, immediate helper handle disposal, and paths with spaces/semicolons.
They pass 3/3; TaskBatch passes 17/17; the full Data suite passes 133/133, with
zero failures/skips. Parent integration subsequently passes all 707 unit tests.
No whole-suite serialization or test skip was added.

The production store, provider version and enabled production pooling remain
unchanged. This proves the test cleanup boundary; applicability of other vendor
timer/reclamation paths in the app has not been established by this probe.

## Evidence

Raw logs, the failed before-fix assertion and sanitized source/probe counters
remain ignored under `artifacts/dep-sqlite-pool-investigation/`.
`investigation-summary.json` records source links, both stress phases, the
deterministic failure and corrected results. The original integration log is
`artifacts/dep-production-next-integration/ml-unit-tests.log`; the successful
full run is retained separately as `final-unit-tests.log`. No user database,
UI, guest, credential, signing or remote setting was used for these checks.
