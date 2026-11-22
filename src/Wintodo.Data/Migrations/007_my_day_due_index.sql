CREATE INDEX ix_tasks_due_live ON tasks(due_utc)
    WHERE is_deleted = 0 AND due_utc IS NOT NULL;

INSERT INTO schema_version(version, applied_utc)
VALUES (7, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
