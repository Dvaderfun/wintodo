# ADR-006 — Recurrence handling on completion

- **Status:** accepted
- **Date:** 2026-07-30 (researched July 2026, locked before M0)
- **Origin:** written inline in `PLAN.md` §2; moved here verbatim by
  [ADR-010](ADR-010-ai-friendly-repo-docs.md). The text below is unchanged.

## Decision

Microsoft To Do's model: completing a recurring task advances it to the next
occurrence; the task itself never leaves the list.

The v1 implementation: keep **one** master `VTODO` carrying `RRULE`. On
completion, compute the next `DUE`/`DTSTART` from the rule, write the new dates,
reset `STATUS` to `NEEDS-ACTION`, and append a row to a local
`completion_history` table. This is what Tasks.org and Nextcloud Tasks do and is
the most interoperable simple behaviour.

Not doing: per-occurrence `RECURRENCE-ID` override instances. That is correct
`VTODO` recurrence but is poorly supported by clients and adds a large amount of
complexity for a v1. Revisit at M13 if server round-trips demand it.
