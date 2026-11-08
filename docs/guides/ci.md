# CI build, security checks and unsigned AOT candidates

`CI / build-and-test` runs on main pushes, PRs, tags, weekly maintenance and
manual dispatch. It uses the explicit `windows-2025` runner family and reviewed
commit revisions of checkout, setup-dotnet, setup-python and upload-artifact.
Checkouts do not persist credentials. Permissions are read-only; there is no
`pull_request_target`, signing secret, certificate trust or deployment step.
Dependabot proposes weekly Actions and centrally managed NuGet updates; each
update needs review and the usual analyzer/test/AOT compatibility evidence.
Do not enable automatic merging to bypass that review.

The build job runs synthetic scanner/privacy regressions, pinned Gitleaks
8.30.1 against fetched reachable history and the current source archive, a
warnings-as-errors restore with all direct/transitive NuGet advisories enabled,
explicit dependency auditing, toolchain verification, the x64 Debug solution
build and all four Microsoft Testing Platform suites. A failed, incomplete or
malformed scanner report fails the gate. NuGet's clean `--vulnerable` JSON
normally omits framework/package fields for each clean project; that omission
is valid only with a successful command and all nine expected projects present.
An explicit malformed collection or command/report error is rejected.

Gitleaks is downloaded into ignored repo-local `artifacts/ci-security`, with
the reviewed Windows x64 ZIP SHA-256 checked before extraction:
`d29144deff3a68aa93ced33dddf84b7fdc26070add4aa0f4513094c8332afc4e`.
Each invocation uses a fresh source extraction directory. Its history/tree
reports are fully redacted but still contain attribution and filenames, so
they and the private logs are never uploaded. The uploaded summaries contain
only scanner names, versions and result counts. Scanner or download failure
removes stale successful summaries. No ignore/baseline file exempts findings.

`CI / release` produces unsigned x64 and ARM64 AOT MSIX candidates after the
build job passes, on `v*` tag pushes and on scheduled/manual **main** runs.
PR and non-main manual runs do not run this matrix. It verifies target linker,
Windows SDK tools, restored WinApp CLI, patched target AOT/compiler/runtime
packages and the final dependency graph. It scans the actual layout, unsigned
MSIX and public metadata for private paths, including the current checkout
root, before upload. This does not establish production signing or installation
acceptance. Tags still use the placeholder manifest identity/version; DEP-16/18
will define the approved production mapping and protected signing stage.

Uploads use explicit allowlisted filenames and 14-day retention. Raw TRX,
coverage, scanner reports, symbols, binlogs and diagnostic logs stay private.
Public evidence records the source commit, SDK/CLI/native tool versions, runner
image version and resolved package versions, with no machine paths. Available
Windows SDK is recorded separately from the centrally restored SDK build-tools
package; the hosted image is serviced, not an immutable VM image. The reviewed
2026-11-01 `windows-2025` image advertises SDK 10.0.26100.0, .NET 10.0.403,
Visual Studio 2022 17.14 and both x64/ARM64 tools. Each run verifies availability
again; actual run evidence takes precedence over that inventory.

The current SDK policy remains `global.json` minimum 10.0.403,
`latestFeature`, stable releases only. NuGet.org was the only effective source
in the local reviewed audit. There is no custom Actions cache and no signing
authority shared with PR jobs; ordinary hosted-runner NuGet package caches
are scoped to the ephemeral job. Central package versions are pinned, but
transitive lockfiles and locked restore are still pending a reviewed policy;
this batch does not claim byte-identical dependency reproduction. Record
restored versions and rerun advisories for each candidate. For a finding, review
the private report, update the narrow affected dependency/tool and rerun the
full gate. A dependency/scanner exception requires an explicit documented owner
disposition; do not suppress advisory warnings or silently skip scans.

GitHub enforcement remains a release blocker. Read-only checks on 2026-11-08
found the private repository's branch-protection and ruleset APIs both returned
HTTP 403: “Upgrade to GitHub Pro or make this repository public to enable this
feature.” No settings were changed. Required checks and protected release refs
cannot be claimed as enforced until the owner resolves plan/visibility support
and the exact protection settings are verified. Fresh remote CI and native
advisory review remain pending after this local implementation.

Local verification (no app launch or desktop automation):

```powershell
python -m unittest discover -s tests/scripts -p 'test_*.py'
.\scripts\Invoke-CiSecretScan.ps1
.\scripts\Invoke-CiDependencyAudit.ps1
.\scripts\Export-CiToolchain.ps1 -Architecture x64
# After the reviewed target's AOT restore/publish:
.\scripts\Export-CiToolchain.ps1 -Architecture arm64 -RequireAot
```
