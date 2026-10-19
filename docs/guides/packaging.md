# Packaging groundwork (M10)

The app name and publisher remain the placeholders in `Package.appxmanifest`:
`5826186B-4750-4C22-BC50-B5DD1EED9CA5`, `CN=AppPublisher`, version `1.0.0.4`.
Final identity and production signing require the owner's decision (PLAN §10).
The remaining decisions, owner-run checks and clean-install premise are tracked
in [release-checklist.md](release-checklist.md). The standard `WintodoTest`
checkpoint is provisioned for development and cannot prove a clean M10 install.
The manifest declares only `runFullTrust`, required for the desktop app and its
COM toast/startup extensions. It does not request `broadFileSystemAccess`;
file import/export uses pickers and CalDAV uses the full-trust HTTP client.
ADR-015 extends the existing ShareTarget to `Text` and `WebLink`; generated
x64/ARM64 manifests retain the same identity, permissions and startup/toast
declarations. Real broker activation is verified in the published x64 AOT output
inside WintodoTest. Share processing uses guest package LocalState, with no
Release broker storage override. This does not close M10 signing/Store gates.

## Unsigned artifacts

On a provisioned Windows machine with WinApp CLI 0.7+, the pinned .NET SDK,
and MSVC tools for the target architecture:

```powershell
./scripts/Publish-Aot.ps1
winapp package src/Wintodo.App/Wintodo.csproj --arch x64 --no-sign --self-contained `
  -p Platform=x64 -p PublishAot=true -p SelfContained=true `
  -p PublishTrimmed=true -p PublishReadyToRun=false -p TreatWarningsAsErrors=true `
  --output publish/msix/Wintodo-x64.msix
winapp package src/Wintodo.App/Wintodo.csproj --arch arm64 --no-sign --self-contained `
  -p Platform=ARM64 -p PublishAot=true -p SelfContained=true `
  -p PublishTrimmed=true -p PublishReadyToRun=false -p TreatWarningsAsErrors=true `
  --output publish/msix/Wintodo-arm64.msix
```

Use the explicit project and AOT properties. With CLI 0.7.1, relying on Release
defaults alone selected the trimmed managed path and produced CsWinRT IL2081
errors; the explicit AOT path packaged cleanly. Do not suppress trim warnings.
No development package registration or launch is needed to package.

Pushes to `v*` tags run the separate `release` matrix in `ci.yml`, after the
build/test job succeeds. It checks the native toolchain, runs `Publish-Aot.ps1`,
then packages with the WinApp CLI restored from the centrally pinned NuGet
reference. Artifacts `M10-unsigned-x64` and `M10-unsigned-arm64` contain the MSIX
and AOT size summary. There is no signing material, certificate installation,
GitHub Release creation or Store publishing in CI. Main pushes and PRs run
build/test only. Tag names do not change the placeholder manifest version.

## Local test signature

Keep the certificate and package copy inside the ignored `publish` directory:

```powershell
winapp cert generate --manifest src/Wintodo.App/Package.appxmanifest `
  --output publish/msix/m10-test.pfx --export-cer
Copy-Item publish/msix/Wintodo-x64.msix publish/msix/Wintodo-x64-test-signed.msix
winapp sign publish/msix/Wintodo-x64-test-signed.msix publish/msix/m10-test.pfx
```

This certificate is for local testing only. Certificate trust and package
installation are separate owner-approved actions; neither is needed to prove
package creation or test signing. Clean-VM install/launch, uninstall cleanup,
final Store assets and production signing remain M10 gate work.

## Prepared artifacts (2026-10-18)

Source: viewport repair commit `23881eb`. Debug/Release x64 builds and 307 unit
tests passed before republishing Native AOT for x64 and ARM64. Fresh unsigned
packages and copies signed with the existing local test certificate are available
under `publish/msix`:

- `Wintodo-x64.msix` and `Wintodo-arm64.msix`.
- `Wintodo-x64-test-signed.msix` and `Wintodo-arm64-test-signed.msix`.
- `inspection.json`: source commit, SHA-256 hashes, package identity, extensions,
  native executable/runtime/SQLite architecture checks and signature metadata.
- `*.verify.log`: SignTool verification output for each test-signed package.

Package inspection confirms the placeholder identity/version, only `runFullTrust`,
toast/COM/startup/share declarations, Text/WebLink share formats, required packaged
assets and native x64/ARM64 binaries with no CLR header. Runtime/deployment gates
remain pending. SignTool verification exits 1 because the development certificate
is untrusted on the host; full signature/trust verification is not reported as
passed. No certificate was installed. Test copies are not timestamped production
artifacts. The original public certificate and PFX remain in the ignored folder.

## Current local x64 update

The import-polish update (2026-10-20) is
`publish/msix/Wintodo-1.0.0.4-x64.msix` (55.6 MiB), built from `e65b605`
plus the manifest version bump. It is self-contained Native AOT and signed
with the existing `m10-test.pfx`; Authenticode and SignTool verify it as Valid
on the owner's machine. Package identity is unchanged for an update from
`1.0.0.3`. Native x64 executable/runtime/SQLite, assets and extensions were
inspected. Details and SHA-256 are in `Wintodo-1.0.0.4-x64.details.json`.
Installation and runtime checking of this exact package are owner-pending;
no host app installation or UI automation was performed.

The preceding artifact remains available below:

The later header-alignment update is version `1.0.0.3` in
`publish/msix/Wintodo-x64.msix`. This exact filename is now signed with the existing
development certificate; it contains the fixed title line bounds. Signature
verification returned Valid on the host. Metadata/hash are in
`Wintodo-x64.details.json`. Earlier ARM64/test-signed copies and `inspection.json`
describe the preceding `1.0.0.0` build.

To reproduce a signed local update at this filename:

```powershell
winapp package src/Wintodo.App/Wintodo.csproj --arch x64 `
  --cert publish/msix/m10-test.pfx --self-contained `
  -p Platform=x64 -p PublishAot=true -p SelfContained=true `
  -p PublishTrimmed=true -p PublishReadyToRun=false -p TreatWarningsAsErrors=true `
  --output publish/msix/Wintodo-x64.msix
```

The unsigned commands above produce unsigned output again if rerun. Local
certificate trust on the installing machine is still required; production
identity/signing remains undecided.
