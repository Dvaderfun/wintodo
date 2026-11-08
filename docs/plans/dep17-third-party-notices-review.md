# DEP-17 shipped dependency notices review

Reviewed 2026-11-08 for clean source `965d86b358d3a384328e35ce7a02b896e58eb805`.
Status: **BOUNDED EXACT-PACKAGE REVIEW PASS; NOTICE ADDITIONS PREPARED; final
licensing and publication acceptance remain pending.** This is a review and proposed source
list, not an approved first-party licence or a change to the app payload.

## Finding and evidence scope

The packaged [ThirdPartyNotices.txt](../../src/Wintodo.App/Assets/ThirdPartyNotices.txt)
lists the 12 centrally declared packages, including build/test tools, and supplies
shared MIT/Apache texts plus Windows App SDK licence terms. That list does not
describe the actual native/runtime closure. The existing Foundation copyright
and MIT grant substantively cover the .NET-owned MIT text; a difference in
heading, quotes or line wrapping is not a missing-copyright finding.

The concrete omissions are vendor-supplied notices for the linked .NET runtime,
Windows App SDK Runtime, Windows ML and WebView2, plus WebView2's distinct
redistribution terms and Windows ML's distinct licence. Some package notices
include tools/tests or code removed by trimming; absence of a whole notice file
does not prove every component in that file is shipped.

Private inventory covers 35 packages in the restored x64 AOT app graph and five
additional target runtime/projection packs: **40 package records, 45 legal files**.
Each captured legal file records its package-relative location, byte count,
SHA-256 and full-text presence in the current app notices after whitespace
normalization. Package-cache files were read without modification. The separate
[native dependency review](dep02-toolchain-review.md) maps 52 x64 and 51 ARM64
root-level DLLs to restored package/runtime-MSIX hashes. The fresh candidate
comparison is recorded below; the prior mapping is not final-package evidence.

## Runtime/native notices to assemble

The following paths are **inside the named original NuGet package**. Download
the exact package/version via the linked primary package record; do not substitute
the newest upstream default-branch licence or assume a parent metapackage licence
covers every child package.

| Shipped or linked component | Exact inherited source | Existing coverage and concrete action |
|---|---|---|
| Native Windows App SDK closure: Foundation `2.3.12`, InteractiveExperiences `2.1.9`, AI/Search `2.5.5`, DWrite `2.1.0`, Widgets `2.0.5`, WinUI `2.3.9`, Runtime `2.5.1` | [Microsoft.WindowsAppSDK.Runtime/2.5.1](https://www.nuget.org/packages/Microsoft.WindowsAppSDK.Runtime/2.5.1): `license.txt` and `NOTICE.txt` | Generic SDK `license.txt` is already present verbatim after whitespace normalization. Add the complete `NOTICE.txt` (340,893 bytes). Runtime/Base/metapackage notice bytes are identical; include one copy with those component associations. The entire WinUI root `NOTICE.txt` is contained in this runtime notice, so no separate duplicate is needed. |
| Windows ML/ONNX/DirectML: `Microsoft.Windows.AI.MachineLearning.dll`, `onnxruntime.dll`, `DirectML.dll` | [Microsoft.Windows.AI.MachineLearning/2.1.74](https://www.nuget.org/packages/Microsoft.Windows.AI.MachineLearning/2.1.74): `license.txt` and `ThirdPartyNotices.txt` | Both texts are absent. Add the distinct Windows ML Runtime licence (13,996 bytes) and supplied notices (331,175 bytes). Its licence §1(d) preserves applicable third-party terms; §3 permits the files binplaced by WindowsAppSDK for self-contained apps and sets distribution conditions; §4(c) prohibits removing/modifying supplier notices. The native fork's upstream component/version mapping remains a separate advisory/SBOM gap. |
| WebView2 loader and managed interop delivered by the SDK | [Microsoft.Web.WebView2/1.0.3719.77](https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.3719.77): `LICENSE.txt` and `NOTICE.txt` | Both absent. `LICENSE.txt` (1,487 bytes) requires binary redistribution to reproduce Microsoft's copyright, conditions and disclaimer; this is a BSD-style text, not the shared MIT grant. Retain package `NOTICE.txt` (3,894 bytes) conservatively while its ANTLR/tool applicability is resolved. Loader/interop redistribution does not establish the installed browser runtime's version, licence or patch state. |
| .NET `10.0.14` runtime linked into `Wintodo.exe` | [Microsoft.NETCore.App.Runtime.NativeAOT.win-x64/10.0.14](https://www.nuget.org/packages/Microsoft.NETCore.App.Runtime.NativeAOT.win-x64/10.0.14), and ARM64 counterpart: `LICENSE.TXT`, `THIRD-PARTY-NOTICES.TXT` | Existing Foundation MIT grant/copyright is substantively present. Add an explicit linked-runtime association and the supplied third-party notices (78,041 bytes), missing from the app notice file. The x64/ARM64 NativeAOT and ordinary runtime-pack legal texts match byte-for-byte. Use runtime-pack ownership as the reason for inclusion; the build-only ILCompiler package happens to carry the same notice bytes. |
| .NET/Windows Community Toolkit runtime/control dependencies | [CommunityToolkit.Mvvm/8.4.2](https://www.nuget.org/packages/CommunityToolkit.Mvvm/8.4.2), [Common/8.2.1](https://www.nuget.org/packages/CommunityToolkit.Common/8.2.1), WinUI SettingsControls/Sizers/Extensions/Helpers/Triggers `8.2.251219`: `License.md`; Common/Mvvm `ThirdPartyNotices.txt` | Shared Foundation MIT rights/copyright are present. List transitive components and preserve the shared Common/Mvvm third-party notice (8,600 bytes) pending applicability review. It explicitly identifies runtime, generator and unit-test material; retaining the whole supplied notice does not assert that every listed tool/test dependency ships. |
| `Microsoft.Data.Sqlite.Core/10.0.12` and `System.Numerics.Tensors/9.0.0` managed runtime inputs | [Sqlite exact source licence](https://github.com/dotnet/dotnet/blob/95017c711e6afc1085133d440e42b4bd78155701/src/efcore/LICENSE.txt); [System.Numerics.Tensors/9.0.0](https://www.nuget.org/packages/System.Numerics.Tensors/9.0.0): `LICENSE.TXT`, `THIRD-PARTY-NOTICES.TXT` | MIT copyright/grant is substantively covered. Add correct component associations. Tensors' 75,640-byte third-party notice differs from the `10.0.14` runtime notice; preserve it conservatively until retained-code evidence allows a narrower scope. Presence among resolved runtime inputs alone does not prove it survived Native AOT trimming. |
| SQLitePCLRaw managed core/config/provider `3.0.5`, plus native SQLite `3.53.4` | [SQLitePCLRaw exact source licence](https://github.com/ericsink/SQLitePCL.raw/blob/ed046114d5a30534e13294d94d78eb73de896ad4/LICENSE.TXT); [SQLite/3.53.4](https://www.nuget.org/packages/SQLite/3.53.4): `LICENSE.txt` | Apache-2.0 and SQLite's public-domain statement are present. Add correct transitive/runtime associations. Reviewed SQLitePCLRaw packages have no additional bundled NOTICE. Preserve any applicable upstream attribution if the final closure or modified sources introduce it; Apache §4 governs licence/NOTICE preservation. No native SQLite third-party notice addition was identified. |

Windows App SDK and Windows ML licence §3 also set end-user/distributor terms
and trademark/distribution conditions. Copying a licence into a notice file
does not by itself establish compliance with all of those conditions. Keep that
review separate from the owner's first-party licence selection.

## Concrete proposed additions and deduplication

Private review files under `artifacts/dep-next-notices/` provide an actionable
assembly without changing the payload:

- `notice-additions-plan.json`: nine exact component/source/byte/hash/reason
  records, with conditional Tensors scope identified.
- `proposed-notice-additions.txt`: unmodified UTF-8 legal bodies with component
  and package-relative source headers; **856,256 bytes**, SHA-256
  `bb76471c1229c2d77132fab89d765c3276f5faa5f39602ca09dac5411acedc56`.
- `legal-source/`: the original captured legal bytes; `inventory.json` and
  `upstream-licenses.json` identify package/source provenance and hashes.

The existing release-path privacy scanner passes the proposed additions as one
text input with **zero findings**; private `proposed-additions-privacy.json`
records that bounded check. This does not replace final package/source privacy,
secret scanning or required legal/public attribution review.

The proposal adds the nine files described in the table, reuses one copy of
identical per-architecture/runtime SDK notices and includes one .NET-owned MIT
text for explicit component attribution. It does not remove the existing
notices or choose an application licence. The Runtime NOTICE already includes
WinUI's root NOTICE. A shared generic licence label never substitutes for
WebView2's separate copyright/conditions or the ML terms.

Keep the vendor legal wording intact, including its “Do Not Translate or
Localize” instruction. Deduplicate only byte-identical or fully contained
whole legal texts with explicit component associations. Avoid selecting
individual permissive-looking paragraphs from a mixed vendor notice. If
trimmed/tool-only components are excluded, retain the evidence establishing
that those notice entries do not pertain to the delivered work.

## Build/test tools and ownership limits

WinApp CLI `0.7.1`, WinUIAnalyzer `0.7.1`, Windows SDK BuildTools/MSIX, ILCompiler,
ILLink, xunit and test-report/coverage extensions are build/test tools. Their
presence in `Directory.Packages.props` or a restored graph is not evidence that
their executables/libraries ship in the MSIX. Existing app notices list several
of them; this review does not remove required attribution from source or tools.
The WinUI package's `tools/NOTICE.txt` applies to tool content and is distinct
from the inherited runtime notice scope. App-level notice additions are driven
by runtime ownership rather than bundling every build tool's licence.

`Microsoft.Windows.SDK.NET.Ref/10.0.26100.57` supplies projection/reference inputs
and points to the [Windows SDK licence](https://aka.ms/WinSDKLicenseURL), with no
legal text file in the examined archive. Source-generated or linked projection
content needs its actual owner/redistributable terms recorded in the final SBOM;
neither an API reference package nor a graph count proves retained-code closure.

The [asset review](dep13-source-privacy-review.md) still leaves redistribution
rights for eight photographic backgrounds unverified. Foundation/Microsoft
dependency licences do not license those images. Final icons, first-party
licence, public attribution/contact and screenshot rights remain owner-pending.

## Fresh candidate comparison and remaining acceptance

The isolated candidate source is the exact clean commit named above; its archive
SHA-256 is `4f5ac51702027cc744d8fec6620cd1cedd1bc32161aa7ef70bf0e8232bb0b7a0`.
The [fresh unsigned candidate validation](dep16-local-candidate-validation.md)
produced both architectures from that unchanged source. Full archive enumeration
and package-hash comparison give these bounded ownership results:

| Exact unsigned package | SHA-256 | Root DLL/package-source matches | Other vendor executable |
|---|---|---|---|
| x64 | `294556265b39a695b27b901ed3d063766df815f68bb172c68be469fb96eaae8d` | **52/52**, no nested DLLs | `RestartAgent.exe` matches Runtime/2.5.1's x64 MSIX and Foundation/2.3.12 native content |
| ARM64 | `fc663445fc3a2b053e1f98454faaef887822028482d047d1abf862cf39eecf8d` | **51/51**, no nested DLLs | `RestartAgent.exe` matches Runtime/2.5.1's ARM64 MSIX and Foundation/2.3.12 native content |

The RestartAgent hashes are
`c0d146fa48dbb765df0829ab9578fa81d3c3d9e694c5f0872172c0718e489a4b`
(x64) and `f3c59966c59290f43a4d06fd816b4a1b6291146eec804d6a67bb01f845984775`
(ARM64). Their ownership is included in the Runtime/Foundation licence/notice
association. This accounts for **105 vendor binary files across both packages**:
103 DLLs and two RestartAgent executables. It does not characterize every DLL as
Native AOT or count `Wintodo.exe` as a vendor binary.

In each package, the only separately packaged licence/notice file is
`Assets/ThirdPartyNotices.txt`, **38,748 bytes**, SHA-256
`4788e0141a69180b3c71acfc7db4466f2b9451fb66ff502f7fd9e6fb47e34065`,
identical to the tracked source. Thus the notice omissions above apply to both
actual package byte sets. Proposed additions have not entered these packages.

Private `candidate-payload-review.json` records the exact package hash, every
root DLL's hash/source matches and actual legal payload entries;
`restart-agent-map.json` records the extra vendor executable. Comparing all
root DLLs establishes the reused native ownership mapping; it does not establish
the full retained-code/SBOM closure of the AOT executable.

To close this part of DEP-17, review the assembled inherited texts, integrate
approved applicable additions, rebuild both packages and verify their actual
notice payload and dependency closure. Final SBOM must distinguish native files,
linked runtime/managed code, build-only tools, trimmed inputs and unresolved
vendor/projection mappings. Signature/deployment, final source attribution,
first-party/image rights and legal-distribution acceptance remain separate gates.

No app behaviour, notice payload, identity, dependency, machine configuration,
remote setting or publication changed. This review used read-only package/archive
and primary-source licence reads, hashes and text comparisons. No app build,
launch, installation or UI automation was performed by this stream.
