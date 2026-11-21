# November 2026 release fallback for dependency servicing

Two local refs preserve different outcomes. `maintenance/nov21-review` keeps Windows App SDK 2.5.1 and takes the other updates: 21 of 22 tracked checks match their stable targets. `refs/archive/branches/maintenance/nov21-sdk27-evaluation` preserves the exact 2.7.0 evaluation: 22 of 22 current, but both actual packages failed the privacy gate. “Current” is a version comparison; it does not certify an update for release.

**Priority:** The four [data-integrity fixes](2026-11-sol-integrity-handoff.md) are in DEV 1.0.1.188. Treat the Windows App SDK update as routine work for the next dependency PR. The finding is a generic Microsoft build-account path in unmodified Microsoft DLLs; the evidence does not indicate exposure of a Wintodo user or developer path.

This update keeps the [.NET 10 LTS and Windows 11 26100 contract](../../Directory.Build.props).
The SDK is still exactly 10.0.404 (runtime 10.0.15). .NET 11.0.100 and
Microsoft.Data.Sqlite 11.0.0 are a separate major-version migration, not a
servicing requirement for this app.

| Project dependency | Previous | Selected |
|---|---:|---:|
| [Windows App SDK](https://learn.microsoft.com/windows/apps/windows-app-sdk/release-channels) | 2.5.1 | 2.5.1 (2.7.0 release gate blocked) |
| [Windows SDK BuildTools](https://learn.microsoft.com/windows/apps/windows-sdk/downloads) | 10.0.28000.2705 | 10.0.28000.2957 |
| [WinApp packaging tool and WinUI Analyzer](https://github.com/microsoft/WinAppCli/releases/tag/v0.8.0) | 0.7.1 | 0.8.0 |
| Microsoft.Data.Sqlite | 10.0.12 | 10.0.15 |
| [SQLite native engine](https://www.sqlite.org/releaselog/3_54_1.html) | 3.53.4 | 3.54.1 |
| xunit.v3 | 4.0.2 | 4.0.3 |
| Microsoft.Testing.Extensions.TrxReport | 2.5.1 | 2.5.3 |

The NuGet package and Windows minimum are different version numbers. The
new Windows SDK build tools do not increase the minimum Windows version.
The SDK and its modular packages retain their prior 2.5.1 graph. The ML
module still permits the explicit Microsoft.Windows.AI.MachineLearning 2.5.96
selection; this is the supported ML version reviewed previously. CommunityToolkit
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
including the SDK Runtime NOTICE and its embedded WinUI notice. The active
2.1.94 ML module license and SQLite's public-domain declaration are included
in full. The notice is 912,876 bytes, SHA-256
`717ed1fd899756313632d4fe5f838ae17f9db14aa29e80be0aa162d01f93c3fb`.
The source notice privacy scan passed; NuGet signature verification of
the 2.7.0 SDK metapackage, WinApp and native SQLite archives passed under the
existing host trust settings.

## Windows App SDK exception

The 2.7.0 candidate from `ba29487` compiles, but its unsigned x64 package
fails the existing privacy gate on 16 Microsoft native DLLs. Fifteen matched
the exact x64 payload bytes inside the official 2.7.0 Runtime package; the
other matched 2.7.0 InteractiveExperiences. The profile prefix belongs to
the supplier's generic container build account, outside CodeView. The app
checkout path was not found in those files. Do not patch these DLLs, omit
required runtime components, or weaken the gate.

NuGet lists exactly two intermediate stable versions, 2.6.4 and 2.6.5.
Their SDK-selected InteractiveExperiences archives contain the
same failing category in the architecture-specific FrameworkUdk DLL for both
x64 and ARM64. Their nested official Runtime MSIX also contain the marker
in Microsoft.UI, Windowing and DWM DLLs on both architectures. All six
Runtime/InteractiveExperiences archives for 2.6.4, 2.6.5 and 2.7.0 matched the
official NuGet catalog SHA-512 and passed NuGet signature verification under
existing host trust. These are supplier-input screening results for the
intermediate versions; only 2.7.0 and this fallback received app-package builds.
The fallback and the private appearance preview still use 2.5.1. That older
patch does not meet Microsoft's latest-patch support condition.

### Recommendation for the next production decision (21 November)

Use **2.7.0** (or the then-current supported stable patch) in the next routine dependency PR. The DEV appearance result still stands on 2.5.1. There is no need for a separate SDK-only release, a Microsoft support case or a vendor deadline.

[DEP-14](deployment-readiness.md#dep-14--remove-identifying-binary-and-metadata-paths) protects against identifying build paths. The [packaging guide](../guides/packaging.md#release-artifact-privacy-dep-14) already distinguishes nonidentifying vendor metadata. Its current scanner makes that distinction for vendor CodeView records; it still reports the profile prefix in the body of these DLLs as a failure. Recognizing the verified generic marker applies the existing privacy rule; it does not require an architectural decision or a blanket exception for Microsoft files.

- [ ] In the dependency PR, report this exact marker as **informational** only for the reviewed DLL bytes from the signed, catalog-hash-verified SDK archives, matched by package version, architecture, archive entry and DLL hash. Keep scanning the same files for private roots and all other findings. Cover a matching input, different bytes and a separate identifying marker in regression tests.
- [ ] Run the normal build, tests, package scans on both x64 and ARM64, and the normal release checks on the final artifacts before shipping. The historical packages below still **failed** their actual gate; this documentation does not turn that result into a pass.

[Microsoft support and release channels](https://learn.microsoft.com/windows/apps/windows-app-sdk/release-channels) · [Windows App SDK 2.7.0 release notes](https://github.com/microsoft/WindowsAppSDK/releases/tag/v2.7.0). The open search timing failure is a separate acceptance issue; changing SDKs does not establish that it is fixed.

Installed-client, OEM/native ARM64, Store and full native provenance
acceptance are separate from these checks. Historical release evidence
remains tied to the package and source revision it originally examined.


## Windows App SDK 2.7.0 evaluation: both packages blocked

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
not a first-party CodeView path or a path from this checkout. The current
scanner rejects it nonetheless. No scanner rule, vendor binary, or
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

The release fallback retains the prior 2.5.1 SDK graph and every
other dependency update. The separate 2.7.0 evaluation branch is the starting
point for the next SDK update. Native compilation does not itself prove runtime
acceptance, privacy compliance or release authorization.

## Fallback validation

At `c12e5e6`, a clean x64 Debug build finished with zero warnings/errors.
The four MTP modules ran once, serially, after compilation: 706/707 passed,
zero skipped. The smart-list queries all passed; 10k FTS worst warm was
428.653 ms against 60 ms. The [recorded times](../PERF.md#2026-11-21--release-fallback-dependency-test-measurements)
are from that run's TRX; no retry, relaxed budget or speed claim. The four
App locks are the only lock-graph differences from the 2.7.0 candidate.
The two candidates have identical library and test dependency graphs.

Before the Settings resource correction, both clean native AOT packages
passed exact manifest/architecture/structural and standalone checkout-root
privacy checks, plus a bound app-only NuGet audit with zero advisories.
Both contained byte-identical reviewed SQLite, Microsoft.UI and FrameworkUdk
supplier DLLs, and the 912,876-byte source notice. The historical x64 package
(333 entries, 59,436,390 bytes) has SHA-256
`2b2817db77b14b10864d4d61d37632c968c33570c1624318d40471156e747bde`;
ARM64 (329 entries, 57,758,948 bytes) has SHA-256
`d69027389a6dd4123e712fa7597114b1fd79277135ee5cff5b770938518058cd`.
These historical packages still display the older Settings package list.

The subsequent source-only correction (`d0c8f0f`) synchronizes
`SettingsLicencesText.Text` with the central versions and supplier labels;
the focused consistency test passed for all 14 entries.


Final, **unsigned**, Native AOT MSIX from corrected source `d0c8f0f`:

| Target | MSIX bytes | Files | MSIX SHA-256 | Structure, checkout-root privacy, AOT graph audit, Settings PRI |
|---|---:|---:|---|---|
| x64 | 59,436,449 | 333 | `265e3d1b8128af96d49943094f4b01e497fd34c7a7c94452f8a1f79e58b263b8` | PASS |
| arm64 | 57,759,025 | 329 | `686a3d2c4f93772dc0987828bd3bc6de8e90845d3ba39db0437f6d7302d44409` | PASS |

Both bound AOT NuGet audits reported zero advisories. Both exact packages
passed the source manifest, first-party native architecture and privacy checks,
with no identifying profile or checkout-root findings. MakePri dumped
`resources.pri` from each final MSIX; its actual English
`SettingsLicencesText.Text` matches the source string for all 14 central
packages. Against each prior fallback package, the only entries whose contents
changed were `resources.pri`, the rebuilt `Wintodo.exe` and `AppxBlockMap.xml`
(330/333 x64 and 326/329 ARM64 entries stayed byte-identical). Both packages
preserve the exact reviewed SDK, SQLite and supplier notice bytes.

These package checks do not close the failed store timing gate or test
installation, Store certification, user data migration, UI behavior, or
ARM64 runtime behavior. No package was signed, installed or published.
