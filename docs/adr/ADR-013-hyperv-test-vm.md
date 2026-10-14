# ADR-013 — UI gates, perf rows and clean-install checks run in a Hyper-V VM

- **Status:** accepted
- **Date:** 2026-10-14
- **Supersedes:** nothing (narrows the "Sandbox or a dedicated VM" wording of the
  main-machine rule added on 2026-10-11)
- **Owner acknowledgement:** the owner chose the `WintodoTest` Hyper-V VM over Windows
  Sandbox on 2026-10-11 and requested this setup on 2026-10-14.

## Context

The development machine is the owner's main desktop. UI gates drive real input,
move the cursor, steal focus and change Windows settings (contrast, animations).
Since 2026-10-11 every UI script refuses to run unless `WINTODO_ALLOW_LOCAL_UI=1`
is set for an owner-authorized session, so UI gates were pending.

Windows Sandbox would need every script migrated to `winapp --on sandbox` with
every UI command routed to the guest. It needs an open, non-minimized client
window and cannot produce valid performance numbers. It also has no persistent
state for M10 install/uninstall checks.

## Decision

A Generation 2 Hyper-V VM named `WintodoTest` (4 vCPUs, 8 GB static memory,
80 GB dynamic disk, Default Switch, Secure Boot, vTPM) runs all UI gates, UI-driven
perf measurements and M10 clean-install checks.

- `scripts/New-TestVm.ps1` creates the VM.
- `scripts/Install-TestVmWindows.ps1` installs Windows 11 Pro unattended. An
  `autounattend.xml` on a temporary second virtual DVD sets up a local
  administrator `tester` and skips OOBE. The DVD boot prompt is answered through
  the VM's own virtual keyboard (`Msvm_Keyboard`), so no input reaches the host.
  It generates the guest password and stores it with DPAPI at
  `%LOCALAPPDATA%\Wintodo\WintodoTest.credential.xml`, outside the repo.
  Guest user locale and time zone follow the host (`uk-UA`, FLE Standard Time
  when written); the UI language and keyboard are en-US.
  `scripts/Set-TestVmCredential.ps1` replaces the saved credential if the
  password is changed by hand.
- `scripts/Initialize-TestVm.ps1` prepares the guest over PowerShell Direct:
  automatic sign-in (password as an LSA secret), no lock or timeouts, Developer
  Mode, PowerShell 7, the .NET SDK pinned in `global.json`, the Windows App
  Runtime and WinApp CLI versions pinned in `Directory.Packages.props`, the M10
  test certificate. It then takes a standard
  checkpoint `clean` that includes memory, so a restore resumes at a signed-in desktop.
- `scripts/Invoke-VmGate.ps1 -Gate run-*.ps1` (or `Measure-Perf.ps1`) restores
  `clean` and copies the repo's sources plus the host-built app output to the same
  repo-relative paths. It runs the gate unchanged in the guest's desktop session
  through an interactive scheduled task, copies artifacts (and a changed
  `docs/PERF.md`) back and returns the exit code.
- `WINTODO_ALLOW_LOCAL_UI=1` is set only in the guest gate process. The host guard stays.
- Host enhanced session mode is off (`Set-VMHost -EnableEnhancedSessionMode
  $false`, owner-approved on 2026-10-14; the owner uses no other VMs). Hyper-V has
  no per-VM switch, and an enhanced (RDP) connection takes over the guest console
  and locks it. `Invoke-VmGate.ps1` also refuses to run when the console desktop
  isn't signed in and unlocked.

## Consequences

- UI gates no longer need local opt-in. Each run starts from the same state and
  leaves nothing behind (the VM is turned off; the next restore discards changes).
- `clean` includes the .NET SDK pinned in `global.json` and a warm NuGet cache.
  Each run copies the repo's sources, and gates that seed databases with
  `dotnet test` get `tests\Wintodo.Data.Tests` built in the guest first.
- The guest never builds the app (no Windows SDK/MSVC). Debug, Release and AOT
  outputs are built on the host and copied: Debug for every gate, AOT for `-Aot`,
  Release FD + AOT for `Measure-Perf.ps1` (run with `-SkipBuild`).
- m7 toast actions and reboot persistence remain manual rows; they need a person
  acting on a real toast.
- Every run is a cold first launch in the guest (11–12 s to the main window).
  `Invoke-VmGate.ps1` warms up with one launch plus `Reset-AppxPackage` before UI
  gates, so suites written against warm dev-machine launches run unchanged.
  Perf runs skip it. The cold run also exposed a vacuous pass in the m0 smoke
  AutomationId check when no window existed.
- The build's `Wintodo.build.appxrecipe` holds absolute host paths, so it and
  the host `AppX\` layout are not copied; winapp lays out the folder in the guest.
- Perf rows measured in the VM are a different environment from earlier host rows.
  `docs/PERF.md` must label the environment, and budgets compare like with like.
- A new runtime or WinApp version in `Directory.Packages.props` requires
  re-running `Initialize-TestVm.ps1 -Force` to rebuild `clean`.
- The guest stores its own password as an LSA secret; the host stores a
  DPAPI-protected copy. Neither is in the repository. The answer file is built in
  ignored `artifacts/vm-staging/` and deleted once Windows is up.
- The guest uses Microsoft's generic Pro installation key and is not activated.
  Personalisation is limited, and an activation watermark can appear in screenshots.

## Alternatives considered

- **Windows Sandbox:** rejected for the reasons in Context.
- **Run gates in a plain PowerShell Direct session:** impossible; it has no
  interactive desktop, so UI Automation and input injection fail.
- **Keep enhanced session on and rely on Basic session:** workable, because
  restoring `clean` repairs a console taken over by an enhanced connection. But
  one accidental enhanced connection during a run breaks that run, and the owner
  doesn't need the feature.
- **Manual Windows install by the owner:** replaced by the unattended install;
  same result without owner time.
- **Production checkpoints:** they boot cold, so every run would wait for
  automatic sign-in; standard checkpoints resume signed in.
