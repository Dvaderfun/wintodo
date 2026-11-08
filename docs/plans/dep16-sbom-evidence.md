# DEP-16 exact-package SBOM evidence

Implemented 2026-11-08. **Bounded exporter, synthetic regressions and fresh x64/ARM64
file/package ownership checks PASS. Full release dependency closure remains
INCOMPLETE.** This is preparation under
[the deployment plan](deployment-readiness.md), not a production-release or
licensing approval.

## What the exporter proves

[Export-ReleaseSbom.py](../../scripts/Export-ReleaseSbom.py) writes CycloneDX 1.6
JSON for a supplied MSIX, matching target-specific app assets and an expected
package SHA-256. It reuses the existing structural/privacy inspector before
reading binary entries. The BOM records the exact package hash, caller-declared
source commit, architecture and private assets-file hash. Source attribution is
explicitly **not a build attestation**.

Every separately shipped DLL/EXE is hashed. Vendor ownership requires an exact
byte hash match inside an original local NuGet archive, including binaries inside
the Windows App SDK Runtime package's nested MSIX files. A filename or restored
dependency entry alone cannot establish ownership. The BOM includes the native
app executable, vendor native/resource/interop files and restart helper; binary
kind and PE machine values remain available without exposing filenames. Binary
entry ordinals and hashes identify files in the exact supplied package.

Only actual binary files and hash-matched source packages become standard
CycloneDX product components. Unmatched restored, SDK-download and known build
tool records are preserved in the JSON-valued
`wintodo:unverified-input-inventory` metadata property. Their records retain
package names, versions, purls, raw archive hashes and hash-binding evidence.
They do not become implicit runtime dependencies: CycloneDX's omitted scope
defaults to `required`, so omitting scope on uncertain product components would
be misleading. Known tools are labelled as build inputs; other restored inputs
remain retention-unverified. No input is declared trimmed without retained-code
evidence.

The standard composition aggregate and `wintodo:full-dependency-closure` are
`incomplete`/`INCOMPLETE`. Linked managed/runtime code, vendor-embedded components
and generated Windows projections remain `PENDING`. Hash ownership does not
resolve embedded ONNX/DirectML versions, every inherited licence obligation,
source authenticity, signatures or deployment behaviour.

[The native retained-code follow-up](dep02-native-closure-followup.md) adds
positive symbol evidence from private PDBs matched to the exact `9106040`
packaged executables. It identifies retained SQLite, Toolkit, WinRT, generated
projections and native runtime helpers on both architectures. Symbol names
associate code with reviewed inputs but do not attest versions, prove absent
code was removed, or resolve vendor-embedded contents. This separate evidence
does not change the exporter's qualified INCOMPLETE closure status.

[The reproducible retained-code exporter](dep16-retained-code-evidence.md)
now supplies separately hash-pinned MSIX/PDB observations with matching
CodeView, DBI machine and final-section mapping fields. It records supported
public-symbol families and unmapped/unsupported records without inferring
component removal or versions. This bounded evidence does not provide the
complete ownership input needed to change the BOM's closure status.

## NuGet hash binding

Signed NuGet archives have two different hashes. The assets `sha512` value is
NuGet's package content hash; it need not equal SHA-512 of the signed `.nupkg`.
The exporter preserves that distinction:

- When assets SHA-512 equals the raw archive digest, it verifies that direct
  binding and records `assets-raw-sha512`.
- For signed archives with a different content hash, it checks raw SHA-512
  against the local `.nupkg.sha512` sidecar and compares assets content hash with
  `.nupkg.metadata.contentHash`. Cache metadata must have the reviewed version-2
  structure and NuGet.org source. The signature entry must exist. Raw archive,
  sidecar and cache-metadata hashes are recorded; their bytes remain unchanged.
- SDK download records lack an assets content hash. Their raw archive digest is
  checked against the sidecar, with that weaker binding explicitly identified.

These local cache bindings **do not independently authenticate upstream packages
or verify NuGet signatures**, and the normalized signing hash is not recomputed.
The exporter does not implement signing normalization or signature cryptography.
Original archives and available sidecar/metadata inputs are checked again after
reading to detect changes during export. Final source/build provenance remains a
separate requirement.

## Use and failure behaviour

Keep a copy of the matching app `project.assets.json` after each architecture's
locked AOT restore. A subsequent target restore changes the shared app graph.
Assets and cache files stay private; only the sanitized BOM may be uploaded after
its own privacy scan.

```powershell
python -B scripts/Export-ReleaseSbom.py <exact-x64.msix> `
  --assets <preserved-x64-project.assets.json> --architecture x64 `
  --source-commit <full-reviewed-commit> `
  --expected-package-sha256 <exact-package-sha256> `
  --output artifacts/release-sbom-x64.json
python -B scripts/Test-ReleasePrivacy.py artifacts/release-sbom-x64.json `
  --output artifacts/release-sbom-x64-privacy.json --private-root $PWD
python -B -m unittest discover -s tests/scripts -p test_release_sbom.py
```

Use `ARM64` and the preserved `win-arm64` graph for the other architecture.
`--package-root` can explicitly replace the assets cache roots; at most four
roots are accepted. Archive identifiers, paths, sizes and expansion are bounded.
The script never extracts, registers, launches or modifies a package/cache.

Exit 0 means the bounded export succeeded; inspect `binary-ownership` and
`full-dependency-closure` separately. An unresolved vendor file remains explicit
in the BOM rather than acquiring an invented owner. `--require-complete` returns
nonzero even when all vendor files map, because no full retained-code proof input
exists yet. Successful export cannot close DEP-16.

Invalid inputs, wrong target/hash, corrupt metadata, missing sources and source
changes return nonzero with fixed-category diagnostics. Outputs must be JSON and
cannot alias supplied inputs, hard-linked files or anything inside package-cache
roots. A safe previous report is replaced with failure evidence on rejected
reruns; when roots/output safety cannot be established or output is unwritable,
the existing output is preserved and callers must honor the failure exit code.

## Observed verification

[Twenty-two focused regressions](../../tests/scripts/test_release_sbom.py) pass.
They cover exact hashes versus misleading filenames, nested runtime MSIX,
unresolved files, original-archive tampering, raw/normalized NuGet hash
differences, malformed/missing/mismatched cache metadata, target mismatch,
redacted identifiers, cache-path confinement, stale-report invalidation,
input/hard-link preservation, uncertain-input scope and incomplete-closure
rejection. No native build or UI session is needed for those tests.

The fresh notice-containing MSIX files use source
`9106040bc1eb460c1cd74fa7122d95460aa38a80`. Their package SHA-256 values are
`4a5d345feb0b4a2e2bac454809436c9bacecf754f67dee5afad683bc2c247d5f` (x64) and
`3c0b688ec46288c5bedcc1c182a3dc0815535c1baf32fca18fa58e3e1be8405c` (ARM64).
Exact exporter runs against each preserved target graph report:

| Evidence | x64 | ARM64 |
|---|---|---|
| Shipped binary file records, including app | 54 | 53 |
| Hash-matched vendor binary ownership | PASS; zero unresolved | PASS; zero unresolved |
| Standard NuGet source-package components | 11 | 11 |
| Separate unverified input records | 29 | 30 |
| BOM privacy scan | PASS; one file, zero findings | PASS; one file, zero findings |
| Official CycloneDX 1.6 schema validation | PASS | PASS |
| Full retained-code/vendor/projection closure | INCOMPLETE | INCOMPLETE |

The two packages therefore account for 105 vendor binary files by exact hash,
plus both first-party executables. This count does not imply that every vendor
file is native or that its embedded third-party/runtime contents are resolved.

Private evidence is under `artifacts/dep-sbom/`: `fresh-x64-bom.json`,
`fresh-arm64-bom.json`, their matching `*-bom-privacy.json` reports and the
official schema downloaded from
`https://cyclonedx.org/schema/bom-1.6.schema.json`. The schema SHA-256 is
`1ebcb88a2c845ecb6ff7bee7aeabdff9422cb0347f3d6875b241bd444b7e098f`;
the BOM SHA-256 values are
`ede7ca552de2feca1de40c55e6938ea3242dfc46250fa1964c6ece7171904aae` (x64) and
`425abdf3335a187f4572dedb7888d3c916b93ec50031a3225840d324dc28b7fe` (ARM64).
PowerShell's built-in `Test-Json -SchemaFile ... -FetchExternalReference` performed
the independent schema validation; no validation dependency was installed.

This evidence describes the stated unsigned source/package bytes. Eventual
hosted CI, final signed packages, independent full payload/identity
approval, legal/asset rights and deployment/runtime acceptance retain their own
gates. No identity, first-party licence, signing provider, machine configuration,
remote setting or publication decision changed.
