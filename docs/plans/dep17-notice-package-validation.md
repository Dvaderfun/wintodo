# DEP-17 notice-containing unsigned package validation

Validated 2026-11-08 for clean source
`9106040bc1eb460c1cd74fa7122d95460aa38a80`.
Status: **x64 AND ARM64 LOCAL UNSIGNED NOTICE/PACKAGE CHECKS PASS;
production release acceptance remains pending.**

[The notice integration](dep17-notice-integration.md) preserves the nine
inherited supplier texts identified by
[the exact shipped-notice review](dep17-third-party-notices-review.md).
This pass builds new packages containing those texts; the older `965d86b`
packages still contain the original notices and are separate evidence.

## Clean source and build boundary

Both targets use one fresh `git archive` of the exact commit above, extracted
under ignored `artifacts/dep-notice-candidates/source`. The archive SHA-256 is
`d30124c7c0cf2763a22e634be5a911d657a20a7fb749f9da58874f0f91c9482f`.
All **500 archived files**, including **20 reviewed lockfiles**, remain
byte-identical after both targets' restores, AOT publishes and packaging.
Generated files are outside that source-content preservation comparison.
The archived `-text -whitespace` notice attribute preserves inherited supplier
bytes, mixed line endings and trailing spacing; compiler/analyzer warnings
remain errors.

Each target runs its matching locked Release/AOT/self-contained restore,
`Publish-Aot.ps1 -Platforms x64` or `ARM64`, then SDK-native WinApp `0.7.1`
packaging with the explicit app project, architecture and AOT properties.
Packaging retains `--no-sign --self-contained --no-restore` and
`TreatWarningsAsErrors=true`, following
[the validated command sequence](dep16-local-candidate-validation.md).
Both restore/publish/package sequences pass without unresolved compiler,
WinUI, trim/AOT or single-file warnings. No tool, dependency, manifest identity,
publisher, version or app behaviour changed during validation.

The original placeholder identity and version `1.0.0.5` remain intact.
Structural inspection passes the expected identity/publisher/version parsed
from the archived source manifest to the existing `inspect_package` function;
the current CLI has no `--source-manifest` flag. This checks source consistency,
not production identity or complete payload approval.

## Exact package and notice evidence

| Check | x64 | ARM64 |
|---|---|---|
| Target-specific locked restore | PASS | PASS |
| Self-contained AOT publish, warnings as errors | PASS | PASS |
| SDK-native unsigned packaging | PASS | PASS |
| Structural/native/assets/capability/activation check | PASS; 54 inspected PE files | PASS; 53 inspected PE files |
| Archived source identity/publisher/version consistency | PASS | PASS |
| Actual notice payload equals archived source | PASS, 898,198 bytes | PASS, 898,198 bytes |
| Common AOT-layout/MSIX bytes, including executable/notices | PASS, 311/311 files | PASS, 307/307 files |
| Whole MSIX plus retained size-summary privacy scan | PASS, 322 files; zero findings | PASS, 318 files; zero findings |
| App-only direct/transitive NuGet vulnerability audit | PASS; zero vulnerable packages/advisories | PASS; zero vulnerable packages/advisories |
| Target AOT graph and available-tool evidence | PASS with scope limits below | PASS with scope limits below |

The x64 MSIX has **321 files**, is **58,709,705 bytes**, and has SHA-256
`4a5d345feb0b4a2e2bac454809436c9bacecf754f67dee5afad683bc2c247d5f`.
Its app executable SHA-256 is
`1a89321c4eb4caff1168a6e29269dd1a28c42125b4708a0a61977abf774e50db`.
The previously reviewed ARM64EC vendor resource is the sole other-architecture
PE; the required app, WinUI/Windows App Runtime and SQLite binaries satisfy
the x64 native contract.

The ARM64 MSIX has **317 files**, is **57,026,556 bytes**, and has SHA-256
`3c0b688ec46288c5bedcc1c182a3dc0815535c1baf32fca18fa58e3e1be8405c`.
Its app executable SHA-256 is
`cdff178613a3f7c7942e83bca9020b2414b0bdf41f84ac00a1137c3049938826`.
Its other-architecture vendor counter is zero, and the required binaries
satisfy the ARM64 native contract. Neither package contains signing metadata.

Both packages contain `Assets/ThirdPartyNotices.txt` at **898,198 bytes**, SHA-256
`ba042235866020798b50fcc24483abaea5a61c827340ca8db943a6819cac98fa`.
The extracted logical entry bytes equal the archived source exactly, including
the original notice prefix and all nine whole supplier legal bodies.
Whole-package privacy scanning includes those actual bytes; a source-only
notice check is not substituted for package evidence.

## Evidence locations and toolchain limits

Private evidence is under `artifacts/dep-notice-candidates/`:

- `source-9106040.zip`, `source.json` and `results.json` identify the archived
  source, exact package hashes, notice inclusion and final source preservation.
- `source/publish/msix/Wintodo-x64.msix` and `Wintodo-arm64.msix` are the exact
  unsigned package files above. `summary-x64.json` and `summary-arm64.json`
  preserve the corresponding relative-path AOT size summaries.
- `x64/` and `ARM64/` each retain `structural.json`, `privacy.json`,
  `dependencies.json`, `toolchain.json` and the matching private
  `project.assets.json`. Each target's assets graph was copied immediately
  after packaging/export, before changing the shared scratch restore graph.
- `notice-win-x64.json` and `notice-win-arm64.json` record package notice and
  layout-equivalence results. Original native/tool logs remain ignored/private;
  the first x64 publish's `Out-Host` diagnostics were visible in its execution
  session, while subsequent child-process invocation captured stdout.

The archived exporter records SDK `10.0.403`, WinApp `0.7.1`, target runtime and
AOT compiler `10.0.14`, local Visual Studio `18.8.12009.203`, MSVC directory
`14.51.36231` and Windows SDK family `10.0.26100.0`, plus exact available-tool
binary versions/hashes and evaluated SDK-tool selection. The exporter obtains
`SourceCommit` from Git's containing repository, not an archive parameter.
The ancestor repository remained at `9106040` until both reports were captured;
their commit fields were checked against the archive record. The independent
500-file source comparison establishes archive content preservation. This is
local source attribution and tool availability/configuration evidence, not
source-to-binary execution attestation or hosted-runner acceptance.

This closes the bounded source-notice-to-actual-package inclusion check. It
does not close complete SBOM/retained-code/vendor ownership, native/tool
advisory disposition, first-party/image rights, public attribution/contact,
vendor end-user/distribution conditions, approved identity/version/payload,
fresh hosted CI/protection, production signing, signed-byte inspection,
clean-VM install/update/uninstall, ARM64 runtime acceptance or publication.
No machine setup, certificate creation/trust, installation, app launch, host UI,
remote settings, signing or publication was performed. Final release work
remains ordered in [the deployment plan](deployment-readiness.md).
