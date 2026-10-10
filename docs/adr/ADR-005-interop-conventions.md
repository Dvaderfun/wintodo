# ADR-005 — De-facto interop conventions we must honour

- **Status:** accepted
- **Date:** 2026-07-30 (researched July 2026, locked before M0)
- **Origin:** written inline in `PLAN.md` §2; moved here verbatim by
  [ADR-010](ADR-010-ai-friendly-repo-docs.md). The text below is unchanged.

## Decision

These are not in any RFC but every real client implements them. Getting them
wrong makes us the app that breaks everyone else's data.

| Convention | Meaning | Who uses it |
|---|---|---|
| `RELATED-TO;RELTYPE=PARENT:<uid>` | Subtask → parent link | Nextcloud Tasks, Tasks.org, Thunderbird, EM Task |
| `X-APPLE-SORT-ORDER:<int>` | Manual sort position | Apple Reminders, Nextcloud Tasks, Tasks.org |
| `CATEGORIES:a,b,c` | Tags | Nextcloud Tasks, Thunderbird, Tasks.org |
| `PRIORITY` 1–4 / 5 / 6–9 | high / medium / low | Everyone |
| `STATUS` + `PERCENT-COMPLETE` + `COMPLETED` | Completion state | Everyone |
| **Unknown-property preservation** | Round-trip every property you don't understand | Mandatory. Non-negotiable. |

Apple Reminders flattens `RELATED-TO` hierarchies to a single level — expected,
document it in the sync UI, do not work around it.
