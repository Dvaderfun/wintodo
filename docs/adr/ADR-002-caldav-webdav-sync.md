# ADR-002 — Sync protocol (M13): CalDAV (RFC 4791) + WebDAV Sync (RFC 6578)

- **Status:** accepted
- **Date:** 2026-07-30 (researched July 2026, locked before M0)
- **Origin:** written inline in `PLAN.md` §2; moved here verbatim by
  [ADR-010](ADR-010-ai-friendly-repo-docs.md). The text below is unchanged.

## Decision

- Discovery: `.well-known/caldav` → `current-user-principal` → `calendar-home-set`.
- Collection enumeration: `PROPFIND` with `supported-calendar-component-set`,
  filter to collections advertising `VTODO`.
- Incremental sync: `sync-collection` `REPORT` (RFC 6578) with a stored
  `sync-token`. Fallback for servers without it: `getctag` + `calendar-query`
  `REPORT` returning `getetag`, then diff.
- Conflict detection: per-resource `ETag`, `If-Match` on `PUT`, `If-None-Match: *`
  on create.
- Auth: HTTP Basic over TLS (app passwords — what Nextcloud/Fastmail actually
  hand out), with an OAuth path deferred.
- Alarms: RFC 9074 (`VALARM` with `UID`, `RELATED-TO` snooze relationships).
