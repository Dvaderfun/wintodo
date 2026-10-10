# ADR-007 — No iCalendar library dependency in v1

- **Status:** accepted
- **Date:** 2026-07-30 (researched July 2026, locked before M0)
- **Origin:** written inline in `PLAN.md` §2; moved here verbatim by
  [ADR-010](ADR-010-ai-friendly-repo-docs.md). The text below is unchanged.

## Decision

`Ical.Net` v5 is the only serious .NET option; it is event-centric, its `VTODO`
support is thin, and its Native AOT / trimming posture is unverified (targets
listed as .NET 8 / .NET Standard). We do **not** take the dependency for v1.

M6 ships a small purpose-built iCalendar reader/writer in
`Wintodo.Core/Ical/` covering exactly the property set in `docs/DATA-MODEL.md`
plus verbatim passthrough of everything else. Line unfolding, `\`-escaping,
parameter parsing, `DATE` vs `DATE-TIME` vs `DATE-TIME;TZID`, and `RRULE`
round-trip must be unit-tested against real exports from Nextcloud Tasks,
Tasks.org and Apple Reminders (fixtures committed under `tests/fixtures/ical/`).
Budget: ~700 lines plus tests. Re-evaluate `Ical.Net` at M13 only if the
hand-rolled parser proves inadequate; the criteria are (a) it passes the AOT
publish gate warning-free and (b) it round-trips the fixture corpus losslessly.
