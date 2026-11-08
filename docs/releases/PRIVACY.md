# Privacy policy — unpublished review draft

**DRAFT — not approved for publication.** Prepared 2026-11-08 from source
`4fa223d`; updated 2026-11-09 against `db6c448` for selected production overrides
and direct-MSIX-first delivery. Wintodo is a placeholder name. The final publisher,
privacy/security contact and effective date must be supplied before release.
Final-package network and uninstall checks remain pending.

Wintodo stores tasks on your Windows device. You can use local lists without
connecting a task account. Optional CalDAV sync exchanges tasks with a server
you choose; Microsoft To Do import copies information from your Microsoft
account into the app. There is no Wintodo-operated task server or account service
in the reviewed implementation.

## Information stored on your device

The local SQLite database contains task titles, notes, dates, recurrence, steps,
list membership, completion information, preferences and synchronization state.
The app preserves supported iCalendar fields and properties it does not
understand when tasks are imported or synchronized. These properties can also
contain information supplied by you or another calendar client.

CalDAV account records contain the display name, username, server and discovered
calendar addresses, synchronization information and a credential lookup key.
Passwords and app passwords are stored separately in Windows PasswordVault;
they are not stored in the SQLite database.

Microsoft import history contains the source account identifier and display
email/name, last-import time and links between source and local items. Source
item identifiers and matching fingerprints support repeat imports. Completing
or dismissing sign-in does not remove information already imported or its
persisted import history.

Attached files are copied into the app's local storage. Their filenames, relative
paths, sizes and content types are recorded with the task. Microsoft downloads
use temporary import staging before final copies are saved. Completed downloads
and imported copies receive an Internet-zone marker where the filesystem
supports it. The marker does not contain the source URL or account identity.

By default, the database and attachments are in the installed package's
LocalState folder. The selected production policy supports advanced local
database overrides in Release, including Native AOT. A custom database location
changes SQLite and its database-backed preferences/history, while attachments,
import staging and credentials retain their existing locations. Local clock-file
inputs can affect task timestamps and date/reminder behavior without changing
Windows' timezone. These launch inputs do not create a portable profile or a
persisted preference for later launches. Windows Share saves received text or
web links to the normal package-local database and uses the real clock, even
when local launch overrides select another database/time for the main shell.

## Optional connections and disclosure

| Action you choose | Recipient and information |
|---|---|
| Connect a CalDAV account and sync a list | Your selected server receives authentication and task information represented in iCalendar `VTODO` resources. Local changes, including imported tasks in a synchronized list, can be uploaded. The server also supplies remote tasks and changes to the app. |
| Import Microsoft To Do | Microsoft handles browser sign-in and receives authorized Graph requests. The app reads profile information, lists, tasks, steps, linked resources and selected attachment content into local storage. |
| Export a list | An iCalendar file is written to the location you select. Anyone you share that file with can read the exported task content and retained calendar properties. |
| Open an attachment or external link | Windows opens the selected file or link using its registered application. That application's and service's privacy practices apply. |

Microsoft sign-in uses the system browser, authorization code with PKCE and a
local loopback redirect. The requested delegated permissions are
`Tasks.ReadWrite` and `User.Read`. The broader task permission is required by
the list-delta endpoint used to read lists; the reviewed Graph client sends
GET requests only. Import does not write changes back to Microsoft To Do.
Imported tasks subsequently edited locally remain independent of their
Microsoft source, apart from an explicitly requested repeat import.

The Microsoft access token is kept in memory for the import session and is not
persisted. Success and expired sign-in release the authenticated connection;
final dismissal, reset and app shutdown clear session state after in-flight
work finishes. Browser sign-in and Microsoft consent can remain active outside
the app; dismissing the wizard does not sign you out of your browser or revoke
Microsoft's consent record.

Use a trusted HTTPS CalDAV endpoint. Your chosen server and Microsoft process
connection data under their own privacy policies. This draft does not establish
their retention, location or security practices.

## Notifications and diagnostics

Windows integration uses local reminder notifications, the taskbar badge, jump
lists and optional launch at login. Notifications can display task information
through Windows; consider your Windows notification and lock-screen settings
when tasks contain private content.

The reviewed application source and declared dependencies contain no configured
publisher-operated analytics, advertising or crash-upload service. This is a
source-review finding, not a completed network-capture verification of the final
package. Windows, Microsoft sign-in, your CalDAV provider and applications you
open can have their own diagnostics or network behavior.

App diagnostic traces use fixed operation labels, allowlisted error categories,
HTTP statuses, counts and timings. They exclude raw exception messages, task
text, account names, credentials and URLs. The durable `--trace-file` listener
exists only in Debug builds. External debugger output, crash dumps and tool logs
can contain private information; review them before sharing. Support reporting
instructions and the organizational security contact remain undecided.

## Deletion, retention and your choices

You control which accounts to connect, which lists to sync or import, and where
to export files. A successful CalDAV disconnect removes the stored account's
credential from PasswordVault; disconnect is blocked while unresolved task
moves need that account. Disconnect is not a request to erase the server's data.

Deleting tasks or lists uses soft deletion and a change log to support undo and
synchronization. It is not immediate secure erasure of database rows, attachment
content, migration backups or provider copies. Removing an import run also uses
soft deletion for the items it created. Removing an attachment through its
detail-pane action updates the task and deletes its managed local file when
accessible; independently retained originals and exports remain yours to manage.

Migration backups can retain older task information. Exported files and custom
database locations can exist outside the package's storage. Uninstall/reinstall
cleanup, PasswordVault retention and Windows integration cleanup require final
deployment verification. This draft therefore makes no complete-erasure promise
for uninstall. Back up anything you want to keep before reset or removal.

## Review basis

The evidence and outstanding publication requirements are recorded in
[DEP-17 release documentation](../plans/dep17-release-docs.md). The
[data model](../DATA-MODEL.md), [diagnostic policy](../guides/diagnostics.md),
[import decision](../adr/ADR-016-microsoft-todo-import.md) and
[attachment provenance policy](../plans/dep10-attachment-provenance.md) provide
the current technical descriptions. Replace this review section and unresolved
draft fields with the approved customer-facing policy before publication.
