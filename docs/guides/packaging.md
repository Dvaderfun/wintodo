# Packaging groundwork (M10)

The app name and publisher remain the placeholders in `Package.appxmanifest`:
`5826186B-4750-4C22-BC50-B5DD1EED9CA5`, `CN=AppPublisher`, version `1.0.0.5`.
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
./scripts/Publish-Aot.ps1 -Platforms x64
winapp package src/Wintodo.App/Wintodo.csproj --arch x64 --no-sign --self-contained --no-restore `
  -p Platform=x64 -p PublishAot=true -p SelfContained=true `
  -p PublishTrimmed=true -p PublishReadyToRun=false -p TreatWarningsAsErrors=true `
  --output publish/msix/Wintodo-x64.msix
./scripts/Publish-Aot.ps1 -Platforms ARM64
winapp package src/Wintodo.App/Wintodo.csproj --arch arm64 --no-sign --self-contained --no-restore `
  -p Platform=ARM64 -p PublishAot=true -p SelfContained=true `
  -p PublishTrimmed=true -p PublishReadyToRun=false -p TreatWarningsAsErrors=true `
  --output publish/msix/Wintodo-arm64.msix
```

Use the explicit project and AOT properties. With CLI 0.7.1, relying on Release
defaults alone selected the trimmed managed path and produced CsWinRT IL2081
errors; the explicit AOT path packaged cleanly. Do not suppress trim warnings.
No development package registration or launch is needed to package.

Run `Publish-Aot.ps1` for each requested architecture immediately before its
package command; the shared assets graph changes on the next target restore. Its
target-specific restore uses the approved AOT locks. Keep `--no-restore` on
the package command: WinApp 0.7.1 otherwise restores the containing solution
with AOT properties, including test projects that have no supported AOT graph.
The flag skips that extra restore while preserving the native build/package
step. Do not add AOT test locks or weaken locked restore to bypass this failure.
`summary.json` describes the last publish invocation; retain each target's
summary separately when keeping both size records.

## Release artifact privacy (DEP-14)

Release builds map first-party source documents to `/_/` and pass MSVC
`/PDBALTPATH:Wintodo.pdb` through ILCompiler's `LinkerArg`. The latter fixes the
native executable's CodeView record; managed `PathMap` alone cannot do that.
`CopyOutputSymbolsToPublishDirectory=false` and a Release payload filter for
project-reference PDBs keep native/managed PDBs in the private build outputs under
`src/**/bin` and `src/**/obj`, while AOT layouts and MSIX omit them. Keep those
symbols with the exact executable hash when debugging;
do not upload raw symbols, build recipes or build/test logs as release assets.

`Publish-Aot.ps1` emits publish locations relative to `summary.json`. The size
summary can be shared after its artifact scan; native tool output remains private
because tools can print the checkout path. Avoid publishing old summaries.

After publishing both architectures, scan their actual bytes with Python's
standard-library gate:

```powershell
python -B scripts/Test-ReleasePrivacy.py publish/aot/win-x64 publish/aot/win-arm64 `
  publish/aot/summary.json --output artifacts/release-layout-privacy.json `
  --private-root $PWD --require-architecture x64 --require-architecture ARM64
python -B -m unittest discover -s tests/scripts -p test_release_privacy.py
```

Repeat the scan on **the final signed MSIX files and public metadata** before
delivery, substituting package paths for layout paths. The scanner streams all
package entries without extracting, parses PE debug directories, rejects loose
symbols/build recipes and embedded first-party PDBs, and checks profile paths and
explicit private roots/markers in UTF-8/UTF-16 and JSON-escaped forms. Supply a
private JSON string array through `--marker-file` for synthetic private markers;
matches and filenames are never printed. Review its sanitized JSON counters and
hashes. Nonidentifying vendor CodeView paths are counted separately; this gate
does not claim to remove Microsoft's own build paths or replace the source secret
scan and final attribution review. Bundles require scanning each contained MSIX.

Any package content change requires rebuilding/repackaging/re-signing and a fresh
scan. Intermediate/unsigned PASS cannot close final signed-package acceptance.

Pushes to `v*` tags and weekly/manual main runs execute the separate `release`
matrix in `ci.yml`, after the build/test job succeeds. It checks the native toolchain, runs `Publish-Aot.ps1`,
then packages with the WinApp CLI restored from the centrally pinned NuGet
reference. Artifacts `M10-unsigned-x64` and `M10-unsigned-arm64` contain the MSIX
and AOT size summary, plus safe dependency/toolchain/privacy evidence. Actions
are pinned to reviewed commits; checkout credentials are not persisted, and
exact unsigned package bytes pass the privacy gate before upload. There is no
signing material, certificate installation, GitHub Release creation or Store
publishing in CI. Ordinary main pushes and PRs run build/test and security
checks. Tag names do not change the placeholder manifest version. See
[CI policy and remaining enforcement gates](ci.md).

## Exact-package dependency evidence and unsigned handoff

Unsigned candidate CI exports a qualified CycloneDX 1.6 record from the actual
MSIX, the matching target-specific private `project.assets.json` and original
NuGet archives. Its source commit and package SHA-256 identify the supplied
inputs. Hash-matched vendor files are recorded separately from restored inputs
whose retention in Native AOT is unverified. The latter remain evidence rather
than declared runtime dependencies. The complete linked-code/vendor/projection
closure and independent source/build attestation remain pending; an export PASS
is not release acceptance. Signed NuGet raw archive hashes and normalized restore
content hashes are distinguished, with local cache-metadata binding qualified.
See [the SBOM evidence record](../plans/dep16-sbom-evidence.md) for the command,
source-integrity limits and remaining closure checks.

[Test-ReleaseCandidate.py](../../scripts/Test-ReleaseCandidate.py) accepts a
private hash-pinned descriptor for one unsigned x64 and one unsigned ARM64 MSIX
plus each target's structural/privacy/toolchain/app-only audit reports. It
rehashes the files and package entries, compares source/report consistency and
identity/version equality, and emits sanitized input-consistency evidence with
`release_authorized: false`. The [handoff record](../plans/dep18-candidate-handoff.md)
describes its input schema and negative tests. Production policy, signing,
protected source ancestry and exact tool/audit execution provenance require
their separate gates. New checksums and inspection are required after signing.

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

The header-descender update (2026-10-20) is
`publish/msix/Wintodo-1.0.0.5-x64.msix` (55.6 MiB), built from `8c5a066` plus
the uncommitted user-list title clipping fix and the manifest version bump.
Self-contained Native AOT, signed with the existing `m10-test.pfx`;
Authenticode Valid, SignTool `/pa /v` zero warnings/errors. Identity unchanged,
so it updates `1.0.0.4`. Details and SHA-256 are in
`Wintodo-1.0.0.5-x64.details.json`. Owner installation/runtime check pending.

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
.\scripts\Publish-Aot.ps1 -Platforms x64
winapp package src/Wintodo.App/Wintodo.csproj --arch x64 `
  --cert publish/msix/m10-test.pfx --self-contained --no-restore `
  -p Platform=x64 -p PublishAot=true -p SelfContained=true `
  -p PublishTrimmed=true -p PublishReadyToRun=false -p TreatWarningsAsErrors=true `
  --output publish/msix/Wintodo-x64.msix
```

The unsigned commands above produce unsigned output again if rerun. Local
certificate trust on the installing machine is still required; production
identity/signing remains undecided.
