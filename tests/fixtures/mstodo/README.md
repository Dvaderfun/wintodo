# Microsoft To Do (Graph) fixtures

Hand-written Microsoft Graph v1.0 responses shaped after the documented
`todoTaskList`, `todoTask`, `checklistItem`, `linkedResource` and
`taskFileAttachment` resources. They contain no real account data.

`FixtureGraphHandler` serves a folder by URL path: `GET /v1.0/me/todo/lists`
reads `me/todo/lists.json`, and a raw attachment (`…/$value`) reads
`…/content.bin`. Query strings are ignored.

`basic/` covers:
- a paged task list (`@odata.nextLink`)
- the default list
- an emoji-prefixed list name
- the flagged-email list
- a shared list that isn't owned
- steps
- a weekly recurrence and an unconvertible relative-monthly one
- a text note and an HTML note
- all importance and status values used by To Do
- a reminder in a Windows time zone
- categories and a linked resource
- an attachment
- an unknown property

It backs `Wintodo.Import.Tests` and the `--import-fixture` UI gate.
