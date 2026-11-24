# ADR-025 — My Day suggestions

- **Status:** accepted
- **Date:** 2026-11-23
- **Supersedes:** the suggestions exclusions in PLAN §1 and M5
- **Owner acknowledgement:** approved in the 2026-11-23 request to implement Suggestions with overdue tasks first, then upcoming tasks

## Context

Wintodo includes tasks due today in My Day automatically and lets users remove them for the current day. UX-19 adds a way to review other unfinished tasks across lists while planning the day.

## Decision

Place an icon-only **Suggestions** lightbulb with a tooltip and accessible name in the upper-right of the My Day header, immediately before the overflow menu. Use Wintodo's native styling and open a keyboard-accessible sidebar to the right of My Day. Show overdue tasks first, then upcoming tasks in due-date order, due-today tasks explicitly removed from My Day, and undated tasks pinned yesterday. Each task appears once. Use the existing local-day calculation. The first day can show eligible overdue and upcoming tasks immediately.

Each result can be added to My Day or marked complete through the existing task actions. Adding preserves the task's source list. Completion uses existing recurrence and undo behavior. Keep the sidebar open across row actions, display an empty state when results run out, and return focus to the trigger on close. A separate carry-over prompt can be considered independently.

## Consequences

Cover empty and first-run lists, rollover and resume, all-day and timed due dates, same-day removal and explicit re-addition, cross-list tasks, duplicate criteria, deleted or completed tasks, recurring tasks, and edits or sync updates while suggestions are open. Validate keyboard navigation and focus, narrow layout, and high contrast through a WintodoTest UI gate.

## Amendment — Undated tasks

Show other unfinished tasks without a due date in a **No Due Date** section after the existing groups. Tasks that were never in My Day can be added directly from this section.

## Amendment — Section labels and recent additions

The owner's 2026-11-24 request names the overdue and future sections **Earlier** and **Later**. Show remaining tasks created today or up to three local calendar days ago under **Recently added**, immediately above **No Due Date**.
