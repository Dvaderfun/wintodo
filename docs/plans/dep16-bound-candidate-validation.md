# DEP-16/18 clean-source bound unsigned candidates

Validated 2026-11-09 against exact source commit
`f1b9af570882ba67ef7ccecf18d7c47321a9e366`.
Status: **x64/ARM64 LOCAL UNSIGNED CHECKS AND CURRENT BOUND HANDOFF PASS;
production release acceptance remains PENDING.**

This batch rebuilds both unsigned architectures after
[the audit binding change](dep18-audit-graph-binding.md). Earlier package pairs
remain historical evidence under their original handoff contract. Their reports
cannot substitute for the current source/target/assets bindings.

## Source and command boundary

Both targets use an isolated local Git checkout under ignored
`artifacts/dep-bound-candidates/source`, detached at the commit above. Checkout
line-ending conversion is disabled locally. The matching source archive SHA-256
is `2e870f344f219fd37d83a3817a2922f5c478e42c9144fb213827e439dbab31ed`.
The checkout's own Git metadata lets the audit observe its exact HEAD regardless
of changes in the working repository. After both targets completed, all **511
archived files**, including **20 reviewed lockfiles**, remained byte-identical
to the archive. The isolated Git worktree also remains clean.

Each architecture follows the existing
[locked restore/publish/package sequence](dep16-local-candidate-validation.md)
and the current [CI commands](../guides/ci.md): target-specific locked
Release/AOT/self-contained restore with all NuGet advisories enabled;
`Publish-Aot.ps1 -Platforms x64` or `ARM64`; then SDK-native WinApp packaging
with explicit AOT properties and `--no-sign --self-contained --no-restore`.
Compiler, WinUI, trim/AOT and single-file warnings remain errors.

After packaging, `Invoke-CiDependencyAudit.ps1 -AppOnly -Architecture x64`
or `arm64` observes the unchanged target graph and checkout HEAD. The matching
toolchain export and private assets bytes are saved before the next target
restore. Structural inspection checks the source manifest identity/publisher/
version. Whole package/layout/public metadata privacy, source notice inclusion,
layout byte equivalence and qualified SBOM/schema checks use exact new bytes.

The original placeholder identity and version `1.0.0.5` remain intact. This
checks source consistency; it does not approve production identity, version
mapping, licensing or a complete payload allowlist.

## Exact evidence

| Check | x64 | ARM64 |
|---|---|---|
| Target-specific locked restore, AOT publish and unsigned MSIX | PASS, warnings as errors | PASS, warnings as errors |
| Exact structure/source manifest/native/runtime/assets/activation checks | PASS, 54 inspected PE files | PASS, 53 inspected PE files |
| Whole layout/MSIX/public metadata privacy | PASS, 650 files; zero findings | PASS, 642 files; zero findings |
| Common AOT-layout/MSIX bytes, including executable and notices | PASS, 311/311 files | PASS, 307/307 files |
| Source notice bytes in actual package | PASS, 898,198 bytes | PASS, 898,198 bytes |
| Final app audit/source/target/assets/tool graph binding | PASS, zero vulnerable packages/advisories | PASS, zero vulnerable packages/advisories |
| Qualified SBOM/vendor file ownership/privacy | PASS, zero unmatched vendor binaries | PASS, zero unmatched vendor binaries |
| Official CycloneDX 1.6 schema validation | PASS | PASS |
| Full retained-code/vendor/projection closure | INCOMPLETE | INCOMPLETE |

| Exact input | x64 | ARM64 |
|---|---|---|
| MSIX bytes / files | 58,709,839 / 321 | 57,027,278 / 317 |
| MSIX SHA-256 | `3d4e8b44e7310af1e8e480a8e28a7cf4bbf6d48e450bb9af9cf877d267316862` | `a6882cda602f170c37a4ca4888e3700a820bebd1605d1caf23568559a7377952` |
| Packaged executable SHA-256 | `94d8f51f417b9fd8517f17934fa9303dde2fe35a7b0b915209024484c632b9b5` | `a8cd161126f385cbacac12d55ddd8b4e36e6115515ee60e971ab004e709696ec` |
| Bound assets SHA-256 | `5d6e7016d23eb2033851be751158bb9ebe59b9adde6006ae609ae709a6c4ee3f` | `2cfd19cd65125d5424c7e44fd024939557b94d1d1855095b8d4ed0a891246e18` |

Both actual `Assets/ThirdPartyNotices.txt` entries equal the archived source,
SHA-256 `ba042235866020798b50fcc24483abaea5a61c827340ca8db943a6819cac98fa`.
The qualified BOMs match all **105 vendor binary files**, separately from the
two app executables. Full closure remains INCOMPLETE. PowerShell `Test-Json`
validates both BOMs against the existing official schema copy, SHA-256
`1ebcb88a2c845ecb6ff7bee7aeabdff9422cb0347f3d6875b241bd444b7e098f`, with
external references fetched. No schema-validation dependency was installed.

The current [unsigned handoff checker](dep18-candidate-handoff.md) passes the
fresh hash-pinned pair, including all ten inputs, source/target/assets bindings,
every MSIX entry, graph/identity/version agreement and final input rehashes.
Its scope is `unsigned_candidate_input_consistency`; `release_authorized` stays
**false**, and nine production/provenance gates remain pending. No legacy
unbound evidence is substituted. These reports identify the exact observed
clean checkout; they do not authenticate execution or a release decision.
The handoff report SHA-256 is
`fbe61db0ad7ef65b3cdfd3545d2cb2be153a9b2397819a71dc542a734f74878a`;
its private descriptor SHA-256 is
`a3f4c223f64a628b27acf07e6ee375b64f15941627582c77de0251229dfe45f6`.

[Positive retained-code evidence](dep16-retained-code-evidence.md) also passes
for both exact fresh package/PDB pairs. The private PDBs match CodeView GUID/age
and executable-section observations. Their symbol families identify retained
code without attesting its source versions or proving full closure.

## Evidence and remaining gates

Private evidence is under `artifacts/dep-bound-candidates/`. Each architecture
directory retains its MSIX, structural/privacy reports, bound dependency audit,
available/configured toolchain record, preserved assets, AOT summary and qualified
SBOM/privacy record. Native tools, raw logs, Git metadata, assets and private
PDBs remain ignored. `source-package-comparison.json` records source/layout/
notice integrity, `candidate-inputs.json` pins the ten inputs, and
`candidate-handoff.json` records the passing current contract. Each target's
`sbom-schema.json` pins the BOM and schema bytes. The descriptor remains private;
sanitized comparison and handoff reports pass their separate privacy scan.
`public-evidence-privacy.json` records five public evidence inputs scanned with
zero findings, including this document and both schema-validation records.
The first x64 publish's `Out-Host` diagnostics are private tool-session output;
the ARM64 outer process additionally retains its full private runner log.

Local graph/source consistency and tool availability/configuration are not
authenticated build execution or hosted acceptance. Full retained-code/vendor/
projection closure, native/tool advisory disposition, independent source
authentication, owner production decisions, current hosted CI/protection,
protected signing, signed-byte checks, clean installed deployment/update/
uninstall, native ARM64 runtime gates and publication remain pending.

No machine setup, guest start/provisioning, certificate trust, installation,
app launch, UI automation, remote mutation, signing or publication occurred.
