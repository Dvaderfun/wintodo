# DEP-02 native dependency and release toolchain review

Reviewed 2026-11-08 against source baseline `7d2fa21`. Status: **REVIEWED WITH
LIMITS; native-tool advisory disposition and actual hosted-runner acceptance
remain pending.** No dependency, machine installation or accepted decision changed.

The existing patched .NET and clean NuGet results remain valid evidence for
their recorded source. This review identifies the native payload and examines
primary vendor advisories that an application-level NuGet scan does not cover.
It does not certify a final signed package or claim all tools are advisory-free.

## Evidence and native payload

The fifth-batch AOT evidence records SDK `10.0.403`, target runtime and both
ILCompiler packages `10.0.14`, Windows App SDK `2.5.1`, and matching x64/ARM64
resolved package graphs. Its source field is `812dd73` plus the recorded reviewed
overlays; that is not a clean final release build. See
[the integrated deployment evidence](deployment-readiness.md#6-verification-commands-and-evidence-register).

This review SHA-256 matched every root-level payload DLL to a file in the
restored package cache or an entry inside its runtime MSIX: **52/52 x64 and
51/51 ARM64**, with zero unmatched files. This includes native DLLs and the
root-level managed interop file; DLL count alone is not a Native AOT assertion.
Private evidence records exact filenames, package identities, relative entries
and hashes in `artifacts/dep02-review/native-payload-map.json`.

| Component | Observed version/source | Delivery and review outcome |
|---|---|---|
| .NET runtime / ILCompiler | `10.0.14`, SDK `10.0.403` | Runtime is linked into the AOT executable. Known Brotli advisory patch matches; reuse recorded target-specific publish/audit evidence. |
| SQLite native | `SQLite/3.53.4`, `e_sqlite3.dll` | Both DLLs match the restored package; x64 `sqlite3_libversion()` and `sqlite3_sourceid()` match upstream `3.53.4` exactly. |
| SQLite managed wrappers | `SQLitePCLRaw` `3.0.5`; `Microsoft.Data.Sqlite`/Core `10.0.12` | Linked first-party dependency graph; existing direct/transitive NuGet audit passed. Native engine version must be considered separately. |
| Windows App SDK | Runtime `2.5.1` and its modular packages | Native files match package/runtime-MSIX content. Empty repository-advisory results and a clean NuGet graph are bounded evidence, not complete Microsoft component coverage. |
| ONNX Runtime / DirectML | `Microsoft.Windows.AI.MachineLearning/2.1.74`; native `onnxruntime.dll` reports `1.24.20260504.1.7444c45`; `DirectML.dll` reports `1.15.4` | Shipped through the SDK's closure despite no application ML feature. Exact native fork/component-to-advisory mapping remains pending; do not infer it from the Windows App SDK metapackage version. |
| WebView2 loader/interop | `Microsoft.Web.WebView2/1.0.3719.77` | Payload hashes match package files. This is SDK/loader content, not proof of the version or patch state of an installed Edge WebView2 browser runtime. |
| WinApp CLI | NuGet `0.7.1`, executable product `0.7.1`, file `0.7.1.6` | Build-only. NuGet repository commit matches the upstream `v0.7.1` release. Its self-contained runtime and bundled native dependencies need separate review. |
| WinApp native Skia/HarfBuzz | Upstream source pins `SkiaSharp/4.152.0`; binary exports report Skia `152.0` and HarfBuzz `14.2.0` | Build-only; absent from the inspected application payload. HarfBuzz has a relevant advisory range below. |
| MSVC compiler/linker/librarian | Directory `14.51.36231`; actual tool product versions `14.51.36248.0` for x64 and ARM64 | Build-only. Existing exporter records directory version, which must not be confused with serviced binary file version. |
| Windows SDK / MSIX tools | Installed SDK family `10.0.26100.0`; NuGet BuildTools `10.0.28000.2705`; MSIX package `1.7.251221100` | Build-only. An SDK family directory is not its servicing version, and the installed SDK is separate from centrally restored build tools. Exact binaries used by the final candidate remain to be recorded. |

## Advisory findings and dispositions

### Known .NET advisory patch still matches

[GHSA-p3jp-9v83-76w9](https://github.com/advisories/GHSA-p3jp-9v83-76w9)
was fetched again from GitHub's advisory API. The .NET 10
`Microsoft.DotNet.ILCompiler` affected range is `>= 10.0.0, < 10.0.14` and the
first patched version is `10.0.14`. The reviewed runtime/compiler graph matches
that patch. This is a disposition of that advisory, not a permanent minimum
covering future advisories. Recheck candidate dependency/runtime evidence.

### SQLite includes the vendor-listed FTS5 fix

[SQLite's CVE table](https://www.sqlite.org/cves.html) lists
`CVE-2026-11822`/`CVE-2026-11824` as fixed in `3.53.2`. The issue involves
arbitrary SQL, FTS5 and disabled defensive configuration. The native engine
actually shipped here is `3.53.4`, whose
[upstream change record](https://www.sqlite.org/changes.html) matches this x64
runtime query exactly:

```text
3.53.4
2026-07-24 19:02:57 bf7c7f30031888f4e796e429ab3978879485813aaca6f641c7b33e4e09459bcc
```

The newer `3.54.x` release is not, by itself, evidence that this native engine
needs an unrelated upgrade. No unpatched issue was identified in the reviewed
vendor CVE table; that table is not an exhaustive claim about unknown defects.

### WinApp's HarfBuzz native code is in an advisory range

The upstream
[GHSA-r62p-jr5f-3mjm](https://github.com/harfbuzz/harfbuzz/security/advisories/GHSA-r62p-jr5f-3mjm)
lists affected releases `>= 5.3.0, <= 14.6.0` and no patched release. It concerns
crafted OpenType feature-substitution parameters during font
subsetting/instancing, potentially disclosing adjacent memory in generated
fonts. The description says ordinary text shaping has not been demonstrated
as an exploitation path and points to proposed patches rather than a released
fixed version.

The restored WinApp DLL reports `14.2.0` and exports
`hb_subset_or_fail`, `hb_subset_input_create_or_fail` and
`hb_subset_plan_create_or_fail`. Therefore its native version and available code
fall within the stated range. **Invocation of the affected operation by the
Wintodo packaging path is unproven.** This finding does not establish a shipped
Wintodo runtime vulnerability: the inspected application DLL closure contains
neither WinApp's HarfBuzz nor its Skia library.

A bounded review of WinApp's pinned source follows this repository's explicit
WinUI `.csproj` packaging into `PackageCommand.ProjectMode.cs`'s native-SDK
branch, which calls `projectRunService.PublishNativeMsixAsync` and delivers the
resulting package. The reviewed command/project-mode files have no font,
HarfBuzz, subsetter or image-asset-service calls. Its separate
`ImageAssetService.cs` rasterizes SVG with Skia and contains no direct font
subsetting call. This narrows the exposure in the current package invocation;
it is not a proof of all transitive SVG operations or every WinApp command.
The source is pinned to
[the `v0.7.1` commit](https://github.com/microsoft/WinAppCli/tree/e3cd0b94f3e7b776ac51ba0564f4908094e979b5),
and private evidence retains the three inspected files and response hashes.

Leave DEP-02's tool advisory disposition open. The follow-up below traces
WinApp/Svg.Skia's actual operations and proposes a bounded disposition.
If applicable, obtain a vendor-patched compatible WinApp/native build and rerun
packaging/AOT/privacy acceptance. No stable patched release was identified, so
an arbitrary WinApp or HarfBuzz version bump is not an established fix. Any
accepted exception needs the deployment plan's explicit documented owner
disposition; a clean application NuGet audit does not close this finding.

Other fetched HarfBuzz advisories list earlier fixed/affected releases below
`14.2.0`, or unversioned/fuzzer-specific paths. A SkiaSharp `4.152.0` GitHub
package query returned no advisories. Upstream WinApp's central package comment
states its selected Skia version carries the libexpat fix for
`CVE-2025-59375`; the bundled expat build was not independently inspected.

### Follow-up: affected HarfBuzz operations and a newer WinApp candidate

The 2026-11-08 follow-up uses Wintodo baseline `4fa223d` and the same pinned
WinApp `v0.7.1` source. It extends the earlier review through startup service
registration, package preparation, native publishing and final delivery:

| Step | Verified operation in pinned WinApp source |
|---|---|
| Command construction | `HostBuilderExtensions.UseCommandHandler` resolves the selected handler inside its command action. Registering image/search services does not invoke their handlers. |
| Input routing | A single explicit `.csproj` enters `RunProjectModeAsync` before folder packaging. `PreparePackageAsync` evaluates project properties and selects active native MSIX tooling. |
| Native publish | `PublishNativeMsixAsync` delegates argument tokens to `dotnet publish`; the Windows App SDK produces the MSIX. The reviewed preparation and native-publish code does not invoke SVG rendering or a font subsetter. |
| Unsigned delivery | `DeliverNativeMsixAsync` reads the ZIP manifest, copies the SDK-produced MSIX to a sibling staging file and moves it to the destination. With `--no-sign`, it skips signing. It neither renders images nor produces a font. |

The current CI invocation selects this explicit project/native-SDK path and
requests unsigned single-architecture packages. This is source evidence for
that invocation; actual hosted execution and final-package inspection remain
separate gates.

The downloaded `Svg.Skia/5.2.3` nuspec pins source commit
[`7910666415a96a09643d1729eb5da5d115d75748`](https://github.com/wieslawsoltes/Svg.Skia/tree/7910666415a96a09643d1729eb5da5d115d75748)
and explicitly depends on `HarfBuzzSharp/14.2.0`. Its
`SkiaModel.TextShaping.cs` calls `Font.Shape` and `Font.SetVariations` while
calculating glyphs. The matching `HarfBuzzSharp/14.2.0` nuspec pins binding
commit [`4e4ce7af7ea8702593af5aeb25d05c65ffb74e90`](https://github.com/mono/SkiaSharp/tree/4e4ce7af7ea8702593af5aeb25d05c65ffb74e90);
`Font.cs` maps those methods to `hb_shape_full` and
`hb_font_set_variations`. No subsetter call was found in the reviewed shaping,
document-font loading or font-provider files. Selecting variation coordinates
for shaping does not itself establish the advisory's generated-font instancing
path. WinApp's separate image service renders its SVG picture to PNG.

**Recommended disposition for owner review:** record this HarfBuzz advisory as
not applicable to the reviewed unsigned native-SDK packaging invocation,
because that path does not invoke the affected operation or emit a generated
font. Keep the affected build-only DLL/version visible in tool inventory and
revisit the assessment after a CLI/asset-generation/command change. This
recommendation is not an accepted exception, a whole-CLI safety claim, or a
proof about every transitive Skia operation. The refreshed advisory still lists
`>= 5.3.0, <= 14.6.0`, with no selected patched version.

A new stable [WinApp `v0.8.0` release](https://github.com/microsoft/WinAppCli/releases/tag/v0.8.0)
was published on 2026-11-07. Its NuGet repository metadata matches release
commit `e5cedcc04921f6f74535cf45668292203e439d45`. The release includes fixes
for private-key container leakage during signing and pins
`System.Security.Cryptography.Pkcs/10.0.13` for
[GHSA-846f-f764-76mx](https://github.com/advisories/GHSA-846f-f764-76mx) and
[GHSA-2fmv-rv2c-59x7](https://github.com/advisories/GHSA-2fmv-rv2c-59x7).
Both advisory records list affected .NET 10 package releases through
`10.0.12` and first patched version `10.0.13`. This identifies a concrete
supported upgrade candidate for review; it does not identify the PKCS version
retained in the old AOT executable or demonstrate exploitability in Wintodo's
unsigned command. The newer source keeps `Svg.Skia/5.2.3` and
`SkiaSharp/4.152.0`, so its version number alone is not a HarfBuzz remediation.
No package reference, lockfile, vendored skill or installed CLI changed.

### Visual Studio servicing is distinct from compiler vulnerability

Local `vswhere` catalogue fields identify **Visual Studio `18.8.0`**, build
`18.8.12009.203`. Microsoft's
[2026 release history](https://learn.microsoft.com/en-us/visualstudio/releases/2026/release-history)
maps that to July 14, 2026; the reviewed current fixed stable release is
`18.11.3`, October 29. Actual compiler/linker/librarian product versions are
`14.51.36248.0`, newer than their directory name. No observed advisory has been
mapped to an exploitable defect in those specific compiler/linker binaries.

[MSRC CVE-2026-118147](https://msrc.microsoft.com/update-guide/vulnerability/CVE-2026-118147)
describes improper Visual Studio certificate validation and lists serviced
Visual Studio `18.11`/`17.14` product branches; fixes are `18.11.3` and
`17.14.44`. The fetched affected-product list does **not** provide an explicit
`18.8` affected-version range or identify this native compiler as its affected
component. Thus the local installation has a **servicing/coverage gap**, not a
proven compiler vulnerability. The known Brotli advisory similarly must not be
reclassified as an MSVC linker defect.

The
[hosted `windows-2025` inventory](https://github.com/actions/runner-images/blob/main/images/windows/Windows2025-Readme.md)
fetched for image `20261101.318.1` advertises Visual Studio build
`17.14.37827.9`. Microsoft's
[2022 release history](https://learn.microsoft.com/en-us/visualstudio/releases/2022/release-history)
maps it to `17.14.44`, which includes the reviewed certificate-validation fix.
The inventory also advertises SDK `10.0.26100.0`, SDK installation servicing
`10.1.26100.7705`, .NET SDK `10.0.403`, and both target tool components.
Component catalogue numbers are not actual linker binary versions. A fresh
remote CI run must still demonstrate its selected tools, exact versions and
successful x64/ARM64 packaging. Hosted-image inventory is not execution proof.

### Coverage that remains incomplete

- Windows App SDK, WinApp, SQLitePCL.raw, ONNX Runtime and DirectML repository
  advisory endpoints were successfully fetched and returned empty lists. These
  projects may publish through MSRC or other vendor channels; empty lists cannot
  establish all native components are safe.
- The October and available November MSRC CVRF documents were reviewed for
  native/tool product matches. ONNX advisories in that document identify Azure
  Linux ONNX products; that alone does not establish applicability to the
  SDK's Windows ONNX Runtime binary. GitHub queries for upstream ONNX Runtime
  `1.24.0` in NuGet and pip returned no advisories, but the binary's
  `1.24.20260504.1.7444c45` vendor build cannot be equated to those distributions
  without source/BOM evidence. Preserve this mapping gap for candidate review.
- WinApp `0.7.1` is Native AOT/self-contained. Its pinned source's official build
  template requests `10.0.x`, and its SDK configuration does not fix the release
  compiler/runtime. Its October 1 release date precedes the November 3 .NET
  advisory publication. Neither date nor managed package versions establish
  the compiler/runtime embedded in the shipped CLI. Obtain vendor release
  provenance or inspect a supported vendor record before claiming its embedded
  runtime is patched. Wintodo's selected SDK does not rebuild this downloaded CLI.
- The follow-up inspected the unchanged restored x64 WinApp executable
  (SHA-256 `bf29f665ab4a030f20cd34a9121276301871ee89fd9c13118c7221563858d692`).
  Its retained metadata includes a `System.Text.Json` informational version
  `10.0.12+95017c711e6afc1085133d440e42b4bd78155701` and other assembly versions
  `10.0.5`/`10.0.3`. These assembly strings do not establish the linked AOT
  compiler/runtime or Brotli patch level. The cached NuGet archive includes no
  SBOM, `deps.json` or `runtimeconfig.json`; the queried GitHub release lists no
  SBOM asset, and the queried artifact-attestation endpoint returned HTTP 404.
  No usable release-runtime provenance was obtained from those routes. The
  pinned source does use `BrotliStream` for lazy loading of embedded `find-ui`
  snapshots. This is a separate command/trusted embedded-resource path, absent
  from the reviewed package path; it is not evidence that WinApp never uses
  Brotli. The `v0.8.0` build template still requests `10.0.x`, leaving its exact
  embedded compiler/runtime to be verified before an upgrade is credited with
  resolving that gap.
- Installed Windows SDK family names and NuGet tool package versions need a
  final actual-tool-to-advisory mapping. No exact component vulnerability was
  established in this bounded review; do not mark the whole SDK advisory-free.

## Licence and notices handoff to DEP-17

The restored package metadata records SQLitePCLRaw `Apache-2.0`, ILCompiler
and WinApp `MIT`, SQLite's supplied public-domain licence, and file-based
Microsoft licences for Windows App SDK/runtime/modular ML/AI/WinUI packages
and WebView2. Windows SDK BuildTools supplies its SDK licence URL; MSIX supplies
`sdk_license.txt`.

The packaged `Assets/ThirdPartyNotices.txt` currently describes centrally
declared packages and includes build/test tools. It is not a complete notice
inventory for the actual transitive/native closure. DEP-17 must compare the
matched payload to runtime package `NOTICE.txt`, ML
`ThirdPartyNotices.txt`, SDK licences and WebView2 terms, preserving component
attribution/redistribution requirements. DLL hash matching identifies origin;
it does not decide redistribution rights or the owner's first-party licence.

## Verification and remaining acceptance

This bounded work used version/file queries, SHA-256 package/MSIX comparison,
read-only native version exports and primary HTTP reads. It did not build or
launch the app, invoke font subsetting, run a UI gate, touch a user database,
provision a VM, install/update a tool or write remote settings. New product
tests are unnecessary because product behavior did not change.

Ignored private evidence is under `artifacts/dep02-review/`: upstream response
URLs/statuses/hashes, restored package metadata/hashes, native payload matches,
local actual-tool versions, SQLite source identity, CLI native versions,
HarfBuzz subsetter-export presence and structured MSRC advisory extracts.
Upstream raw responses and local package paths should not be copied wholesale
to public release evidence.

Follow-up private evidence is under `artifacts/dep-next-toolchain/`: pinned
source files and SHA-256 records, downloaded nuspec/package metadata, refreshed
advisory/version responses and bounded binary metadata. Read-only HTTP and
archive/string inspection added this evidence; no CLI was launched, installed
or updated. Product behavior is unchanged, so no app build/UI gate was run.

To close DEP-02, resolve the HarfBuzz/WinApp embedded-runtime and native vendor
mapping dispositions, then produce exact reviewed-tool evidence in a fresh
hosted-runner x64/ARM64 candidate run. Preserve local machine setup as a separate
owner-invoked action. Final signed-package inspection remains DEP-16/18/19.

The subsequent [exact-tool evidence improvement](dep15-exact-toolchain-evidence.md)
adds numeric compiler/linker/librarian, restored CLI and SDK-tool versions/hashes
to the CI exporter. It distinguishes available installed tools from MSBuild's
evaluated Release packaging tools; actual invocation remains a separate proof.
These local observations narrow the provenance gap without resolving the
advisory dispositions or replacing fresh hosted candidate evidence.
