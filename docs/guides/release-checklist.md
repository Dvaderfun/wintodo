# Release readiness: UX-20 and M10

Prepared 2026-10-18 after the completion viewport repair (`23881eb`). This is
the remaining-work checklist, not a record of passed manual gates. Record
observed results in `docs/PROGRESS.md`; only the owner can close UX-20 rows.
M9 performance remains owner-deferred.

## Owner decisions

| Decision | Current value | Needed before |
|---|---|---|
| App display name | Wintodo (placeholder) | Final icons, screenshots and listing copy |
| Package identity / publisher | GUID identity, `CN=AppPublisher` | Production signing and final install gates |
| Distribution | Undecided: Store, direct MSIX, or both | Signing and release CI |
| Release version | `1.0.0.5` in the manifest (local header-descender test update) | Release tag; tags do not change this value |

For Store distribution, use the exact reserved Partner Center identity and
publisher. For direct distribution, the signing certificate subject must match
the manifest publisher. A display name alone is insufficient to choose either.
Review preservation of existing package data before changing identity; a new
identity has a different package data folder. Do not uninstall the owner's app
to test the transition.

## Manual gate session

All app interaction stays inside `WintodoTest`. Open its console with
`vmconnect localhost WintodoTest`. Use a disposable database and test calendar
accounts. Keep the real clock for the reboot reminder check.

The existing gate runner restores `clean` on **every invocation**. After
starting an owner-run session with `-KeepRunning`, do not invoke another gate
until the evidence has been saved: that would discard the session's database,
reminders and server state. A reboot must reboot the guest without restoring a
checkpoint. Save screenshots and exports under `C:\wintodo\tests\ui\artifacts`
inside the guest and copy them out before the next restore.

Build the Debug x64 output on the host before invoking a gate. To prepare a
general manual session with the current sources and a signed-in guest:

```powershell
.\scripts\Invoke-VmGate.ps1 -Gate run-m0-gate.ps1 -KeepRunning
```

In the guest console, launch its copied app layout with a scratch database:

```powershell
winapp run C:\wintodo\src\Wintodo.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64 `
  --detach --json -- --database-path C:\wintodo\tests\ui\artifacts\owner-manual.db
```

### M6: export to another calendar client

1. In the guest, launch the packaged app with `winapp run` using a disposable
   database. Create or import a list containing a date-only due task, a timed due task, a
   high-priority task, a task with steps, and a recurring task. Record the
   expected dates, priority, relationships and recurrence before exporting.
2. Export that list through its menu to an `.ics` file. Use the app's actual
   export, rather than importing only the input fixtures.
3. Import the file into a current Nextcloud Tasks or Thunderbird installation
   using a disposable calendar. Record client/server versions and capture the
   imported tasks and their details.
4. PASS requires the exported tasks to appear with correct dates, priority,
   steps/parent relationships and recurrence. Record client limitations or
   differences explicitly; do not treat a successful file-open as a pass.

The checked-in fixture corpus and automated M6 gate remain separate evidence.

### M7: reboot reminder and visible badge

Prepare the isolated session from the host:

```powershell
.\scripts\Invoke-VmGate.ps1 -Gate run-m7-gate.ps1 -KeepRunning
```

The runner prints the guest scratch database path and closes the app after its
preflight. In the **guest console**, relaunch that same Debug output with
`winapp run <app-folder> --detach --json -- --database-path <printed-db-path>`.

1. Create a uniquely named reminder five minutes ahead with notifications
   enabled. Record its due time and close the app.
2. Reboot Windows inside the guest, sign in, and wait through the due time.
   PASS requires the reminder for that exact task to appear while the app is
   closed. Capture the toast and note the reboot and delivery times. If boot
   finishes after the scheduled time, repeat with enough time to distinguish
   scheduled delivery from launch/resume recovery.
3. Relaunch the same database. With a known set of incomplete overdue and
   due-today tasks, capture the actual taskbar badge and compare its count to
   the union of those tasks. Complete one and confirm the badge updates; clear
   the remaining qualifying tasks and confirm it clears.
4. Record reboot reminder and visible badge as separate results. The database
   preflight's expected count does not prove that Windows rendered the badge.

Complete/Snooze persistence was already verified previously; these remaining
rows do not require reclassifying that evidence.

### M8: Narrator listening check

In an isolated guest session, enable Narrator with `Win+Ctrl+Enter`. Use the
keyboard to create a task, give it a due date, navigate to its row, complete it,
then delete it (and handle the confirmation if enabled). Listen for the title,
due date, completion state, and completion state-change announcement. Verify
that each operation can be completed without the mouse, and that focus remains
usable after completion/deletion. Record what Narrator actually says and any
missing or duplicate announcements. Disable Narrator after the check.

PASS requires the listening and keyboard workflow; the automated accessibility
audit alone does not close this row.

### M13: real-server convergence and conflicts

Use disposable calendars on **both Radicale and Nextcloud**, recording versions
and the second client used. The existing loopback server gate is not evidence
for either real server. Keep credentials in the app's credential store and out
of screenshots, logs and the repository.

For each server:

- Create, edit, complete and delete on each side; after one successful sync
  cycle, verify both sides agree and no duplicate tasks appeared.
- Disconnect network access, edit locally, reconnect, sync and verify that
  queued changes converge.
- Start from the same task revision, edit it differently on both sides before
  sync, and verify the conflict chooser appears. Exercise Keep mine, Keep
  theirs and Keep both with separate conflicts; verify the resulting task(s)
  and that neither version disappears silently.
- Add an unknown iCalendar property from the other client, sync into Wintodo,
  edit a modeled field, sync out and inspect the server resource to confirm
  the unknown property survived.

The conflict-dialog UI Automation row remains pending until the actual dialog
has been exercised and its result captured. Any automation for this session
must run wholly in the guest. A manual screenshot alone does not close that
automation row.

## M10: package and release

Build commands and local test signing are in [packaging.md](packaging.md).
Current placeholder packages are useful for local validation; they are not
final branded, production-signed artifacts.

- [ ] Finalize identity, publisher, version and display strings.
- [ ] Replace placeholder icons: title-bar `.ico`, Store logo, square logos,
      wide tile, splash screen and required scale/target-size variants. Verify
      their manifest paths and appearance against the chosen brand.
- [ ] Capture release screenshots from the final build and prepare listing
      text, support/contact details, privacy policy and Store submission fields
      required for the selected distribution route.
- [ ] Build unsigned self-contained Native AOT x64 and ARM64 packages. Inspect
      the packaged manifests, executable architecture, assets and extensions;
      confirm only the reviewed `runFullTrust` capability is present. Retain
      artifact hashes and the source commit used to build them.
- [ ] Apply production signing according to the selected distribution route.
      Keep signing material in protected CI secrets, use an approved timestamp
      service for direct distribution, and verify the delivered signatures.
- [ ] Extend tag CI to sign and attach the approved artifacts to a release.
      The current workflow uploads unsigned build artifacts only. For Store
      upload, prepare the SDK/Partner Center submission artifact separately.
- [ ] Run the clean-install and uninstall checks below against the final
      delivered package. ARM64 runtime remains unverified without an ARM64
      machine; successful cross-compilation does not close that row.

### Clean-install environment and acceptance

`WintodoTest`'s existing `clean` checkpoint has Developer Mode, development
runtimes, and the M10 test certificate already trusted. It does **not** satisfy
the M10 clean-install premise. Prepare a separately identified baseline in that
VM with Developer Mode off, no Wintodo package registered, and no signing
certificate already trusted. Record installed runtime dependencies so a
preinstalled development runtime cannot mask a missing package dependency.
Preserve the normal `clean` checkpoint used by UI gates.

For direct MSIX testing, first verify installation rejects the untrusted test
certificate. Then, with owner approval for that guest session, trust the public
certificate in `LocalMachine\TrustedPeople`, install the signed package, and
launch the installed app from the guest's Start menu using packaged activation.
Do not run the `.exe` directly or substitute `winapp run <extracted-layout>`:
the latter creates a development registration and would invalidate this
installed-package check. Verify first launch, task persistence after restart,
and that the reviewed toast/startup/share declarations are retained. Keep
Developer Mode off.

Uninstall the tested package and check that package registration is gone and
no app-created files remain outside its package data folder. Inspect the known
startup/jump-list/integration surfaces as well as filesystem changes from the
recorded baseline. Keep exports intentionally created by the tester distinct
from app leftovers. Record PASS/FAIL, package hash, architecture, OS build,
dependencies, install/uninstall output and supporting captures. Remove any
test-only guest certificate trust after the session.

Certificate trust, guest provisioning and the install session must be approved
before execution. This checklist does not change the standard VM checkpoint.

## Result record

Use one row per observation when the owner reports results:

| Check | Result | Build/package hash | Environment/client | Evidence and notes |
|---|---|---|---|---|
| M6 external import | PENDING | | | |
| M7 reminder after reboot | PENDING | | | |
| M7 visible badge | PENDING | | | |
| M8 Narrator | PENDING | | | |
| M13 Radicale convergence/unknown properties | PENDING | | | |
| M13 Nextcloud convergence/unknown properties | PENDING | | | |
| M13 conflict chooser / UI Automation | PENDING | | | |
| M10 clean install / launch | PENDING | | | |
| M10 uninstall cleanup | PENDING | | | |

Append the confirmed results to PROGRESS History and update Current state.
Keep failed, skipped and deferred rows visible.
