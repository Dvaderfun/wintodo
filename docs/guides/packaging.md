# Packaging groundwork (M10)

The app name and publisher remain the placeholders in `Package.appxmanifest`:
`5826186B-4750-4C22-BC50-B5DD1EED9CA5`, `CN=AppPublisher`, version `1.0.0.0`.
Final identity and production signing require the owner's decision (PLAN §10).
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
