# ADR-014 — Show the sort indicator only for non-default ordering

- **Status:** accepted
- **Date:** 2026-10-18
- **Supersedes:** DESIGN §3.1's always-visible sort mode label
- **Owner acknowledgement:** Explicitly approved in the UX-14 implementation
  request on 2026-10-18, before implementation.

## Context

The header repeats the current sort mode even at its default. Microsoft To Do
uses a compact Sort affordance and shows an indicator when ordering changes.
Smart lists have different defaults; Completed always orders by completion time.

## Decision

Keep the compact Sort dropdown. Show a Sorted by … chip with direction and a
reset action below the header only when effective ordering differs from that
list's default. Reset restores its default mode and direction together.
Preserve sorting choices, existing AutomationIds and persistence semantics.
DESIGN §3.1 describes the defaults and Completed's effective-order comparison.

## Consequences

Default state is quieter. User lists persist reset via the existing sort store
operation; smart-list state keeps its existing lifetime. Regression coverage
checks defaults, reversed ordering, reset, navigation and restart persistence.

## Alternatives considered

Keep the always-visible label (declined by the owner), or reset every list to
Manual (incorrect for Important, Planned, All and Completed).
