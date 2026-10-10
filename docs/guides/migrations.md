# Adding a schema migration

The schema lives in `src/Wintodo.Data/Migrations/NNN_name.sql`, embedded into
`Wintodo.Data`. `SqliteTaskStore.ApplyMigrations` runs, in order, every script
whose number is above `MAX(schema_version.version)`. Each script runs in its own
transaction. Before upgrading an **existing** database it writes a one-time
backup to `<db>.bak-<N>`.

Migrations are forward-only. A user's database is never rebuilt from scratch.

## Steps

1. Update [docs/DATA-MODEL.md](../DATA-MODEL.md) first: the model (§2), the
   table (§3), and the `VTODO` mapping (§4) if the column round-trips to iCalendar.
   A new invented field maps to an `X-WINTODO-*` property
   ([ADR-001](../adr/ADR-001-ical-vtodo-canonical-format.md)).
2. Add `src/Wintodo.Data/Migrations/NNN_short_name.sql` with the next number.
   End it by recording its version:
   ```sql
   ALTER TABLE lists ADD COLUMN icon_emoji TEXT NULL;

   INSERT INTO schema_version(version, applied_utc)
   VALUES (2, strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));
   ```
3. Append the resource name to `MigrationResourceNames` in
   `src/Wintodo.Data/SqliteTaskStore.cs`. The array index **is** the version —
   order matters and entries are never removed or reordered.
4. Update `SqliteMapper` and the affected `SqliteTaskStore.*.cs` partial. Every
   statement is parameterised; never concatenate input into SQL.
5. Add tests in `tests/Wintodo.Data.Tests/MigrationAndListTests.cs`:
   - a fresh database reaches the new version;
   - an old-version database upgrades, keeps its rows, and gets a `.bak-N` file;
   - running the store twice is a no-op.
6. If the column is synced, extend `IcalReader`/`IcalWriter` and add a
   round-trip fixture assertion.

## Rules

- Additive changes only (`ADD COLUMN`, new tables, new indexes). If you need a
  destructive reshape, write an ADR first.
- Nullable or defaulted columns, so existing rows stay valid.
- Never store a credential in SQLite — `PasswordVault` only.
- Deleting user data is a soft delete plus a `change_log` row.

Reference migration: `002_list_icon_emoji.sql` with
[ADR-009](../adr/ADR-009-list-emoji-icons.md).
