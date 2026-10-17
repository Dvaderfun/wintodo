# Architecture decision records

One file per significant decision: context → decision → consequences.
Write one when you change architecture, the stack, the data format, a budget,
or a hard rule. Use the next free number and link it in the table below.

Never rewrite an accepted ADR — supersede it with a new one, and get the
owner's acknowledgement first (`AGENTS.md`, Working style).

| # | Decision | Status |
|---|---|---|
| [001](ADR-001-ical-vtodo-canonical-format.md) | iCalendar `VTODO` (RFC 5545) is the canonical task format | accepted |
| [002](ADR-002-caldav-webdav-sync.md) | Sync over CalDAV (RFC 4791) + WebDAV Sync (RFC 6578) | accepted |
| [003](ADR-003-jmap-rejected.md) | JMAP rejected for now | accepted |
| [004](ADR-004-jscalendar-internal-model.md) | JSCalendar shapes the internal model, not the wire | accepted |
| [005](ADR-005-interop-conventions.md) | De-facto interop conventions we honour | accepted |
| [006](ADR-006-recurrence-on-completion.md) | Recurrence advances one master `VTODO` on completion | accepted |
| [007](ADR-007-no-icalendar-library.md) | No iCalendar library dependency in v1 | accepted |
| [008](ADR-008-perf-budget-rebaseline.md) | Performance budgets re-baselined to the measured WinUI 3 floor | accepted |
| [009](ADR-009-list-emoji-icons.md) | Optional emoji icons for user lists | accepted |
| [010](ADR-010-ai-friendly-repo-docs.md) | AI-friendly repo layout, vendored skills, toolchain 0.7 | accepted |
| [011](ADR-011-ux13-validation-harnesses.md) | UX-13 validation with separately corrected harnesses | accepted |
| [012](ADR-012-session-undo.md) | Session undo with atomic field restoration and list-delete membership | accepted |
| [013](ADR-013-hyperv-test-vm.md) | UI gates, perf rows and clean-install checks run in the `WintodoTest` Hyper-V VM | accepted |
| [014](ADR-014-default-sort-indicator.md) | Show the sort indicator only for non-default ordering | accepted |

ADR-001…007 were written inline in `PLAN.md` §2 and moved here verbatim by ADR-010.

## Template

```markdown
# ADR-NNN — Title

- **Status:** proposed | accepted | superseded by ADR-NNN
- **Date:** YYYY-MM-DD
- **Supersedes:** what this replaces, or "nothing"
- **Owner acknowledgement:** who approved it, and how

## Context
## Decision
## Consequences
## Alternatives considered
```
