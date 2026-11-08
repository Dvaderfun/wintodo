# iCalendar interoperability fixtures

These files are sanitized, minimized fixtures shaped from public exports and
client documentation. They contain no personal data:

- `nextcloud.ics` follows a published Nextcloud Tasks export example:
  <https://help.nextcloud.com/t/nextcloud-tasks-with-php-and-html/57705>
- `tasks-org.ics` follows Tasks.org's documented manual-order convention:
  <https://tasks.org/docs/manual_sort_mode/>
- `thunderbird.ics` follows Mozilla Calendar VTODO examples and timezone/alarm
  behavior discussed in Thunderbird bug 1872989:
  <https://bugzilla.mozilla.org/show_bug.cgi?id=1872989>
- `apple-reminders.ics` follows a published Apple Reminders VTODO export:
  <https://gist.github.com/n8henrie/d2288416b28dd90d3b1c>
- `malformed.ics` is an intentionally invalid local regression fixture.
- `task-move-missing-revision.ics` is hand-written synthetic input without
  SEQUENCE, modeled parent or sort metadata. It retains sibling and opaque
  vendor relations to prove a locally authored move preserves unknown links
  while adding its revision, or promoting a step out of its modeled parent.

The corpus verifies repeatable parser, writer, storage, and UI behavior. It does
not replace the M6 gate's manual import of a Wintodo export into current
Nextcloud and Thunderbird installations.
