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
root, before upload. A separate structural gate checks those exact MSIX bytes
against the source manifest identity/version and requested native architecture,
runtime/assets, capabilities and activation declarations. Its sanitized
`release-package.json` report accompanies the candidate. The independently
approved complete payload inventory remains pending; source-manifest consistency
does not approve the placeholder production identity. These checks do not
establish production signing or installation acceptance. The placeholder
identity and first production version still require owner values; tags do not
rewrite the manifest. DEP-18's protected signing stage remains pending.

Tag pushes must pass [the release-ref checker](../plans/dep18-release-ref-policy.md)
before the target restore. It verifies canonical `vMAJOR.MINOR.PATCH` syntax,
the matching committed `MAJOR.MINOR.PATCH.0` manifest version, exact source/tag/
HEAD bindings and ancestry under the explicitly pinned reviewed main commit.
The release checkout fetches full history; the manifest's LF attribute preserves
its exact committed bytes on Windows. Event/ref values are passed as arguments
through environment variables, without interpolating untrusted text into shell
code. Synthetic real-Git regressions run in the existing script-test discovery.

`RELEASE_REVIEWED_MAIN_SHA` is a required repository variable containing the
full lowercase reviewed `refs/remotes/origin/main` commit. A missing, malformed
or stale pin rejects tag candidates. Optional `RELEASE_PREVIOUS_MSIX_VERSION`
enables a strictly increasing four-part version check; omitting it leaves prior-
version acceptance pending. Neither variable is configured by this local change.
These caller-supplied pins do not authenticate owner approval or replace actual
remote protection. Both policy and privacy reports join the explicit artifact
allowlist on tag runs, with `release_authorized: false`. Scheduled/manual main
builds remain unsigned maintenance checks and omit the tag-only reports.

Each unsigned matrix artifact also includes `release-sbom.json` and its
separate `release-sbom-privacy.json` check. The CycloneDX exporter binds actual
MSIX and original NuGet archive hashes to the supplied source and target graph;
unverified restored-code inputs remain separate evidence. Complete dependency
closure and independent source/execution provenance remain pending. Export or
metadata-privacy failures stop artifact upload; a qualified export does not
replace final legal/SBOM acceptance. See [the SBOM evidence record](../plans/dep16-sbom-evidence.md).
The [unsigned handoff checker](../plans/dep18-candidate-handoff.md) can compare
downloaded hash-pinned matrix inputs locally, including the normal combined
layout/MSIX privacy reports, while retaining signing/production authorization
and provenance gates as pending.

Candidate app audits now pass `-AppOnly -Architecture x64` (or `arm64`). The
audit observes the actual app assets bytes and checked-out HEAD before and
after the no-restore NuGet command and before publishing its safe summary. A
changed source/graph or mismatched target fails without retaining a public
PASS. `dependencies.json` records the full source commit, target and exact
assets SHA-256, and the final toolchain export hashes the exact assets bytes
it parses, rejecting changes during export. The unsigned handoff requires
those bindings to agree; legacy unbound reports must be regenerated. Generic
solution audits remain supported outside candidate handoff. These checks bind
the supplied reports to the same observed graph; they do not authenticate
execution or prove that those inputs produced the package bytes. See
[the audit binding record](../plans/dep18-audit-graph-binding.md).

Packaging uses `--no-restore` after the explicit target-specific locked AOT
restore and publish. WinApp 0.7.1's default containing-solution restore would
also request unsupported AOT test-project locks (`WTDLOCK001`). Skipping that
extra restore preserves the reviewed app graph and the native packaging step.

Toolchain reports retain SDK-family/MSVC-directory fields and add exact numeric
file/product versions and SHA-256 for available compiler/linker/librarian,
installed SDK tools and the restored WinApp executable. MSBuild-evaluated
Release MakeAppx/MakePri/RC tools are recorded separately, including whether
they come from the restored BuildTools package or an external override. These
records describe availability/configuration; actual invocation and candidate
byte provenance require separate evidence. Missing/invalid tools or query
failures reject export without printing machine paths. See
[the exact-tool evidence review](../plans/dep15-exact-toolchain-evidence.md).
Seven synthetic tool-metadata/selection/privacy regressions run through the
existing Python test discovery.

Uploads use explicit allowlisted filenames and 14-day retention. Raw TRX,
coverage, scanner reports, symbols, binlogs and diagnostic logs stay private.
Public evidence records the source commit, SDK/CLI/native tool versions, runner
image version and resolved package versions, with no machine paths. Available
Windows SDK is recorded separately from the centrally restored SDK build-tools
package; the hosted image is serviced, not an immutable VM image. The reviewed
2026-11-01 `windows-2025` image advertises SDK 10.0.26100.0, .NET 10.0.403,
Visual Studio 2022 17.14 and both x64/ARM64 tools. Each run verifies availability
again; actual run evidence takes precedence over that inventory.

The SDK policy remains `global.json` minimum 10.0.403, `latestFeature`, stable
releases only. `NuGet.Config` clears inherited package sources and uses only
the HTTPS NuGet.org v3 feed. There is no custom Actions cache and no signing
authority shared with PR jobs; ordinary hosted-runner NuGet package caches are
scoped to the ephemeral job. Package-source changes require review with the
dependency graph; do not put credentials in this config.

NuGet restores use checked-in lockfiles and default to `RestoreLockedMode=true`
through `Directory.Build.props`. CI explicitly uses `--locked-mode` and passes
locked mode through WinApp packaging; the ordinary publish helper also inherits
the default. Missing locks, requested dependency drift and package content-hash
mismatches must fail rather than silently resolve a new graph.
NuGet itself creates missing locks even with locked mode, so a per-project
pre-restore check in `Directory.Build.targets` rejects an absent reviewed lock
(`WTDLOCK001`) and a force-evaluate request while locked (`WTDLOCK002`). The
check includes referenced projects and permits explicit update mode.
The same file selects the lock after project RID/AOT properties are
known: `packages.lock.json` for an ordinary library/test restore,
`packages.<rid>.lock.json` for a non-AOT app, and
`packages.aot.<rid>.lock.json` for the AOT app and its libraries. The four
libraries each have ordinary/x64-AOT/ARM64-AOT locks, the four test projects
have ordinary locks, and the app has two ordinary and two AOT locks: 20 files.
NuGet's `dotnet restore -r` supplies plural `RuntimeIdentifiers` to referenced
libraries, so selection handles that property as well as `RuntimeIdentifier`.
This keeps x64 and ARM64 jobs from rewriting the same transitive graph.
The supported proofs cover Debug/Release solution restores on both platforms
and Release AOT app restores for both RIDs, including the property form used by
implicit publishing. Other restore combinations need their own reviewed lock.

For an intentional SDK/package update, regenerate the supported graphs in an
isolated checkout with `-p:RestoreLockedMode=false --force-evaluate`, review
the requested/resolved versions and hashes, and commit the changed locks with
the dependency change. **Do not pass `--force-evaluate` to verification:** NuGet
normally uses it to override locked mode and update a lock; the repository's
guard requires explicit `RestoreLockedMode=false` for that request. For example:

```powershell
# Repeat the solution command for ARM64 and each target command for win-arm64.
dotnet restore Wintodo.sln -p:Platform=x64 -p:RestoreLockedMode=false --force-evaluate
dotnet restore src/Wintodo.App/Wintodo.csproj -r win-x64 `
  -p:Platform=x64 -p:Configuration=Release -p:PublishAot=true -p:SelfContained=true `
  -p:RestoreLockedMode=false --force-evaluate
# Review/commit locks, then verify with --locked-mode and no update override.
dotnet restore Wintodo.sln -p:Platform=x64 --locked-mode -warnaserror
```

Lockfiles cover the NuGet dependency graph and content hashes; SDK implicit
download dependencies/runtime packs can be outside that graph. SDK roll-forward
and the serviced runner image remain deliberate sources of change, so lockfiles
do not establish identical native binaries or complete toolchain reproducibility.
Record SDK, target compiler/runtime/download versions and rerun advisories for
each candidate. A rolled-forward SDK that changes locked analyzer/compiler
dependencies requires the same reviewed update. For a finding, review
the private report, update the narrow affected dependency/tool and rerun the
full gate. A dependency/scanner exception requires an explicit documented owner
disposition; do not suppress advisory warnings or silently skip scans.

GitHub enforcement remains a release blocker. Read-only checks on 2026-11-08
found the private repository's branch-protection and ruleset APIs both returned
HTTP 403: “Upgrade to GitHub Pro or make this repository public to enable this
feature.” No settings were changed. Required checks and protected release refs
cannot be claimed as enforced until the owner resolves plan/visibility support
and the exact protection settings are verified. Fresh remote CI and native
advisory dispositions remain pending; see the
[native/tool review](../plans/dep02-toolchain-review.md) for current evidence.

Local verification (no app launch or desktop automation):

DEP-15 locked-restore checks pass on SDK 10.0.403: eight supported restore
shapes, unchanged hashes for all 20 lockfiles, requested-version/content-hash
drift rejection, missing direct/reference lock rejection, force-evaluate rejection
while locked, explicit update-mode regeneration, unreviewed-RID rejection and
valid restore after injected failures. Local redacted evidence is
`artifacts/dep15-locks/evidence.json`; raw logs remain ignored/private. This proof
does not close the pending fresh hosted CI or final native-tool/package gates.

```powershell
dotnet restore Wintodo.sln -p:Platform=x64 --locked-mode -warnaserror
python -m unittest discover -s tests/scripts -p 'test_*.py'
.\scripts\Invoke-CiSecretScan.ps1
.\scripts\Invoke-CiDependencyAudit.ps1
.\scripts\Export-CiToolchain.ps1 -Architecture x64
# After the reviewed target's AOT restore/publish:
.\scripts\Export-CiToolchain.ps1 -Architecture arm64 -RequireAot
.\scripts\Invoke-CiDependencyAudit.ps1 -AppOnly -Architecture arm64
```
