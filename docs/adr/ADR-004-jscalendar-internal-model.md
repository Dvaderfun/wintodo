# ADR-004 — JSCalendar (RFC 8984) shapes the *internal* model, not the wire

- **Status:** accepted
- **Date:** 2026-07-30 (researched July 2026, locked before M0)
- **Origin:** written inline in `PLAN.md` §2; moved here verbatim by
  [ADR-010](ADR-010-ai-friendly-repo-docs.md). The text below is unchanged.

## Decision

RFC 8984 defines a `Task` object and is the modern, sane data model — nested
`participants`, `alerts`, `locations`, proper `duration`/`recurrenceRules`.
We shape our C# domain types after JSCalendar semantics (clearer than raw
iCalendar) but we **serialise to `VTODO`** because that is what servers accept.
The iCalendar↔JSCalendar conversion rules
(`draft-ietf-calext-jscalendar-icalendar`, rev 23, still a draft) are our
reference for the mapping table in `docs/DATA-MODEL.md`.
