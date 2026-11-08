# DEP-16 package inspection preparation

Prepared: 2026-11-08. Status: **IMPLEMENTED; synthetic checks PASS; final
candidate, identity and payload approval remain PENDING.**

This bounded part of [the deployment plan](deployment-readiness.md) adds a
repeatable structural check for one supplied MSIX. It does not change the
manifest, application name, publisher, version, signing or distribution policy.
The exact eventual signed x64 and ARM64 packages still need independent final
acceptance under DEP-16/18/19.

## Existing evidence and the missing check

[The packaging guide](../guides/packaging.md) records manual structural inspection
of October test packages. [Test-ReleasePrivacy.py](../../scripts/Test-ReleasePrivacy.py)
already streams MSIX entries, reads real PE headers/debug directories and checks
first-party native architecture, symbols and identifying paths. Its synthetic
regressions remain the privacy seam; the new script calls those functions rather
than implementing another binary or privacy parser.

There was no checked-in reusable gate that connected a supplied package's
manifest architecture and activation declarations to its runtime files, packaged
assets and a reviewed payload inventory. The new
[Test-ReleasePackage.py](../../scripts/Test-ReleasePackage.py) performs those
checks without extraction, package registration, installation or executable
activation.

## Usage

Inspect a supplied candidate and write a sanitized report:

```powershell
python -B scripts/Test-ReleasePackage.py publish/msix/Wintodo-x64.msix `
  --architecture x64 --output artifacts/release-x64-inspection.json
python -B scripts/Test-ReleasePackage.py publish/msix/Wintodo-arm64.msix `
  --architecture ARM64 --output artifacts/release-arm64-inspection.json
python -B -m unittest discover -s tests/scripts -p test_release_package.py
```

When approved identity/version and payload records exist, pass
`--expected-identity`, `--expected-publisher`, `--expected-version` and
`--approved-payload`. The last argument is a JSON file containing every exact
package-relative filename, including `[Content_Types].xml`, `AppxBlockMap.xml`
and the signing metadata that applies to those delivered bytes. It must be
reviewed independently; deriving an inventory from a package and automatically
approving that same inventory does not establish a release allowlist.

The report exposes identity/payload policy as `PENDING` when their inputs are
absent, even if structural checks return `PASS`. It returns exit code 1 for
failed checks. Diagnostics use fixed categories and entry ordinals; manifest
identity values, filenames and local input/output paths are omitted. Candidate
and manifest SHA-256 hashes identify the bytes inspected. Hash consistency is
checked across structural and privacy passes.

## Checked contract

- ZIP entry count, declared expanded size and individual entry size are bounded
  to 4,096 entries, 1 GiB total and 256 MiB per file. Encrypted entries, duplicate
  normalized names, drive/absolute paths and traversal segments are rejected.
- The manifest is a bounded UTF-8 foundation-namespace package document without
  DTDs or entity declarations. Identity version has four valid MSIX components;
  processor architecture matches the requested architecture. Optional approved
  identity, publisher and version inputs must match exactly.
- Only the current restricted `runFullTrust` capability is allowed. Device
  families must be Windows Desktop/Universal, include Desktop and require at
  least Windows 11 24H2 (`10.0.26100.0`). External package dependencies are
  rejected for the current self-contained candidate contract.
- One `Wintodo.exe` full-trust application retains exactly the reviewed
  toast/COM/startup/share extensions. COM executable and activation arguments,
  matching toast/COM identifiers, disabled-by-default startup and Text/WebLink
  share formats are checked. Additional executable references are rejected.
- The package contains application resources, the manifest-referenced logos,
  tile and splash assets, MSIX metadata, WinUI/Windows App Runtime and SQLite
  files. Manifest assets may be present as their scale-qualified resource files.
  The executable and required WinUI/Windows App Runtime/SQLite headers must be
  native and match the requested x64 or ARM64 architecture. Other vendor binary
  architectures are counted for dependency-closure review; Microsoft includes
  an ARM64EC workloads resource DLL in its x64 payload.
- Databases, keys/certificates, private symbols, traces, crash/test/build logs and
  test/staging/attachment folders are rejected. An optional complete reviewed
  payload list must match exactly. The existing privacy scan must also pass.

## Verification and remaining gates

[Twenty-four synthetic regression tests](../../tests/scripts/test_release_package.py)
pass. They cover both architectures; reviewed/mismatched identity and payload;
wrong-architecture and managed binaries; missing runtime/assets; forbidden files;
duplicate/traversal names; unsupported capability/dependency/extension changes;
toast/startup/share errors and duplicate COM/application registrations;
malformed/DTD/UTF-16 XML; invalid version/OS requirements and maximum-tested versions;
declared archive/manifest size limits; privacy failures; inconsistent package
hashes; resource-qualified assets and ARM64EC vendor resources; redacted CLI
reports, failure exit codes and preservation of candidate/policy inputs when an
output path aliases either file (including hard links); invalid-policy reruns
replace previous PASS reports with FAIL evidence. Callers must honor the failure
exit code if the output is unwritable. No native build or UI session
is needed for these tests.

These checks establish structural and bounded privacy evidence. They do not
validate MSIX block-map/signature cryptography, certificate trust, Windows schema
acceptance, PRI/XBF or image validity, runtime DLL dependency resolution,
licensing/SBOM closure, clean-source equivalence or installation/runtime behavior.
Required runtime filenames are a presence/header contract rather than proof of
complete dependency closure. UTF-8 is the supported generated-manifest format;
other encodings are rejected. The inherited privacy check keeps the current
`Wintodo.exe`/`Wintodo.pdb` contract, so a future executable rename requires a
reviewed coordinated change to that seam.

Unsigned-candidate CI now invokes this check with architecture and expected
identity/publisher/version read from the source manifest; actual hosted acceptance
remains pending. Those equality checks preserve the reviewed source contract and
do not approve the current placeholder identity as a final release identity.

Final identity/version choices, exact reviewed payload inventory,
complete provenance/SBOM, final signed-byte inspection and guest/native
ARM64 deployment gates remain open. Existing local test packages predate the
current source and cannot close any final-release row.
