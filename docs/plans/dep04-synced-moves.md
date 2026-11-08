# DEP-04 — Recoverable synced moves proposal

Prepared 2026-11-08 against `79ce9b1`. Status: **IMPLEMENTED; automated
acceptance PASS; guest UI/live/final-package checks pending**. The owner acknowledged
[ADR-017](../adr/ADR-017-recoverable-synced-task-moves.md) in this session on
2026-11-08. The baseline evidence below predates implementation.

## Outcome

A local A-to-B move preserves the UID and content, eventually creates/confirms B
and removes A, and survives failure/restart without losing a task version. Moving
parents moves their steps. Single/bulk/undo paths share the same durable policy.

## Evidence and cause

- `SqliteTaskStore.Tasks.cs:254`: single move changes membership/sequence, but not
  remote identity or children.
- `SqliteTaskStore.Batch.cs:80`: bulk move has the same source-identity gap.
- `SqliteTaskStore.Undo.cs:43`: restoring membership bypasses durable remote intent.
- `SqliteTaskStore.Sync.cs:410`: latest changes are grouped per UID and selected
  using the task's current account; source cleanup can disappear from that queue.
- `CalDavSyncService.cs:659`: a nonempty old href takes precedence over the new list.
- `SqliteTaskStore.Sync.cs:755`: a clean existing UID in another collection becomes
  a pull conflict. Current guards protect revisions but do not reconcile remote moves.

Baseline runtime **FAIL: 7/7 regressions failed as expected**, zero passed or
skipped, against committed `79ce9b1`. Single/bulk sync both issued only
`PUT /cal/tasks/synthetic.ics`; neither created a destination nor removed the
source. Local/cross-account source queues were empty, both parent move paths
left steps behind, and restart retained no source cleanup. These are defect
reproductions, not passing release acceptance. No production fix has shipped.
Redacted [evidence](../../artifacts/dep04/evidence.json), regression source and
private raw TRX/logs are under ignored `artifacts/dep04/`; failing tests remain
outside the integrated suites. Recovery/two-client acceptance is still pending.

## Durable data

Migration `005_task_move_outbox.sql` adds a table with these reviewed responsibilities;
the implementation may choose narrower column names without changing this policy.

| Field | Purpose |
|---|---|
| id, task_uid, group_id, predecessor_id | Resource identity, atomic parent/bulk group, ordered repeated moves |
| source_account_id, source_list_uid, source_href, source_etag | Durable source-removal obligation using its own credential/origin |
| destination_account_id, destination_list_uid, destination_href | Intended target; null account means local storage |
| local_sequence, change_id | Captured revision and acknowledgement boundary |
| phase | Planned, destination-attempted, destination-confirmed, source-removed, complete |
| attempted_payload, attempted_sequence, attempted_change_id | Immutable pre-send bytes/revision for ambiguous-success retry |
| destination_etag | Conditional update/compensation proof after confirmed creation |
| created_utc, updated_utc | IClock-based diagnostics/recovery ordering |

No account/list cascade may discard pending rows. No password/token is stored.
Finalization releases completed payloads and acknowledgements in one transaction;
it updates current task remote metadata only when its membership/revision still
belongs to that intent. Newer changes retain dirty/pending state.

## State transitions and failure proof

1. **Planned:** local membership and outbox commit together. If destination is
   synced, create its collection through the existing list path if necessary.
2. **Destination attempted:** persist payload, target href and revision before PUT.
   `If-None-Match: *` protects unrelated contents. Timeout/cancel leaves intent.
3. **Destination confirmed:** record ETag. A retry after uncertain success performs
   bounded GET and compares all persisted task semantics, including unknowns;
   equivocation or incompatible content becomes attention, not overwrite.
4. **Source removed:** DELETE uses the captured source account and `If-Match`.
   404/410 permits idempotent completion; 412 preserves conflict evidence and both
   copies. Failed/unconfirmed destination creation never reaches DELETE.
5. **Complete:** atomically finalize outbox/change boundary and remote identity
   if still current. Replaying completion is harmless; newer edits/moves remain.

Local targets skip network creation after the local transaction commits. Local
sources skip removal. Cancel/deletion while an attempted target may exist must
inspect/conditionally clean that target; dropping the row cannot cancel a
possibly successful write. A second move after an attempt queues a successor.

## Conflict and pull integration

The existing chooser receives move context and immutable fetched revisions. Keep
mine/theirs/both must revalidate task sequence, local membership, attempted payload
and all relevant ETags. A source conflict never silently disposes its new version.
Keep both gives the retained source copy a fresh UID through conditional writes
before removing the old UID; the moved version keeps its original UID.

Pull collects enough source/destination deletion and creation evidence to reconcile
clean moved UIDs across collection order, while deferring incomplete duplicates
and preserving dirty-client conflicts. The guarded writer transaction consults
active intents even after an ordinary task's dirty bit was acknowledged. Source
removal of an old binding cannot delete the current destination task by UID alone.

Disconnected/disabled accounts and deleted lists must not erase pending obligations.
Missing credentials or blocked origins retain attention. Cross-account execution
uses separately authenticated source and destination requests and durable ordering,
regardless of which configured account is synced first.

## Implementation ownership after acknowledgement

| Area | Files/responsibility |
|---|---|
| Data/contracts | New migration/resource registration; Core move contracts; Tasks/Batch/Undo + new Data move partial; Sync query/ack/pull guards |
| Sync execution | New move executor partial/helper and CalDavSyncService orchestration; transport stays within DEP-07 ownership until integrated |
| Conflict/UI | Existing chooser view-model/service paths and localized move guidance; no new navigation surface |
| Tests/docs | Sync/Data move regressions, migration compatibility and undo/step tests; DATA-MODEL and architecture; existing affected VM gates |

Migration tests currently use schema 5 as an unsupported future schema. Update
those synthetic fixtures to the new maximum plus one, including the VM seed,
without weakening unchanged-rejection assertions.

## Acceptance matrix

Implementation evidence: baseline `3615b31` plus reviewed overlays. The
independent SQLite/CalDAV suite passes 71/71, and the final integrated solution
passes 678/678 (Core 231, Data 130, Import 155, Sync 162), zero failed/skipped.
Debug/Release x64 warnings-as-errors builds are clean. The final source commit,
hashes and architecture evidence are recorded under
`artifacts/dep04-integration/`; raw test/build logs remain private.
Fresh Native AOT publishes for x64 and ARM64 pass with compiler/runtime
10.0.14. The actual layouts and relative summary pass the privacy scan across
649 files with zero findings. A final nine-project NuGet audit has zero
advisories; unresolved native/tool findings remain DEP-02's separate disposition.

The implementation uses atomic family preparation, phase updates, source
restoration and completion. Real attempted payloads remain immutable; empty
skipped destinations that never received a PUT can capture a fresh live family
after restoration. Source bindings require an approved collection/account
proof before credentials are selected. Unknown or ambiguous legacy bindings
retain attention rather than guessing an origin. Remote-pull tombstones and
held known versions use internal `remote_delete_state` metadata; explicit user
deletions retain their separate behavior. Cross-list chooser actions use the
same durable machinery, with stale family/remote-snapshot checks.

The move writer exports newly authored fields and preserves unknown properties
and category parameters. Ordinary import/export retains its presence policy.
Generic conflict choices now revalidate remote snapshots; an offline verification
returns Offline without copying or overwriting a cached version. The matching
older test uses a fresh synthetic GET, and a new offline no-mutation regression
preserves that distinction.

Initial evidence is retained: baseline 3/19 passed and 16/19 failed; intermediate
move runs were 38/51, 50/51 and 57/62. The first integrated solution run was
673/677 with four failures. Fixes preserve the original step UID under ordinary
remote-deletion KeepBoth, and move races defer through durable outbox state
instead of an obsolete generic chooser. The unchanged data/revision/token checks
and the final full suite pass. Fixture-only status/category mutation corrections
are recorded separately from production fixes.

The guest still has SDK 10.0.401, below the required 10.0.403. Schema warning,
M13, move/undo, interop and visual checks remain pending in WintodoTest; no host
UI fallback or guest setup was performed. A separate schema-6 seed preserves the
older fixture. Live Radicale/Nextcloud and installed-package checks remain DEP-19.

| Scenario | Required result |
|---|---|
| Single/bulk; parent plus steps; independently moved step | UID/content/parent relationships retained; one local transaction and notifications |
| Same account; cross account; local-to-synced; synced-to-local | Correct authenticated origin, destination membership, source cleanup |
| Failure before PUT, after PUT/before phase commit, before/after DELETE | Restart retries converge; no premature acknowledgement |
| New destination content, altered source, missing/changed ETag | Conditional conflict; both versions preserved until explicit choice |
| Source/destination pull races; local edit/delete; stale choice | Current revisions preserved; old source cannot move task back or hide it |
| Repeated moves and undo in each phase | Coalescing only before attempts; later operations ordered/recoverable |
| Two cycles; restarted client; clean/dirty second client | No unexplained duplicate or version loss; clean membership converges |
| Account disabled/disconnected or list deleted while pending | No outbox loss; visible pending/attention and safe recovery |
| Schema 1–4 upgrade/backup, reopen, future-schema rejection | Existing user data retained and old binaries safely rejected |

Live Radicale/Nextcloud and final installed-package chooser tests remain DEP-19.
