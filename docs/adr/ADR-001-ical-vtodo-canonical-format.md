# ADR-001 — Canonical task format: iCalendar `VTODO` (RFC 5545)

- **Status:** accepted
- **Date:** 2026-07-30 (researched July 2026, locked before M0)
- **Origin:** written inline in `PLAN.md` §2; moved here verbatim by
  [ADR-010](ADR-010-ai-friendly-repo-docs.md). The text below is unchanged.

## Decision

`VTODO` is the only task format with real cross-vendor deployment. Everything we
would ever want to sync with speaks it: Nextcloud Tasks, Radicale, Baïkal, SOGo,
Fastmail, Thunderbird, Apple Reminders, Tasks.org (Android), DAVx⁵, todoman.

Our domain model is a superset of `VTODO` and every field maps to a property.
Anything we invent lives under an `X-WINTODO-*` property so it degrades to a
no-op on other clients instead of corrupting them.
