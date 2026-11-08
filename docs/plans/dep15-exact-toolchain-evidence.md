# DEP-02/15 exact toolchain evidence

Reviewed 2026-11-08 against `965d86b` plus this exporter change. Status:
**LOCAL EVIDENCE PASS; hosted execution, tool invocation and advisory dispositions
remain pending.** No SDK/tool upgrade, machine setup, signing or remote setting
changed.

## What the exporter now records

[Export-CiToolchain.ps1](../../scripts/Export-CiToolchain.ps1) retains the existing
SDK, package graph, target-runtime and source fields, then adds binary records
containing numeric file/product versions and SHA-256. Paths, vendor descriptive
strings, copyright fields and account names are omitted.

| Evidence | Scope |
|---|---|
| `Msvc` | Default tool-directory version from the latest Visual Studio installation satisfying the requested target component; retained for compatibility. |
| `MsvcToolsAvailable` | Exact `cl.exe`, `link.exe` and `lib.exe` in that installation's `Hostx64/x64` or `Hostx64/arm64` directory. Availability does not establish which linker a publish invoked. |
| `WindowsSdkAvailable` / `WindowsSdkToolsAvailable` | Latest eligible installed SDK family and its x64 RC, MakeAppx and SignTool binaries. This is machine availability. |
| `WinAppCli` / `WinAppBinary` | Centrally restored CLI package version, verified CLI response, and exact executable version/hash. This does not establish its embedded AOT runtime's patch level. |
| `ProjectToolConfiguration` | MakeAppx, MakePri and RC paths evaluated by MSBuild for the requested architecture, Release and the selected AOT flag, converted to version/hash records. Each records `restored-sdk-buildtools` or `external-override` without exporting the path. |

The MSBuild query evaluates properties without executing targets or restoring.
The configuration scope explicitly says `invocation-not-verified`; a successful
query is not evidence that a build ran those tools. The installed SDK can differ
from the package selected by the project. Missing tools, absent numeric versions,
invalid hashes, missing/relative/wrong tool paths and evaluation errors fail
export. The top-level exporter removes stale successful output before probing
and reports only a fixed stage on failure.

No arbitrary tool executable is invoked for binary inspection: version resources
and file hashes are read. The existing SDK/CLI version probes remain unchanged.

## Local observations and verification

Both requested architectures passed ordinary local exporter runs:

```powershell
./scripts/Export-CiToolchain.ps1 -Architecture x64
./scripts/Export-CiToolchain.ps1 -Architecture arm64
python -B -m unittest discover -s tests/scripts -p test_ci_toolchain.py
```

| Component | Observed exact version |
|---|---|
| MSVC directory | `14.51.36231` |
| Compiler file / product | `19.51.36248.0` / `14.51.36248.0`, both targets |
| Linker and librarian file / product | `14.51.36248.0`, both targets; target-specific hashes differ |
| Installed SDK family / tool file version | `10.0.26100.0` / `10.0.26100.8249` |
| Evaluated restored MakeAppx, MakePri and RC | `10.0.28000.2705`, both target configurations use x64 host tools |
| Restored WinApp file / product | `0.7.1.6` / `0.7.1.0` |

Sanitized local reports with exact SHA-256 values are ignored under
`artifacts/dep-next-ci/toolchain-x64.json` and `toolchain-arm64.json`. These
runs used the existing ordinary app asset graph; they do not replace
`-RequireAot` checks against each fresh candidate's final target graph.

[Seven new regression tests](../../tests/scripts/test_ci_toolchain.py) pass.
They cover serviced versions differing from directory versions; invalid or
unversioned metadata/hashes; exact-file SHA-256; omission of private marker paths
containing spaces/apostrophes; missing/nonbinary/directory inputs; selected SDK
tools and package containment; a similar path prefix identified as an external
override; and missing, empty, relative, wrong-name or absent selected tools.
Tests inspect only the exporter's function definitions and disposable fixtures;
they do not launch the app, require native tool installation or alter a real
tool binary. Existing Python discovery in CI includes these tests automatically.

## Hosted and final-candidate gates

Read-only GitHub checks found remote `main` at
`da9451997553b2e5e69910110819dc8191965ebd`; its latest
[CI run 42429107588](https://github.com/Dvaderfun/wintodo/actions/runs/42429107588)
passed. That older source predates `965d86b` and these exporter changes, so
current hosted acceptance remains pending. No push or workflow dispatch was
performed during this stream.

After integration, run the existing warnings-as-errors/locked-restoration
workflow against the reviewed current source and retain sanitized toolchain
reports before and after each x64/ARM64 package. Match actual linker/packaging
invocations where recorded; an evaluated property alone does not prove final
byte provenance. The [native/tool review](dep02-toolchain-review.md) still owns
HarfBuzz applicability, WinApp embedded-runtime provenance, native vendor
mapping and servicing dispositions. Exact version/hash evidence narrows those
reviews without automatically closing them or the signing/deployment gates.
