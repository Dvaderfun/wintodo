# Wintodo privacy policy

Publisher: Univera LLC. Contact: support@univera.app. This policy applies to Wintodo 1.0 for Windows.

You can use Wintodo without an account. Your tasks and lists stay on your Windows device unless you choose to sync, export or share them. Univera LLC does not operate a task server or account service for Wintodo.

## What stays on your device

Wintodo stores task titles, notes, dates, reminders, steps, list membership, completion history, settings and sync records in a local database. It also preserves extra calendar properties from imported iCalendar files. Attachments are copied to local app storage, with their filenames, paths relative to that storage, sizes and content types. Downloaded Microsoft attachments are staged locally before being saved. Where supported, Windows marks those downloads as coming from the Internet without storing the source URL or account identity in the marker.

If you connect a CalDAV account, Wintodo records its display name, username, server and calendar addresses. Passwords and app passwords are kept separately in Windows PasswordVault. If you import from Microsoft To Do, the app records the source account identifier, display name or email, last import time and item links so it can recognize a repeat import. Closing the import wizard does not remove imported items or this history.

The default database and attachments are in the app’s local storage. If you start Wintodo with a custom database location, attachments, import staging and credentials keep their usual locations. A local clock override can affect dates and reminders for that launch. Text or links sent to Wintodo through Windows Share use the default local database and the real clock.

## When information leaves your device

CalDAV sync: When you connect a server and sync a list, the server you select receives your credentials and the task data in that list, including changes to tasks you imported. Wintodo also reads tasks and changes from that server. Use a trusted HTTPS server. Its access, retention and privacy rules apply.

Microsoft To Do import: Microsoft handles sign-in in your browser. Wintodo reads profile details, lists, tasks, steps, linked resources and the attachments chosen for import. It requests the Microsoft permissions Tasks.ReadWrite and User.Read because the list-changes endpoint requires the broader permission. The import sends read requests only; it does not change your tasks in Microsoft To Do. The access token stays in memory for the import session. Closing the wizard does not sign you out of your browser or revoke consent previously given to Microsoft.

Export, attachments and links: Wintodo writes exported tasks and retained calendar properties to the file location you choose. People you give the file to can read it. Opening an attachment or a link hands it to the application registered with Windows; that application or service has its own privacy practices.

## Notifications and diagnostics

Windows reminders and taskbar features can show task information on your device. Your Windows notification and lock-screen settings control who can see reminder text. Wintodo has no publisher-operated advertising, analytics or crash-upload service. Its diagnostic traces use operation names, error categories, HTTP status codes, counts and timings; they exclude task text, account names, credentials and URLs. Persistent diagnostic trace files are a Debug-build feature. Windows, connected providers and other applications can have their own diagnostics. Review any crash dump or log before sharing it for support.

## Your choices and deletion

You choose which accounts to connect, which lists to sync or import, and where to export. Disconnecting a CalDAV account removes its saved credential; you may first need to resolve pending task moves. Disconnecting does not erase information on the server.

Deleting a task, list or import marks records as deleted so undo and synchronization can work. It does not immediately erase every copy. Removing an attachment deletes its managed local copy when accessible; your original file, exports, migration backups and copies held by a provider may remain. Exported files and custom databases may be outside the app’s storage. Do not rely on uninstalling to erase all data or credentials. Back up anything you want to keep before resetting or removing the app.

For privacy, security or support questions, contact support@univera.app. A description of the issue is usually enough; share task or account contents only if needed and agreed.
