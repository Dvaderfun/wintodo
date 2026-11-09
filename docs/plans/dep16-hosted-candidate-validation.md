# DEP-15/16/18 — Hosted unsigned candidate validation

Recorded 2026-11-09. **Hosted maintenance build/tests and both unsigned AOT
package jobs PASS; downloaded handoff and qualified SBOM schema checks PASS.**
Production signing, identity/version approval and final deployment remain pending.

## Exact source and run

[CI run 42537269138](https://github.com/Dvaderfun/wintodo/actions/runs/42537269138)
is a manual main maintenance run against immutable source
`c5610bbb8f44c23c6fb6ceb6865389aca021b134`. GitHub reports success for
`build-and-test`, `release (x64, x64)` and `release (ARM64, arm64)`.

The uploaded complete unit summary contains Core 257, Data 133, Import 155 and
Sync 162: **707 passed, zero failed/skipped/unresolved**. The script stage passes
**204 tests**. Locked restore, all-project dependency auditing, source/history
scans and the warnings-as-errors Debug build pass. Gitleaks 8.30.1 reports zero
findings; the nine-project NuGet summary reports zero vulnerable packages/advisories.

The release jobs use SDK 10.0.403, target runtime/AOT compiler 10.0.14, Windows
App SDK 2.5.1, deliberately selected ML 2.5.96 and restored WinApp 0.7.1.
Both target restores, AOT/MSIX builds, exact structure/privacy checks, bound
app-only audits and qualified dependency exports pass before upload.

## Downloaded package and graph bindings

| Input | x64 | ARM64 |
|---|---|---|
| Unsigned MSIX SHA-256 | `c07e0010dd252133130c5345eceaaf139ac4f209895a4ae918a35624b7b92822` | `3091fd1f32ae6fc0428557e9d4a14de710a2b7af592a0955cee3617e9f5e3a29` |
| App assets SHA-256 | `fd37caedc13e4d61f3181670a64ce46f92abbd9ef636d2c9c52495911339aafb` | `bd4779238c092e7da0ad823e9d881b41e8b521a10af5c09cde3c512b1b5f7180` |
| Qualified BOM SHA-256 | `e2ac094e874cbaa5e00cb3f31d63e4bf9e12d67f13a1c749fb845156077920c9` | `be8e22f360586af9c6613ae22b12e9a2be1d5e39e03021e71dc2af8021fe7da2` |

The ten package/report files are hash-pinned in a private descriptor. The
[unsigned handoff checker](../../scripts/Test-ReleaseCandidate.py) rehashes and
inspects both downloaded MSIX files, their entry/privacy records, matching
source/target/tool graphs and zero-advisory app audits. It reports PASS for
`unsigned_candidate_input_consistency`, with `release_authorized: false` and
nine production/provenance gates pending.

Both BOMs pass the official CycloneDX 1.6 schema with zero errors. The preserved
schema SHA-256 is `1ebcb88a2c845ecb6ff7bee7aeabdff9422cb0347f3d6875b241bd444b7e098f`.
Referenced SPDX/JSF schemas come from the official specification's `1.6` source.
JSON Schema 4.26.0 is installed only in ignored repo-local verification files;
it is not an app dependency. The initial schema-origin request returned HTTP
403; the official GitHub source succeeds. The handoff and two schema reports
pass privacy with three files and zero findings.

## Acceptance limits

- These are unsigned packages with the existing placeholder identity/publisher
  and manifest version `1.0.0.5`. No production identity, first version, signer,
  timestamp/trust, legal/contact or asset-rights decision is inferred.
- This manual maintenance run skips the tag-only ref/version policy step.
  Thirty-three local real-Git tests verify that checker, but protected release
  refs and configured reviewed-main pins require their separate acceptance.
- The qualified BOM reports vendor binary ownership PASS while full dependency
  closure remains INCOMPLETE; linked-code/projection and vendor build/backport/
  runtime/advisory/legal attestations remain pending.
- Downloaded artifact/report agreement and a passing hosted run do not constitute
  independent signed source-to-build attestation or production release authority.
- Native builds and package inspection do not establish installed execution,
  clean customer trust/dependencies, update/uninstall or native ARM64 runtime.
  No guest, host UI, certificate trust, signing or installation ran here.
- GitHub emitted a Node 20 action-runtime deprecation/forced Node 24 notice for
  existing reviewed action pins. Those pins require normal maintenance review;
  the notice is retained and is not a compiler warning or a failed gate.

The earlier push run
[42536237534](https://github.com/Dvaderfun/wintodo/actions/runs/42536237534) at
`8e562d8` remains FAIL with one script-test failure and later stages skipped.
The [Windows path correction](dep18-release-ref-policy.md) and its successful
rerun do not rewrite that history. M9 remains owner-deferred.

## Private evidence

`artifacts/dep-production-next-integration/hosted-42537269138/` contains both
downloaded architecture artifacts, complete safe unit/scanner summaries,
`candidate-inputs.json`, `handoff.json`, per-target `sbom-schema.json` and
`final-evidence-privacy.json`. Raw watcher/failure logs remain ignored nearby.
Use the exact source/hash association above; a later source or signature needs
its own package evidence.
