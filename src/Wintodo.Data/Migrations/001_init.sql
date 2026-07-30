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
    color              INTEGER NOT NULL DEFAULT 0,
    background_theme   TEXT    NULL,
    group_id           INTEGER NULL REFERENCES list_groups(id) ON DELETE SET NULL,
    sort_order         REAL    NOT NULL,
    sort_mode          INTEGER NOT NULL DEFAULT 0,
    sort_descending    INTEGER NOT NULL DEFAULT 0,
    show_completed     INTEGER NOT NULL DEFAULT 1,
    completed_expanded INTEGER NOT NULL DEFAULT 0,
    is_deleted         INTEGER NOT NULL DEFAULT 0,
    created_utc        TEXT    NOT NULL,
    modified_utc       TEXT    NOT NULL,
    account_id         INTEGER NULL REFERENCES accounts(id) ON DELETE CASCADE,
    remote_url         TEXT    NULL,
    remote_ctag        TEXT    NULL,
    sync_token         TEXT    NULL
);

CREATE TABLE tasks (
    id                  INTEGER PRIMARY KEY,
    uid                 TEXT    NOT NULL UNIQUE,
    list_id             INTEGER NOT NULL REFERENCES lists(id) ON DELETE CASCADE,
    parent_uid          TEXT    NULL,
    summary             TEXT    NOT NULL,
    description         TEXT    NULL,
    status              INTEGER NOT NULL DEFAULT 0,
    percent_complete    INTEGER NULL,
    priority            INTEGER NOT NULL DEFAULT 0,
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
    my_day_date         TEXT    NULL,
    sort_order          REAL    NOT NULL,
    location            TEXT    NULL,
    url                 TEXT    NULL,
    classification      INTEGER NOT NULL DEFAULT 0,
    ical_unknown        TEXT    NULL,
    is_deleted          INTEGER NOT NULL DEFAULT 0,
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
    tag_id  INTEGER NOT NULL REFERENCES tags(id) ON DELETE CASCADE,
    PRIMARY KEY (task_id, tag_id)
) WITHOUT ROWID;

CREATE TABLE alarms (
    id                     INTEGER PRIMARY KEY,
    task_id                INTEGER NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    uid                    TEXT    NOT NULL,
    action                 INTEGER NOT NULL DEFAULT 0,
    trigger_utc            TEXT    NULL,
    trigger_offset_seconds INTEGER NULL,
    trigger_related        INTEGER NOT NULL DEFAULT 0,
    description            TEXT    NULL,
    snoozed_until_utc      TEXT    NULL,
    is_acknowledged        INTEGER NOT NULL DEFAULT 0,
    scheduled_toast_tag    TEXT    NULL
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
    entity_type INTEGER NOT NULL,
    entity_uid  TEXT    NOT NULL,
    operation   INTEGER NOT NULL,
    changed_utc TEXT    NOT NULL,
    synced      INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE saved_views (
    id         INTEGER PRIMARY KEY,
    name       TEXT NOT NULL,
    query      TEXT NOT NULL,
    icon       TEXT NULL,
    sort_order REAL NOT NULL
);

CREATE TABLE accounts (
    id             INTEGER PRIMARY KEY,
    display_name   TEXT NOT NULL,
    server_url     TEXT NOT NULL,
    username       TEXT NOT NULL,
    credential_key TEXT NOT NULL,
    principal_url  TEXT NULL,
    home_set_url   TEXT NULL,
    last_sync_utc  TEXT NULL
);

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

CREATE VIRTUAL TABLE tasks_fts USING fts5(
    summary,
    description,
    content = 'tasks',
    content_rowid = 'id',
    tokenize = 'unicode61 remove_diacritics 2'
);

CREATE TRIGGER tasks_fts_after_insert
AFTER INSERT ON tasks
BEGIN
    INSERT INTO tasks_fts(rowid, summary, description)
    VALUES (new.id, new.summary, new.description);
END;

CREATE TRIGGER tasks_fts_after_update
AFTER UPDATE ON tasks
BEGIN
    INSERT INTO tasks_fts(tasks_fts, rowid, summary, description)
    VALUES ('delete', old.id, old.summary, old.description);
    INSERT INTO tasks_fts(rowid, summary, description)
    VALUES (new.id, new.summary, new.description);
END;

CREATE TRIGGER tasks_fts_after_delete
AFTER DELETE ON tasks
BEGIN
    INSERT INTO tasks_fts(tasks_fts, rowid, summary, description)
    VALUES ('delete', old.id, old.summary, old.description);
END;

INSERT INTO schema_version(version, applied_utc)
VALUES (1, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
