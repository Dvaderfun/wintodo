"""Create the synthetic database used by the guest schema-compatibility gate."""

import sqlite3
from pathlib import Path


destination = Path(__file__).resolve().parent / "artifacts" / "schema-guard" / "seed-v8.db"
destination.parent.mkdir(parents=True, exist_ok=True)
if destination.exists():
    raise SystemExit("Schema seed already exists; keep the existing fixture.")

connection = sqlite3.connect(destination)
try:
    with connection:
        connection.executescript(
            "CREATE TABLE schema_version(version INTEGER PRIMARY KEY, applied_utc TEXT NOT NULL);"
            "INSERT INTO schema_version VALUES(8, '2026-11-08T00:00:00Z');"
            "CREATE TABLE tasks(uid TEXT PRIMARY KEY, summary TEXT NOT NULL);"
            "INSERT INTO tasks VALUES('synthetic-schema-task', 'Synthetic newer-schema task');"
        )
finally:
    connection.close()

print("Created tests/ui/artifacts/schema-guard/seed-v8.db with synthetic schema 8.")
