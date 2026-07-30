# Wintodo — Data Model

> Read before writing any model or storage code. This schema is designed so that
> **M13 (CalDAV sync) needs zero migrations** — every column sync will need is
> already here in `001_init.sql`, unused until then.

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

`TodoList`, `ListGroup`, `TaskAlarm`, `TaskAttachment`, `SavedView`, and
`Account` mirror their tables in §3 one-for-one. Steps are **not** a separate
type — a step is a `TodoTask` whose `ParentUid` is set. That is what makes M11
(deep trees) a UI change rather than a data migration.

---

## 3. SQLite schema — `Wintodo.Data/Migrations/001_init.sql`

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
    group_id           INTEGER NULL REFERENCES list_groups(id) ON DELETE SET NULL,
    sort_order         REAL    NOT NULL,
    sort_mode          INTEGER NOT NULL DEFAULT 0,     -- SortMode
    sort_descending    INTEGER NOT NULL DEFAULT 0,
    show_completed     INTEGER NOT NULL DEFAULT 1,
    completed_expanded INTEGER NOT NULL DEFAULT 0,
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

```sql
CREATE INDEX ix_tasks_list_status_sort ON tasks(list_id, status, sort_order) WHERE is_deleted = 0;
CREATE INDEX ix_tasks_due              ON tasks(due_utc)      WHERE is_deleted = 0 AND status <> 2;
CREATE INDEX ix_tasks_parent           ON tasks(parent_uid)   WHERE is_deleted = 0 AND parent_uid IS NOT NULL;
CREATE INDEX ix_tasks_myday            ON tasks(my_day_date, sort_order) WHERE is_deleted = 0 AND my_day_date IS NOT NULL;
CREATE INDEX ix_tasks_important        ON tasks(priority)     WHERE is_deleted = 0 AND priority BETWEEN 1 AND 4;
CREATE INDEX ix_tasks_completed        ON tasks(status, completed_utc DESC) WHERE is_deleted = 0 AND status = 2;
CREATE INDEX ix_tasks_created          ON tasks(created_utc)  WHERE is_deleted = 0;
CREATE INDEX ix_tasks_dirty            ON tasks(is_dirty)     WHERE is_dirty = 1;
CREATE INDEX ix_alarms_trigger         ON alarms(trigger_utc) WHERE is_acknowledged = 0;
CREATE INDEX ix_changelog_unsynced     ON change_log(synced)  WHERE synced = 0;
CREATE INDEX ix_lists_group_sort       ON lists(group_id, sort_order) WHERE is_deleted = 0;
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
- Each migration runs inside one transaction. A failure rolls back and the app
  starts read-only with an error banner rather than half-migrated.
- Before any migration above 001, copy the DB to `wintodo.db.bak-<version>`.
- Migrations are **forward-only**. No down-migrations.

---

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
| **anything else** | `ical_unknown` | Verbatim, re-emitted in original order |

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
| **My Day** | `my_day_date = @today AND is_deleted = 0` |
| **Important** | `priority BETWEEN 1 AND 4 AND is_deleted = 0` |
| **Planned** | `due_utc IS NOT NULL AND is_deleted = 0`, grouped: overdue `< @todayStart AND status <> 2` · today · tomorrow · this week · later · (no-date excluded) |
| **All** | `is_deleted = 0 AND parent_uid IS NULL` |
| **Completed** | `status = 2 AND is_deleted = 0` ordered by `completed_utc DESC` |
| **A user list** | `list_id = @id AND is_deleted = 0 AND parent_uid IS NULL` |
| **Search** | join `tasks_fts MATCH @q` on `is_deleted = 0`, ranked by `bm25()` |

`parent_uid IS NULL` in list views is what keeps steps out of the top level.
M11's tree view drops that predicate and builds the hierarchy in memory from a
single flat query — never one query per node.

`My Day` reset: a task's `my_day_date` is not cleared at midnight. The query
simply stops matching. A single `DispatcherQueueTimer` set for the next local
midnight (plus a check on app resume and on window activation) re-runs the query.
Nothing is written at midnight — this survives the app being closed, suspended,
or the machine being asleep.

---

## 6. Recurrence on completion (ADR-006)

```
complete(task):
    if task.rrule is null:
        status = Completed; completed_utc = now; percent_complete = 100
    else:
        next = RecurrenceRule.Parse(task.rrule).ComputeNext(task.due ?? now)
        if next is null:                       # COUNT exhausted or past UNTIL
            status = Completed; completed_utc = now
        else:
            insert completion_history(task.uid, now, task.due)
            shift start by (next - due) if start is set
            due = next
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

```
Lists:  GetListsAsync · CreateListAsync · RenameListAsync · SetListColorAsync
        · ReorderListAsync · MoveListToGroupAsync · DeleteListAsync
Groups: GetGroupsAsync · CreateGroupAsync · RenameGroupAsync · DeleteGroupAsync
Tasks:  GetTasksAsync(ListQuery) · GetTaskAsync(uid) · CreateTaskAsync
        · UpdateTaskAsync · CompleteTaskAsync · UncompleteTaskAsync
        · DeleteTaskAsync · RestoreTaskAsync · ReorderTaskAsync · MoveTaskAsync
Steps:  GetChildrenAsync(parentUid) · AddStepAsync · ReorderStepAsync
Search: SearchAsync(text, limit)
Alarms: GetPendingAlarmsAsync · SetAlarmAsync · SnoozeAlarmAsync · AcknowledgeAlarmAsync
Maint:  VacuumAsync · GetStatsAsync
```

`GetTasksAsync` takes a `ListQuery` descriptor (list id or smart-list kind, sort
mode, include-completed, optional filter) rather than a set of boolean
parameters. M12's query language compiles into the same descriptor, so the
filter path is built once.
