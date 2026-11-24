# Wintodo — Data Model

> Read before writing any model or storage code. This schema is designed so that
> **Initial M13 (CalDAV sync) needs zero migrations** — its columns are in
> `001_init.sql`. Recoverable synced moves add the durable outbox in migration
> 005 under accepted ADR-017.

---

## 1. Principles

1. **`VTODO` is the canonical shape.** Every task field either maps to an
   iCalendar property or lives under `X-WINTODO-*`. Nothing is invented that has
   no wire representation.
2. **`UID` is the identity**, not the rowid. `UID` is a UUIDv4 string generated
   on create and never changed. Cross-entity references (`parent_uid`,
   `change_log.entity_uid`, `completion_history.task_uid`) use `UID`, because a
   server may deliver a child before its parent.
3. **Never destroy data you don't understand.** Every iCalendar property we do
   not model is stored verbatim in `tasks.ical_unknown` and re-emitted on export
   or `PUT`. This is what makes us a good citizen instead of the app that wipes
   everyone else's fields.
4. **Soft delete.** `is_deleted = 1` plus a `change_log` row. Hard-delete is a
   maintenance sweep that runs only for rows already confirmed deleted remotely
   (or, pre-sync, older than 30 days).
5. **Timestamps are ISO-8601 UTC text** — `2026-07-30T14:05:00Z`. Text sorts
   lexicographically in the right order, indexes fine, and is readable when
   debugging a DB by hand. Date-only values are stored as `2026-07-30` with the
   matching `*_is_date_only` flag set — the distinction is semantic in
   iCalendar (`VALUE=DATE` vs `VALUE=DATE-TIME`) and must not be flattened.
6. **All local time arithmetic goes through `IClock`.** Nothing calls
   `DateTimeOffset.Now` outside its implementation, or My Day, recurrence, and
   reminders become untestable.
7. **Every task lives in a list; one list per account is the default.**
   `tasks.list_id` is `NOT NULL` because a `VTODO` has no meaning outside a
   collection — a listless task could never be synced. The "no list" bucket users
   expect is `lists.is_default = 1` ("Tasks"), the same shape Microsoft To Do
   uses. `ux_lists_default` enforces exactly one per account (local account keys
   as `COALESCE(account_id, 0)`), `ITaskStore.EnsureDefaultListAsync` creates it
   on first run, and `DeleteListAsync` refuses to delete it. On CalDAV sync each
   account's default maps to its own remote collection.

---

## 2. Domain model (`Wintodo.Core/Models`)

All types are `sealed`. Mutable classes, not records — these are edited in place
by ViewModels and copying them per keystroke is wasted allocation.

```csharp
public enum TaskStatus  { NeedsAction = 0, InProcess = 1, Completed = 2, Cancelled = 3 }
public enum TaskPriority { Undefined = 0, High = 1, Medium = 5, Low = 9 }   // raw 1-9 preserved
public enum Classification { Public = 0, Private = 1, Confidential = 2 }
public enum ListColor { Blue = 0, Red, Orange, Yellow, Green, Teal, Lime, Purple, Pink, Olive, Graphite, Navy }
public enum SortMode { Manual = 0, Importance, DueDate, Alphabetical, CreationDate }
public enum AlarmAction { Display = 0, Audio = 1, Email = 2 }
public enum AlarmRelation { Start = 0, End = 1 }
public enum ChangeOperation { Create = 0, Update = 1, Delete = 2 }

public sealed class TodoTask
{
    public long   Id { get; set; }              // rowid, local only
    public string Uid { get; set; }             // iCalendar UID — the real identity
    public long   ListId { get; set; }
    public string? ParentUid { get; set; }      // RELATED-TO;RELTYPE=PARENT

    public string  Summary { get; set; }        // SUMMARY — the title
    public string? Description { get; set; }    // DESCRIPTION — the note

    public TaskStatus Status { get; set; }
    public int?   PercentComplete { get; set; }
    public int    Priority { get; set; }        // raw 0-9, never collapsed to an enum on the wire

    public DateTimeOffset? Due { get; set; }
    public bool   DueIsDateOnly { get; set; }
    public string? DueTzid { get; set; }
    public DateTimeOffset? Start { get; set; }
    public bool   StartIsDateOnly { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset Created { get; set; }
    public DateTimeOffset LastModified { get; set; }
    public int    Sequence { get; set; }

    public string? RRule { get; set; }          // raw RRULE text; parsed by RecurrenceRule
    public DateOnly? MyDayDate { get; set; }    // X-WINTODO-MYDAY
    public double SortOrder { get; set; }       // X-APPLE-SORT-ORDER (see §3.3)

    public string? Location { get; set; }
    public string? Url { get; set; }
    public Classification Class { get; set; }

    public List<string> Categories { get; } = [];   // CATEGORIES — tags (M12)
    public List<TaskAlarm> Alarms { get; } = [];
    public List<TaskAttachment> Attachments { get; } = [];

    public string? IcalUnknown { get; set; }    // verbatim passthrough — §1.3

    // sync scaffolding, unused before M13
    public string? RemoteHref { get; set; }
    public string? RemoteETag { get; set; }
    public bool   IsDirty { get; set; }
    public bool   IsDeleted { get; set; }
}
```

`ListGroup`, `TaskAlarm`, `TaskAttachment`, `SavedView`, and `Account` mirror
their tables in §3 one-for-one. `TodoList.IconEmoji` is a nullable
single-grapheme user icon; `ListEmoji` owns its validation.
`TodoList.IncompleteTaskCount` is query-derived and is never stored. It counts
non-deleted, non-completed root tasks in that list; child steps are excluded.
Smart-list navigation counts are query-derived in the same way:
`GetSmartListTaskCountsAsync` returns incomplete roots matching My Day,
Important, Planned, and All, plus completed roots for Completed. Soft-deleted
tasks and tasks belonging to soft-deleted lists are excluded.
Steps are **not** a separate type — a step is a `TodoTask` whose `ParentUid` is
set. That is what makes M11 (deep trees) a UI change rather than a data
migration.

---

## 3. SQLite schema — `Wintodo.Data/Migrations/001_init.sql` plus forward migrations

Location: `%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalState\wintodo.db`
Attachments: `…\LocalState\attachments\<task-uid>\<filename>`

Connection pragmas, applied on every open:

```sql
PRAGMA journal_mode = WAL;
PRAGMA synchronous  = NORMAL;
PRAGMA foreign_keys = ON;
PRAGMA busy_timeout = 5000;
PRAGMA temp_store   = MEMORY;
```

### 3.1 Tables

```sql
CREATE TABLE schema_version (
    version     INTEGER NOT NULL,
    applied_utc TEXT    NOT NULL
);

CREATE TABLE settings (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
) WITHOUT ROWID;

CREATE TABLE list_groups (
    id          INTEGER PRIMARY KEY,
    name        TEXT    NOT NULL,
    sort_order  REAL    NOT NULL,
    is_expanded INTEGER NOT NULL DEFAULT 1,
    created_utc TEXT    NOT NULL
);

CREATE TABLE lists (
    id                 INTEGER PRIMARY KEY,
    uid                TEXT    NOT NULL UNIQUE,
    display_name       TEXT    NOT NULL,
    color              INTEGER NOT NULL DEFAULT 0,     -- ListColor
    background_theme   TEXT    NULL,
    icon_emoji         TEXT    NULL,                    -- one emoji grapheme
    group_id           INTEGER NULL REFERENCES list_groups(id) ON DELETE SET NULL,
    sort_order         REAL    NOT NULL,
    sort_mode          INTEGER NOT NULL DEFAULT 0,     -- SortMode
    sort_descending    INTEGER NOT NULL DEFAULT 0,
    show_completed     INTEGER NOT NULL DEFAULT 1,
    completed_expanded INTEGER NOT NULL DEFAULT 0,
    is_default         INTEGER NOT NULL DEFAULT 0,     -- the account's "Tasks" list
    is_deleted         INTEGER NOT NULL DEFAULT 0,
    created_utc        TEXT    NOT NULL,
    modified_utc       TEXT    NOT NULL,
    -- sync (M13)
    account_id         INTEGER NULL REFERENCES accounts(id) ON DELETE CASCADE,
    remote_url         TEXT    NULL,
    remote_ctag        TEXT    NULL,
    sync_token         TEXT    NULL
);

CREATE TABLE tasks (
    id                  INTEGER PRIMARY KEY,
    uid                 TEXT    NOT NULL UNIQUE,
    list_id             INTEGER NOT NULL REFERENCES lists(id) ON DELETE CASCADE,
    parent_uid          TEXT    NULL,                  -- no FK: parent may arrive later

    summary             TEXT    NOT NULL,
    description         TEXT    NULL,

    status              INTEGER NOT NULL DEFAULT 0,    -- TaskStatus
    percent_complete    INTEGER NULL,
    priority            INTEGER NOT NULL DEFAULT 0,    -- raw 0-9

    due_utc             TEXT    NULL,
    due_is_date_only    INTEGER NOT NULL DEFAULT 0,
    due_tzid            TEXT    NULL,
    start_utc           TEXT    NULL,
    start_is_date_only  INTEGER NOT NULL DEFAULT 0,
    completed_utc       TEXT    NULL,

    created_utc         TEXT    NOT NULL,
    modified_utc        TEXT    NOT NULL,
    sequence            INTEGER NOT NULL DEFAULT 0,

    rrule               TEXT    NULL,
    my_day_date         TEXT    NULL,                  -- 'YYYY-MM-DD', local date
    sort_order          REAL    NOT NULL,

    location            TEXT    NULL,
    url                 TEXT    NULL,
    classification      INTEGER NOT NULL DEFAULT 0,

    ical_unknown        TEXT    NULL,                  -- verbatim unmodelled properties

    is_deleted          INTEGER NOT NULL DEFAULT 0,
    remote_delete_state INTEGER NOT NULL DEFAULT 0,   -- migration 005, internal provisional/held deletion
    -- sync (M13)
    remote_href         TEXT    NULL,
    remote_etag         TEXT    NULL,
    is_dirty            INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE tags (
    id    INTEGER PRIMARY KEY,
    name  TEXT NOT NULL COLLATE NOCASE UNIQUE,
    color INTEGER NULL
);

CREATE TABLE task_tags (
    task_id INTEGER NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    tag_id  INTEGER NOT NULL REFERENCES tags(id)  ON DELETE CASCADE,
    PRIMARY KEY (task_id, tag_id)
) WITHOUT ROWID;

CREATE TABLE alarms (
    id                     INTEGER PRIMARY KEY,
    task_id                INTEGER NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    uid                    TEXT    NOT NULL,           -- RFC 9074 alarm UID
    action                 INTEGER NOT NULL DEFAULT 0, -- AlarmAction
    trigger_utc            TEXT    NULL,               -- absolute trigger
    trigger_offset_seconds INTEGER NULL,               -- or relative trigger
    trigger_related        INTEGER NOT NULL DEFAULT 0, -- AlarmRelation
    description            TEXT    NULL,
    snoozed_until_utc      TEXT    NULL,
    is_acknowledged        INTEGER NOT NULL DEFAULT 0,
    scheduled_toast_tag    TEXT    NULL                -- so a scheduled toast can be cancelled
);

CREATE TABLE attachments (
    id            INTEGER PRIMARY KEY,
    task_id       INTEGER NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    file_name     TEXT    NOT NULL,
    relative_path TEXT    NOT NULL,
    size_bytes    INTEGER NOT NULL,
    mime_type     TEXT    NULL,
    added_utc     TEXT    NOT NULL
);

CREATE TABLE completion_history (
    id                 INTEGER PRIMARY KEY,
    task_uid           TEXT NOT NULL,
    completed_utc      TEXT NOT NULL,
    occurrence_due_utc TEXT NULL
);

CREATE TABLE change_log (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    entity_type INTEGER NOT NULL,     -- 0 task, 1 list
    entity_uid  TEXT    NOT NULL,
    operation   INTEGER NOT NULL,     -- ChangeOperation
    changed_utc TEXT    NOT NULL,
    synced      INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE saved_views (              -- M12
    id         INTEGER PRIMARY KEY,
    name       TEXT NOT NULL,
    query      TEXT NOT NULL,
    icon       TEXT NULL,
    sort_order REAL NOT NULL
);

CREATE TABLE accounts (                 -- M13
    id             INTEGER PRIMARY KEY,
    display_name   TEXT NOT NULL,
    server_url     TEXT NOT NULL,
    username       TEXT NOT NULL,
    credential_key TEXT NOT NULL,       -- PasswordVault resource key. NEVER the password.
    principal_url  TEXT NULL,
    home_set_url   TEXT NULL,
    last_sync_utc  TEXT NULL
);
```

**Credentials never touch this database.** `accounts.credential_key` is a lookup
key into `Windows.Security.Credentials.PasswordVault`. A password, token, or
app-password stored in SQLite is a defect, not a shortcut.

### 3.2 Indexes

Migration `003_list_delete_membership.sql` adds `list_deleted_tasks(list_id,
task_uid)`, a composite primary key with foreign keys to lists and tasks. List
delete records only tasks it actually soft-deletes. Restore uses this membership
to retain earlier task deletions; restores append list/task change-log rows.
Undo is session-only, capped at 20 actions; task restore updates only captured
fields and retains current sync identity, unknown properties and unrelated edits.


```sql
CREATE INDEX ix_tasks_list_status_sort ON tasks(list_id, status, sort_order) WHERE is_deleted = 0;
CREATE INDEX ix_tasks_due              ON tasks(due_utc)      WHERE is_deleted = 0 AND status <> 2;
CREATE INDEX ix_tasks_due_live         ON tasks(due_utc)      WHERE is_deleted = 0 AND due_utc IS NOT NULL;
CREATE INDEX ix_tasks_parent           ON tasks(parent_uid)   WHERE is_deleted = 0 AND parent_uid IS NOT NULL;
CREATE INDEX ix_tasks_myday            ON tasks(my_day_date, sort_order) WHERE is_deleted = 0 AND my_day_date IS NOT NULL;
CREATE INDEX ix_tasks_important        ON tasks(priority)     WHERE is_deleted = 0 AND priority BETWEEN 1 AND 4;
CREATE INDEX ix_tasks_completed        ON tasks(status, completed_utc DESC) WHERE is_deleted = 0 AND status = 2;
CREATE INDEX ix_tasks_created          ON tasks(created_utc)  WHERE is_deleted = 0;
CREATE INDEX ix_tasks_dirty            ON tasks(is_dirty)     WHERE is_dirty = 1;
CREATE INDEX ix_alarms_trigger         ON alarms(trigger_utc) WHERE is_acknowledged = 0;
CREATE INDEX ix_changelog_unsynced     ON change_log(synced)  WHERE synced = 0;
CREATE INDEX ix_lists_group_sort       ON lists(group_id, sort_order) WHERE is_deleted = 0;
CREATE UNIQUE INDEX ux_lists_default   ON lists(COALESCE(account_id, 0)) WHERE is_default = 1 AND is_deleted = 0;
```

Partial indexes are deliberate — they keep the index small and let SQLite skip
soft-deleted rows entirely. Every hot query must include the same `WHERE`
predicate so the partial index is usable. Verify with `EXPLAIN QUERY PLAN` in
the `Wintodo.Data.Tests` perf tests; a query that falls back to a table scan
fails the test.

### 3.3 `sort_order` — REAL locally, INTEGER on the wire

Manual reordering stores `REAL` so inserting between two rows is
`(prev + next) / 2` with no renumbering pass. `X-APPLE-SORT-ORDER` is an
integer, so on export/`PUT` the value is projected to a dense integer sequence
via `ROW_NUMBER() OVER (PARTITION BY list_id ORDER BY sort_order)`. When the gap
between neighbours drops below `1e-6`, renormalise that list to
`1.0, 2.0, 3.0, …` in one statement.

### 3.4 Full-text search

```sql
CREATE VIRTUAL TABLE tasks_fts USING fts5(
    summary, description,
    content = 'tasks', content_rowid = 'id',
    tokenize = 'unicode61 remove_diacritics 2'
);
```

Plus `AFTER INSERT` / `AFTER UPDATE` / `AFTER DELETE` triggers on `tasks` to
keep the external-content index in sync. Search excludes `is_deleted = 1` in the
join, not in the FTS query.

FTS5 ships in `SQLitePCLRaw.bundle_e_sqlite3` — no extra dependency, and no
in-memory index competing for the working-set budget.

### 3.5 Migrations

- Numbered, embedded SQL resources: `001_init.sql`, `002_*.sql`, …
- `schema_version` holds the highest applied version.
- Versions above the highest embedded migration are rejected before journal
  mode changes or ordinary writes. The older app preserves the database and
  gives localized update guidance; schema downgrades remain unsupported.
- Each migration runs inside one transaction. A failure rolls back and the app
  starts read-only with an error banner rather than half-migrated.
- Before any migration above 001, copy the DB to `wintodo.db.bak-<version>`.
- Migrations are **forward-only**. No down-migrations.
- `002_list_icon_emoji.sql` adds nullable `lists.icon_emoji`; existing lists keep
  the colour-dot fallback.
- `003_list_delete_membership.sql` records which tasks a list delete removed (§3.2).
- `004_import_links.sql` adds `import_sources` and `import_links` (§3.6).
- `005_task_move_outbox.sql` adds durable conditional move/recovery intent (§3.7).

### 3.6 Import links (migration 004, ADR-016)

```sql
CREATE TABLE import_sources (id INTEGER PRIMARY KEY, provider TEXT NOT NULL,
    account_key TEXT NOT NULL, display_name TEXT NOT NULL, last_import_utc TEXT NULL,
    UNIQUE (provider, account_key));
CREATE TABLE import_links (source_id INTEGER NOT NULL REFERENCES import_sources(id) ON DELETE CASCADE,
    entity_type INTEGER NOT NULL,      -- 0 task, 1 list, 2 step, 3 attachment
    external_id TEXT NOT NULL, local_uid TEXT NOT NULL, fingerprint TEXT NOT NULL,
    external_modified TEXT NULL, local_modified TEXT NULL, imported_utc TEXT NOT NULL,
    created_run_id TEXT NULL,
    PRIMARY KEY (source_id, entity_type, external_id)) WITHOUT ROWID;
```

These tables let an import from another task manager run again without
duplicating anything. `provider` is `mstodo`. `account_key` is the Graph user
id, and `display_name` is the email shown in Settings. Each imported list,
task, step and attachment keeps a link to its source id. Attachment links store
the owning task's uid.

`IImportStore.ApplyImportAsync` runs one writer transaction. Every new or
updated task is dirty and gets a change-log row, so CalDAV pushes it.
`PreviewImportAsync` runs the same code in a transaction that is rolled back.
Matching order:

1. Link by external id.
2. For a task whose id disappeared from the source (Graph re-identifies a task
   that moves list), the `fingerprint`: creation second + normalised title.
3. The Graph default list maps to the local default list. Other lists merge
   with a non-deleted list of the same name (case-insensitive).
4. An unlinked local root task in the same list with the same normalised title
   and due date counts as a possible duplicate and is skipped.

New rows get UUIDv5 uids from provider + account + type + external id. If such
a uid exists without a link, the row is adopted. If it is soft-deleted, a
random uid is used.

For a matched task:
- Source unchanged (`external_modified` equal): skip.
- Source changed and local unchanged (`local_modified` equal to the row's
  `modified_utc`): update it, keeping My Day, sync identity and other clients'
  unknown properties.
- Both changed: keep the local edits, unless the overwrite option is set.
- Soft-deleted locally, or the imported list was deleted: never resurrected.

Nothing is deleted because it disappeared from the source.
`RemoveImportRunAsync(runId)` soft-deletes the lists, tasks and steps a run
created (`created_run_id`) and forgets those links.

---

### 3.7 Recoverable task moves (migration 005, ADR-017)

`task_move_outbox` stores pending remote obligations, separately from task
content and the ordinary change log. A movement transaction records source and
destination list/account identity, source href/ETag, group/predecessor ordering,
task sequence and acknowledgement boundary. The same transaction moves a root
and its existing steps. A repeated move may coalesce only before a remote attempt;
later moves append ordered successors. No credential is stored, and pending rows
have no cascade-delete foreign keys.

An attempt stores exact VTODO bytes and their sequence/change boundary before a
conditional destination creation. Versioned phase transitions persist destination
confirmation before conditional source removal. Completion releases attempt bytes,
binds only current destination membership, and acknowledges only the attempted
change boundary; later edits and deletes remain queued. Source conflict choices
persist their fetched source payload/ETag and any conditional KeepBoth copy or
KeepTheirs destination compensation before network operations.

The table contains `id`, `task_uid`, `group_id`, `predecessor_id`, `version`,
`source_account_id`, `source_list_id`, `source_list_uid`, `source_href`,
`source_etag`, `source_payload`, `source_restored`, `restored_source_etag`,
`source_binding_verified`,
`destination_account_id`, `destination_list_id`,
`destination_list_uid`, `destination_href`, `local_sequence`, `change_id`,
`phase`, `attempted_payload`, `attempted_sequence`, `attempted_change_id`,
`destination_etag`, `destination_removed`, `resolution_choice`, `resolution_sequence`,
`resolution_list_id`,
`resolution_source_payload`, `resolution_source_etag`, `resolution_copy_uid`,
`resolution_copy_href`, `resolution_copy_payload`, `resolution_copy_etag`,
`resolution_copy_confirmed`, `resolution_destination_removed`,
`resolution_is_destination_conflict`, `created_utc`,
and `updated_utc`. Integer revision/phase fields and nullable BLOB payloads are
mapped manually; dates use `IClock`. Active intent guards ordinary pushes,
pulls and acknowledgements. Account disconnect and list deletion are blocked
while pending obligations reference them. Disabling a list retains its intent.
Each root and its steps share a group; unrelated roots in one bulk selection
remain separate chooser decisions within the same movement transaction. Group
attempts and source restoration commit atomically under all captured revisions.
Renewed conflict choices require explicit fresh group revision/membership checks.
Confirmed source/destination recreation receipts preserve immutable attempts and
prevent completion while a recreated source resource still needs cleanup. A
deleted root suppresses child destination creation; restoring it requeues children
whose completed move left no remote binding.
An empty skipped-destination placeholder has issued no remote PUT. If that task
or its root is restored, the same verified source/destination intent may freeze
a live payload and newer acknowledgement boundary again, atomically with the
family. Existing nonempty attempts remain immutable; source snapshots and ETags
are retained so source-removal replay remains conditional or idempotent.
Migration 006 adds `tasks.my_day_excluded_date`, a local calendar date recording
when automatic My Day inclusion was dismissed. CalDAV replacements preserve the
current local value, including conflict resolution and cross-list moves.
Changing the exclusion alone, including undo, notifies store listeners without
changing the task sequence, modification timestamp, or pending CalDAV changes.
Changing a manual My Day pin continues to queue a sync update.

Migration 007 indexes deadlines of undeleted tasks, including completed ones.
My Day can look up pins and deadlines separately without scanning unrelated
tasks. A task that is both pinned and due appears once.

Migration 005 also adds internal `tasks.remote_delete_state`: 0 means explicit
or unproven state, 1 records an ordinary pull tombstone, and 2 holds a known
visible version when a confirmed remote move offers an older or ambiguous
destination. Only a proven pull tombstone may be revived; old-schema and user
deletions remain conservative. Held versions defer further pull deletion/update
without automatically creating a source copy. Local writes and explicit conflict
choices reset the marker; it is not a public model or iCalendar field.
Ordinary cross-collection UID conflicts also use a guarded family snapshot.
Keep mine queues a durable reverse move from the proven foreign resource to the
current local collection; it never binds a foreign href to the current list.
Keep theirs and Keep both recheck every captured local revision before adopting
the fetched remote version. Keep both copies the retained local family under
fresh UIDs, including fresh child-to-parent relationships, in the same writer
transaction. Source/destination credentials remain independently selected.
For an ordinary remote-parent deletion, Keep both retains existing step UIDs
and reparents those steps to the new local copy, queuing their updates after
the copy's creation. This preserves the existing step/undo contract; no original
remote parent family remains at a destination requiring competing child UIDs.

## 4. `VTODO` mapping

The wire format. `VCALENDAR` wrapper with `PRODID:-//Wintodo//Wintodo 1.0//EN`,
`VERSION:2.0`, one `VTODO` per task (steps are separate `VTODO`s linked by
`RELATED-TO`).

| iCalendar property | Column | Notes |
|---|---|---|
| `UID` | `uid` | UUIDv4, immutable |
| `SUMMARY` | `summary` | Required, non-empty |
| `DESCRIPTION` | `description` | The note. Escape `\n` `\,` `\;` `\\` |
| `STATUS` | `status` | `NEEDS-ACTION` / `IN-PROCESS` / `COMPLETED` / `CANCELLED` |
| `PERCENT-COMPLETE` | `percent_complete` | 0–100. Write `100` with `COMPLETED` |
| `PRIORITY` | `priority` | 0 undefined, 1–4 high, 5 medium, 6–9 low. **Preserve the raw value** — do not round-trip 3 into 1 |
| `DUE` | `due_utc` + `due_is_date_only` + `due_tzid` | `VALUE=DATE` when date-only; `TZID=` when floating-local |
| `DTSTART` | `start_utc` + `start_is_date_only` | |
| `COMPLETED` | `completed_utc` | UTC only per RFC 5545 |
| `CREATED` | `created_utc` | |
| `LAST-MODIFIED` | `modified_utc` | Bump on every write |
| `DTSTAMP` | — | Emitted as "now" on export; never stored |
| `SEQUENCE` | `sequence` | Increment on each material change |
| `RRULE` | `rrule` | Raw text preserved verbatim |
| `RELATED-TO;RELTYPE=PARENT` | `parent_uid` | Subtasks and steps both use this |
| `CATEGORIES` | `tags` / `task_tags` | Comma-separated, order preserved on export |
| `LOCATION` | `location` | |
| `URL` | `url` | |
| `CLASS` | `classification` | |
| `VALARM` | `alarms` | RFC 9074: keep the alarm `UID`; `TRIGGER` absolute or relative |
| `ATTACH` | `attachments` | Local files exported as `FMTTYPE` + `file:` URI; remote URIs preserved |
| `X-APPLE-SORT-ORDER` | `sort_order` | §3.3 |
| `X-WINTODO-MYDAY` | `my_day_date` | `YYYY-MM-DD`. Other clients ignore it harmlessly |
| `X-WINTODO-LIST-COLOR` | `lists.color` | Only on export of a whole list |
| `X-WINTODO-LIST-ICON` | `lists.icon_emoji` | One emoji grapheme; only on export of a whole list |
| **anything else** | `ical_unknown` | Verbatim, re-emitted in original order |

Microsoft To Do imports (ADR-016) keep Graph values Wintodo doesn't model as
extension properties in `ical_unknown`:

| Property | Holds |
|---|---|
| `X-MSTODO-ID` | Graph task id |
| `X-MSTODO-LIST-ID` | Graph list id |
| `X-MSTODO-STATUS` | `waitingOnOthers` or `deferred`; the task itself is `NEEDS-ACTION` |
| `X-MSTODO-RECURRENCE` | Raw `patternedRecurrence` JSON when RRULE can't express it exactly |
| `X-MSTODO-LINKED-RESOURCE` | One JSON value per linked resource; the first web URL also goes to `URL` |
| `X-MSTODO-EXTRA` | JSON of task properties this version doesn't know |
| `X-ALT-DESC;FMTTYPE=text/html` | Original HTML note; `DESCRIPTION` holds the plain text |

To Do importance maps `high`→`PRIORITY:1`, `normal`→`0` and `low`→`9`.
Due and start dates are date-only. A reminder becomes one absolute `VALARM`.
It is acknowledged when it is already past or the task is completed.

Importance (the To Do star) maps to `PRIORITY`: starring writes `1`, unstarring
writes `0`. A task arriving from another client with `PRIORITY` 1–4 displays as
starred; its exact value is preserved on write-back unless the user toggles the
star.

**Round-trip requirement (M6 gate):** import then export must produce a
semantically identical `VTODO` — same property set, same values — ignoring only
`PRODID`, `DTSTAMP`, and property ordering. Unknown properties must come back
byte-for-byte.

---

## 5. Smart-list queries

All take `@today` (local date) and `@nowUtc` from `IClock`. All must use a
partial index — verify with `EXPLAIN QUERY PLAN`.

| List | Predicate |
|---|---|
| **My Day** | Manually added today, or due today and not removed from My Day today. Deleted tasks are excluded. Date-only deadlines match the local calendar date. Timed deadlines match the UTC interval from the first valid instant of this date to the first valid instant of the next date. |
| **Important** | `priority BETWEEN 1 AND 4 AND is_deleted = 0` |
| **Planned** | `due_utc IS NOT NULL AND is_deleted = 0`, grouped: overdue `< @todayStart AND status <> 2` · today · tomorrow · this week · later · (no-date excluded) |
| **All** | `is_deleted = 0 AND parent_uid IS NULL` |
| **Completed** | `status = 2 AND is_deleted = 0` ordered by `completed_utc DESC` |
| **A user list** | `list_id = @id AND is_deleted = 0 AND parent_uid IS NULL` |
| **Search** | join `tasks_fts MATCH @q` on `is_deleted = 0`, ranked by `bm25()` |

`parent_uid IS NULL` in list views is what keeps steps out of the top level.
M11's tree view drops that predicate and builds the hierarchy in memory from a
single flat query — never one query per node.

`My Day` suggestions use the same local date and due-date interpretation as
My Day membership. The guarded add and complete batch operations check task
eligibility again inside the write transaction before changing the task.

`My Day` reset: a task's `my_day_date` is not cleared at midnight. The query
stops matching yesterday's manual additions and deadlines, and picks up the new
day's deadlines. A single `DispatcherQueueTimer` set for the first valid instant
of the next local date (plus a check on app resume and on window activation)
re-runs the query. This is the first occurrence if midnight repeats, or the
first valid time after the jump if midnight is skipped.
Nothing is written at midnight — this survives the app being closed, suspended,
or the machine being asleep.

---

## 6. Recurrence on completion (ADR-006)

```
complete(task):
    if task.rrule is null:
        status = Completed; completed_utc = now; percent_complete = 100
    else:
        occurrence = task.due ?? task.start ?? localCalendarDate(now)
        next = RecurrenceRule.Parse(task.rrule).ComputeNext(occurrence, completionCount + 1)
        insert completion_history(task.uid, now, occurrence)
        if next is null:                       # COUNT exhausted or past UNTIL
            status = Completed; completed_utc = now
        else:
            shift start by (next - occurrence) if start is set
            if due is set: advance due by (next - occurrence)
            if neither date was set: set due to next as a date only
            status = NeedsAction
            completed_utc = null
            sequence += 1
    modified_utc = now
    reschedule alarms relative to the new due
    log change
```

Reminders are recomputed from the new `DUE` on every completion. A relative
`TRIGGER` (`-PT30M` etc.) moves automatically; an absolute `TRIGGER` is shifted
by the same delta as the due date.

---

## 7. Store API surface (`Wintodo.Core/Services/ITaskStore.cs`)

Every method is `async` and runs off the UI thread. All writes go through the
single writer connection serialised by a `SemaphoreSlim(1,1)`, and every write
also appends to `change_log` and raises a coalesced `StoreChanged` carrying the
affected list ids and task uids.

CalDAV pull uses `ISyncStore.ApplyRemotePullAsync` with the previously observed
task states. Inside the writer transaction it checks pending changes, dirty and
deleted state, list membership, remote version and the local `SEQUENCE` stamp.
Concurrent local changes are returned as full conflict snapshots instead of
being overwritten or acknowledged. Applied counts reflect committed changes;
unresolved pull conflicts or deferrals retain the list's prior sync token for
retry after restart. Explicit conflict choices use separate opt-in behavior
to queue a clean local version or preserve/discard direct steps as selected.

```
Lists:  GetListsAsync · GetIncompleteTaskCountsAsync · GetSmartListTaskCountsAsync
        · CreateListAsync
        · RenameListAsync · SetListColorAsync · SetListIconEmojiAsync
        · ReorderListAsync · MoveListToGroupAsync · DeleteListAsync · RestoreListAsync
Groups: GetGroupsAsync · CreateGroupAsync · RenameGroupAsync · DeleteGroupAsync
Tasks:  GetTasksAsync(ListQuery) · GetTaskAsync(uid) · CreateTaskAsync
        · UpdateTaskAsync · RenameTaskAsync · SetTaskTextAsync · SetTaskImportanceAsync
        · TryAddTaskAttachmentAsync · CompleteTaskAsync · UncompleteTaskAsync
        · DeleteTaskAsync · RestoreTaskAsync · ReorderTaskAsync · MoveTaskAsync
        · ApplyTaskBatchAsync(uids, action)
        · CaptureTaskUndoAsync(uids, fields) · RestoreTaskFieldsAsync(states)
Steps:  GetChildrenAsync(parentUid) · AddStepAsync · ReorderStepAsync
Search: SearchAsync(text, limit)
Alarms: GetPendingAlarmsAsync · SetAlarmAsync · SnoozeAlarmAsync · AcknowledgeAlarmAsync
Maint:  VacuumAsync · GetStatsAsync
```

`GetTasksAsync` takes a `ListQuery` descriptor (list id or smart-list kind, sort
mode, include-completed, optional filter) rather than a set of boolean
parameters. M12's query language compiles into the same descriptor, so the
filter path is built once. List results do not include owned collections.
Row rename, importance and detail text edits read the current row in the writer
transaction and leave the owned collections in place. Adding a local attachment
checks the task's remaining space in that same transaction and inserts only the
new reference. These writes preserve unknown properties and use the normal
sequence, dirty flag and change log for sync.

`ApplyTaskBatchAsync` reads and mutates each selected UID inside one writer
transaction. It supports completion (including recurrence), importance, My Day,
date-only due dates, moves, soft delete and restore. Unknown properties and owned
collections are retained; each changed task gets one change-log row, and one
StoreChanged event reports the committed selection and affected lists. Missing
tasks or invalid destinations roll back the whole batch without notifications.

Fixed-category appearance preferences live in the generic `settings` table:
`appearance.fixed_category_background.<category>` stores the selected built-in
background key and `appearance.tint_fixed_category_icons` stores whether fixed
navigation/header glyphs use their category accent or the neutral foreground.
