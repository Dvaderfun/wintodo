# DEP-19 final signed deployment gate matrix

Prepared 2026-11-09 against source
`8d54cc15a2b2bd0e2e2a8cf847942ebb1ca73145` and the current
[deployment plan](deployment-readiness.md). **Inventory PASS; all final signed
deployment acceptance remains PENDING.** No installation, certificate trust,
guest start, guest authentication or UI automation was performed for this record.

## What can run next

The existing tests supply useful assertions, but there is no checked-in runner
that accepts an exact production-signed MSIX and carries it through clean
install, installed activation, update and uninstall. The next deployment work
is to prepare that approved session and reuse the assertions against its installed
PID, while preserving the development gate baseline. Producing another unsigned
package or passing a development-layout gate cannot close this gap.

[Invoke-VmGate.ps1](../../scripts/Invoke-VmGate.ps1) restores `clean`, copies
Debug x64 output (and optionally the published x64 AOT layout), uses `winapp run`
for development registration, and normally warms the app before resetting its
data. It has no signed-package input or native ARM64 execution path. Even
`-NoWarmup` retains development-layout registration. Its mutex also permits
only one gate at a time; agents must not run parallel gates in this VM.

The [testing guide](../guides/testing.md) and
[release checklist](../guides/release-checklist.md) already distinguish the two
baselines. The ordinary `clean` checkpoint includes Developer Mode, development
runtime/SDK tooling and the M10 test certificate. A separately identified clean
deployment baseline in `WintodoTest` must keep Developer Mode off and document
actual runtime and trust state. Preserve `clean`; the ordinary runner would
restore it and discard a deployment session.

## Read-only readiness observation

Hyper-V metadata on 2026-11-09 reports `WintodoTest` **Off**, Generation 2,
configuration version `12.2`, with a checkpoint named `clean` whose creation
time is `2026-10-14T03:04:35.855503+03:00`. Checkpoint creation time does not prove
the current guest SDK version, OS build, desktop readiness or native architecture.
No guest session was opened. The separate prior evidence in
[PROGRESS Current state](../PROGRESS.md) records guest SDK `10.0.401` versus
required minimum `10.0.403`, causing the attachment-provenance preflight to fail
before its tests. That existing failure remains unresolved by this inventory.

The metadata-only commands used were `Get-VM -Name WintodoTest` and
`Get-VMCheckpoint -VMName WintodoTest`, projecting only state, generation,
configuration version, checkpoint name and creation time. A sanitized snapshot
is private under `artifacts/dep19-gate-inventory/readiness.json`.

## Requirement-to-evidence matrix

All runtime rows below apply to **native x64 and native ARM64** unless stated
otherwise. Existing development runners are x64. No native ARM64 test target is
established by this record; cross-compilation and x64 emulation do not qualify.
Package hashes in every result must be computed after production signing.

| DEP-19 requirement | Existing coverage to reuse | Final-package procedure / evidence required | Current prerequisite or gap |
|---|---|---|---|
| Clean baseline, customer trust and dependencies | [Release checklist clean-install procedure](../guides/release-checklist.md); [installation draft](../releases/INSTALLATION.md) | Record OS build, native architecture, Developer Mode off, no registered app, installed dependency inventory and trust baseline. Verify exact downloaded hash and signature, then install through the selected customer route. Retain sanitized install result and package registration identity/version. | PENDING approved identity, signing, distribution and separately identified baseline. `Initialize-TestVm.ps1` creates a development baseline and cannot substitute. |
| Installed activation, first run, persistence and restart | [M0 smoke](../../tests/ui/m0-smoke.ps1), [M3 runner](../../tests/ui/run-m3-gate.ps1), [M4 detail](../../tests/ui/m4-detail.ps1), [onboarding import runner](../../tests/ui/run-import-gate.ps1) | Launch from Start; capture installed PID and registration. Use default LocalState and real time for first run. Create lists/tasks/steps and a benign attachment, close, reactivate from Start, and verify retained state. Preserve structured results and each capture. | PENDING signed bytes and installed-PID session. Existing M3/M4 runners launch layouts and use scratch SQLite; import runner requires Debug-only fixture switches. |
| Supported update, forward migration and recovery | [Schema compatibility tests](../../tests/Wintodo.Data.Tests/SchemaCompatibilityTests.cs), [migration tests](../../tests/Wintodo.Data.Tests/MigrationAndListTests.cs), [schema startup runner](../../tests/ui/run-schema-guard-gate.ps1) | Install an approved prior signed version in the disposable baseline, create representative state, close and back up full data, update through the customer route, restart and compare state. Retain old/new hashes, identities/versions, migration backup evidence and a separate backup recovery result. | PENDING supported prior-version/identity preservation decision. Unit upgrade and unchanged-newer-schema tests do not prove MSIX update or recovery in an installed app. Never substitute uninstall/reinstall for update. |
| Import/session/errors/cancel and untrusted attachments | [Import runner](../../tests/ui/run-import-gate.ps1), [provenance runner](../../tests/ui/run-attachment-provenance-gate.ps1), [provenance policy](dep10-attachment-provenance.md) | Use approved disposable sign-in accounts with final Release/AOT registration. Exercise cancel, retry, dismissal, expired-session recovery, attachment zone marking, benign opening through Windows, removal/restart and failed staging cleanup. Record metadata/cleanup counters and warning captures without tokens, account names or URLs. | PENDING guest SDK correction for existing regression gate, final installed execution and existing benign-document association. Debug fixture paths cannot run unchanged against production Release; live work/school consent remains a separate pending gate. |
| Concurrent pull edits, durable moves and restart | [Pull concurrency tests](../../tests/Wintodo.Sync.Tests/CalDavSyncServiceTests.PullConcurrency.cs), [pull persistence tests](../../tests/Wintodo.Sync.Tests/SyncPersistenceTests.PullConcurrency.cs), [move recovery tests](../../tests/Wintodo.Sync.Tests/SyncMoveRecoveryTests.cs), [move plan](dep04-synced-moves.md), [undo runner](../../tests/ui/run-undo-gate.ps1) | In the installed app, interrupt a move and restart at a recoverable boundary; verify confirmed destination before source deletion, parent/steps, newer edits and undo. Commit a local edit while a pull is delayed and verify retained content plus explicit conflict. Capture resulting task/version counts and recovery outcome. | PENDING final-package/UI scenario. Existing fake transport/SQLite regressions establish logic, while the M13 loopback UI suite does not exercise durable-move interruption or concurrent pull conflict. A controllable disposable server/session is needed. |
| Live Radicale/Nextcloud, offline edits, unknown properties and every chooser outcome | [M13 runner](../../tests/ui/run-m13-gate.ps1), [M13 UI suite](../../tests/ui/m13-sync.ps1), [manual convergence procedure](../guides/release-checklist.md) | For both real servers, record versions and second client, then verify CRUD on each side, offline edit/reconnect, unknown-property round-trip, and separate Keep mine/theirs/both conflicts. Run chooser UIA in the guest attached to the installed PID; inspect the actual resulting server resources. | PENDING disposable live endpoints and final-package chooser automation. Existing runner starts `m13-caldav-server.ps1`, not Radicale/Nextcloud, and its UI suite covers configuration/sync controls rather than the conflict chooser. |
| M6 external-client import | [M6 iCalendar suite](../../tests/ui/m6-ical.ps1), [external import procedure](../guides/release-checklist.md) | Export from the installed final app, import into current Nextcloud Tasks or Thunderbird, and verify due dates, priority, parent/steps and recurrence. Record external-client versions, expected values and actual results. | PENDING manual external-client session. UI file export plus fixture round-trip is separate evidence. Provisioning another client/provider requires the approved session. |
| M7 reboot reminder and visible badge; M8 Narrator and keyboard | [M7 runner](../../tests/ui/run-m7-gate.ps1), [M8 suite](../../tests/ui/m8-design-accessibility.ps1), [manual procedures](../guides/release-checklist.md) | Use real time and reboot the guest without checkpoint restoration. Observe the exact reminder with app closed; separately verify rendered badge count/update/clear. Listen to Narrator during keyboard create/due/complete/delete and verify usable focus. Record what was delivered, rendered and spoken. | PENDING manual final-package session. Scheduled/database counts do not prove a rendered badge; accessibility-tree checks do not prove Narrator speech. Save evidence before any checkpoint restore. M9 remains owner-deferred. |
| Normal shell, toast actions, startup and ShareTarget in final AOT | [M7 runner](../../tests/ui/run-m7-gate.ps1), [notification settings runner](../../tests/ui/run-notification-settings-gate.ps1), [Share runner](../../tests/ui/run-share-gate.ps1), [Share regression runner](../../tests/ui/run-share-regression-gate.ps1) | Activate the installed shell from Start, act on Complete/Snooze with app closed, verify selected startup behavior after sign-in, and share Text/WebLink from the native source fixture. Check persisted tasks, expected process exit and already-open shell refresh, retaining installed registration/hash. | PENDING installed-package adaptation. Share `-Aot` calls `winapp run --no-launch` to register a layout; it is not signed-package installation. Share regressions combine Debug M0/M7 with optional AOT M13. M7 verification modes need the same database and cannot be run by a fresh restoring VM invocation. |
| Uninstall, integration/file cleanup and reinstall/PasswordVault behavior | [Release checklist cleanup procedure](../guides/release-checklist.md), [privacy draft](../releases/PRIVACY.md), [installation draft](../releases/INSTALLATION.md) | From the approved disposable profile, record baseline, exercise app integrations/accounts, uninstall through the customer route, and check registration/startup/jump-list/toasts and app-created files. Distinguish intentional exports/backups/custom databases. Inspect only synthetic credential resources; record counts and reinstall behavior without credential values. | PENDING final signed uninstall session and documented cleanup outcome. No checked-in end-to-end uninstall/PasswordVault cleanup gate exists. Test-only certificate trust must be removed when the approved direct-install test uses it. |
| WinUI review, public evidence privacy and perf claims | [WinUI review skill](../../.agents/skills/winui-code-review/SKILL.md), [privacy scanner](../../scripts/Test-ReleasePrivacy.py), [performance guide](../guides/performance.md) | Review changed UI with nothing above informational; inspect every public screenshot and structured result for private data. Scan exact final signed package bytes and reviewed metadata. Any new perf claim needs environment-labelled rows in PERF. | PENDING exact signed bytes and final captures. This docs inventory introduces no UI change or perf claim. M9 is deferred and must not be relabelled PASS. |

## Concrete commands and their boundaries

The following existing host commands are **development regression gates**, to run
sequentially after the owner's guest SDK setup and the matching host build.
They prepare useful regression evidence; none installs the signed candidate.

```powershell
dotnet build Wintodo.sln -p:Platform=x64 -warnaserror
python -B tests/ui/New-SchemaGuardSeed.py
.\scripts\Invoke-VmGate.ps1 -Gate run-schema-guard-gate.ps1 `
  -Include 'tests\ui\artifacts\schema-guard\seed-v6.db'
.\scripts\Invoke-VmGate.ps1 -Gate run-import-gate.ps1
.\scripts\Invoke-VmGate.ps1 -Gate run-attachment-provenance-gate.ps1
.\scripts\Invoke-VmGate.ps1 -Gate run-undo-gate.ps1
.\scripts\Invoke-VmGate.ps1 -Gate run-m13-gate.ps1 -GateArguments '-Aot'
```

The last command additionally requires a matching fresh x64 AOT publish. Share
development coverage requires the host-built native fixture:

```powershell
.\scripts\Build-ShareSource.ps1
.\scripts\Invoke-VmGate.ps1 -Gate run-share-gate.ps1 -GateArguments '-Aot' `
  -Include 'tests\ui\artifacts\share-source\share-source.exe'
```

For final-package testing, use Windows App Installer / approved Store installation,
then Start menu activation in the approved guest. The existing direct installation
alternative is `Add-AppxPackage -Path '<verified-final.msix>'`, executed in that
guest only after session approval and the chosen customer trust requirements.
Record hashes and signature/trust results **before** installation. Do not use
`winapp run <layout>` or the packaged executable for this installed-package gate.

Some lower-level suites can attach to that captured installed PID. For example,
`m0-smoke.ps1 -AppPid <guest-installed-pid>` and
`m8-design-accessibility.ps1 -AppPid <guest-installed-pid>
-ArtifactDirectory <guest-evidence-directory>` run assertions without their
development launch wrapper. They still need an approved guest UI workflow and
their own fixture assumptions checked. Do not pass the guest PID to host UIA.
M13's lower-level restart step invokes `winapp run $AppDirectory`, so its whole
suite cannot be reused unchanged for final installed activation.

The existing read-only static final-package checks can run on the host after
signing, substituting the actual immutable signed paths and approved identity,
publisher and version where the structural inspector supports them:

```powershell
python -B scripts/Test-ReleasePackage.py '<final-x64.msix>' --architecture x64 `
  --output artifacts/final-x64-structure.json
python -B scripts/Test-ReleasePackage.py '<final-arm64.msix>' --architecture ARM64 `
  --output artifacts/final-arm64-structure.json
python -B scripts/Test-ReleasePrivacy.py '<final-x64.msix>' '<final-arm64.msix>' `
  --private-root $PWD --require-architecture x64 --require-architecture ARM64 `
  --output artifacts/final-signed-privacy.json
```

Structure/privacy cannot replace signature trust verification or installed
runtime behavior. `Test-ReleaseCandidate.py` intentionally rejects signed
packages: its unsigned handoff is not a final signed-deployment gate.

## Execution order and required approvals

1. Record distribution, final identity/version and supported prior versions,
   signing/trust requirements and production override policy. Preserve the owner
   app/data; only approved disposable installations participate in this matrix.
2. Refresh the development guest SDK through owner-invoked setup and run affected
   development regressions. Reuse their assertions and fixtures when preparing
   the installed-package session; do not mark final rows passed from those runs.
3. Finish protected-source candidate/signing evidence and compute new final
   signed checksums. Static checks cover both delivered architectures.
4. Prepare a separately named customer baseline and native ARM64 target with
   approved provisioning/install/trust sessions. Record dependencies and trust
   before adding anything; install only what the selected customer route requires.
5. Run installed first launch, persistence, update/recovery, provider, import,
   attachment, shell/integration and manual rows. Run destructive uninstall last.
   Retain the same guest state for reboot/reminder and update; checkpoint restores
   must not erase the scenario being proved.
6. Save and inspect all evidence, sanitize public records, and record one explicit
   PASS/FAIL/PENDING/SKIPPED/DEFERRED row per requirement and architecture. Only
   then can DEP-19 close and an approved DEP-20 publication be requested.

The approval boundaries come from [AGENTS.md](../../AGENTS.md):
"Ask the owner before: ... anything outside this repo", and the deployment plan
execution rule: "Ask before certificate trust, guest provisioning/install
sessions, changes outside the repository ... or toolchain setup requiring owner
approval." The [packaging skill](../../.agents/skills/winui-packaging/SKILL.md)
also states "Packaging alone is not permission to install an app." This inventory
does not request or imply those approvals. Ordinary source/build/unit work and
read-only metadata inspection can continue independently.

For direct MSIX, validate the approved publisher chain/timestamp and installed
bytes through App Installer or Add-AppxPackage. For Store, use the approved
Partner Center identity, submission validation and Store-delivered installation/
update path; a locally packaged development MSIX cannot prove that path. If both
routes are selected, retain separate route evidence even if app payloads match.
