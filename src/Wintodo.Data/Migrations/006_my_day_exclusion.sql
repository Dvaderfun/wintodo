ALTER TABLE tasks ADD COLUMN my_day_excluded_date TEXT NULL;

INSERT INTO schema_version(version, applied_utc)
VALUES (6, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
