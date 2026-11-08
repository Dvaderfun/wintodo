# DEP-12 — Supported production override policy

Status: **Policy A selected/documented; 26 host launch-policy tests PASS;
guest and final-package acceptance PENDING.**
Prepared on 2026-11-08 against `812dd73`; source rechecked on 2026-11-09 against
`db6c448`, followed by the reviewed launch-policy extraction. The owner authorized the proposed retain/document approach with
"Lets do it" on 2026-11-09. This records established behavior and extracts
testable startup decisions without changing supported behavior or an accepted
ADR. DEP-12 stays open until its required runtime checks pass.

Selected policy: retain and document the current local launch overrides
in Release, including Native AOT, as advanced diagnostics/test controls. Keep
Graph fixtures, persistent trace-file output and notification scratch-path
propagation Debug-only. Broker shares continue to use package LocalState and
the real system clock in every configuration.

This preserves the ability to exercise the same Release/AOT app code with a
disposable database. A database override selects only the SQLite file; it is
not a portable mode or an isolated app profile. Attachments and other package
state remain under the package's LocalState directory.

## Recorded decision

The selected policy is A. B remains an unselected alternative from the original
proposal, with its implementation consequences retained for reference.

| Choice | Result | Verification consequence |
|---|---|---|
| **A — Keep and document existing overrides (selected 2026-11-09)** | Current Release/AOT launch behavior stays supported for advanced local use. Debug-only fixture/trace behavior and broker defaults stay explicit. | Existing AOT M13, Release diagnostics, warmup and performance paths keep testing the ordinary Release app. Add missing default/precedence/time/share checks. |
| B — Restrict overrides to test builds | Production Release ignores database, environment, clock, onboarding and scroll-output controls. A distinct build opt-in enables them for test Release/AOT artifacts. | Adapt affected gates and label test artifacts clearly. Production candidate still needs separate default-storage/time and functional deployment checks; an opt-in build alone cannot prove the shipped package. |

Choice A is documentation and verification of established behavior. Choice B
changes established launch behavior, including the onboarding contract in
[ADR-016](../adr/ADR-016-microsoft-todo-import.md); record the decision and obtain
acknowledgement before applying that change. Neither choice authorizes host UI
automation, certificate trust, guest provisioning or publication.

## Supported behavior matrix

The table describes established code rechecked for selected Policy A. Release
JIT and Release Native AOT share these preprocessor paths. This is source
evidence, not a new runtime gate result.

| Input / activation | Debug ordinary launch | Release / Native AOT ordinary launch | Broker ShareTarget, Debug and Release |
|---|---|---|---|
| No overrides | Package `LocalState/wintodo.db`; `SystemClock` | Same | Same, used directly by the share handler |
| `--database-path <file>` | Resolved with `Path.GetFullPath`; wins over environment | Same | Ignored by the share handler |
| `WINTODO_DATABASE_PATH` | Used when command-line path is absent/blank and no eligible Debug notification path applies | Used when command-line path is absent/blank | Ignored |
| `--clock-file <file>` | `FileBackedClock` reads invariant UTC text on each `UtcNow` access; local timezone remains system timezone | Same | Ignored; share uses `SystemClock` |
| `--show-onboarding` | Allows normal fresh-database onboarding when a command-line database path would otherwise suppress it | Same | No Welcome/import shell is created |
| `--import-fixture <directory>` | Replaces the Graph connector with synthetic fixture transport | Ignored; real connector remains | No Graph/import connector is created |
| `--import-fixture-expire-once` | With a fixture connector, first sign-in simulates expired attachment authorization | Ignored | No effect |
| `--import-fixture-throttle-once` | With a fixture connector, simulates throttling | Ignored | No effect |
| `--trace-file <file>` | Installs an app trace listener with safe app formatting | Ignored; no app trace-file listener | Debug constructor can install the listener before activation routing; Release cannot |
| `--perf-scroll-result <file>` | When the list reaches the requested item threshold, creates parent directory, writes `<file>.ready` and the numeric result JSON | Same | No task-list probe is created |
| `--perf-scroll-items <count>` | Threshold for the scroll probe; absent or unparseable value defaults to 10,000 | Same | No effect |
| Toast payload `debugDatabasePath` | Accepted only for AppNotification activation and an existing file named `m7-gate.db`, after command-line precedence | Ignored | No effect |

Supported usage and scope:

- `--show-onboarding` allows onboarding; it does not force Welcome for an
  already onboarded or populated database. Only a **command-line** database
  path suppresses onboarding. An environment-selected database retains the
  ordinary fresh-database onboarding checks.
- A custom SQLite file does not move imported/attached files, application
  settings stored outside that file, credentials or broker share storage.
  `CreateImportEnvironment` still selects the package LocalState attachment
  root. Tests needing whole-profile isolation continue to use the disposable VM
  package, as the existing broker share gate does.
- Settings persisted through the selected SQLite store, including task
  preferences, onboarding completion and shell placement/navigation, follow that
  database. Files in LocalState and PasswordVault entries do not. Imported and
  manually attached files use the package's `attachments` root even with a
  custom database; a SQLite-only backup cannot preserve the whole profile.
- Ordinary startup resolves overrides before notification/task handling.
  Debug M7 toasts can carry their validated scratch path across activation;
  Release toasts do not embed that Debug field. The supported matrix must not
  promise that Release notifications or broker shares follow a command-line
  scratch database across later activations.
- Value options are parsed from process command-line arguments with exact,
  case-sensitive names; the first matching value wins. A recognized value
  option without a following argument throws. There is no general unknown-flag
  rejection. Fixture modifiers alone have no effect without a fixture connector.
- Overrides are local launch inputs, not persisted launch preferences. A later
  Start, startup-task or toast activation resolves its own process inputs.
  Non-share activations use the same database/clock resolution; no Release toast
  embeds or restores a previous command-line database path. A shell opened on a
  custom database and a broker share can therefore write different databases.
- These are locally controlled launch inputs. The inventory does not classify
  them as remote vulnerabilities. A clock file can affect stored task timestamps
  and time-dependent behavior; use synthetic data for reproducible gates.
- `WINTODO_ALLOW_LOCAL_UI` is a harness guard, not an app capability or Release
  override. It is set only in the guest by the authorized VM runner. Test-seeding
  `WINTODO_PERF_*` variables belong to test projects, not the production app.

## Source evidence

Paths are relative to the repository. Line anchors refer to the inspected source
and may move during later work.

| Behavior | Responsible source |
|---|---|
| Share routing before ordinary startup resolution | [App.xaml.cs](../../src/Wintodo.App/App.xaml.cs#L68), `OnLaunched` |
| Command-line / notification / environment / default database precedence | [App.xaml.cs](../../src/Wintodo.App/App.xaml.cs#L305), `ResolveDatabasePath`, `IsM7GateDatabasePath` |
| Import connector and command-line-only onboarding suppression | [App.xaml.cs](../../src/Wintodo.App/App.xaml.cs#L344), `CreateImportEnvironment`; [LaunchOptions.cs](../../src/Wintodo.App/Services/LaunchOptions.cs#L53), `AllowsOnboarding` |
| Flag/value parsing and clock selection | [App.xaml.cs](../../src/Wintodo.App/App.xaml.cs#L371), `HasCommandLineFlag`, `ResolveClock`, `GetCommandLineOption` |
| Debug persistent app trace listener | [App.xaml.cs](../../src/Wintodo.App/App.xaml.cs#L42), `App` constructor |
| Broker storage and time fixed in both configurations | [App.Share.cs](../../src/Wintodo.App/App.Share.cs#L43), `HandleShareAsync` |
| Per-read file clock and unchanged timezone | [FileBackedClock.cs](../../src/Wintodo.App/Services/FileBackedClock.cs#L6), `UtcNow`, `LocalTimeZone` |
| Scroll output controls and fixed numeric output | [ScrollPerformanceProbe.cs](../../src/Wintodo.App/Controls/ScrollPerformanceProbe.cs#L25), `TryStart`, `Finish` |
| Debug notification scratch path and state export | [WindowsIntegrationService.cs](../../src/Wintodo.App/Services/WindowsIntegrationService.cs#L23), `DebugDatabasePathArgument`, `BuildNotification`, `WriteGateNotificationState` |
| Welcome requires unset completion and an otherwise fresh database | [ShellViewModel.Import.cs](../../src/Wintodo.App/ViewModels/ShellViewModel.Import.cs#L70), `InitializeOnboardingAsync` |
| Package LocalState attachment staging | [ImportWizardViewModel.cs](../../src/Wintodo.App/ViewModels/ImportWizardViewModel.cs#L320), `ImportAsync`; `CreateImportEnvironment` |
| Manually attached files and path resolution remain package-local | [TaskDetailViewModel.Attachments.cs](../../src/Wintodo.App/ViewModels/TaskDetailViewModel.Attachments.cs#L39), `AttachFileAsync`, `TryResolveAttachmentPath` |
| Database-backed shell placement/navigation follows the selected store | [ShellStateService.cs](../../src/Wintodo.App/Services/ShellStateService.cs#L17), settings-store constructor and load/save methods |
| Credential location remains PasswordVault | [PasswordVaultCredentialStore.cs](../../src/Wintodo.App/Services/PasswordVaultCredentialStore.cs#L8), `_vault` |
| Executable value/flag, lazy database, onboarding and clock policy | [LaunchOptions.cs](../../src/Wintodo.App/Services/LaunchOptions.cs#L8), linked unchanged into [Core.Tests](../../tests/Wintodo.Core.Tests/Wintodo.Core.Tests.csproj) |

Related accepted decisions: [ADR-013](../adr/ADR-013-hyperv-test-vm.md) fixes
the guest-only gate boundary; [ADR-016](../adr/ADR-016-microsoft-todo-import.md)
defines scratch-database onboarding. Existing operational descriptions are in
[build and run](../guides/build-and-run.md), [testing](../guides/testing.md),
[performance](../guides/performance.md) and
[diagnostic privacy](../guides/diagnostics.md).

## Affected verification paths

| Path | Dependence on current behavior |
|---|---|
| [Invoke-VmGate.ps1](../../scripts/Invoke-VmGate.ps1#L153) | Warmup launches with a scratch database before resetting the disposable guest package. |
| [run-m13-gate.ps1](../../tests/ui/run-m13-gate.ps1#L54) | Both Debug and `-Aot` runs select a scratch database. Its restart suite also preserves that database. |
| [run-release-diagnostics-gate.ps1](../../tests/ui/run-release-diagnostics-gate.ps1#L56) | Release JIT selects a disposable database, including a newer-schema fixture, and proves `--trace-file` is ignored. |
| [Measure-Perf.ps1](../../scripts/Measure-Perf.ps1#L165) | Release JIT/AOT candidates use seeded SQLite files and the numeric scroll-output controls. |
| [run-import-gate.ps1](../../tests/ui/run-import-gate.ps1#L45) | Debug scratch database, file clock, fixtures, trace file, onboarding opt-in, expiration and throttling. |
| [run-m7-gate.ps1](../../tests/ui/run-m7-gate.ps1#L74) | Debug scratch database and special notification path propagation/state inspection. |
| [run-share-gate.ps1](../../tests/ui/run-share-gate.ps1) | Uses real broker activation and disposable package LocalState, including `-Aot`; no scratch-path exception is needed. |
| [run-schema-guard-gate.ps1](../../tests/ui/run-schema-guard-gate.ps1#L39) | Selects a synthetic newer-schema database for startup rejection. |
| M0/M3/M4/M5/M6/M8, settings, undo, sorting and layout gate runners | Scratch database launch; time-sensitive M5/settings/undo/sort paths also use `--clock-file`. |

## Documentation and remaining acceptance

Policy A is recorded here. The [operational option table](../guides/build-and-run.md),
[installation/recovery draft](../releases/INSTALLATION.md) and
[privacy draft](../releases/PRIVACY.md) now describe supported overrides and their
database-only scope. The direct-MSIX-first route is selected, while exact public
identity, legal/contact fields, signing and final deployment remain pending.
Existing production behavior stays intact. A BCL/Core-only `LaunchOptions` helper
now owns the extracted value/flag, lazy database, onboarding and clock decisions.
App retains platform activation routing, Debug notification eligibility and
environment lookup; LocalFolder is evaluated only when no selected override
exists. Core.Tests compiles the same production helper and existing file clock,
with no App project reference, dependency or lock changes. This work adds no portable
profile, settings UI, Release fixture support or storage format.

Required acceptance evidence:

1. Debug and Release/AOT default startup use the disposable guest package's
   LocalState database and real `IClock`; a fresh package receives Welcome.
2. Selected command-line/environment database precedence, first-value behavior
   and missing-value handling match the documented matrix. A file clock supplies
   the selected UTC instant and retains the local timezone; default time remains
   real. Synthetic inputs cover paths with spaces and an invalid clock value.
3. Command-line scratch onboarding suppression and explicit opt-in still pass.
   Add the environment-only fresh-database case so its existing difference is
   intentional and visible.
4. Release ignores Graph fixture modifiers and `--trace-file` without installing
   their Debug behavior. Numeric scroll output works where intentionally used;
   ordinary launches create no probe artifacts.
5. Real Debug and Release/AOT broker shares persist to package LocalState and
   use real time even when supported launch/environment overrides are present.
   Verify scratch sentinel files are unchanged, attachment behavior is unaffected
   and no main-shell/import graph is created by ShareTarget.
6. Existing affected gates pass in `WintodoTest`, including AOT M13, Release
   diagnostics, import and share. Run the performance override path only when
   needed to prove it works; make no unmeasured performance claims.

For the unselected Choice B, additionally define one explicit test build property, separate
output/package handling and a complete production-default replacement for
gates whose scratch controls are removed. Record any required ADR amendment
before editing. Build/test artifacts must be identified honestly in release
evidence; final deployment checks still use the exact production candidate.

The 2026-11-09 source review confirms precedence, first-value parsing, clock,
onboarding, Debug exclusions, broker defaults and package-local attachment/vault
scope. The focused [LaunchOptionsTests](../../tests/Wintodo.Core.Tests/LaunchOptionsTests.cs)
run passes **26/26**, zero failed/skipped, exercising the production helper for
case/first-value/missing-value behavior, lazy command-line/fallback/default path
selection, invalid selected paths, command-line versus environment onboarding,
real/file clock selection, per-read UTC normalization, timezone and missing/invalid
clock contents. These tests use synthetic files and never activate the app.

```powershell
dotnet test --project tests/Wintodo.Core.Tests/Wintodo.Core.Tests.csproj `
  -p:Platform=x64 --filter-class '*LaunchOptionsTests' --report-trx
```

Host proof is scoped to pure decisions. Actual Windows LocalFolder, Debug toast
eligibility, Release fixture/trace exclusion, app error/onboarding presentation,
broker activation, numeric probe and affected guest/final Release/AOT gates in
the six acceptance rows remain PENDING. Previously recorded runtime gates are
background evidence only. No UI, performance or installed-package gate ran in
this stream. Documentation results and private focused logs are under
`artifacts/dep12-policy/`; parent integration records full build/unit outcomes
separately without converting host tests into final-package acceptance.
