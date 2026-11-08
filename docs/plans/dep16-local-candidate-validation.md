# DEP-16 clean-source unsigned candidate validation

Validated 2026-11-08 against source commit
`965d86b358d3a384328e35ce7a02b896e58eb805`. Status: **x64 AND ARM64 LOCAL
UNSIGNED CHECKS PASS. Production release acceptance remains pending.**

## Source and build boundary

Both architectures use one fresh `git archive` of the exact commit, extracted
inside ignored `artifacts/dep-next-candidates/source`. Its SHA-256 is
`4f5ac51702027cc744d8fec6620cd1cedd1bc32161aa7ef70bf0e8232bb0b7a0`.
After both architecture packages completed, all **495 archived files**, including **20 reviewed
lockfiles**, remained byte-identical to their archive entries. Generated build
files are separate from this source-preservation comparison.

The original placeholder manifest identity, publisher, executable name and
version `1.0.0.5` are preserved. Source-manifest equality establishes consistency
with the reviewed source; it does not approve a production identity. No machine
setup, certificate creation/trust, signing, installation, app launch, UI
automation, remote settings or publication was performed.

## Reproduced packaging failure and correction

The first x64 AOT restore and publish passed, but the documented WinApp 0.7.1
packaging command failed with `WTDLOCK001`. Before packaging the explicit app
project, WinApp restored the containing solution with the AOT/RID properties,
then retried every solution project individually. That included test projects
whose supported graph has an ordinary lockfile, rather than an AOT/RID lockfile.
The reviewed-lock guard rejected those unsupported test restore graphs.

The CLI's supported `--no-restore` option corrects the packaging invocation
after an explicit target-specific locked restore and AOT publish. The repaired
x64 command completed SDK-native packaging successfully. It retains the
existing source, reviewed locks, AOT properties and warnings-as-errors.

The validated sequence is repeated for each architecture:

```powershell
# Substitute ARM64 / win-arm64 / arm64 for the second candidate.
dotnet restore src/Wintodo.App/Wintodo.csproj -r win-x64 --locked-mode `
  -p:Configuration=Release -p:Platform=x64 -p:PublishAot=true `
  -p:SelfContained=true -p:WindowsAppSDKSelfContained=true `
  -p:PublishTrimmed=true -p:PublishReadyToRun=false `
  -p:NuGetAudit=true -p:NuGetAuditMode=all -p:NuGetAuditLevel=low -warnaserror
./scripts/Publish-Aot.ps1 -Platforms x64
# Invoke the centrally restored 0.7.1 CLI; $cli is its executable path.
& $cli package src/Wintodo.App/Wintodo.csproj --arch x64 `
  --no-sign --self-contained --no-restore `
  -p Platform=x64 -p PublishAot=true -p SelfContained=true `
  -p PublishTrimmed=true -p PublishReadyToRun=false `
  -p TreatWarningsAsErrors=true -p RestoreLockedMode=true `
  --output publish/msix/Wintodo-x64.msix
```

`--no-restore` has a precondition: the app and its referenced projects must
already have the matching target/AOT/self-contained locked restore. These
commands execute from the isolated source directory. Native/tool stdout and
the original failure remain in ignored private logs.

## Exact package evidence

| Check | x64 | ARM64 |
|---|---|---|
| Target-specific locked restore and final vulnerability audit | PASS | PASS |
| Self-contained AOT publish, warnings as errors | PASS | PASS |
| SDK-native unsigned MSIX packaging | PASS after command repair | PASS with repaired command |
| Exact-MSIX structural check against source manifest | PASS | PASS |
| Exact-MSIX and size-summary privacy check | PASS, zero findings | PASS, zero findings |
| Target compiler/runtime graph and available tools | PASS with recorded limits | PASS with recorded limits |
| Common AOT-layout/MSIX file equivalence | 311/311, including executable | 307/307, including executable |

The x64 package has **321 files**, is **58,567,883 bytes**, and its SHA-256 is
`294556265b39a695b27b901ed3d063766df815f68bb172c68be469fb96eaae8d`.
Its native app executable SHA-256 is
`fcd3d476bfe66e0ae9a6bc4cc989ea797eba9b35929ffa9e52992d60f1550c84`.
The structural inspector parses 54 PE files; one vendor file has a different
machine type, the previously reviewed ARM64EC resource payload. The required
app, WinUI/Windows App Runtime and SQLite files satisfy the native x64 contract.

The ARM64 package has **317 files**, is **56,884,271 bytes**, and its SHA-256 is
`fc663445fc3a2b053e1f98454faaef887822028482d047d1abf862cf39eecf8d`.
Its native app executable SHA-256 is
`cd37a736df8d3aa90623c1fdacd3c116b318a1bb175c1db14991909b916165df`.
The structural inspector parses 53 PE files; its other-architecture vendor
counter is zero, and the required binaries satisfy the native ARM64 contract.

All 311 x64 and 307 ARM64 files common to the intermediate AOT layouts and
candidate MSIX files match byte-for-byte. The packages' additional files remain
part of their independent payload/notice review. This proves the packaged app
executables were not replaced by a different duplicate publish, while retaining
the need to inspect full MSIX files rather than infer package contents from
layouts. Privacy checks cover 322 x64 and 318 ARM64 files respectively, including
each architecture's size summary, and report zero findings.

## Toolchain evidence and limits

The archived exporter records SDK `10.0.403`, WinApp CLI `0.7.1`, Visual Studio
installation `18.8.12009.203`, MSVC directory `14.51.36231`, available Windows SDK
family `10.0.26100.0`, and target runtime/compiler `10.0.14`. Final NuGet audit
reports zero vulnerable packages and zero advisories in the app graph.

The exporter in this source commit records native-tool availability and MSVC
directory labels; those labels do not identify serviced binary versions or
prove which exact linker/SDK tools ran. The separate
[exact-tool evidence follow-up](dep15-exact-toolchain-evidence.md) adds binary
file/product versions and hashes, with evaluated MSBuild selection recorded
separately from execution. Existing WinApp/native-runtime
advisory/provenance and vendor component mapping dispositions remain open in
[the DEP-02 review](dep02-toolchain-review.md).

## Evidence locations and remaining gates

Private evidence is under `artifacts/dep-next-candidates/`: the exact source
archive; original `package-x64-initial-failure.log` and `initial-failure.json`;
fresh package files; per-architecture structural, privacy, dependency and
toolchain JSON; size summaries; source/layout equivalence JSON; and native/tool
logs. The reusable gates are
[Test-ReleasePackage.py](../../scripts/Test-ReleasePackage.py) and
[Test-ReleasePrivacy.py](../../scripts/Test-ReleasePrivacy.py). Their reports
identify actual package hashes and contain category/counter evidence without
local paths or raw identity values. The
[independent notices review](dep17-third-party-notices-review.md) checks fresh
candidate vendor binaries and legal payload, including the restart executable.
The linked-code/SBOM closure remains qualified separately. Both packages preserve
the current notices SHA-256
`4788e0141a69180b3c71acfc7db4466f2b9451fb66ff502f7fd9e6fb47e34065`.

These local unsigned results do not close final identity/version approval,
independently approved complete payload lists, first-party/image rights,
complete SBOM/runtime closure, native/tool advisory disposition, fresh hosted
CI, production signing, final signed-byte inspection, clean-VM installation,
uninstall cleanup or ARM64 runtime acceptance. The packages preserve the
existing notices pending the independent DEP-17 follow-up; any added notices
require a newly built and inspected candidate. Final release work remains
ordered in [the deployment plan](deployment-readiness.md).
