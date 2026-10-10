# ADR-012 — Session undo with field restoration

- **Status:** accepted
- **Date:** 2026-10-11
- **Supersedes:** nothing
- **Owner acknowledgement:** implements the approved UX-10 package requested by the owner; optional row-completion snackbar is separately pending.

## Context

UX-10 requires shared undo for destructive task/list actions and bulk edits,
preserved across navigation, with a 20-entry limit and CalDAV-safe restoration.
Restoring whole task snapshots would overwrite newer titles, notes, and remote
versions. Restoring every deleted task in a list would revive older deletions.

## Decision

ShellViewModel owns a Core UndoService with session-only labels, affected task
UIDs and async revert delegates. Snackbar expiry hides feedback; history remains
available to Ctrl+Z. Failed reverts retain their entry for retry. Task restore
reads current data in the writer transaction and applies only captured fields.
Undo flushes pending detail edits before restore. Remote changes invalidate
affected entries; local edits and push acknowledgements retain them.

Forward migration 003 records list-delete membership in `list_deleted_tasks`.
Restore revives only those tasks and logs new changes. If CalDAV collection
deletion was pushed, restore queues collection creation before task uploads,
clears obsolete task remote identity, and retains list/task UIDs locally.

## Consequences

No new dependency or reflection. Undo history ends when the process exits.
Schema upgrade preserves rows and writes the normal migration backup. Recurring
bulk completion undo restores occurrence dates, shifted alarms and the completion
count. Nonrecurring completion undo preserves subsequently edited dates/alarms.
The optional row-completion snackbar requires its separate DESIGN acknowledgement.

## Alternatives considered

- Whole-model snapshots: rejected because unrelated later edits would be lost.
- Restore all deleted rows in a list: rejected because earlier deletions would return.
- Disable undo for synced lists: unnecessary; collection re-creation is supported.
