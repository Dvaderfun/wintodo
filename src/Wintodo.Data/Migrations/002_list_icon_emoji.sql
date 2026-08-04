ALTER TABLE lists ADD COLUMN icon_emoji TEXT NULL;

INSERT INTO schema_version(version, applied_utc)
VALUES (2, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
