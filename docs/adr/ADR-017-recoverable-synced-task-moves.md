# ADR-017 — Recoverable synced task moves

- **Status:** proposed; implementation requires owner acknowledgement
- **Date:** 2026-11-08
- **Supersedes:** nothing; extends ADR-002 and ADR-012
- **Owner acknowledgement:** pending

## Context

DEP-04 identifies a mismatch between local membership and remote identity.
Single and bulk moves update `tasks.list_id` but retain the source resource's
`remote_href` and ETag. Push chooses that old href, and the change-log query
selects the task's current account. Moving to a local list therefore drops
source cleanup from account queues; moving across accounts can select the
destination credential for a source resource. Direct steps remain in the old
list. Session undo also changes membership without recording remote move intent.

The existing `change_log` records UID, operation, time and acknowledgement,
without source account, source ETag, destination or operation phase. Clearing
the old href loses the information needed to remove the old resource after a
crash. Removing the source first creates a window in which no remote copy exists.

## Decision

Add forward migration 005 with a durable task-move outbox, separate from the
ordinary change log. Preserve the task UID, unknown iCalendar properties,
owned collections, soft deletion, and existing conditional CalDAV writes.
Do not add dependencies or a server-specific WebDAV MOVE requirement.

Within the same writer transaction that changes local membership, record the
source account/list/resource/ETag, destination account/list and planned resource,
task revision/change-log boundary, a move group and any preceding move. Store no
credential values. Keep this intent until all remote obligations are confirmed.

For a synced destination, persist the exact attempted VTODO payload and revision
before sending a conditional create (`If-None-Match: *`). Confirm the destination
before conditionally removing the source (`If-Match` with its recorded ETag).
For a local destination, the committed local database is the retained copy and
only conditional source removal is required. For a local source, only conditional
destination creation is required. Account credentials are selected separately for
each approved source/destination origin.

Persist transitions before acknowledging ordinary changes. If creation succeeds
but the process stops before saving its response, GET the planned destination on
retry. Accept it only when its UID, revision and complete task semantics match the
persisted attempt; otherwise report a conflict. Never overwrite an unrelated
destination or delete a source based only on matching UID. A source 404/410 is
idempotent removal; a source ETag conflict preserves both versions and requires
an explicit conflict choice. Missing ETags require a reviewed conditional proof,
or the move stays pending; no unconditional delete fallback.

Move a root and its existing steps atomically, retaining each step UID and parent
relationship. Single, bulk and undo entry points use one move-intent helper. A
bulk selection containing a parent and its step visits each task once. Moving a
step independently promotes it to a root, matching the existing move behavior.

Coalesce moves only before a remote attempt. Once an attempt is persisted, keep
its immutable intent and append subsequent moves in order. Later edits remain
queued and later deletes retain their soft-delete/change-log record. Undo after
an attempt appends a compensating move; it does not erase unfinished obligations.
Source/destination pulls must respect active intents inside the writer transaction,
and remote-version acknowledgements must not clear newer membership or edits.

Use the existing conflict chooser, with move-specific handling. Keep mine requires
explicit disposal of the fetched conflicting source revision; keep theirs requires
safe destination compensation and revision-guarded restoration; keep both preserves
the remote source version under a fresh copy UID before removing the old duplicate
UID, while retaining the moved task's original UID at the destination. Stale choices
must recheck local revisions and remote ETags before affecting either version.

Clean second clients must reconcile a confirmed source deletion and a destination
creation of the same UID as a move rather than applying an unconditional UID
collision overwrite. Incomplete duplicates remain pending/attention, and dirty
second-client versions remain explicit conflicts. Collection iteration order must
not determine the result. Cross-account moves apply the same rule with independently
authenticated account snapshots; no foreign account is contacted without its
existing configured credential/origin policy.

Retain or explicitly block pending obligations when accounts/collections are
disconnected, disabled or deleted; never cascade-delete a pending outbox. Continue
to show pending/attention through the existing sync status. An unavailable source
can temporarily leave two remote copies; completion is not reported until cleanup
converges. No new navigation surface is introduced.

The detailed persisted fields, phases, tests and implementation ownership are in
[the DEP-04 proposal](../plans/dep04-synced-moves.md).

## Consequences

- Schema 5 is additive and gets the existing pre-upgrade backup; older binaries
  reject it. Restore/re-upgrade and newer-schema fixtures must be updated.
- A move is locally immediate and remotely recoverable, but is not a distributed
  atomic operation. Temporary remote duplicates are an explicit pending state.
- The outbox temporarily retains attempted task bytes needed for crash recovery;
  it holds no credential and releases completed payloads after safe acknowledgement.
- Source or destination changes can require attention rather than silently losing
  a version. Recovery must remain possible after any network/local commit boundary.
- M11 deep subtasks remain outside scope. Existing steps travel with their parent.
- Final runtime/live-provider evidence remains DEP-19; deterministic SQLite and
  synthetic transport tests do not certify a final signed package.

## Alternatives considered

- **Clear the href:** cannot remove an orphaned source after restart.
- **Delete source first:** exposes remote task loss if destination creation fails.
- **Use only the existing change log:** has no durable source/version/phase and
  groups away earlier operations; reusing `settings` would hide a schema protocol.
- **WebDAV MOVE:** does not solve cross-account/local transfers or universal
  provider support and still needs retry/acknowledgement recovery.
- **Assign a fresh destination UID:** violates the requested task UID preservation
  and weakens second-client continuity.
