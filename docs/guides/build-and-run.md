# Build and run

## Prerequisites

| Tool | Version | Check | Install / upgrade |
|---|---|---|---|
| .NET SDK | 10.0.4xx (pinned in `global.json`, `latestFeature` roll-forward) | `dotnet --list-sdks` | `winget upgrade Microsoft.DotNet.SDK.10` |
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

### App command-line options (for tests and debugging)

| Option | Effect |
|---|---|
| `--database-path <file>` | Use this SQLite file instead of `LocalState\wintodo.db`. Use a scratch file for anything destructive. |
| `--clock-file <file>` | Read "now" from the file (`FileBackedClock`) so time-dependent behaviour is testable. |

## Diagnose a crash

Run attached (the default adds `--debug-output`). On a WinUI crash, WinApp CLI
runs stowed-exception triage and prints the real XAML error behind an opaque
`0x8000FFFF` / `E_FAIL`. Add `--symbols` for richer native frames. The app also
traces `WINTODO_UNHANDLED_EXCEPTION=` and, in Debug, `WINTODO_BINDING_FAILED=`
lines to the debug output.

## Native AOT

Release builds of the app publish as Native AOT, self-contained.

```powershell
.\scripts\Publish-Aot.ps1                         # x64 + ARM64 into publish\aot, size report
winapp run src\Wintodo.App\Wintodo.csproj --aot -c Release --arch x64 --detach --json
```

Plain `winapp run -c Release` builds JIT and does **not** validate AOT. Fix every
IL/trim/AOT warning; never suppress one to get green. Performance numbers come
from the AOT build — see [performance.md](performance.md).
