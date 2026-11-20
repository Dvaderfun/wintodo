# November 2026 project dependency servicing

This update keeps the [.NET 10 LTS and Windows 11 26100 contract](../../Directory.Build.props).
The SDK is still exactly 10.0.404 (runtime 10.0.15). .NET 11.0.100 and
Microsoft.Data.Sqlite 11.0.0 are a separate major-version migration, not a
servicing requirement for this app.

| Project dependency | Previous | Selected |
|---|---:|---:|
| [Windows App SDK](https://github.com/microsoft/WindowsAppSDK/releases/tag/v2.7.0) | 2.5.1 | 2.7.0 |
| [Windows SDK BuildTools](https://learn.microsoft.com/windows/apps/windows-sdk/downloads) | 10.0.28000.2705 | 10.0.28000.2957 |
| [WinApp packaging tool and WinUI Analyzer](https://github.com/microsoft/WinAppCli/releases/tag/v0.8.0) | 0.7.1 | 0.8.0 |
| Microsoft.Data.Sqlite | 10.0.12 | 10.0.15 |
| [SQLite native engine](https://www.sqlite.org/releaselog/3_54_1.html) | 3.53.4 | 3.54.1 |
| xunit.v3 | 4.0.2 | 4.0.3 |
| Microsoft.Testing.Extensions.TrxReport | 2.5.1 | 2.5.3 |

The NuGet package and Windows minimum are different version numbers. The
new Windows SDK build tools do not increase the minimum Windows version.
The Windows App SDK now selects its modular packages at 2.7.0; its ML
module requests `Microsoft.Windows.AI.MachineLearning >= 2.5.96, < 3`.
The existing direct 2.5.96 selection satisfies that range. CommunityToolkit
MVVM, the two WinUI Toolkit controls, CodeCoverage and the SQLitePCLRaw
bundle already match their latest stable releases. Do not independently
replace the SDK's modular components outside its vendor-selected graph.

SQLitePCLRaw 3.0.5 requests a native SQLite minimum of 3.53.4. Data now
directly selects the current native 3.54.1 package. On x64 the exact DLL
from the restored archive reports SQLite 3.54.1 and the same source ID as
the upstream release: `2026-10-16 14:45:20 555e07ef33f69a0957d24d8e555b702464a131d6f1607f4c3c1e4551e8e6ca76`.
This does not change the database schema. The November backport 3.53.6 is
part of the older branch; it is not the maximum stable NuGet engine version.

All 20 supported lockfiles were reviewed after regenerating both ordinary
solution and both Release AOT app graphs. Eleven changed semantically.
The updated [shipped notices](../../src/Wintodo.App/Assets/ThirdPartyNotices.txt)
contain all eleven labeled original legal files, verified byte-for-byte
against the NuGet archives. The nine previous supplier texts remain intact,
including the SDK Runtime NOTICE and its embedded WinUI notice. The new
2.7.0 ML module license and SQLite's public-domain declaration are included
in full. The notice is 912,846 bytes, SHA-256
`4f0675a126ee4d8a66bbe4426df58a3517e1624eaafc2e953aebcf28c7f13c8d`.
The source notice privacy scan passed; NuGet signature verification of
the Windows App SDK, WinApp and native SQLite archives passed under the
existing host trust settings.

Installed-client, OEM/native ARM64, Store and full native provenance
acceptance are separate from these checks. Historical release evidence
remains tied to the package and source revision it originally examined.


## Actual build and release-gate result: blocked

Source `ba29487` builds x64 Debug and Release with zero warnings/errors. The
four-suite unit run passed 706/707: only the existing timed My Day gate missed
(6.483 ms against 5 ms). The focused performance class passed 2/2 afterward;
a full serialized rerun still missed My Day at 5.881 ms. See [the measurements](../PERF.md#2026-11-21--dependency-servicing-headless-10000-task-checks).
The nine-project NuGet audit reported zero advisories. Targeted locked AOT
restores and both native publishes succeeded with no lock changes.

| Native target | Executable / staged MiB | Unsigned MSIX bytes | MSIX SHA-256 | Structure/privacy |
|---|---:|---:|---|---|
| x64 | 21.29 / 144.52 | 60,627,794 | `5d438b55c6c5cb21fd9986eb48252fe19b1061fc4ba8aa5826509bebba9b0f42` | FAIL: 16 supplier DLLs |
| ARM64 | 21.97 / 153.48 | 59,488,219 | `1ae8c0acf85794db9e9484ce9c1db7d5d93903a84bc95b179b64a81440160f87` | FAIL: 16 supplier DLLs |

Each MSIX contains the exact reviewed SQLite native DLL and supplier notice.
The structural gate includes its privacy scan; a structural and privacy
failure on the same entries are not independent defects. Every one of the
16 reported DLLs on each architecture matched supplier bytes exactly:
15 matched the target architecture's nested `Microsoft.WindowsAppRuntime.2.msix`
in `Microsoft.WindowsAppSDK.Runtime/2.7.0`; the remaining FrameworkUdk DLL
matched `runtimes-framework/win-{arch}/native` inside
`Microsoft.WindowsAppSDK.InteractiveExperiences/2.7.0`. The flagged profile
prefix identifies a generic vendor container build account in binary content,
not a first-party CodeView path or a path from this checkout. The existing
release policy rejects it nonetheless. No scanner rule, vendor binary, or
mandatory payload was changed to obtain a pass. These archives must not be
distributed under that gate.

The complete original Runtime and InteractiveExperiences NuGet archives
for 2.7.0, 2.6.5 and 2.6.4 matched their official NuGet catalog SHA-512
and passed `dotnet nuget verify --all` under current host trust. On both
architectures the 2.6.5 and 2.6.4 Runtime MSIX also contain the flagged
prefix in Microsoft.UI, Windowing and DWM components; the matching versions
of InteractiveExperiences contain the prefix in FrameworkUdk. These were
supplier-input checks, not app-package builds on intermediate SDKs.
NuGet lists no other stable SDK between 2.5.1 and 2.7.0.

The separate release fallback retains the prior 2.5.1 SDK graph and every
other dependency update. The newer SDK remains available on this branch for
vendor-servicing evaluation. Native compilation does not itself prove runtime
acceptance, privacy compliance or release authorization.
