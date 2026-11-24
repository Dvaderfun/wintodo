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
| [015](ADR-015-share-target.md) | Complete the Windows text and URL share target | accepted |
| [016](ADR-016-microsoft-todo-import.md) | One-way Microsoft To Do import through Microsoft Graph | accepted |
| [017](ADR-017-recoverable-synced-task-moves.md) | Durable recovery for synced task moves, steps and undo | accepted |
| [018](ADR-018-direct-msix-first-release.md) | Direct MSIX first release with preserved production launch controls | distribution superseded by ADR-019; launch policy retained |
| [019](ADR-019-store-first-source-available-release.md) | Store-first under Univera LLC; free self-builds with commercial redistribution reserved | accepted |
| [020](ADR-020-pin-sdk-with-lockfiles.md) | Service the exact .NET SDK and its reviewed lockfiles together | accepted |
| [021](ADR-021-store-package-icon-proposal.md) | Use Glide for the Wintodo package icons | accepted |
| [022](ADR-022-public-free-store-launch.md) | Launch free in the public Store without a tester group; retain manual publication | accepted |
| [023](ADR-023-first-party-rights-and-draft-artwork.md) | Reserve official branding and monetization; retain eight backgrounds in the Store draft | accepted |
| [024](ADR-024-glide-taskbar-sizing.md) | Enlarge Glide at taskbar sizes | accepted |
| [025](ADR-025-my-day-suggestions.md) | Add rule-based My Day suggestions | accepted |

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
