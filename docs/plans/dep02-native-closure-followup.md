# DEP-02/16 native and retained-code closure follow-up

Reviewed 2026-11-09. **Positive retained-code evidence PASS; native vendor-build
and embedded WinApp runtime closure remain PENDING.** This bounded investigation
extends [the toolchain review](dep02-toolchain-review.md) and
[the qualified SBOM](dep16-sbom-evidence.md). It changes no dependency or release
decision and does not mark DEP-02 or DEP-16 complete.

## Exact candidate baseline

The inspected unsigned packages are the existing clean notice-containing
`9106040bc1eb460c1cd74fa7122d95460aa38a80` pair:

| Architecture | MSIX SHA-256 | Packaged Wintodo.exe SHA-256 |
|---|---|---|
| x64 | `4a5d345feb0b4a2e2bac454809436c9bacecf754f67dee5afad683bc2c247d5f` | `1a89321c4eb4caff1168a6e29269dd1a28c42125b4708a0a61977abf774e50db` |
| ARM64 | `3c0b688ec46288c5bedcc1c182a3dc0815535c1baf32fca18fa58e3e1be8405c` | `cdff178613a3f7c7942e83bca9020b2414b0bdf41f84ac00a1137c3049938826` |

The investigation reads those bytes, matching private native PDBs, original
package-cache content and primary vendor endpoints. It does not rebuild, execute
or modify the packages or cache. These are earlier unsigned candidates, not the
eventual signed release or proof of the current source's hosted execution.

## Retained managed code and generated projections are observable

The native PDBs are available in the private clean-source build outputs. Their
MSF stream directories and DBI symbol-record streams were parsed directly. Each
complete symbol stream contains address-bearing public symbols whose segment
and offset resolve into an executable section of the exact packaged EXE.
The EXE's CodeView GUID and age match the corresponding PDB:

| Architecture | PDB SHA-256 | CodeView/PDB GUID bytes | Age |
|---|---|---|---|
| x64 | `2ea8b3d10cb7b566105832950b255e201efffb3a031c3916c9ccffd440ef64e3` | `de9bd1c94baa3f40b60d5dac7b18722d` | 1 |
| ARM64 | `0fe50b6ea0c179c67a2464541ef7b2460034cba08b6a807a8b4e9592dad624d0` | `ad97e6cadfb9704a9403c45eacafc28f` | 1 |

This corroborates positive retained-code ownership instead of relying only on
restored package entries. The following counts are named executable-section
symbol records, including compiler-generated code; they are not unique method
counts, component sizes or performance measurements.

| Symbol family | x64 | ARM64 | Relationship to reviewed inputs |
|---|---:|---:|---|
| `Microsoft_Data_Sqlite` | 349 | 343 | SQLite managed wrapper code; reviewed Core package `10.0.12` |
| `SQLitePCLRaw_core` | 104 | 104 | SQLitePCLRaw core `3.0.5` |
| `SQLitePCLRaw_provider` | 79 | 82 | SQLitePCLRaw native provider `3.0.5` |
| `CommunityToolkit_Mvvm` | 230 | 230 | MVVM runtime/command code; reviewed package `8.4.2` |
| `CommunityToolkit_WinUI` | 1,249 | 1,249 | Toolkit controls, helpers and extensions; reviewed `8.2.251219` inputs |
| `Microsoft_Windows_SDK_NET` | 3,098 | 3,098 | Generated Windows projections from reviewed Windows SDK reference inputs |
| `WinRT_Runtime` | 5,696 | 5,707 | Retained WinRT support code, requiring its own provenance/licence association |
| `Rhp` | 109 | 106 | Native runtime helpers, consistent with the reviewed Native AOT runtime inputs |

Private `retained-code-observations.json` records complete-stream parsing,
package/EXE/PDB hashes, CodeView matching and representative names, addresses and
32-byte code hashes. It contains no private source paths. The version column
associates observations with the existing reviewed input graph; symbol names
alone do not independently attest those input versions or which compiler ran.

No `System_Numerics_Tensors` or `System.Numerics.Tensors` symbol string was
observed in either native PDB, and no corresponding public executable-section
symbol was found. This is **not proof of removal**: generic sharing, inlining,
name transformation and metadata retention can obscure source ownership. Keep
the existing Tensors notice and retention-unverified inventory. The SBOM still
lacks a supported input for complete retained-code proof and must continue to
report full dependency closure as INCOMPLETE.

The next candidate can preserve compiler dependency/retention diagnostics and
private PDBs for a reviewed ownership export. GUID matching is a local consistency
check, not authenticated build attestation; private PDBs must not be uploaded as
public release assets. Do not narrow notices from symbol absence alone.

## The ONNX version suffix cannot establish an upstream release

The shipped Windows ML package `Microsoft.Windows.AI.MachineLearning/2.1.74`
contains `onnxruntime.dll` with the previously reviewed product version
`1.24.20260504.1.7444c45`. Its exact original-package DLL hashes are:

| Architecture | Bytes | SHA-256 |
|---|---:|---|
| x64 | 21,659,280 | `3cffeff2d7c25b247a814212baab70eb1f37d727335d4c813ed73785df80a794` |
| ARM64 | 21,854,440 | `3e5959cfcdd1c9c09586c7ec27c547270a95182df2d060ece5650c317628e79e` |

The public ONNX repository resolves the suffix to
[`7444c45fdb4bfdbd37afde77b31dcb610b974a55`](https://github.com/microsoft/onnxruntime/commit/7444c45fdb4bfdbd37afde77b31dcb610b974a55),
dated 2024-10-04. That commit's
[`VERSION_NUMBER`](https://github.com/microsoft/onnxruntime/blob/7444c45fdb4bfdbd37afde77b31dcb610b974a55/VERSION_NUMBER)
contains `1.18.0`, not `1.24`. The raw version response SHA-256 is
`04d659518a1793d5396007fcabdc9211a8c1db8406bf4e0fa8069171fdb06518`.

This does not prove that the DLL was built from that public commit, nor that it
contains an old vulnerability. It demonstrates why the displayed suffix cannot
be substituted for a vendor-authenticated source commit, patch inventory or
upstream `1.24.x` release identity. The original ML nuspec has no repository
commit or embedded SBOM linking this vendor build to upstream sources.

Minimal closure evidence is a Microsoft component/BOM record that binds these
DLL hashes to the actual fork/source revision, relevant backports and embedded
third-party versions. Reusing an upstream ONNX package audit under an inferred
version cannot close this gap.

## DirectML product-version equality is also insufficient

The ML package's previously reviewed DirectML version is `1.15.4`. A read-only
ranged ZIP inspection of the official
[Microsoft.AI.DirectML/1.15.4 package](https://www.nuget.org/packages/Microsoft.AI.DirectML/1.15.4)
compared its desktop DLL entries with the ML package's actual DLLs:

| Source and architecture | Bytes | DLL SHA-256 |
|---|---:|---|
| ML package x64 | 18,700,224 | `257c75b2f607940c986d0b96d9309a2c897e57ef3192b6c678d707c22d747611` |
| DirectML package `bin/x64-win/DirectML.dll` | 18,527,776 | `9c9e6d822561c6c41b90e6994b3e8857cf1d66dbfb1e0c4c799c7c89b4e92da1` |
| ML package ARM64 | 18,616,640 | `08b031bcd5f79f7aa5cfc140806ddcf90cc2d43c6b2efc77982c18d926adf730` |
| DirectML package `bin/arm64-win/DirectML.dll` | 18,444,832 | `77b0db83ff903f2323f5caf538499d75af6038bbea23b7959f7d232d9a4ab9d4` |

Neither desktop entry matches. The ARM64EC entries also differ. This does not
establish a functional or security difference: signatures, packaging and vendor
build configuration can change bytes. It does establish that exact ownership
cannot be transferred to the public DirectML redistributable simply because the
version string agrees. Request the vendor build mapping rather than replacing
the DLL or claiming public release equivalence.

Ranged reads validated response offsets/lengths against one ETag and ZIP CRCs.
They did not calculate the complete 202,292,617-byte archive hash or verify its
NuGet signature. Entry-byte equality/difference is the bounded observation;
upstream package authenticity and full native closure remain separate.

## Current primary advisory checks and WinApp provenance

The primary records were fetched again on 2026-11-09:

| Record | Refreshed result |
|---|---|
| [HarfBuzz GHSA-r62p-jr5f-3mjm](https://github.com/harfbuzz/harfbuzz/security/advisories/GHSA-r62p-jr5f-3mjm) | Still lists `>= 5.3.0, <= 14.6.0`, with no patched release. Existing `14.2.0` tool inventory and bounded unsigned-command non-applicability proposal remain valid, owner disposition PENDING. |
| [.NET Brotli GHSA-p3jp-9v83-76w9](https://github.com/advisories/GHSA-p3jp-9v83-76w9) | Reviewed .NET 10 AOT/compiler range remains below `10.0.14`; Wintodo's reviewed target inputs meet that fix. This does not patch downloaded WinApp. |
| [PKCS GHSA-846f-f764-76mx](https://github.com/advisories/GHSA-846f-f764-76mx) and [GHSA-2fmv-rv2c-59x7](https://github.com/advisories/GHSA-2fmv-rv2c-59x7) | Still list .NET 10 PKCS through `10.0.12`, first fix `10.0.13`; retain the existing supported WinApp upgrade proposal. |
| [WinApp latest stable release](https://github.com/microsoft/WinAppCli/releases/tag/v0.8.0) | Still `v0.8.0`, published 2026-11-07; no SBOM appears among the returned release assets. No upgrade was made. |
| ONNX and DirectML public repository advisory endpoints | Both return empty lists. This is not full MSRC/vendor component coverage or an advisory-free certification. |

WinApp's pinned `v0.7.1` source commit has one returned public GitHub Actions run,
named `Copilot Setup Steps`, ID `36937857455`. It is not release build provenance.
The original `0.7.1` x64 executable remains SHA-256
`bf29f665ab4a030f20cd34a9121276301871ee89fd9c13118c7221563858d692`.
The already observed managed informational-version strings were reproduced, but
no supported embedded-runtime version record was identified. They cannot prove
the Native AOT runtime/compiler or Brotli patch level.

Obtain vendor release provenance binding that executable hash to the exact
compiler/runtime, or prepare a reviewed compatible tool replacement with such
evidence. Upgrading to `0.8.0` for its documented signing fixes remains a concrete
proposal, but its version alone cannot close the embedded-runtime or HarfBuzz
gaps. Do not use the pinned source's floating `10.0.x` build request as proof.

## Evidence and next work

Private evidence is under `artifacts/dep-native-closure-followup/`: retained-code
observations, PDB symbol observations, original primary responses and response
hash inventories, DirectML entry comparison and original inherited package
metadata. Raw PDBs, native paths and package-cache files remain private and
unchanged. Public records contain relative evidence locations only.
The existing release privacy scanner passes this report and the sanitized
retained-code observation JSON: two files, zero findings. Its result is
`public-evidence-privacy.json`; repository whitespace checks also pass.

The next useful actions are to integrate positive retained-code/projection
ownership into reviewed SBOM evidence, preserve retention diagnostics for the
final candidate, obtain Windows ML/DirectML and WinApp vendor build provenance,
and record the existing bounded HarfBuzz disposition with the owner. Full
SBOM/legal closure, current hosted execution, signed-package privacy, trust and
deployment gates remain open. No runtime/UI test, installation, trust change,
signing, remote mutation or publication occurred in this stream.
