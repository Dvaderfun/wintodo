CREATE TABLE list_deleted_tasks (
    list_id INTEGER NOT NULL REFERENCES lists(id) ON DELETE CASCADE,
    task_uid TEXT NOT NULL REFERENCES tasks(uid) ON DELETE CASCADE,
    PRIMARY KEY (list_id, task_uid)
) WITHOUT ROWID;

INSERT INTO schema_version(version, applied_utc)
VALUES (3, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
