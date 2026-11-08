# DEP-18 unsigned candidate handoff checks

Prepared 2026-11-08; audit-binding follow-up 2026-11-09. Status: **IMPLEMENTED;
28 synthetic regressions PASS; historical old/fresh unsigned input-consistency
checks PASS under their then-current contract. Protected signing and
release acceptance remain PENDING.**

[Test-ReleaseCandidate.py](../../scripts/Test-ReleaseCandidate.py) checks one
x64 and one ARM64 unsigned MSIX together with hash-pinned structural, privacy,
toolchain and dependency reports. It prepares a reviewable input set for future
DEP-18 work without selecting identity, version/tag mapping, signing provider,
distribution or repository-protection policy. It never creates a release,
signs, installs or launches an application. Every report has
`release_authorized: false`, including a consistency `PASS`.

## Input record and use

Create a private JSON descriptor containing the full 40-character source commit
and exactly two package records. Paths resolve relative to the descriptor;
absolute paths are also accepted as local inputs and are never copied into the
output. Each file must have an independently reviewed, lowercase SHA-256 pin.
The descriptor itself is hashed in the resulting report. Creating pins from
files identifies their bytes; it does not approve those files or their provenance.

```json
{
  "source_commit": "<full lowercase 40-character commit>",
  "packages": [
    {
      "architecture": "x64",
      "package": {"path": "x64/Wintodo.msix", "sha256": "<64 lowercase hex>"},
      "structural": {"path": "x64/release-package.json", "sha256": "<64 lowercase hex>"},
      "privacy": {"path": "x64/release-privacy.json", "sha256": "<64 lowercase hex>"},
      "toolchain": {"path": "x64/toolchain.json", "sha256": "<64 lowercase hex>"},
      "dependencies": {"path": "x64/dependencies.json", "sha256": "<64 lowercase hex>"}
    },
    {
      "architecture": "ARM64",
      "package": {"path": "arm64/Wintodo.msix", "sha256": "<64 lowercase hex>"},
      "structural": {"path": "arm64/release-package.json", "sha256": "<64 lowercase hex>"},
      "privacy": {"path": "arm64/release-privacy.json", "sha256": "<64 lowercase hex>"},
      "toolchain": {"path": "arm64/toolchain.json", "sha256": "<64 lowercase hex>"},
      "dependencies": {"path": "arm64/dependencies.json", "sha256": "<64 lowercase hex>"}
    }
  ]
}
```

Replace the placeholders with reviewed records before running:

```powershell
python -B scripts/Test-ReleaseCandidate.py artifacts/release-candidate/inputs.json `
  --output artifacts/release-candidate/handoff.json
python -B -m unittest discover -s tests/scripts -p test_release_candidate.py
```

Use the app-only, target-specific `dependencies.json` produced by
`Invoke-CiDependencyAudit.ps1 -AppOnly -Architecture x64` (or `arm64`), with
`AssetsSha256` in the matching new toolchain export. The checker now rejects
legacy unbound summaries; regenerate audit/toolchain evidence for a new candidate
from that candidate's reviewed source and restored graph. A solution-wide report
cannot substitute for the candidate's app audit. Reports from the existing combined CI privacy
scan are supported: layout/metadata records are retained in the input report,
while package records and its first-party binary are selected by the MSIX input
ordinal. A layout executable cannot substitute for the executable in the package.

## Checked consistency

The checker requires exactly one package per supported architecture and a full,
nonzero immutable source identifier. It pins all ten input files, rejects
duplicate JSON keys and bounds each JSON document to 16 MiB. It reruns
[the package inspector](../../scripts/Test-ReleasePackage.py), including its
structural/privacy checks, and rejects signed packages in this unsigned handoff.

Existing structural reports must be passing, have no findings and match the
actual package, manifest, architecture, payload count and PE count. Privacy
reports must be passing with no findings; the package hash, every package-entry
ordinal/size/SHA-256, and the actual executable's ordinal/hash/architecture must
match. The checker rehashes all ZIP contents and all input files before completing.
It compares package identity, publisher and version across architectures without
putting those raw values in the report or treating equality as production approval.

Toolchain records must identify the same full source commit and correct target,
have an explicitly verified AOT graph, contain numeric tool/runtime versions,
and associate the target compiler/runtime and restored WinApp consistently.
Common resolved package versions and SDK/CLI/runtime versions must agree across
architectures. The dependency summary must be a passing NuGet app audit with one
project and zero vulnerable packages/advisories. Its binding must identify the
descriptor's source commit, exact lowercase target, app-only scope and assets
SHA-256 recorded by the toolchain export. The wrapper verifies that HEAD and the
fixed app assets bytes remain unchanged around the no-restore audit. Passing
handoffs record `audit_to_resolved_graph_binding: MATCH; locally_observed_reports_only`
for each target. These checks validate the supplied reports' observed graph
binding and consistency, not the underlying execution's authenticity. See
[the binding follow-up](dep18-audit-graph-binding.md).

Diagnostics contain fixed categories and package/evidence ordinals, never input
paths, raw identity values, exception text or report fields supplied by an input.
Output aliasing the descriptor, an MSIX or any report is rejected, including
hard links. Reports are written atomically; a failed rerun replaces a prior
`PASS` with `FAIL` when the destination can safely be written. If it aliases an
input or cannot be written, it is preserved and the command exits nonzero.
Consumers must honor that exit code rather than reading an older report alone.

## Evidence and remaining gates

[Twenty-eight focused regressions](../../tests/scripts/test_release_candidate.py)
pass with real synthetic MSIX files. They cover valid pairs and combined CI
reports, missing/duplicate/swapped architectures, abbreviated/private source
values, altered package/report bytes, stale structural or privacy reports,
entry-hash/size/ordinal substitution, incomplete/failed gates, source/target/AOT
graph mismatches, divergent dependency versions, failed or solution-wide audits,
signature presence, differing identities/versions, malformed/duplicate-key JSON,
stale output invalidation, alias/hard-link protection, late mutation and failed
atomic output cleanup. Four binding regressions additionally reject missing or
malformed legacy evidence, source/architecture/graph mismatch, incorrect scope
and missing toolchain assets hashes. No build or desktop automation is required.

The private bounded validation descriptor for the existing clean `965d86b`
unsigned pair is under `artifacts/dep18-handoff/old-candidate-inputs.json`;
its result is `old-candidate-report.json`. The source packages/reports are those
in [the clean candidate review](dep16-local-candidate-validation.md). Their
documented source identity and notice payload are a previous baseline; packages
with integrated notice additions require their own fresh descriptor and checks.

The notice-integrated pair from clean source
`9106040bc1eb460c1cd74fa7122d95460aa38a80` passed the then-current local
consistency check on 2026-11-08, including recomputed structural/privacy evidence, every
package-entry hash/size/ordinal and the shared identity/version/tool graph.
Both package hashes match the separate fresh-package validation records:

| Architecture | Exact unsigned MSIX SHA-256 |
|---|---|
| x64 | `4a5d345feb0b4a2e2bac454809436c9bacecf754f67dee5afad683bc2c247d5f` |
| ARM64 | `3c0b688ec46288c5bedcc1c182a3dc0815535c1baf32fca18fa58e3e1be8405c` |

Private evidence is `artifacts/dep18-handoff/fresh-candidate-inputs.json`,
`fresh-candidate-report.json` and `fresh-report-privacy.json`. The handoff report
SHA-256 is `b3b327634c232ac64837379fa6c5e7aa71364174151c8add61dd87f2eb68b73a`;
its pinned descriptor SHA-256 is
`ce475332c701a1fc3c7fb36d23077e79bcc53cdf4ee0dd65c96a9b173285ce43`.
The existing release privacy scanner checks the public handoff report as one
text input with zero findings. The descriptor remains private because it names
local inputs. No candidate, source or input report was modified by these checks.
That historical handoff records ten pending production/provenance gates and
`release_authorized: false`. It has not been regenerated or upgraded in place;
its old unbound audit/toolchain inputs fail the current stricter checker.

New app-only bound dependency summaries record the source commit, architecture
and exact observed assets SHA-256; matching new toolchain evidence closes the
local audit/report graph-consistency gap. A newly passing report retains nine
production/provenance gates. It does not bind audit execution cryptographically
to the candidate or verify clean-source ancestry. Toolchain exports describe
reported availability/configuration and resolved inputs, not which binaries
executed to create the supplied MSIX. Matching a declared commit and assets hash
in those records is not proof that the package was built from them. The output
keeps `source_to_package_execution_provenance` pending, along with native/tool
advisories, complete SBOM/legal review, hosted CI
and protected refs, approved identity/version/payload, production signing/trust,
final signed-byte/deployment gates and explicit publication authorization.

This helper is a local unsigned-input consistency check, not an authenticated
artifact attestation or a replacement for protected GitHub artifacts, reviewed
source ancestry, signing approval or DEP-18's end-to-end negative tests. Those
future stages must validate their own trusted event/source/provenance and compute
new checksums after signing; an unsigned consistency `PASS` cannot authorize them.
