# DEP-02/16 vendor provenance resolution and supported ML proposal

Reviewed 2026-11-09 from `db6c448` and existing `f1b9af5` candidate evidence.
**WinApp published-asset ownership PASS; vendor-declared ONNX source mapping
ESTABLISHED; embedded build/patch attestation and final release acceptance PENDING.**
This extends [the native next-actions record](dep02-native-closure-next-actions.md).
Root integration is separate from this stream's isolated dependency evaluation.
It changes no existing candidate, signing provider or advisory disposition.
Existing archive-signature and retained-code checks were not rerun; the new ML
archive receives its own verification.

## Exact WinApp executable matches the published release asset

The primary [WinApp v0.7.1 release API](https://api.github.com/repos/microsoft/WinAppCli/releases/tags/v0.7.1)
identifies target commit `e3cd0b94f3e7b776ac51ba0564f4908094e979b5` and provides
the x64 ZIP's size and SHA-256 digest. The complete official ZIP was downloaded
into ignored evidence, hashed, and read without extraction or execution:

| Observation | Exact value |
|---|---|
| Official asset | [winappcli-x64.zip](https://github.com/microsoft/WinAppCli/releases/download/v0.7.1/winappcli-x64.zip) |
| ZIP bytes | 99,013,508; matches release API |
| ZIP SHA-256 | `d925d1e32cdc320b6d271f653fd2cd3c05b5d7558deb20d1b548bead77900fcf`; matches release API digest |
| Contained `winapp.exe` bytes | 45,080,104 |
| Contained executable SHA-256 | `bf29f665ab4a030f20cd34a9121276301871ee89fd9c13118c7221563858d692`; exact reviewed tool match |

This establishes that the reviewed executable is present in Microsoft's published
release asset. The digest is release metadata observed through GitHub HTTPS;
it is not an independent signed build attestation or authenticated execution log.
The NuGet archive's separate repository signature remains its own evidence.

The pinned [build-environment template](https://github.com/microsoft/WinAppCli/blob/e3cd0b94f3e7b776ac51ba0564f4908094e979b5/.pipelines/templates/build-env.yaml#L17)
still requests `10.0.x`. The [release pipeline](https://github.com/microsoft/WinAppCli/blob/e3cd0b94f3e7b776ac51ba0564f4908094e979b5/.pipelines/release.yml)
and [build template](https://github.com/microsoft/WinAppCli/blob/e3cd0b94f3e7b776ac51ba0564f4908094e979b5/.pipelines/templates/build.yaml)
describe signing and repacking published artifacts, but these source descriptions
do not establish which compiler/runtime actually produced this executable.
Exact embedded runtime/compiler, PKCS backports and advisory disposition remain
pending. The existing `0.8.0` signing-fix proposal requires its own reviewed
artifact/tool acceptance; no upgrade occurred here.

## Microsoft supplies an ONNX source association for the exact ML package version

Microsoft's [ONNX Runtime versions shipped in Windows ML](https://learn.microsoft.com/en-us/windows/ai/new-windows-ml/onnx-versions)
states that its tables identify the ORT version or commit shipped with each ML
package release. The page, last updated **2026-10-26** and retrieved here on
**2026-11-09**, explicitly lists:

| Windows ML package | Vendor-listed release date | Vendor-listed ORT source | Vendor-listed ORT date |
|---|---|---|---|
| `Microsoft.Windows.AI.MachineLearning/2.1.74` | 2026-07-13 | `800ac32` (`1.24.6`) | 2026-04-28 |

The public ONNX repository resolves that abbreviation to
[`800ac32bc82d562c611d641b1112a8aa9f90c4f9`](https://github.com/microsoft/onnxruntime/commit/800ac32bc82d562c611d641b1112a8aa9f90c4f9).
Its [`VERSION_NUMBER`](https://github.com/microsoft/onnxruntime/blob/800ac32bc82d562c611d641b1112a8aa9f90c4f9/VERSION_NUMBER)
contains `1.24.6`; the returned version bytes have SHA-256
`066a140424616e1adef7f55944111fdec4bb5894c3c955a2a0a83f185c993320`.
The commit's committer timestamp is 2026-04-29T04:30:25Z, separately from the
vendor table's release date. These dates are not interchangeable build proof.

The existing verified original ML archive is SHA-256
`691165fa3c07a04b752cbf4a07e93ed13a418e9dea1ee89eb163d2225e2ba3af`,
bound to version `2.1.74` in both candidate inventories, with author/repository
signatures under the previously checked host trust configuration. Its previously
matched ONNX DLL hashes remain:

- x64: `3cffeff2d7c25b247a814212baab70eb1f37d727335d4c813ed73785df80a794`.
- ARM64: `3e5959cfcdd1c9c09586c7ec27c547270a95182df2d060ece5650c317628e79e`.

The earlier [suffix-only limitation](dep02-native-closure-followup.md#the-onnx-version-suffix-cannot-establish-an-upstream-release)
can now be narrowed: `1.24.6`/`800ac32` is a supported vendor declaration for the
exact package version, rather than an inference from the DLL's displayed suffix.
The declaration does not independently bind either DLL hash to a complete build
recipe, vendor modifications/backports or embedded third-party inventory. Those
attestation/closure gates, and DirectML's exact build mapping, remain pending.

## A current-support gap has a concrete, narrow upgrade candidate

The same Microsoft page says: **“Only the current release is officially supported.
Past and preview/experimental versions are shown for release history only.”**
It lists ML `2.5.96` / ORT `1.28.3` as current, and `2.1.74` in release history.
This establishes a vendor-documented ML support gap for the retained candidate
dependency. It does not establish a vulnerability, or classify the entire pinned
Windows App SDK as unsupported.

Read-only NuGet registration/catalog records establish the following:

| Input | Observed constraint |
|---|---|
| [ML `2.5.96`](https://api.nuget.org/v3/catalog0/data/2026.10.26.22.31.47/microsoft.windows.ai.machinelearning.2.5.96.json) | Listed; published 2026-10-26; targets `net8.0-windows10.0.17763`; `System.Numerics.Tensors >= 9.0.0` |
| ML `2.5.96` description | Includes ORT `1.28.3`; minimum OS build 18362, within this app's minimum 26100 |
| [Pinned SDK `2.5.1`](https://api.nuget.org/v3/catalog0/data/2026.09.16.22.47.15/microsoft.windowsappsdk.2.5.1.json) | Requires `Microsoft.WindowsAppSDK.ML >= 2.1.94` and exact runtime `2.5.1` |
| [Pinned SDK ML `2.1.94`](https://api.nuget.org/v3/catalog0/data/2026.09.16.22.47.15/microsoft.windowsappsdk.ml.2.1.94.json) | Permits `Microsoft.Windows.AI.MachineLearning [2.1.74, 3.0.0)` in native/net6/net8 groups |
| [SDK `2.6.5`](https://api.nuget.org/v3/catalog0/data/2026.11.03.02.58.04/microsoft.windowsappsdk.2.6.5.json) | Available; broader coordinated modular SDK/runtime update |

**Proposed bounded dependency update:** retain Windows App SDK `2.5.1`, add a
deliberate app reference to `Microsoft.Windows.AI.MachineLearning` with centrally
declared version `2.5.96`, and review the resulting app graph. This version meets
the existing declared ML range; its framework and stated OS minimum present no
identified metadata-level conflict. The repository currently enables central
versions without transitive pinning, so a central version entry alone would not
establish the intended direct selection. Root references and versions remain
unchanged in this stream; the isolated evaluation below applies this overlay.

NuGet-range compatibility does not establish API, Native AOT, runtime or vendor
integration compatibility. Before accepting the update, use an isolated checkout
to regenerate the supported locks in explicit update mode, review requested/
resolved changes, and run build/unit/AOT/package/privacy/notice/SBOM checks for
both architectures. Final guest/runtime acceptance remains separate. Recheck
the actual latest stable/support status at execution; no older candidate becomes
patched or supported by writing this proposal. A whole-SDK `2.6.5` upgrade is a
broader alternative requiring its own impact review.

## Isolated servicing evaluation

An ignored independent checkout under `artifacts/dep-ml-upgrade-evaluation/source`
is detached at exact `db6c448e95bce9ea3e89f528379f97d8fad0c7ef`, with the direct
ML reference, central `2.5.96` version and seven supplier-origin label updates.
The Windows App SDK version remains `2.5.1`. Supported ordinary x64/ARM64
restore regeneration and the Debug x64 warnings-as-errors build pass; all 678
existing unit tests pass with zero failures/skips. Both actual AOT publishes,
unsigned MSIX builds, structural inspections, package/layout privacy checks and
bound app-only NuGet audits pass. Both audits report zero vulnerable packages
and zero advisories; both privacy checks have zero findings.

The newly downloaded original ML archive is SHA-256
`35d6e84feb4142e8cecaf36704fa3ab0fc8251d892850fb80e8341f7d09653a3`.
NuGet signature verification passes under existing host settings/trust, with
unchanged archive bytes. Both its 13,996-byte `license.txt` and 331,175-byte
`ThirdPartyNotices.txt` are byte-identical to the previously inherited texts.
Only the source headings/URLs need the new package version; the supplier legal
bytes remain intact. The evaluation introduces no banned dependency under
PLAN §3 and services an already-retained SDK component. Actual AOT compatibility
is observed for this source overlay; installed/native runtime remains pending.

| Exact evaluated target | x64 | ARM64 |
|---|---|---|
| Unsigned MSIX SHA-256 | `c59f8551476b670e627772fd5a6fde89962238389ac1f902ee56f8d96586f07b` | `0f5be2903bd735b61a64189b1a4fc58543558d682b622959789ad9498024e812` |
| MSIX bytes / entries | 59,330,120 / 321 | 57,663,388 / 317 |
| Package/layout/summary files privacy-checked | 648; zero findings | 640; zero findings |
| Packaged executable and notices equal evaluated source/layout | PASS | PASS |

All four supported ordinary/AOT locked restore shapes pass afterward without
changing the reviewed lock bytes. Exactly four app lockfiles change, solely for
ML's `2.1.74` transitive input becoming an explicit `2.5.96` selection; the other
16 locks preserve their baseline bytes. NuGet's update-mode regeneration changed
line endings in otherwise identical graphs; those files were restored only after
confirming complete JSON equality. App locks are normalized to their baseline LF
format. The seven-file delta is `servicing-overlay.patch`; its read-only apply
check succeeds against the main checkout before integration.

The notice file remains 898,198 bytes. Reversing only the seven exact ASCII
origin-label substitutions returns its baseline bytes exactly: SHA-256
`ba042235866020798b50fcc24483abaea5a61c827340ca8db943a6819cac98fa` becomes
`428d0885ae8320200f6d9dbe1191e5d45accb2c295eb14dd315f0289262125ca`.
The latter bytes appear in both evaluated packages. No supplier body was
reformatted. The first attempted restore was prevented by an incorrect private
log path before restore execution; the corrected command passes. Native helper
`Out-Host` progress partly bypasses per-target redirection; its observed session
tail remains private alongside target logs and successful process/gate results.

These are unsigned evaluation artifacts from `db6c448` plus a dependency overlay,
not final packages from the parent's subsequently updated source. Current-source
integration/build/AOT, fresh qualified SBOM/legal closure, approved identity,
production signing and actual native runtime/deployment acceptance remain their
separate gates.

## Remaining actions and evidence

| Gate | Result from this stream | Remaining action |
|---|---|---|
| WinApp published release ownership | PASS; exact executable in digest-matched official ZIP | Preserve binding for final tool inputs |
| ML package-to-ONNX source declaration | ESTABLISHED; official package-version mapping and verified upstream version file | Vendor binary/build/backport/component attestation |
| ML supported dependency | Earlier candidate GAP; isolated and parent `2.5.96` servicing checks LOCAL PASS | Final reviewed-source MSIX/SBOM and actual runtime gates |
| WinApp runtime/PKCS/HarfBuzz | PENDING | Supported artifact/build mapping; review signing path separately from the prior unsigned-path assessment; owner advisory disposition |
| DirectML and complete native/legal closure | PENDING / INCOMPLETE | Exact vendor build/component records and final licence/SBOM acceptance |

Primary responses, content-encoding-qualified hashes, exact ZIP/executable
comparison and registration/catalog records are private under
`artifacts/dep-vendor-resolution/`. `resolution-summary.json` is a sanitized
summary. Microsoft Windows App SDK's pinned build-information specification and
release notes were also checked; their SDK-release metadata does not supply the
missing DLL build attestation. The isolated evaluation performs ordinary package
downloads, restores and builds within the repository. No root dependency or lock
change, external message, app launch, guest operation, signing, trust change or
remote write occurred. Shared tracking and integration remain the parent's
responsibility.

## Parent integration after 703757b

The reviewed seven-file servicing delta is applied to the current source,
including the launch-policy helper. Debug/Release x64 builds pass with zero
warnings/errors; both x64/ARM64 AOT publishes pass. The final layout/summary
privacy scan passes 649 files with zero findings. These are integration outputs
from `703757b` plus the servicing/test overlay, rather than final production MSIX.

The unit run initially failed 1/704 in a Data test's pooled helper. Investigation
reproduced process-global fixture cleanup interference, and a test-only scoped
cleanup/unpooled-helper correction passes three deterministic regressions and
the final full 707/707 run, zero failed/skipped. See
[the SQLite test-isolation record](dep15-sqlite-test-pool-isolation.md). Production
pooling/provider behavior remains unchanged.

After integration restores, 16 semantically unchanged locks were restored to
their exact baseline bytes; the four app locks match the reviewed new graphs
and bytes. The 898,198-byte notice file has SHA-256
`428d0885ae8320200f6d9dbe1191e5d45accb2c295eb14dd315f0289262125ca`.
Reversing only the seven origin-label substitutions yields the old exact file.
Root preservation, AOT/privacy and unit evidence is private under
`artifacts/dep-production-next-integration/`. Full final-source package/SBOM,
owner identity/signing/legal values and actual deployment acceptance remain
pending; the previously evaluated MSIX hashes above are not substituted for them.
