# DEP-19 installed-package smoke preparation

Prepared 2026-11-09 from `f1b9af5` with the reviewed runner/test overlay.
**Runner preparation and 12 mocked/static regressions PASS. Actual guest
preflight, smoke automation, visual review and final deployment remain PENDING.**

[The runner](../../tests/ui/run-installed-package-smoke-gate.ps1) supplies the
first bounded installed-PID path identified by
[the deployment matrix](dep19-deployment-gate-matrix.md). It inspects an already
installed production-signed MSIX and its running native process. Its default
mode performs preflight; `-Execute` adds four smoke assertions. It never installs,
activates, registers, uninstalls or signs an app, changes certificate trust,
provisions/starts a guest or changes a checkpoint.

## Execution boundary and prerequisites

This script runs wholly inside the approved `WintodoTest` Hyper-V guest,
in the installed app user's interactive desktop session. The host can inspect
the new source and run the mocked regressions. Do **not** pass this runner to
`Invoke-VmGate.ps1`: that development wrapper restores `clean`, warms up the app
and registers a loose layout, invalidating the installed-package premise.

Preflight requires all of these before any UI command:

- Hyper-V's guest integration registry reports `VirtualMachineName` exactly
  `WintodoTest`, with `VirtualMachineId` equal to the GUID independently reviewed
  from that host VM's metadata. A GUID argument alone, a hostname or
  `HypervisorPresent` cannot substitute for the guest-only metadata.
- The observed computer is Microsoft's `Virtual Machine`, the native OS
  architecture equals the package target, Windows build is at least 26100,
  Developer Mode is off and the process has an interactive session.
- Final identity/publisher/version, source commit, signed-MSIX hash,
  packaged executable hash, signing-certificate thumbprint and expected window
  title are supplied from the reviewed candidate and customer-route decisions.
- The exact MSIX passes the existing structural/privacy inspector with all
  three identity expectations supplied. It contains a nonempty signature entry
  and the executable entry matches the reviewed executable hash.
- `Get-AuthenticodeSignature` reports `Valid` and its signing certificate matches
  the approved thumbprint. Signature presence alone does not pass this check.
  Windows' MSIX SIP/Authenticode behavior and actual trust state must still be
  exercised in the approved guest; this preparation has not done so. Unsupported
  or unverifiable signatures fail closed. Timestamp/customer-chain policy and
  the selected delivery route remain separate final gates.
- Exactly one current-user registration matches the exact identity, publisher,
  version and architecture. Its status is `Ok`, `IsDevelopmentMode` is false
  and `SignatureKind` is `Enterprise` or `Store`. Developer/unsigned registrations
  and loose development layouts fail preflight.
- The supplied PID responds in this desktop session and its exact executable
  path is the registered package's installed `Wintodo.exe`. That installed file's
  hash equals the executable entry in the reviewed MSIX. PID/name similarity
  cannot substitute for the path and hash association.

The MSIX and installed executable are held open without write/delete sharing
throughout the run. The process ID, start time, executable path, session and
responding state are rechecked around every assertion to reject changed or
reused PIDs. These are local path/hash/registration consistency checks, not
authenticated build provenance or a complete installed-payload comparison.
They do not query the process's OS `GetPackageFullName` identity or establish
how it was activated. Record the approved Start/customer-route activation
separately; a matching executable path cannot establish that history.

The exact Hyper-V guest metadata contract has not been observed through a live
guest session in this batch. Missing metadata rejects execution; no fallback
to the host or another guest is implemented. Native ARM64 needs its own approved
target with this contract. Cross-compilation or x64 emulation does not qualify.

## Default preflight and explicit automation

After production decisions, customer baseline, signing, installation and Start
activation have been separately authorized and completed, run from the guest's
copied repository using a private argument record. The placeholders below are
inputs to replace with reviewed values, not executable release approval:

```powershell
$inputs = @{
    PackagePath = '<exact signed guest MSIX path>'
    SourceCommit = '<full reviewed source commit>'
    PackageSha256 = '<signed MSIX lowercase SHA-256>'
    ExecutableSha256 = '<packaged Wintodo.exe lowercase SHA-256>'
    SignerThumbprint = '<approved signing certificate thumbprint>'
    ExpectedIdentity = '<approved package identity>'
    ExpectedPublisher = '<approved publisher>'
    ExpectedVersion = '<approved four-part package version>'
    Architecture = 'x64' # use ARM64 only on the approved native ARM64 target
    AppPid = 1234       # current installed process, activated from guest Start
    ExpectedVmId = '<host-reviewed WintodoTest GUID>'
    ExpectedTitle = '<approved main-window title>'
}
.\tests\ui\run-installed-package-smoke-gate.ps1 @inputs
```

Default preflight emits `scope: installed_package_smoke_preflight`, with
`preflight: PASS` only when its checks pass. `automation`, `visual_review` and
`deployment_acceptance` remain `PENDING`, and `release_authorized` remains false.
The source commit is a validated supplied candidate identifier; it is not
independently authenticated by this runner. Retain the separate signed-source
and customer-route records.

Only during the separately approved guest UI session, add `-Execute`. It also
requires the process-only guest UI opt-in established by that approved session.
The runner never sets that opt-in. A nonblocking guest-local named mutex rejects
another simultaneous installed-smoke run. A fresh WinApp workflow ID is scoped
to that run, yielded only after UI starts and restored afterward. Early failed
or default preflight cannot issue even `ui yield`.

Automation checks the responding installed process, exactly one visible
window with the approved title, a nonempty interactive tree with AutomationIds,
and a nonempty PNG of the discovered main HWND. Every WinApp command checks its
actual native exit code; absent exit codes cannot reuse a previous success.
Nested/window and flat inspection shapes are supported and limited to 4096
nodes. Each complete assertion gets a fixed PASS row; a failure stops further
assertions and emits a fixed stage category.

These reuse the four assertion concepts from `m0-smoke.ps1`, rather than calling
it unchanged. That older suite hardcodes the title `Wintodo`, can accept zero
interactive elements and does not check every native command's exit status.
The new runner allows the approved title, rejects empty audits and checks each
native exit. The existing M0 file is unchanged by this bounded package work.

## Evidence, privacy and scope limits

Every run uses a fresh child of the guest repository's ignored
`tests/ui/artifacts/installed-smoke/`. Existing directories and reparse-point
ancestors are rejected. New outputs cannot overwrite old PASS or screenshots.
The structured result contains only validated hashes/source/architecture,
fixed scope/status/assertion labels and fixed failure stages. It omits guest
GUID/name, process ID, installation paths, raw identity/publisher/title,
certificate objects, exception messages and UI element names.

The inspected structure report and captured `initial.png` remain private until
reviewed. A screenshot can contain task/account content even though the runner
does not export the inspection tree. Use the approved synthetic profile, inspect
the actual PNG and run the release privacy checks before sharing evidence.
An automation PASS leaves `visual_review: PENDING`; no clipping/theming/visual
claim can follow from mocked tests or a merely nonempty screenshot.

This gate does not create tasks, verify default storage/time/launch overrides,
restart, persistence, update/migration/recovery, provider/import/sign-in,
attachments, toasts/startup/share, reboot reminders, uninstall/cleanup or Store
delivery. Those rows in the deployment matrix stay pending. It also does not
prove the initial customer baseline or how installation occurred. Preserve
the separate baseline/install/signature/dependency evidence.

## Local verification and correction record

[Twelve focused regressions](../../tests/scripts/test_installed_package_smoke.py)
are discovered by the existing Python script-test command. They parse/extract
PowerShell functions and use synthetic objects or mocked commands; no UI,
package deployment, process killing or registry changes occur. They cover:

- Exact guest name/GUID and baseline rejection; normalized GUIDs; architecture,
  Developer Mode, OS build and interactive-session mismatches.
- Registration identity/version/publisher/architecture, development/signature
  kind/status, process ID/path/session/responding mismatches and reused PID.
- Flat/nested nonempty audits, missing AutomationIds and a bounded tree.
- Native error/exception/missing-exit rejection, correct PID selectors and
  selector-free workflow yield; signature trust/signer mismatch; missing/empty
  screenshot rejection; whole-runner host rejection before UI.

The first focused run found that an empty PowerShell selector became a null
native argument for `yield`. The selector construction was corrected, and the
rerun passed. During early mocked regressions, two real process-only UI opt-in
assignments ran in short-lived host subprocesses, contrary to the repository rule.
No actual WinApp/UI command was executed by those tests. Both assignments were
removed; guard independence is now proved by static ordering and pure guard
tests. The corrected 12-test rerun passes without setting any host opt-in.
This mistake and correction are retained rather than claiming it never occurred.
The public documentation privacy scan passes with one file and zero findings;
its sanitized report is `artifacts/dep-installed-smoke/public-doc-privacy.json`.

```powershell
python -B -m unittest discover -s tests/scripts -p test_installed_package_smoke.py
```

Guest preflight/automation, actual Appx/Authenticode/CIM contracts and visual
review have not run. No guest login/start/provisioning, installation, trust,
signing, remote mutation or publication occurred. Parent integration owns the
shared deployment/progress records and final review/commit.
