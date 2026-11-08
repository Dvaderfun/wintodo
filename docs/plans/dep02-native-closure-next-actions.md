# DEP-02/16 native closure: verified archives and next actions

Reviewed 2026-11-09. **Original NuGet archive signature verification LOCAL PASS;
embedded vendor/build provenance and complete dependency closure PENDING.**
This adds one evidence gate to [the native closure follow-up](dep02-native-closure-followup.md).
It does not change dependencies, the qualified BOM, release decisions or notices.

## Newly established evidence

The two [fresh bound candidate BOMs](dep16-bound-candidate-validation.md) identify
11 distinct hash-matched vendor-source packages each, plus separate unverified
input inventories of 29 x64 and 30 ARM64 records. Their combined inventory has
45 unique package/version archives, including the pinned WinApp tool and both
Native AOT runtime/compiler targets. Unverified inventory membership does not
establish that an input is shipped or retained.

The installed SDK's supported verification command ran against those original
cached archives:

```powershell
dotnet nuget verify '<exact-original.nupkg>' --all --verbosity normal --force-english-output
```

All **45/45** commands exit zero with no NuGet diagnostic codes. All 45 report
repository signatures; 40 also report author signatures. Every archive's
complete raw SHA-256 matches the corresponding record in the supplied BOMs and
remains equal before/after verification. The two BOM files also remain unchanged
and declare source `f1b9af570882ba67ef7ccecf18d7c47321a9e366` with the expected
architecture. This binds the observations to the provided records, while
preserving their caller-declared source/build-attestation limitation.

Raw archive SHA-256 is used here. It is distinct from the assets file's normalized
NuGet SHA-512 content hash; signature verification does not silently substitute
one for the other. The pinned WinApp archive's raw SHA-256 is
`211e774cf742c32cd7fc70223d93d175b7b3c07666839b52a7bd39fc72d93323`.

NuGet verification uses the host's effective NuGet settings and existing trust
store. No new certificate, signer allowlist or explicit approved fingerprint was
adopted. A successful signed-archive check supplies integrity/trust evidence
under that configuration; it does not attest the embedded compiler, vendor fork,
backports, all component licences, or the final MSIX build. This result closes
the bounded signature-verification gap for these supplied archives, without
making [the qualified SBOM](dep16-sbom-evidence.md) complete.

Sanitized results, exact hashes, exit codes, signature kinds and private-log
hashes are in `artifacts/dep-native-next-actions/original-archive-signature-verification.json`.
The corresponding one-off read-only runner is `verify-original-archives.py` in
the same ignored directory. Raw logs remain private because NuGet prints cache
paths and certificate details. No package/archive was modified, registered or
launched. No tool installation, signing, trust change or remote write occurred.

The release privacy scanner passes this document and the sanitized summary:
two files, zero findings, recorded in `public-evidence-privacy.json` in the same
ignored directory. Local document links and whitespace checks pass.

## Existing evidence and remaining closure

The following evidence is inherited from the linked reviews; this stream did
not repeat native version queries, PDB exports or primary-advisory HTTP reads.

| Closure item | Existing supported evidence | What remains before acceptance |
|---|---|---|
| Original archive authenticity | New 45-archive signature gate above; exact raw hashes match supplied inventories | Reproduce against final hosted candidate inputs under reviewed trust policy; source/build attestation is separate |
| Separate vendor binary ownership | Every vendor DLL/EXE in both fresh MSIX files hash-matches original package content | Embedded third-party ownership/version mapping and licence disposition |
| Linked managed/runtime/projection ownership | Eight positive symbol families on exact MSIX/EXE/PDB pairs; final-section mappings verified | Complete supported retention/ownership input and authenticated version/build association; absent symbols do not prove removal |
| Wintodo .NET runtime/compiler | Reviewed target graphs select `10.0.14`, meeting the recorded Brotli fix | Exact hosted execution and current advisory check for final candidate; this does not patch downloaded WinApp |
| WinApp embedded runtime | Exact `0.7.1` executable/hash and pinned source; no usable runtime SBOM/attestation found in prior review | Vendor evidence binding the executable hash to runtime/compiler and relevant backports, or a reviewed replacement with that proof |
| WinApp HarfBuzz | Bundled `14.2.0` is within the advisory range; reviewed unsigned native-SDK path does not invoke font subsetting or emit fonts | Owner disposition of the bounded invocation-specific non-applicability proposal, with review trigger on command/asset/tool changes |
| Windows ML ONNX/DirectML | Exact `Microsoft.Windows.AI.MachineLearning/2.1.74` DLL ownership; displayed ONNX suffix and DirectML version do not establish upstream build equivalence | Microsoft hash-bound fork/component/backport/BOM record and applicable vendor advisory disposition |
| MSVC/SDK tools | Exact available/evaluated binary versions/hashes and reviewed hosted-image inventory | Actual current hosted invocation evidence and component/servicing disposition |

The existing [WinApp upgrade proposal](dep02-toolchain-review.md#follow-up-affected-harfbuzz-operations-and-a-newer-winapp-candidate)
identifies `0.8.0` signing/PKCS fixes. The newer version is not evidence of a
patched HarfBuzz or a particular embedded Native AOT runtime. This stream does
not recommend replacing vendor DLLs or pruning the SDK closure to bypass review.

## Precise vendor evidence requests

Prepare these requests for the owner to send through the selected support
channel. No external message has been sent.

1. **WinApp:** for the original `0.7.1` x64 executable SHA-256
   `bf29f665ab4a030f20cd34a9121276301871ee89fd9c13118c7221563858d692`,
   provide the release build record/SBOM binding that artifact to exact SDK,
   Native AOT compiler/runtime, PKCS and bundled Skia/HarfBuzz versions or
   backports. Identify the operation-specific disposition for
   [GHSA-p3jp-9v83-76w9](https://github.com/advisories/GHSA-p3jp-9v83-76w9),
   [GHSA-846f-f764-76mx](https://github.com/advisories/GHSA-846f-f764-76mx),
   [GHSA-2fmv-rv2c-59x7](https://github.com/advisories/GHSA-2fmv-rv2c-59x7), and
   [GHSA-r62p-jr5f-3mjm](https://github.com/harfbuzz/harfbuzz/security/advisories/GHSA-r62p-jr5f-3mjm).
   If `0.7.1` is unsupported, identify a supported replacement and equivalent
   hash-bound build evidence. A source template requesting `10.0.x` is insufficient.
2. **Windows ML:** for package `Microsoft.Windows.AI.MachineLearning/2.1.74`,
   bind the following DLL hashes to actual source/fork revisions, embedded
   component versions, relevant backports and vendor advisory coverage. Explain
   the ONNX informational-version suffix rather than equating it to an upstream
   release. Supply redistribution/BOM records for the supplied builds.

| Binary | x64 SHA-256 | ARM64 SHA-256 |
|---|---|---|
| ONNX Runtime | `3cffeff2d7c25b247a814212baab70eb1f37d727335d4c813ed73785df80a794` | `3e5959cfcdd1c9c09586c7ec27c547270a95182df2d060ece5650c317628e79e` |
| DirectML | `257c75b2f607940c986d0b96d9309a2c897e57ef3192b6c678d707c22d747611` | `08b031bcd5f79f7aa5cfc140806ddcf90cc2d43c6b2efc77982c18d926adf730` |

These hashes come from the earlier original-package comparison. Validate their
association again when reviewing final candidate bytes; a later package/version
must have its own vendor request and evidence.

## Smallest next action

Record the existing bounded HarfBuzz disposition with the owner, and pursue the
two precise vendor build records while hosted/deployment work proceeds
independently. Preserve this signature gate for final candidate input validation.
Complete native/SBOM/legal closure remains INCOMPLETE; DEP-02/16 remain open.
