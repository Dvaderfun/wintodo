CREATE TABLE import_sources (
    id              INTEGER PRIMARY KEY,
    provider        TEXT NOT NULL,
    account_key     TEXT NOT NULL,
    display_name    TEXT NOT NULL,
    last_import_utc TEXT NULL,
    UNIQUE (provider, account_key)
);

CREATE TABLE import_links (
    source_id         INTEGER NOT NULL REFERENCES import_sources(id) ON DELETE CASCADE,
    entity_type       INTEGER NOT NULL,     -- 0 task, 1 list, 2 step, 3 attachment
    external_id       TEXT    NOT NULL,
    local_uid         TEXT    NOT NULL,     -- task/list uid; attachment: owning task uid
    fingerprint       TEXT    NOT NULL,
    external_modified TEXT    NULL,
    local_modified    TEXT    NULL,
    imported_utc      TEXT    NOT NULL,
    created_run_id    TEXT    NULL,
    PRIMARY KEY (source_id, entity_type, external_id)
) WITHOUT ROWID;

CREATE INDEX ix_import_links_fingerprint ON import_links(source_id, entity_type, fingerprint);
CREATE INDEX ix_import_links_local ON import_links(local_uid);
CREATE INDEX ix_import_links_run ON import_links(created_run_id) WHERE created_run_id IS NOT NULL;

INSERT INTO schema_version(version, applied_utc)
VALUES (4, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
