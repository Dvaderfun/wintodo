ALTER TABLE tasks ADD COLUMN remote_delete_state INTEGER NOT NULL DEFAULT 0
    CHECK (remote_delete_state BETWEEN 0 AND 2);

CREATE TABLE task_move_outbox (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    task_uid TEXT NOT NULL,
    group_id TEXT NOT NULL,
    predecessor_id INTEGER NULL,
    version INTEGER NOT NULL DEFAULT 0,
    source_account_id INTEGER NULL,
    source_list_id INTEGER NOT NULL,
    source_list_uid TEXT NOT NULL,
    source_href TEXT NULL,
    source_etag TEXT NULL,
    source_payload BLOB NULL,
    source_restored INTEGER NOT NULL DEFAULT 0,
    restored_source_etag TEXT NULL,
    source_binding_verified INTEGER NOT NULL DEFAULT 1,
    destination_account_id INTEGER NULL,
    destination_list_id INTEGER NOT NULL,
    destination_list_uid TEXT NOT NULL,
    destination_href TEXT NULL,
    local_sequence INTEGER NOT NULL,
    change_id INTEGER NOT NULL,
    phase INTEGER NOT NULL DEFAULT 0 CHECK (phase BETWEEN 0 AND 3),
    attempted_payload BLOB NULL,
    attempted_sequence INTEGER NULL,
    attempted_change_id INTEGER NULL,
    destination_etag TEXT NULL,
    destination_removed INTEGER NOT NULL DEFAULT 0,
    resolution_choice INTEGER NULL,
    resolution_sequence INTEGER NULL,
    resolution_list_id INTEGER NULL,
    resolution_source_payload BLOB NULL,
    resolution_source_etag TEXT NULL,
    resolution_copy_uid TEXT NULL,
    resolution_copy_href TEXT NULL,
    resolution_copy_payload BLOB NULL,
    resolution_copy_etag TEXT NULL,
    resolution_copy_confirmed INTEGER NOT NULL DEFAULT 0,
    resolution_destination_removed INTEGER NOT NULL DEFAULT 0,
    resolution_is_destination_conflict INTEGER NOT NULL DEFAULT 0,
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL
);
CREATE INDEX ix_task_move_uid ON task_move_outbox(task_uid, id);
CREATE INDEX ix_task_move_source_account ON task_move_outbox(source_account_id);
CREATE INDEX ix_task_move_destination_account ON task_move_outbox(destination_account_id);
INSERT INTO schema_version(version, applied_utc)
VALUES (5, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
