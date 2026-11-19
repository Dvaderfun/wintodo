# Build and run

## Prerequisites

| Tool | Version | Check | Install / upgrade |
|---|---|---|---|
| .NET SDK | Exact stable version in `global.json` (currently 10.0.404) | `dotnet --version` from the repo | Use that SDK side by side; a newer SDK alone does not satisfy the pin |
| WinApp CLI | **0.7+** | `winapp --version` | `winget upgrade --id Microsoft.WinAppCli` |
| Developer Mode | on | Settings → System → For developers | **ask the owner** — it needs UAC |
| Git | any recent | `git --version` | `winget install Git.Git` |

Visual Studio is optional. The XAML compiler diagnostics gap that once made it
necessary was fixed in Windows App SDK 2.1.3; we are on 2.5.1.

`/winui-setup` verifies and repairs all of the above. Run it only when the owner
asks for setup; otherwise report what is missing.

## Build

```powershell
dotnet build Wintodo.sln -p:Platform=x64 -warnaserror      # all projects, analyzers on
dotnet build Wintodo.sln -p:Platform=x64 -c Release -warnaserror
```

`Platform` is `x64` or `ARM64`. Never `AnyCPU`. The `WinUIAnalyzer` NuGet package
and the trim/AOT analyzers run in every build, so a green `dotnet build` is the
same verdict CI and the IDE give.

**Build fails?** Read every error, fix them all in one pass, rebuild once. A
cryptic `MSB3073` / `XamlCompiler.exe exited with code 1` with no file named is
no longer expected on this SDK version — treat it as a real XAML error and look
at the `.xaml` files changed since the last green build.

## Run

```powershell
.\BuildAndRun.ps1                       # Debug, attached, --debug-output — invoke async
.\BuildAndRun.ps1 --detach --json       # returns {"AUMID": ..., "ProcessId": ...}
.\BuildAndRun.ps1 -c Release --arch arm64
.\BuildAndRun.ps1 --args "--database-path C:\temp\scratch.db"
```

`BuildAndRun.ps1` is a thin wrapper over project-mode
`winapp run src\Wintodo.App\Wintodo.csproj`; any `winapp run` option works.
Attached runs stay attached to the app, so start them asynchronously.

Never launch the packaged `.exe` directly, never add
`<WindowsPackageType>None</WindowsPackageType>`, never delete
`Package.appxmanifest`.

### Supported local launch overrides (DEP-12)

Policy A, selected on 2026-11-09, supports these advanced local controls in
Debug, Release JIT and Release Native AOT. They are process launch inputs;
Start, startup-task and toast activations resolve their own inputs. The default
is the package's `LocalState\wintodo.db` and the real system clock.

| Input | Effect | Configuration |
|---|---|---|
| `--database-path <file>` | Resolve an absolute SQLite path; it wins over `WINTODO_DATABASE_PATH`. Use a scratch database for destructive tests. | Debug and Release/AOT |
| `WINTODO_DATABASE_PATH` | Select SQLite when the command-line path is absent/blank; an eligible Debug notification scratch path takes precedence over this environment value. | Debug and Release/AOT |
| `--clock-file <file>` | Read invariant date/time text from the file on each `UtcNow` access, assuming/normalizing UTC; the local timezone remains Windows' timezone. | Debug and Release/AOT |
| `--show-onboarding` | Allow normal fresh-database onboarding with a command-line database override. It does not force Welcome on a populated/onboarded database. | Debug and Release/AOT |
| `--perf-scroll-result <file>` | Opt into numeric scroll evidence: create `<file>.ready`, then write numeric JSON at the requested path. | Debug and Release/AOT |
| `--perf-scroll-items <count>` | Item threshold for that probe; absent or unparseable values use 10,000. Without a result path no probe starts. | Debug and Release/AOT |
| `--import-fixture <directory>` | Use the recorded Graph fixture connector. | Debug only; ignored in Release/AOT |
| `--import-fixture-expire-once`, `--import-fixture-throttle-once` | Enable the corresponding fixture simulation; alone they have no effect. | Debug only; ignored in Release/AOT |
| `--trace-file <file>` | Install the app's safe persistent trace listener. | Debug only; ignored in Release/AOT |

Value options use exact case-sensitive names and a separate following argument;
the first matching option wins. A recognized value option with no following
argument throws. There is no `--name=value` form or general unknown-option
rejection. Quote paths with spaces. A clock-file example is
`2026-11-09T12:00:00Z`; missing/unreadable files or invalid contents fail when
the app reads the clock. A synthetic clock affects timestamps and time-dependent
behavior, so use synthetic data and remove it for real-time reminder checks.

A database override changes SQLite and the settings persisted in that database.
Attachments, import staging and other package files remain under LocalState;
credentials stay in PasswordVault. It does not create an isolated or portable
profile. Only a **command-line** database override suppresses onboarding by
default; an environment-only override keeps normal fresh-database checks.

Broker ShareTarget activation always uses package LocalState and real time,
ignoring database/environment/clock overrides, in Debug and Release/AOT. It
creates no shell/import graph or scroll probe. A Debug constructor can still
install `--trace-file` before share routing; Release cannot. Debug M7 toasts can
carry an existing `m7-gate.db` scratch path; Release never propagates that field,
so later notifications do not restore a prior command-line scratch path.

These controls do not authorize host UI automation. Whole-profile/UI isolation
still uses the approved test VM. The [full policy matrix](../plans/dep12-production-overrides.md)
retains pending default/precedence/clock/share runtime acceptance. M9 performance
remains owner-deferred; a scroll-output switch is not a performance gate result.

## Diagnose a crash

Run attached (the default adds `--debug-output`). On a WinUI crash, WinApp CLI
runs stowed-exception triage and prints the real XAML error behind an opaque
`0x8000FFFF` / `E_FAIL`. Add `--symbols` for richer native frames. Treat debugger
output and crash dumps as private: external tools can include raw runtime data.
The app's `WINTODO_UNHANDLED_EXCEPTION=` and other failure lines contain only
reviewed type and HTTP status codes, without messages or stack traces. Debug
`--trace-file` captures these app lines; Release has no app trace-file listener.
Native binding tracing is disabled because it logs raw details independently
of app handlers. Compiled `x:Bind`, analyzers and affected guest gates remain
the binding checks. See [diagnostics.md](diagnostics.md) for privacy evidence.

## Native AOT

Release builds of the app publish as Native AOT, self-contained.

```powershell
.\scripts\Publish-Aot.ps1                         # x64 + ARM64 into publish\aot, size report
winapp run src\Wintodo.App\Wintodo.csproj --aot -c Release --arch x64 --detach --json
```

Plain `winapp run -c Release` builds JIT and does **not** validate AOT. Fix every
IL/trim/AOT warning; never suppress one to get green. Performance numbers come
from the AOT build — see [performance.md](performance.md).
