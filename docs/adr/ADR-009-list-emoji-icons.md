# ADR-009 - Optional emoji icons for user lists

- **Status:** accepted
- **Date:** 2026-08-04
- **Supersedes:** the emoji prohibition in `docs/DESIGN.md` section 13 for
  user-selected list icons only
- **Owner acknowledgement:** the repository owner explicitly requested custom
  emoji list icons to match Microsoft To Do.

## Context

The approved design represented every user list with its theme-colour dot and
prohibited emoji as icons. Microsoft To Do also lets a person choose an emoji
for a list, and the owner has made that behavior part of the parity target.

The list colour still controls the task-list accent. An emoji is separate
optional metadata used in the navigation pane; lists without one must retain
the existing colour dot. The fixed default Tasks list continues to use Home.

## Decision

- Add nullable `lists.icon_emoji` in forward migration 002. Existing databases
  are backed up before the migration, and existing rows remain `NULL`.
- Store one extended Unicode grapheme cluster, up to 32 UTF-16 code units. The
  value must contain an emoji symbol; ordinary letters and multi-icon strings
  are rejected by the shared domain validator.
- Put an icon button directly beside the editable title for non-default user
  lists. Its flyout offers a six-column preset grid and clear action while the
  existing list menu retains a custom-emoji dialog for any valid emoji outside
  that catalog. `Remove icon` restores the colour dot.
- Render the emoji with Segoe UI Emoji in the title header and navigation icon
  column. Keep emoji out of the navigation item's accessible name.
- Add `X-WINTODO-LIST-ICON` to whole-list iCalendar import/export so the new
  metadata has an open, round-trippable wire representation.
- Keep product and command icons in Segoe Fluent Icons. This ADR permits emoji
  only where it is user-provided list content.

## Consequences

- Existing list visuals and data are unchanged until a person selects an emoji.
- Theme colours remain independent and continue to tint headers, task controls,
  and the fallback navigation dot.
- The default Tasks list cannot receive an emoji through either UI or store API.
- CalDAV collection metadata currently synchronizes only the display name, so
  emoji icons, like list colours and backgrounds, remain local to this client.
  Manual `.ics` export/import preserves them.

## UX-15 amendment — quieter unset header icon (2026-10-18)

**Owner acknowledgement:** The owner approved the UX-15 proposal in this
session, including this amendment. The original decision above is retained.

- For a non-default user list without an emoji, show its 16 px theme-colour
  dot in the existing 44 px header button, without an idle border.
- Reveal the Fluent Emoji picker affordance only on hover or keyboard focus.
  Keep the semantic Button, tab stop, accessible name and AutomationId; Enter
  still opens the existing flyout. A selected emoji stays visible.
- High Contrast uses system window text for the idle header dot and native
  button hover/focus chrome. Navigation keeps its selected/unselected system
  colours appropriate to its own background.
- Clearing an icon returns to the dot. Presets, custom emoji, title editing,
  persistence and iCalendar metadata retain their existing behavior.
