# Deployment readiness and GitHub release plan

Prepared: 2026-11-08. Audit baseline: `d8c2ac3`.

Status: **IN PROGRESS — DEP-01, DEP-03, DEP-04, DEP-05, DEP-06, DEP-07, DEP-08, DEP-09 and DEP-11 automated acceptance passed. DEP-04 guest UI/live checks and DEP-14 final signed-package acceptance remain pending with the remaining release work.**

**Release check, 2026-11-19: ready for review; NOT READY for publication.** The [package source passed CI on GitHub](https://github.com/Dvaderfun/wintodo/actions/runs/44593397992). Both [locally built unsigned MSIX files](#2026-11-19--pr-ci-and-local-store-candidates) match the reserved Store identity. The [English screenshots, captions and hero art](../releases/store/en-US/README.md) are ready for listing review. Effective ref protection, rights, native dependency evidence, Store certification and installed customer tests remain open.

This plan turns the deployment-readiness audit into work packages for a safe
GitHub release and GitHub Actions CI/CD. It covers data integrity, API security,
credentials, personal-data exclusion, packaging, signing, documentation and
deployment verification. Writing this plan does not authorize publication,
changes to accepted decisions, certificate trust or machine provisioning.

## 1. Outcome and boundaries

The intended outcome is reviewed, production-signed, self-contained Native AOT
MSIX packages for x64 and ARM64, produced from one clean source commit, with
passing CI, verified privacy, reproducible provenance and tested installation,
updates and removal. The owner selects Store-first under Univera LLC in
[ADR-019](../adr/ADR-019-store-first-source-available-release.md). Microsoft
supplies the Store MSIX signature; direct production downloads and purchased
Windows signing certificates are outside this first route. Public source uses
the selected source-available/free-self-build policy after final rights review.

Personal-data exclusion applies to repository content, public history,
distributed packages, release metadata, screenshots and public diagnostics.
The application will still store its users' tasks locally and exchange data
with services they explicitly choose; document these flows accurately.

Preserve the existing architecture, Microsoft To Do interaction model,
iCalendar unknown-property round-tripping, soft deletion and Native AOT.
M11, M12 and unrelated UX redesign are outside this release preparation.
M9 performance remains owner-deferred until the owner changes that disposition.

Read [AGENTS.md](../../AGENTS.md), [ARCHITECTURE.md](../../ARCHITECTURE.md),
the Current state section of [PROGRESS.md](../PROGRESS.md),
[PLAN.md M10](../../PLAN.md), [packaging](../guides/packaging.md) and
[release checklist](../guides/release-checklist.md) before execution.
Read the relevant specs and load the applicable vendored WinUI skills before
each work package. Use CodeGraph before locating or reading implementation code.

## 2. Evidence baseline and limits

The audit established the following baseline; recheck it when execution starts.

| Area | Observation at the audit baseline |
|---|---|
| CI | Latest GitHub run for `d8c2ac3` passed compilation and failed two import byte-preservation tests after Windows checkout changed LF fixture bytes to CRLF. |
| Data integrity | Pull dirty-state checks precede the writer transaction; a newer local edit can be overwritten and its changes acknowledged. Synced task moves retain the source remote URL and ETag. Both findings need deterministic runtime regressions. |
| API security | Graph network-path pagination links bypass destination validation. CalDAV automatic redirects bypass the initial origin policy. Body-size checks occur after buffering; attachment streams have no enforced byte cap. |
| Privacy | Six prepared MSIX files contain an identifying absolute PDB path inside the executable's CodeView record. Local AOT summaries and private symbols contain local build paths. |
| Source scan | 400 tracked files, 105 commits and 1,237 reachable historical blobs yielded no high-confidence secret matches in targeted text scans. Fixtures were sanitized or synthetic; 16 tracked raster images lacked identifying metadata. |
| Credentials | CalDAV passwords use PasswordVault. Graph access tokens are not persisted but remain reachable after some wizard dismissal paths. The Entra public client ID is not a secret. |
| Dependencies | Existing resolved assets include `Microsoft.DotNet.ILCompiler 10.0.12`, affected by [GHSA-p3jp-9v83-76w9](https://github.com/advisories/GHSA-p3jp-9v83-76w9); the .NET 10 fix starts at `10.0.14`. No application Brotli usage was found, so exploitability was not established. |
| Delivery | Tag CI uploads unsigned Actions artifacts only. Package identity/signing choices are unresolved, and tags do not control the fixed manifest version. Local x64 and ARM64 packages represent different source states and versions. |
| GitHub | Repository was private with no tags or releases. Branch-protection/ruleset reads were restricted; enforcement was not verified. |
| Toolchain | Published Windows runner inventories include the required native tools. Toolchain absence was not the observed CI failure. The manifest minimum requires Windows 11 24H2 or later compatible desktop systems. |

The audit did not run fresh builds, restores or UI gates. There was no dedicated
secret scanner installed. Unreachable Git objects, screenshot OCR and local
database contents were not verified. These observations are evidence for work,
not a certification of release safety.

## 3. Owner decisions and execution rules

Record decisions before dependent work. Keep unresolved choices visible.

[The owner decision packet](deployment-owner-decisions.md) groups the recorded
choices, remaining production values and execution order. Wintodo/current
artwork, public source after review, a fresh production installation with
data-preserving subsequent updates, and `v1.0.0` / `1.0.0.0` are selected.
[ADR-019](../adr/ADR-019-store-first-source-available-release.md) records the
owner's Store-first/Univera LLC selection, commercial redistribution restriction
and support@univera.app contact. [Store preparation](deployment-store-first-preparation.md)
and [the licence draft](wintodo-source-available-license-draft.md) identify exact
Store identity and remaining rights/legal gaps. Operational approvals follow concrete preparation
of their respective sessions and release stages.

| Decision | Required record | Blocks |
|---|---|---|
| App name and branding | Wintodo and current artwork retained; asset source/redistribution rights pending | DEP-16, DEP-17 |
| Distribution | Store-first selected (ADR-019); public source after review selected; reservation confirmed; submission/protection/visibility transition pending | DEP-16, DEP-18, DEP-20 |
| Package identity and publisher | Univera LLC selected; fresh production installation acceptable; future updates preserve data; reserved package identity and publisher applied | DEP-16, DEP-18, DEP-19 |
| Versioning | Stable `vMAJOR.MINOR.PATCH` → `MAJOR.MINOR.PATCH.0`, first `v1.0.0` / `1.0.0.0` applied on the new production family; monotonic installed-update test pending | DEP-16, DEP-18 |
| Signing | Store-managed MSIX signing selected; actual certified/signed-package evidence and scoped submission authority pending; direct signing is a separate later route | DEP-18 |
| Public attribution and licence | Free self-builds/commercial redistribution restriction selected; support@univera.app; copyright authority/exact source-available terms, Git attribution and asset rights pending | DEP-13, DEP-17 |
| Production overrides | DEP-12 A selected: retain/document established controls; runtime acceptance pending | DEP-12 |
| Deferred gates | Explicit M9 disposition and any other release limitations; no failed/skipped gate becomes PASS | DEP-19, DEP-20 |

- Changes to accepted decisions require an ADR and owner acknowledgement.
  Forward schema changes follow the [migrations guide](../guides/migrations.md).
- Host UI automation is forbidden without explicit permission for that session.
  Run affected gates through `scripts/Invoke-VmGate.ps1` in `WintodoTest`.
  If unavailable, leave UI gates pending and continue independent work.
- Ask before certificate trust, guest provisioning/install sessions, changes
  outside the repository, Developer Mode/Sandbox enablement or toolchain setup
  requiring owner approval. The setup skill remains owner-invoked only.
- Never launch the packaged executable directly. Use approved project launch
  helpers for development and installed-package activation for deployment gates.
- Keep x64/ARM64 packaging, the manifest, AOT analyzers and warnings-as-errors.
  New dependencies must meet PLAN's banned-dependency and AOT requirements.
- Track one owner/implementer per active work package, preserve others' edits,
  and commit reviewable units. Update affected docs and PROGRESS with actual
  results. UI work requires `winui-code-review` with no issue above informational.
- All future planning documents belong under `docs/plans/`.

## 4. Work tracking and order

Claim a row before implementation. A row becomes
complete only when its acceptance criteria pass and its evidence is recorded.

| ID | Work package | Priority | Depends on | Owner | Status |
|---|---|---|---|---|---|
| DEP-01 | Preserve fixture bytes and repair CI | Blocker | — | Codex / dep01_fixture_ci | COMPLETE; CI PASS (`5604256`) |
| DEP-02 | Patch and verify release toolchain | Security | — | Codex / dep02_toolchain_review + release_toolchain_followup + exact_toolchain_evidence + native_closure_followup + retained_code_export + native_closure_next + vendor_provenance_resolution | Exact WinApp published-asset ownership and vendor ONNX mapping ESTABLISHED; ML 2.5.96 servicing integrated with isolated package/current-helper AOT LOCAL PASS; embedded/runtime/advisory/hosted acceptance PENDING |
| DEP-03 | Protect local edits during sync pull | Blocker | DEP-01 | Codex / dep03_sync_pull | COMPLETE; automated acceptance PASS |
| DEP-04 | Implement recoverable synced task moves | Blocker | DEP-03; ADR-017 accepted | Codex / dep04_data + dep04_sync + dep04_regressions | AUTOMATED ACCEPTANCE PASS; guest UI/live/final-package checks PENDING |
| DEP-05 | Validate Graph request destinations | Blocker | DEP-01 | Codex / dep05_graph_origin | COMPLETE; automated acceptance PASS |
| DEP-06 | Enforce CalDAV redirect origin policy | Security | DEP-01 | Codex / dep06_caldav_redirects | COMPLETE; automated acceptance PASS |
| DEP-07 | Bound network bodies and attachment downloads | Security | DEP-05, DEP-06 | Codex / dep07_network_limits | COMPLETE; automated acceptance PASS |
| DEP-08 | Sanitize all diagnostic paths | Privacy blocker | DEP-01 | Codex / dep08_diagnostics | COMPLETE; automated app/CI privacy acceptance PASS |
| DEP-09 | End sensitive import sessions correctly | Security | DEP-01 | Codex / dep09_import_lifecycle | COMPLETE; automated acceptance PASS |
| DEP-10 | Preserve untrusted attachment provenance | Security | DEP-07; API/policy review | Codex / dep10_provenance | IMPLEMENTED; host/AOT PASS; guest preflight FAIL, UI acceptance PENDING |
| DEP-11 | Reject unsupported database schemas | Data safety | DEP-01 | Codex / dep11_schema_guard | COMPLETE; automated acceptance PASS |
| DEP-12 | Resolve production test/storage overrides | Policy | Policy A selected | Codex / dep12_overrides + production_override_policy | A SELECTED/DOCUMENTED; 26 host launch-policy tests PASS; actual guest/Release/AOT/default/broker/final-package acceptance PENDING |
| DEP-13 | Verify repository/history and source privacy | Privacy blocker | Attribution decision | Codex / dep13_archive_review | Archive/fixture/asset checks PASS; nine package icons mapped to the MIT template. Background rights, public attribution and final-source acceptance PENDING. |
| DEP-14 | Remove identifying binary and metadata paths | Privacy blocker | DEP-02 | Codex / dep14_binary_privacy | PRE-SIGNING PASS; final signed-package acceptance PENDING DEP-16/18/19 |
| DEP-15 | Harden and reproduce CI builds | Blocker | DEP-01, DEP-02 | Codex / dep15_locked_restore + exact_toolchain_evidence + audit_graph_binding + production_override_policy | 19 Nov hosted PR locked restore, build, 707 unit and 206 script tests PASS; [16 Nov failure retained](#2026-11-19-hosted-restore-failure). New main packaging run, ref protection and native advisory review PENDING. |
| DEP-16 | Finalize identity, version and package contents | Blocker | Reserved Store identity; DEP-14 | Codex / package_inspection + unsigned_candidate_validation + package_sbom + bound_candidate_pair + retained_code_export + vendor_provenance_resolution | Both local `c2c8919` unsigned packages PASS identity, version, structure, audit, notice inclusion and privacy checks; final payload/vendor review and Store-signed acceptance PENDING. |
| DEP-17 | Complete licensing, privacy, branding and sign-in docs | Release | Rights/licence finalization | Codex / release_documentation + shipped_notices_review + notice_integration + owner_decision_followup | Nine supplier texts integrated; five English screenshots, captions, text-free Store hero and five separate promo renders prepared. Copyright/free-sharing terms, shipped artwork rights, final privacy URL and live work/school sign-in PENDING. |
| DEP-18 | Add protected Store submission and release stages | Blocker | DEP-13–17; ADR-019 | Codex / candidate_handoff + audit_graph_binding + bound_candidate_pair + deployment_next + release_ref_policy | Read-only tag job rejects unprotected tags/main and unavailable API; Store-managed signing selected; direct production signing deferred; release authorization false; actual protected Store upload/certification/signed-result/release acceptance PENDING |
| DEP-19 | Verify final deployment and functional gates | Blocker | DEP-03–12, DEP-16–18 | Codex / deployment_gate_inventory + installed_package_smoke | Guest-only installed-smoke runner PREPARED; 12 mocked/static checks PASS; actual guest/API/runtime/visual, signed install/update/uninstall and final deployment acceptance PENDING |
| DEP-20 | Publish approved assets and maintain releases | Release | DEP-19; explicit publication authorization | Unassigned | PENDING |

Execution sequence:

1. Establish decisions and repair CI; select a patched compatible toolchain.
2. Fix data/API/privacy defects and add meaningful regression coverage.
   Independent work packages may proceed concurrently with clear ownership.
3. Harden CI, sanitize outputs and complete identity/documentation/assets.
4. Produce reviewed Store-upload candidates, obtain the approved staged/certified
   signed results, then test those actual Store packages.
5. Publish the approved Store release and reviewed source metadata, then
   establish ongoing release maintenance.

Parallel execution started on 2026-11-08 with three bounded streams: DEP-01
fixture/checkout repair, DEP-02 toolchain verification, and the independent
scanner/inventory portion of DEP-13. The DEP-13 attribution decision and final
privacy acceptance remain pending. Codex owns integration and shared progress
records; agents use separate ignored scratch checkouts for build outputs and
do not edit shared tracking documents.

After DEP-01's acceptance was recorded, the second batch split DEP-03
(sync pull and its store contracts), DEP-05 (Graph request validation), and
DEP-11 (schema opening/migration validation), with explicit file ownership.
All three implementations and their automated acceptance are complete; the
second batch's evidence below distinguishes these fixes from final-package gates.
DEP-04 follows DEP-03; DEP-07 follows both origin-policy fixes. Final signing,
deployment and publication remain sequential after their listed dependencies.

The third batch splits DEP-06 (CalDAV transport and redirect regressions),
DEP-08 (safe diagnostic output and synthetic privacy regressions), and DEP-09
(import session lifetime and dismissal recovery). Codex owns shared tracking,
integration, build outputs and guest gate execution. DEP-09 owns the wizard,
connector and shell import lifecycle files; DEP-08 supplies changes for their
diagnostic call sites through DEP-09 or integration rather than editing them
concurrently. DEP-06 supplies its App composition change for integration.

The fourth batch splits DEP-07 (bounded CalDAV/Graph responses and import
attachment staging), DEP-14 (native build paths and publish metadata), and
DEP-04 (deterministic move reproduction and a concrete recovery design).
DEP-04 must obtain owner acknowledgement before implementing any required
schema/protocol ADR. Agents own disjoint files and use ignored isolated
checkouts for validation. Codex owns shared tracking, integration builds,
guest gates and commits; final signed-package checks remain pending.

The fifth batch splits DEP-10 (downloaded attachment provenance and opening),
DEP-15 (secret-free CI, pinned Actions, audit gates and trusted AOT cadence),
and DEP-12 (read-only override inventory and a concrete production-policy
proposal). Agents own disjoint files; Codex owns shared tracking, integration
builds, guest gates and commits. DEP-12 behavior changes and DEP-04's proposed
ADR-017 remain owner-pending. Remote settings, signing and publication are
outside this batch. GitHub's protection and ruleset APIs currently return
HTTP 403 for the private repository, requiring an eligible plan or visibility
decision before those enforcement gates can be verified.

The sixth parallel batch closes independent remaining work: DEP-02 native/tool
advisory review, DEP-15 locked dependency resolution and CI reproducibility,
and DEP-13 source-archive/fixture/image privacy review. Agents own disjoint
files and keep raw evidence in separate ignored scratch directories. Codex owns
shared tracking, integrated verification and commits. No identity, attribution,
ADR, machine setup, remote protection, signing or publication decision is changed.

The next parallel preparation batch (2026-11-08), authorized by the owner's
request to use subagents, splits three decision-independent streams: DEP-02
advisory reachability/tool provenance follow-up, DEP-16 exact-package structural
inspection, and DEP-17 evidence-backed privacy/installation drafts. Agents own
separate review documents and new scripts/docs; Codex owns shared tracking,
integration and verification. Existing accepted identity, signing, override and
distribution decisions are not changed by preparing these materials. No machine
setup, host UI, remote settings, signing or publication is part of this batch.

This preparation batch is now integrated: the structural gate and 24 new
regressions pass locally, unsigned-candidate CI invokes it, privacy/installation
drafts are ready for review, and the native/tool follow-up identifies WinApp
0.8.0 as an upgrade candidate without changing the pinned CLI. These results
complete the bounded streams, not DEP-02/16/17's final acceptance.

The immediate subsequent gates still require the DEP-12 production policy,
guest SDK setup by the owner, native advisory disposition, final identity/version
and signing choices, attribution/image rights, and actual hosted CI for the
current reviewed source. Final signed-package testing and publication remain
ordered after those prerequisites.

The owner authorized another parallel batch on 2026-11-08. Three bounded
streams prepare evidence from source `965d86b`: clean-source unsigned x64/ARM64
MSIX validation (DEP-16), shipped third-party notice obligations (DEP-17), and
exact native/tool binary provenance in the existing CI exporter (DEP-02/15).
Each agent owns separate files and ignored evidence directories. Codex owns
shared tracking, integrated checks and commits. This batch does not choose
production policy, identity, signing or licensing, provision the guest, modify
remote settings, sign packages or publish assets. Read-only GitHub checks show
the latest run targets `da945199`, rather than this source; hosted acceptance
therefore remains pending until the reviewed source is available remotely.

This batch is integrated. Both fresh unsigned packages pass structural/privacy
checks and layout equivalence; the first attempt reproduced `WTDLOCK001` because
WinApp restored the containing solution's unsupported AOT test graphs. The
supported `--no-restore` correction succeeds after each matching target locked
restore/publish and is now in CI and the packaging guide. Exact-tool metadata
and seven new regressions pass locally. The notice review matches 103 DLLs and
two vendor helper executables to package sources and prepares nine inherited
legal texts; the app payload is unchanged. These bounded results do not close
the outstanding owner, hosted, legal/SBOM, signing or deployment gates.

The owner requested the next parallel batch on 2026-11-08. Three bounded
streams integrate inherited vendor notice texts (DEP-17), produce exact-package
dependency/SBOM evidence with unresolved linked-code ownership visible (DEP-16),
and validate unsigned candidate hashes/evidence for a future signing handoff
(DEP-18). Agents own disjoint files; Codex owns shared tracking, integrated
verification and commits. This batch does not select first-party licensing,
identity/version policy, signing, distribution or production overrides, provision
the guest, alter remote settings, sign packages or publish assets. Final
release acceptance remains dependent on the listed owner and hosted gates.

This batch is integrated locally. Nine inherited texts are in source and
both freshly rebuilt unsigned `9106040` packages. Both packages pass structure,
privacy, source notice inclusion and layout equivalence. Qualified CycloneDX
exports pass official schema/privacy checks and hash-match all 105 vendor
binary files, while full retained-code/vendor/projection closure remains
INCOMPLETE. The fresh-pair handoff and 24 regressions pass with release
authorization false; all 104 script tests and 678 MTP tests pass. Unsigned CI
now exports and privacy-checks the qualified BOM. Hosted CI, owner decisions,
signing and final deployment acceptance remain pending.

The owner authorized parallel work on 2026-11-09. Three bounded streams address
the dependency-audit/source/target graph binding gap (DEP-15/18), investigate
remaining native/runtime provenance and advisories (DEP-02/16), and map existing
gates to final signed deployment requirements (DEP-19). Agents own disjoint
files; Codex owns shared tracking, integration and verification. Investigation
results must distinguish established ownership from unresolved vendor evidence.
The gate inventory does not execute installation, trust changes, host UI or
guest provisioning. Production decisions, remote changes, signing and
publication retain their existing prerequisites.

This batch is integrated locally: actual x64/ARM64 audit/tool graph bindings,
124 script regressions, clean Debug build and full 678-test rerun pass. The
initial SQLite fixture failure is retained in the evidence register. Native
retained-code observations and the signed-deployment gate matrix are linked
below. Fresh bound candidates, hosted/protected execution, vendor dispositions,
owner production decisions and signed deployment remain pending.

The owner requested parallel work again on 2026-11-09. The bounded batch
split three independent streams: `bound_candidate_pair` rebuilds and verifies
clean-source unsigned x64/ARM64 packages with current audit bindings (DEP-16/18);
`retained_code_export` prepares reproducible positive retained-code evidence
without claiming complete native closure (DEP-02/16); `installed_package_smoke`
prepares a guest-only installed-package smoke runner using existing assertions
(DEP-19). Each agent owns separate files and ignored evidence directories.
Codex owns shared tracking, integration, review and commits. No installation,
guest provisioning/start, host UI, certificate trust, remote write, signing,
publication or unresolved production-policy decision is authorized by this batch.

This batch is integrated locally: both fresh bound candidates and the current
unsigned handoff pass, all 511 source files/20 locks are preserved, historical
and fresh native observations pass, Debug build is clean, and 678 unit tests
plus 152 script regressions pass. The installed-smoke runner is prepared with
12 mocked/static checks; actual guest/API/runtime/visual acceptance remains
pending. Its early mocked tests used process-only host UI opt-in contrary to
policy; both assignments were removed and the corrected tests run without it.
No actual host UI command ran. Full closure, owner decisions, hosted/protected
execution, signing, installed update/uninstall and publication remain pending.

The owner requested the next parallel batch on 2026-11-09. Three bounded streams
verify original NuGet archive signatures (DEP-02/16), assess the next deployment
gap and prepare a signed-payload continuity checker (DEP-18/19), and assemble
the owner decision packet (DEP-12/13/16/17/18). Agents own separate new
scripts/tests/documents and ignored evidence; Codex owns integration and shared
tracking. No production decision, machine/guest action, signing, remote write or
publication is part of this batch.

This batch is integrated locally: all 45 original archives verify with exact
hash preservation, the continuity check passes 19 synthetic regressions and
one historical ARM64 pair, Debug build is clean, and all 678 unit/171 script
tests pass. The misleading historical x64 filenames are rejected as a signing
pair without changing files. The seven-choice owner packet is ready; choices
remain pending. The next critical path is recording those choices, obtaining
vendor/advisory disposition and current hosted/protected execution, then
producing/signing approved candidates for exact-package deployment tests.

The owner's 2026-11-09 “Lets do it” response starts the production-preparation
batch. Direct MSIX first and DEP-12 A are recorded in ADR-018. Parallel streams
document/test the preserved launch policy, implement local release-ref/version
eligibility and obtain new primary vendor evidence. Codex owns shared tracking,
CI wiring, review and integration; agent files and build outputs are isolated.

The policy/ref phase is integrated: 26 new launch tests, 31 real-Git ref tests,
704 full unit tests and 202 full script tests pass; Debug/Release builds and
independent review pass. Tag CI fetches full history and requires a reviewed
main pin before restore; exact manifest LF checkout is declared. Remote settings,
signing, guest installation and publication remain pending. The separate Windows
ML servicing evaluation retains root dependencies until both target proofs and
the source/lock/notice changes are reviewed.

## 5. Detailed work packages

After the owner acknowledged ADR-017, DEP-04 implementation starts in three
streams: durable Core/Data contracts and move transactions, Sync execution and
conflict recovery, and independent SQLite/transport regression coverage. Codex
owns integration, schema-compatibility fixtures, existing chooser messaging,
shared tracking and validation. Other owner decisions remain pending. Agents
own disjoint files and preserve others' edits; no host UI or machine setup is
authorized by the ADR acknowledgement.

### DEP-01 — Preserve fixture bytes and repair CI

Targets: `.gitattributes`,
`tests/fixtures/mstodo/basic/me/todo/lists/L-default/tasks/T3/attachments/A1/content.bin`,
`tests/Wintodo.Import.Tests/GraphTodoClientTests.cs`,
`tests/Wintodo.Import.Tests/ImportStoreTests.cs`, `.github/workflows/ci.yml`.

- [x] Add narrow attributes preserving raw attachment bytes, including `.bin`
      fixtures. Do not weaken byte-preservation assertions or normalize data
      inside production download code to hide checkout changes.
- [x] Reproduce the prior failure on a disposable fresh Windows checkout and
      prove repository and working-tree fixture bytes agree after the fix.
- [x] Run all four test projects through the existing MTP runner and a clean
      warnings-as-errors solution build; verify a new GitHub run succeeds.
- [ ] Investigate IMP-26/intermittent SQLite initialization separately only if
      it reproduces; do not classify the known line-ending failure as a flake.

The conditional IMP-26 investigation was not triggered: local and GitHub full
runs passed without an initialization failure. DEP-01 acceptance passed in
[GitHub CI run 42424857115](https://github.com/Dvaderfun/wintodo/actions/runs/42424857115)
for `5604256`, which contains `2e149ca`'s configuration fixes and tracking docs.
Downloaded TRX counters confirm all four suites passed 392/392 with zero skips.

Acceptance: fresh-checkout CI passes, raw attachment bytes remain unchanged,
and results identify the exact commit with no unexplained failures/skips.

### DEP-02 — Patch and verify the release toolchain

Targets: `global.json`, `Directory.Packages.props`, publish profiles and CI.

- [x] Select a supported stable .NET 10 SDK whose resolved runtime/AOT compiler
      includes the advisory fix (`10.0.14` or later patched compatible version).
      Recheck current advisories instead of treating this minimum as permanent.
- [x] Verify resolved target-specific AOT/runtime packages after restore for
      both architectures; avoid manually adding an ILCompiler dependency to
      bypass the SDK's supported package selection.
- [ ] Audit direct and transitive packages, native dependencies and tools;
      review updates for AOT/API compatibility rather than upgrading everything.
- [ ] Verify selected SDK, MSVC, Windows SDK and restored WinApp availability
      on the chosen runner. Record versions without personal machine paths.
- [x] Publish fresh x64 and ARM64 candidates with zero unsolved compiler,
      WinUI, trim/AOT or single-file analyzer warnings.

Acceptance: the supported dependency graph is audited, no unresolved release
security advisory remains, and both AOT outputs use the reviewed patched tools.

[The native/tool review](dep02-toolchain-review.md) maps all 103 root payload
DLLs to restored packages and verifies the actual SQLite engine and upstream
patch records. It identifies WinApp's bundled HarfBuzz `14.2.0` in
`GHSA-r62p-jr5f-3mjm`'s range; the current native-SDK packaging branch has no
direct subsetting calls, but transitive applicability and an owner disposition
remain pending. No stable patched release was identified. WinApp's embedded
runtime provenance and native vendor-build mappings also remain open. Hosted
inventory maps Visual Studio to patched `17.14.44`; actual runner execution is
still required. The local `18.8.0` installation has a servicing/coverage gap,
without proof of an affected compiler. No machine/tool dependency was changed.

The follow-up traces the unsigned native-SDK command path and proposes a
bounded HarfBuzz non-applicability disposition for owner review. Stable WinApp
0.8.0 includes signing fixes but retains the same graphics dependencies; it is
an upgrade candidate, not a demonstrated HarfBuzz or embedded-runtime fix.

[The 2026-11-09 native closure follow-up](dep02-native-closure-followup.md)
matches private PDBs to the exact earlier unsigned executables and identifies
positive retained SQLite/Toolkit/WinRT/projection/runtime symbols. Current
primary advisories were refreshed. ONNX suffix and DirectML product-version
comparisons fail to establish vendor source/build equivalence; full native
closure and WinApp embedded-runtime provenance remain pending.

### DEP-03 — Protect local edits during sync pull

Targets: `src/Wintodo.Sync/CalDavSyncService.cs`,
`src/Wintodo.Data/SqliteTaskStore.Sync.cs`, Core sync contracts and Sync/Data tests.

- [x] Write deterministic regressions that pause after the clean-state read,
      commit a local edit, then resume remote update/deletion application.
- [x] Check current dirty/revision/change state inside the writer transaction;
      preserve concurrent edits and avoid acknowledging changes not applied.
- [x] Return/propagate conflicts or deferred outcomes so counts, sync tokens,
      subsequent retries and UI feedback agree with the actual committed state.
- [x] Cover fields, owned collections, local deletes/restores and pending
      change-log entries; preserve existing newer-edit push acknowledgment.

Acceptance: no intervening local edit disappears, remote deletion cannot hide a
newly edited task silently, and queued changes survive or become explicit conflicts.

### DEP-04 — Implement recoverable synced task moves

Targets: `SqliteTaskStore.Tasks.cs`, `SqliteTaskStore.Batch.cs`,
`CalDavSyncService.cs`, sync contracts/change log and Sync/Data tests.

- [x] Reproduce a synced A-to-B move retaining the source remote URL/ETag.
- [x] Define recoverable source removal and destination creation, including
      ETags, UID preservation, offline retries and restart recovery. Do not
      simply clear the source URL and leave an orphan remote copy.
- [x] Handle single/bulk moves, children, same-account collection moves,
      cross-account moves, local-to-synced and synced-to-local moves.
- [x] Protect against a source pull moving the task back and against premature
      acknowledgment or loss when either remote operation fails/conflicts.
- [x] Use an acknowledged ADR and forward migration if durable move intent
      requires a schema/protocol decision.
- [x] Test two sync cycles and second-client convergence, including failure
      between remote operations and resume after process restart.

Acceptance: destination and source membership match the intended move, no task
version is silently lost, and retries converge without unexplained duplicates.

The fourth batch reproduced seven deterministic baseline failures against
`79ce9b1`; no production fix is claimed. [ADR-017](../adr/ADR-017-recoverable-synced-task-moves.md)
and [the detailed implementation plan](dep04-synced-moves.md) define durable
recovery. The owner acknowledged ADR-017 on 2026-11-08. Migration 005 and the
durable move implementation now pass automated acceptance: the independent
71-case SQLite/CalDAV matrix and full 678-test solution pass without skips.
Guest UI/live-provider and final installed-package checks remain pending.
Baseline failures remain historical proof.

### DEP-05 — Validate Graph request destinations

Targets: `src/Wintodo.Import/MicrosoftTodo/GraphTodoClient.cs` and its tests.

- [x] Resolve relative pagination/download URLs before validation; validate
      final scheme, normalized host, effective port, userinfo and fragment.
- [x] Enforce validation at the authenticated-send boundary and review handler
      redirect behavior so destination checks cannot be bypassed later.
- [x] Test network-path links, foreign hosts, alternate ports, userinfo,
      fragments and valid same-origin paging with synthetic responses.
- [x] Assert rejected requests never reach the handler carrying authorization.
      Preserve GET-only Graph enforcement and existing retry/cancellation behavior.

Acceptance: bearer tokens are sent only to the approved Graph origin through
all request paths. No test contacts a hostile host or uses a live access token.

### DEP-06 — Enforce CalDAV redirect origin policy

Targets: `src/Wintodo.App/App.xaml.cs`, `src/Wintodo.Sync/CalDavClient.cs`, tests.

- [x] Disable implicit authenticated-client redirects and explicitly reject or
      process redirects through the same scheme/host/port/origin policy.
- [x] Use real handler behavior against isolated loopback test servers to cover
      307/308 PUT body replay, foreign origin, HTTPS downgrade and redirect loops.
- [x] Preserve allowed loopback development servers and normal HTTPS discovery.
      Any provider compatibility exception needs a documented decision.

Acceptance: a redirect cannot disclose task bodies to an unapproved origin or
silently accept foreign content. Do not report the audit as proving a password leak.

### DEP-07 — Bound network bodies and attachment downloads

Targets: `CalDavClient.cs`, `GraphTodoClient.cs`, `MicrosoftTodoReader.cs`,
`AttachmentContentDecoder.cs` and relevant tests.

- [x] Use headers-first HTTP completion and bounded streaming before allocating
      XML/calendar/JSON bodies. Keep or explicitly document existing limit changes.
- [x] Enforce actual byte limits for declared, unknown and misleading lengths;
      account for decoded attachment output and any enabled decompression.
- [x] Define per-attachment and per-import byte budgets, cancellation and
      disk-full behavior. Reject over-budget data before unbounded writes.
- [x] Remove partial staging files on failure/cancel and preserve committed
      imports and existing files; test malformed, oversized and truncated bodies.
- [x] Retain XML DTD prohibition, disabled external resolution and parser limits.

Acceptance: hostile/oversized streams stop at reviewed limits without buffering
the whole response or leaving partial downloads; ordinary import/sync still passes.

Exact byte/deadline policies and cleanup limitations are documented in
[network-limits.md](../guides/network-limits.md). Final installed-package and
live-provider verification remains DEP-19.

### DEP-08 — Sanitize all diagnostic paths

Targets: App startup/share handlers, import view models, sign-in, sync,
transfer/cleanup handlers, scripts and public CI/release diagnostics.

- [x] Inventory full exception/string logging, including Graph service errors,
      identity-provider descriptions, account names, URLs and filesystem paths.
- [x] Emit reviewed operation/category/type/status codes; remove unsafe values
      from nested exceptions and every public diagnostic route.
- [x] Preserve localized actionable UI errors without publishing raw server
      messages or private identifiers. Use resource strings for any new UI text.
- [x] Add synthetic marker tests covering tokens, passwords, emails, task text,
      account names and paths; assert markers are absent from safe diagnostics.
- [x] Verify Debug trace files and Release tracing separately. Retain useful
      debugging without claiming Release had a durable trace-file listener.
- [x] Close IMP-25 only after its complete regression gate passes.

Acceptance: public diagnostics and reproducible synthetic captures contain no
secrets or private content across startup, import, sync, share and failure paths.

### DEP-09 — End sensitive import sessions correctly

Targets: import wizard lifecycle, `ShellViewModel.Import.cs`, connector/reader.

- [x] Release reader/client/token references on success, final cancel,
      navigation dismissal, reset and process shutdown as appropriate.
- [x] Cancel and finish in-flight operations before tearing down their session;
      prevent callbacks from restoring dismissed sensitive state.
- [x] Preserve expired-sign-in resume choices without retaining the old token.
      Keep account/task preview state only as long as the documented flow needs.
- [x] Test finish, idle/busy cancel, navigation away, expiration/reconnect and
      close-during-saving, including a clear outcome for committed data.

Acceptance: final dismissal ends the sensitive session; stale tokens cannot be
used by subsequent operations. Do not promise secure erasure of immutable strings.

### DEP-10 — Preserve untrusted attachment provenance

Targets: Graph staging/copying and `TaskDetailViewModel.Attachments.cs`.

- [x] Verify supported Windows provenance/attachment APIs against restored
      project references and Native AOT; select and document the opening policy.
- [x] Preserve or apply appropriate Internet-origin metadata for downloaded
      attachments through staging, copying and final storage where supported.
- [x] Keep filename/path confinement and Windows Launcher restrictions; do not
      introduce shell execution or claim a demonstrated executable exploit.
- [ ] Test provenance and benign document opening in the guest, including
      failed imports and supported file types, using synthetic attachments.

Acceptance: downloaded files retain the reviewed trust policy through opening,
and attachment import/open/delete functionality and path confinement still pass.

[The DEP-10 record](dep10-attachment-provenance.md) documents the reviewed
Internet-zone marker and `LauncherOptions.TreatAsUntrusted` policy. Host
regressions pass 11/11, and both fresh AOT publishes pass. The focused guest
gate stopped before executing tests: the clean checkpoint has SDK 10.0.401,
while `global.json` requires 10.0.403. Guest setup is owner-invoked; no SDK
installation, weakened pin or host UI fallback was performed. Guest opening,
removal/restart and failed-wizard-import acceptance remain pending.

### DEP-11 — Reject unsupported database schemas

Targets: `src/Wintodo.Data/SqliteTaskStore.cs` and migration tests.

- [x] Reject versions above the supported migration maximum before ordinary
      writes; give a clear localized recovery/update message where appropriate.
- [x] Test newer-schema rejection with unchanged schema and user rows, fresh
      initialization, every supported upgrade path and idempotent reopening.
- [x] Preserve pre-upgrade backups and prove backup restoration in a disposable
      environment; describe safe rollback when schema downgrade is unsupported.

Acceptance: an older app cannot silently write a newer schema. Existing upgrades
preserve user data and their documented backup behavior.

### DEP-12 — Resolve production test/storage overrides

Targets: App command-line/environment parsing, clock/database hooks, UI gates.

- [x] Inventory `--database-path`, `WINTODO_DATABASE_PATH`, `--clock-file`,
      onboarding and fixture/trace switches, plus Release share activation.
- [ ] Obtain the production policy decision; document intended supported
      overrides or gate test-only behavior without silently changing AOT gates.
- [ ] Test default Release storage/time, selected overrides and broker shares;
      preserve the existing prohibition on Release broker storage overrides.

Acceptance: production and test behavior are explicit, documented and tested.
These locally controlled switches are not classified as remote vulnerabilities.

[The DEP-12 proposal](dep12-production-overrides.md) records the observed
Debug/Release/AOT/share matrix and affected gates. The recommended choice
retains and documents existing advanced local overrides; an alternative gates them
to separate test builds. A database override moves only SQLite, and broker
shares use package LocalState and real time in both configurations. No behavior
changed; owner policy selection and runtime acceptance remain pending.

### DEP-13 — Verify repository/history and source privacy

Targets: tracked source/config/docs/fixtures, Git history, `.gitignore`,
`.gitattributes`, scanner configuration and source archive policy.

- [x] Run a pinned dedicated secret scanner with redacted output against the
      release tree and reachable history; inspect findings structurally.
- [x] Check tokens, private keys, credential URLs, real account endpoints,
      identifying names/emails/paths, fixture provenance and image metadata.
      Distinguish public client IDs and synthetic examples from credentials.
- [ ] Review screenshots visually/OCR before publication; use disposable
      synthetic data. Keep installed private databases and PasswordVault out
      of scans, fixtures and exported evidence.
- [ ] Review Git attribution and commit messages against the owner's public
      identity policy. Preserve required legal attribution; never silently
      rewrite history or force-push. Any discovered exposed credential requires
      rotation/revocation before removal can be considered sufficient.
- [x] Add appropriate ignore rules for local secret/export files outside known
      output directories without hiding intentional test fixtures or notices.
- [x] Verify the source archive separately from `.gitignore`; define export
      exclusions for local/tooling artifacts where appropriate.

Acceptance: the release tree/history/source archive have reviewed redacted scan
results, fixtures remain synthetic, and every finding has a recorded disposition.

[The source/archive review](dep13-source-privacy-review.md) records 23 passing
ignore checks, exact preservation of all 19 fixtures and legal notices, and
visual/metadata review of all 17 current raster assets. Its reviewed baseline
archive has 444 files and zero Gitleaks findings; only the local `.codegraph`
scaffold is excluded. Owner attribution policy, future screenshot review and
redistribution proof for eight photographic backgrounds remain pending. Final
release-source/archive scans must use the exact eventual release commit.

### DEP-14 — Remove identifying binary and metadata paths

Targets: app/native build configuration, `scripts/Publish-Aot.ps1`, final MSIX,
release summaries, symbols and provenance generators.

- [x] Configure source-path mapping and a supported native linker alternate PDB
      reference policy. Verify actual APIs/properties rather than assuming
      managed `PathMap` alone removes the PE CodeView path.
- [x] Emit relative/sanitized publish locations and reviewed metadata fields.
      Keep private symbols and machine-specific diagnostic data separate.
- [x] Scan both architectures' executables, CodeView records, packaged resources
      and summaries for identifying build paths and synthetic private markers.
- [ ] Inspect final signed package bytes as well as intermediate layouts;
      rebuild/repackage/re-sign after any content change.

Acceptance: both delivered architectures and public metadata pass the privacy
gate. Omitting PDB files alone is not accepted as remediation.

### DEP-15 — Harden and reproduce CI builds

Targets: `.github/workflows/`, dependency update/scanner configuration,
`global.json`, lockfiles/source configuration and CI documentation.

- [x] Keep PR builds secret-free with read-only permissions; avoid executing
      untrusted PR code through privileged events or privileged artifacts.
      Disable persisted checkout credentials where unnecessary.
- [ ] Pin reviewed Actions revisions and automate reviewed updates. Choose a
      Windows runner policy and verify native toolchain components explicitly.
- [x] Define SDK roll-forward and lockfile/locked-restore policy for the
      compatible transitive graph; document package sources and cache boundaries.
- [x] Add dependency updates, redacted secret scanning and direct/transitive
      vulnerability gates with a documented review process for exceptions.
- [x] Require clean warnings-as-errors builds, all four MTP test projects and
      trusted-source x64/ARM64 Release AOT validation on an appropriate cadence.
- [x] Record compiler/SDK/CLI/package versions and source commit. Sanitize TRX,
      coverage/log artifacts; set deliberate retention and upload paths.
- [ ] Verify actual GitHub protection/plan support, required checks and protected
      release refs; record settings evidence without exposing account identities.

Acceptance: a fresh checkout reproduces successful CI with reviewed dependency
resolution, no unresolved scanner findings and no PR access to signing authority.

[The CI guide](../guides/ci.md) records pinned upstream Action revisions,
checksum-verified Gitleaks, safe summaries, native/toolchain evidence and the
weekly/manual trusted-main unsigned AOT matrix. Local script/parser/scanner/
audit checks pass. Twenty checked-in lockfiles cover ordinary and AOT target
graphs; 17 focused checks verify all eight supported restore forms and reject
dependency/hash drift, missing direct/reference locks and locked force-evaluate.
Explicit update mode remains available. SDK runtime downloads and serviced
native tools still need exact candidate evidence. Actual `windows-2025`
execution, native/tool advisory dispositions and required-ref/check enforcement
remain open.
The private repository's protection/ruleset APIs return the recorded HTTP 403
plan limitation; no remote settings or visibility changed.

### DEP-16 — Finalize identity, version and package contents

Targets: `Package.appxmanifest`, app resources/assets, publish profiles,
packaging scripts, inspection scripts and package tests.

- [x] Apply the reserved name/identity/publisher on a separate first-production family.
      Do not uninstall the owner's existing app to test an identity transition.
- [ ] Validate strict tag/version mapping, MSIX component constraints and
      monotonic updates, including earlier test-package versions when identity
      is retained. Check Store version rules if that route is chosen.
- [ ] Build both architectures from one clean reviewed commit. Inspect/hash the
      final package, not an earlier AOT layout later republished by packaging.
      Consolidate duplicate publishing where supported or prove final equivalence.
- [ ] Verify native PE architecture/no CLR header, required Windows App SDK and
      SQLite runtime files, packaged assets and expected self-contained behavior.
- [ ] Verify only reviewed capabilities, current `runFullTrust`, required
      toast/COM/startup/share declarations and absence of unwanted dependencies.
- [ ] Verify no databases, signing keys, traces, fixtures, private symbols or
      identifying metadata entered the package. Define a release file allowlist.
- [ ] Produce sanitized inspection/provenance records, SHA-256 values and an
      SBOM describing the actual shipped dependency/runtime closure.

Acceptance: both candidates share the approved commit/version/identity policy
and pass structural, dependency, capability and privacy inspection.

[The package inspection preparation](dep16-package-inspection.md) supplies a
reusable exact-MSIX structural/privacy gate, 24 passing synthetic regressions
and unsigned-candidate CI integration. Source-manifest equality is checked;
production identity/payload approval, SBOM closure, hosted acceptance and final
signed/deployment evidence remain pending.

[The exact-package SBOM exporter](dep16-sbom-evidence.md) now records both
fresh notice-containing packages' file hashes and original NuGet ownership,
with official CycloneDX 1.6 schema and metadata privacy PASS. All 105 vendor
binary files match source-package bytes. Unverified restored/SDK/build inputs
remain separate metadata evidence; complete linked-code/vendor/projection
closure, independent source authentication and execution attestation remain
pending. Unsigned CI exports this qualified record and checks its privacy.

### DEP-17 — Complete release documentation, assets and sign-in setup

Targets: README, packaging/testing/release guides, legal/privacy documents,
app assets/resources and Microsoft To Do deployment documentation.

- [ ] Add the selected first-party licence and verify third-party notices
      against the actual shipped transitive/native closure. Check image/icon
      provenance; preserve required attribution and permitted redistribution.
- [x] Identify and attribute the retained package icons at all shipped scales
      and target sizes. The [nine source files match the MIT WinUI template](dep13-source-privacy-review.md#2026-11-19--source-of-the-nine-package-icons).
- [ ] Validate final title/tile/splash appearance and the rights to the eight
      in-app background JPEGs. The current package still uses default template
      icons; [ADR-021](../adr/ADR-021-store-package-icon-proposal.md) documents
      the branding risk and the proposed replacement.
- [x] Prepare English screenshots and captions with synthetic tasks, a text-free
      Store hero, listing and certification copy, and a support/security contact.
- [ ] Compare the listing assets with the certified private build before public availability.
- [ ] Publish an accurate privacy policy covering local tasks/account metadata,
      PasswordVault, optional CalDAV, one-way Graph import, attachments,
      diagnostics, deletion/uninstall and the verified telemetry behavior.
- [ ] Document Windows 11 24H2+, architecture selection, signature verification,
      install/update/uninstall, backup/recovery and known/deferred limitations.
- [ ] Verify production Entra public-client registration, PKCE/loopback redirect,
      personal/work-school audience, publisher verification and admin consent.
      Preserve ADR-016's accepted `Tasks.ReadWrite`/`User.Read` scopes and GET-only
      implementation; do not introduce a desktop client secret.
- [ ] Complete work/school sign-in and admin-consent URL verification in an
      authorized disposable session. Keep actual accounts/tokens out of evidence.
- [ ] Reconcile checklist/progress claims using actual results; retain open
      manual and owner-deferred rows rather than copying old PASS claims.

Acceptance: release documentation/assets are complete, privacy-reviewed and
consistent with the delivered behavior and selected distribution route.

[The documentation preparation record](dep17-release-docs.md) links unpublished
[privacy](../releases/PRIVACY.md) and
[installation/update/recovery](../releases/INSTALLATION.md) drafts. Their claims
are tied to current source and explicitly retain unresolved owner fields,
asset/licensing, telemetry-network, sign-in and final-package cleanup evidence.

[Nine whole supplier legal texts are integrated](dep17-notice-integration.md)
with original bytes/attribution intact. [Fresh clean-source x64/ARM64 packages](dep17-notice-package-validation.md)
from `9106040` contain the exact new notices and pass structure, privacy and
layout equivalence. This closes bounded supplier-text inclusion, while selected
first-party licensing, image rights and vendor distribution/end-user conditions
remain pending; it does not mark DEP-17 complete.

### DEP-18 — Add protected Store submission and GitHub release stages

Targets: GitHub workflow files, signing/release scripts and repository settings.

The selected first route is Store MSIX under ADR-019. Its stages are:

| Stage | Trigger/input | Authority | Output |
|---|---|---|---|
| Validate | PR/main source | Read-only, no production credentials | Build/tests, redacted security/dependency results |
| Candidate | Valid protected release tag on reviewed main ancestry | No signing secrets; narrowly scoped provenance permission if used | Inspected unsigned x64/ARM64 packages, sanitized manifest/SBOM |
| Store submission/certification | Approved upload candidates, exact reserved identity and listing | Scoped protected Partner Center session/job; no purchased CA signer | Controlled staged/certified Store result with uploaded/delivered provenance and signed-package verification |
| Draft release | Verified delivered architectures and required gates | `contents: write` only for GitHub draft job; scoped Store authority separately | Reviewed Store publication state and source-release draft with approved metadata/notes; no direct production binaries |
| Publish | Verified Store/source drafts plus explicit owner authorization | Approved release operator/job | Selected Store release and reviewed public source/release metadata |

- [ ] Validate event, tag syntax, source ancestry, version and candidate hashes;
      do not interpolate untrusted ref/body values directly into shell scripts.
- [ ] Verify environment protection works with the repository's GitHub plan.
      Define an equivalent authorized gate if unavailable; never silently weaken it.
- [ ] For the selected Store route, bind exact upload hashes/source to the
      certification/submission and delivered signed package identity/version/
      content. Test the actual signed results before release. Keep Store
      publication controlled and permissions limited to the approved stage.
- [ ] For a separately approved future direct route, prefer supported
      short-lived signing authority. If PFX is selected, use
      protected secrets, ephemeral files, restrictive access and cleanup on every
      outcome. Never upload keys/passwords or trust a dev certificate in CI.
- [ ] For that future direct route, match certificate subject and manifest
      publisher; timestamp direct
      signatures with the approved service and verify signature/trust chains.
- [ ] Limit credential/publication permissions by job and trusted trigger;
      validate downloaded artifact provenance and protect cancellation/retries.
- [ ] For the first Store/source release, attach only reviewed source-release
      metadata, terms, notes and sanitized provenance/SBOM as applicable. Keep
      upload packages private unless separately approved. A future direct route
      may attach approved signed MSIX/bundles and their final SHA-256 values.
      No wholesale `publish/` upload.
- [ ] Support safe reruns without overwriting a published version with different
      code. Compute hashes after signing; verify both architectures before release.
- [ ] Implement the selected Store SDK/Partner Center submission artifact
      and validation path separately; WinApp packaging is not Store submission.

Acceptance: a complete authorized Store preparation/certification path produces
the reviewed publication state and source-release draft, with verified signed
Store results. Negative checks demonstrate that PRs, malformed tags, missing
gates and mismatched artifacts cannot submit or publish. Direct CA/PFX signing
rows apply only if that later route is separately selected; no blocking Store,
provenance, signed-runtime or deployment requirement is removed.

[The unsigned candidate handoff checker](dep18-candidate-handoff.md) now
validates exactly one x64/ARM64 pair and ten hash-pinned inputs, including
actual package-entry privacy evidence and consistent source/tool/graph reports.
The fresh `9106040` pair and 24 negative/positive regressions pass; its sanitized
report also passes privacy. Every result keeps `release_authorized: false` and
ten production/provenance gates pending. This bounded helper does not close
protected-event/ref checks, signer authority, final signed bytes or draft/public
release stages.

[The audit graph-binding follow-up](dep18-audit-graph-binding.md) now records
actual observed HEAD, target and app assets hashes around the no-restore audit.
The stricter handoff requires them to match the toolchain export. Real x64/ARM64
local target audits and 28 handoff regressions pass. Historical unbound reports
must be regenerated; local report agreement is not authenticated build execution.

### DEP-19 — Verify final deployment and functional gates

Use the actual Store-delivered signed candidates; record reviewed upload and
delivered provenance/content, submission/certification IDs, commit, final
SHA-256, architecture,
OS build, dependency/trust baseline, commands, results and redacted evidence.

- [ ] Prepare a separately identified clean test baseline with Developer Mode
      off and no inherited development runtimes or test-certificate trust.
      Provision/install only the dependencies and trust justified by the chosen
      customer distribution path, with session approval.
- [ ] Test installed-package activation, first-run onboarding, task persistence,
      restart, update from the supported previous version and migration recovery.
- [ ] Validate native x64 and ARM64 execution. Cross-compilation and x64
      emulation do not prove ARM64 runtime behavior.
- [ ] Run affected import/sync/attachments/UI gates in the guest, including
      concurrent pull edits, recoverable moves, errors, cancellation and restart.
- [ ] Verify real disposable Radicale and Nextcloud convergence, offline edits,
      unknown properties and all conflict outcomes; exercise the actual chooser
      through guest UI Automation and review the results.
- [ ] Complete M6 external-client import, M7 reboot reminder/visible badge and
      M8 Narrator listening/keyboard checks that automation alone cannot close.
- [ ] Verify toast actions, startup, ShareTarget and normal shell activation in
      the final AOT package; run applicable existing regression gates.
- [ ] Verify uninstall registration/startup/jump-list/integration cleanup and
      app-created files outside package storage. Distinguish intentional exports
      from leftovers; verify documented PasswordVault cleanup/reinstall behavior.
- [ ] Keep M9 performance deferred unless explicitly resumed; measure any new
      performance claims and record environment-specific rows in PERF.
- [ ] Run WinUI code review for UI work and inspect all public captures/results
      for private data. Never upload real user state to satisfy a gate.

Acceptance: all blocking final-package gates pass for each claimed architecture.
Any unavailable, failed, skipped or deferred row remains explicit; do not call
the full release ready while a blocking architecture/deployment gate is open.

[The final deployment gate matrix](dep19-deployment-gate-matrix.md) maps every
requirement to existing assertions and concrete prerequisites. The development
VM/layout runners cannot establish exact signed install/update/uninstall;
those sessions need a separate customer baseline and verified native ARM64
target. Read-only inventory PASS does not close any final runtime gate.

### DEP-20 — Publish approved assets and maintain releases

- [ ] Present the reviewed Store publication state and GitHub source-release
      draft, exact package/provenance evidence and gate results to the
      owner. Obtain publication/submission authorization after preparation.
- [ ] Publish the reviewed Store release and source metadata; verify customer
      listing/publisher/version, Store installation/update/signatures and public
      source/download metadata against the approved results. Direct production
      files are outside this first route.
- [ ] Define withdrawal/replacement and hotfix procedures. Prefer a new higher
      signed version; never replace published bytes under an existing version.
- [ ] Preserve last-known-good artifacts and migration backups. An older
      binary is not a safe rollback if it cannot read the current schema.
- [ ] Establish dependency/Actions advisory monitoring, scheduled trusted AOT
      checks, signing expiry/renewal alerts and subsequent release verification.
- [ ] Publish limitations and security/support reporting instructions; record
      final release evidence in PROGRESS and mark only completed rows done.

Acceptance: the approved Store release is obtainable with the verified identity,
version/signatures and matched source/provenance, public source rights/metadata
are correct,
and recovery, maintenance and signing responsibilities have named owners.

## 6. Verification commands and evidence register

Commands below are for implementation sessions; they were not run by writing
this plan. Follow the current guides if the MTP/CLI syntax changes.

```powershell
dotnet build Wintodo.sln -p:Platform=x64 -warnaserror
dotnet test --solution Wintodo.sln -p:Platform=x64 --no-build `
  --report-trx --coverage --results-directory TestResults
dotnet build Wintodo.sln -c Release -p:Platform=x64 -warnaserror
.\scripts\Publish-Aot.ps1 -Platforms x64,ARM64
.\scripts\Invoke-VmGate.ps1 -Gate run-import-gate.ps1
.\scripts\Invoke-VmGate.ps1 -Gate run-m13-gate.ps1
.\scripts\Invoke-VmGate.ps1 -Gate run-m8-gate.ps1
```

Use additional affected existing gates and focused new regressions, including
Release/AOT guest gates. Development registration through `winapp run` does not
substitute for final installed-MSIX clean-deployment evidence.

| Gate | Required proof | Status |
|---|---|---|
| Fresh-checkout CI | Exact commit; all four test suites; no unexplained skips | Latest remote PASS (`da94519`, GitHub run 42429107588); current source and newer-batch hosted acceptance PENDING |
| Pull concurrency | Newer edits/deletes survive or conflict; no false acknowledgment | PASS (`97e7107`, 33 Sync tests); final-package/live chooser proof remains DEP-19 |
| Synced moves | Correct source/destination membership and retry/restart convergence | AUTOMATED PASS: 71/71; guest UI/live/final packages PENDING |
| Graph origin/CalDAV redirects | Real request boundaries; no foreign token/body disclosure | Graph PASS (`0d5dc94`, 31 destination tests); CalDAV PASS (41 regressions, M13 VM 11/11) |
| Network limits | Declared/unknown/decoded lengths; bounded reads and staging cleanup | PASS (49 new cases; import VM 25/25, Debug/AOT M13 11/11 each); final installed-package gates DEP-19 PENDING |
| Diagnostic privacy | Synthetic private markers absent from all safe output routes | Automated app/CI PASS (Debug import 25/25, Release formatter 9/9 and guest 6/6); final output gates DEP-14/15/19 PENDING |
| Import session and attachment trust | Final dismissal, expiration/resume and provenance policy | Session PASS (11 new unit tests, import VM 25/25); attachment provenance DEP-10 PENDING |
| Schema/update recovery | Supported upgrades/backups; newer-schema rejection | Schema/backup PASS (`015a6a8`, 9 new cases + VM 5/5); final installed-update DEP-19 PENDING |
| Production override policy | Approved Release behavior and broker isolation | PENDING |
| Dependency/AOT security | Fresh resolved graph; reviewed patched compiler/runtime | LOCAL x64/ARM64 AOT/NuGet PASS (`2e149ca`); runner/native review PENDING |
| Repository/history/archive privacy | Redacted scanner findings and reviewed attribution | PRELIMINARY SCANS PASS (`2e149ca`); policy/final review PENDING |
| Binary/public metadata privacy | Both final signed architectures; no identifying build paths | PRE-SIGNING PASS (649 integrated x64/ARM64 AOT files; isolated unsigned x64 MSIX 322 files); final signed x64/ARM64 PENDING |
| Package identity/version/content | Tag mapping, native/runtime/assets/capabilities and SBOM | Structural inspector synthetic PASS (24/24), unsigned CI wired; hosted/final identity/payload/SBOM/deployment PENDING |
| Signing and pipeline protection | Final chain/timestamp; trusted inputs; negative permission tests | PENDING |
| Licence/privacy/assets/support docs | Complete approved customer-facing material | Privacy/installation DRAFTS PREPARED; approval/licence/assets/support/final-package evidence PENDING |
| Work/school import/admin consent | Authorized live verification, sanitized evidence | PENDING |
| x64 clean install/update/uninstall | Exact final hash and clean environment baseline | PENDING |
| ARM64 clean install/update/uninstall | Native ARM64 runtime evidence and exact final hash | PENDING |
| M6 external interoperability | Actual app export opened and checked in another client | PENDING |
| M7 reboot reminder/visible badge | Actual delivery/rendering and persistence | PENDING |
| M8 Narrator/affected UI review | Listening/keyboard workflow and clean review findings | PENDING |
| M13 real servers/conflict chooser | Radicale, Nextcloud, second-client and guest chooser evidence | PENDING |
| AOT Windows integrations | Toast/startup/share/shell regression results | PENDING |
| M9 performance disposition | Existing owner deferral preserved; any resumed measures recorded | OWNER-DEFERRED |
| Publication and maintenance | Owner authorization; downloaded hashes; recovery/signing owners | PENDING |

Initial parallel batch evidence (2026-11-08):

- **Source:** configuration fixes committed as `2e149ca`; tree
  `b68275dacebc6683ac36810f47a01122792715cd` exactly matches the isolated
  build/test candidate. Shared plan/progress edits remain outside that commit.
- **DEP-01 — LOCAL PASS; GitHub CI PENDING:** a fresh Windows checkout with
  `core.autocrlf=true` reproduced the two attachment tests failing (71/73 pass,
  raw fixture 24 to 26 bytes). The narrow `.bin` attribute restored the original
  repository blob and 24 bytes without changing fixtures, production code or
  assertions. `dotnet build Wintodo.sln -p:Platform=x64 -warnaserror` returned
  zero warnings/errors; the four MTP suites passed 392/392, zero skipped
  (Core 212, Data 97, Import 73, Sync 10), with TRX and coverage collected.
  [Sanitized evidence](../../artifacts/dep01/evidence.json) is local and ignored;
  raw logs/results are private. The patch has not been pushed; a new GitHub
  run for this commit remains required before DEP-01 is complete.
- **DEP-02 — LOCAL AOT/NuGet PASS; runner/native review PENDING:** raised the
  SDK minimum to installed stable 10.0.403. Official advisory/release metadata
  confirmed runtime/ILCompiler 10.0.14 fixes GHSA-p3jp-9v83-76w9. Fresh x64 and
  ARM64 restores resolved target compiler/runtime packs to 10.0.14; both
  `Publish-Aot.ps1` runs passed with warnings-as-errors. PE checks confirmed
  the respective native architectures and absent CLR directories. NuGet
  direct/transitive scans of all nine solution projects and separate target
  app graphs reported zero vulnerable packages. Local tools inspected:
  MSVC 14.51.36231, Windows SDK 10.0.26100.0 and restored WinApp 0.7.1.
  [Sanitized evidence](../../artifacts/dep02-toolchain/result.json) includes
  source/tree identity and intermediate executable hashes; raw publish logs
  and path-bearing summaries remain private. Fresh GitHub runner verification
  and comprehensive native/tool advisory review remain pending. These are
  isolated AOT layouts, not final MSIX, signatures or runtime/install evidence.
- **DEP-13 — PRELIMINARY SCANS PASS; policy/final review PENDING:** checksum-
  verified Gitleaks 8.30.1 scanned reachable history, the committed source
  archive and the working tree including the new plan, with zero findings and
  exit 0 in each scope. Reviewed 401 tracked files, 106 reachable commits and
  1,239 blobs; targeted text markers found no credential URLs, private-key
  headers or absolute user paths. Sixteen email markers were classified as
  synthetic/documentation examples; eight JPEGs, eight PNGs and one ICO had
  no identifying metadata in the inspected fields. The archive has 47 agent-
  tooling entries requiring policy review. [Redacted evidence](../../artifacts/dep13/privacy-audit.json)
  is local and ignored. Attribution/licence, archive policy, screenshot
  visual/OCR review and final-release scans remain pending. Private app data,
  PasswordVault, ignored user files and unreachable Git objects were not scanned.

Second parallel batch evidence (2026-11-08):

- **Source:** DEP-03 `97e7107`, DEP-05 `0d5dc94`, DEP-11 `015a6a8`.
  Implementations were tested together before these separate commits; source
  hashes match the committed code. Documentation/evidence updates are separate.
- **DEP-03 — PASS:** all 12 initial paused clean-read races failed against the
  baseline. The guarded writer transaction preserves intervening fields,
  collections, deletes/restores, membership and acknowledged local revisions;
  it returns actual applied counts and complete conflicts. Unresolved changes
  retain the prior sync token for restart. Direct steps survive Keep mine/Keep
  both; Keep theirs soft-deletes/logs the explicitly discarded steps. Chooser
  options default false for ordinary callers. Final Sync suite: 33/33, zero
  skips, including all three choices for clean post-ack and step-only conflicts
  plus restart/second-client simulations. Integration exposed three existing
  undo tests failing because the collection helper cleared automatic transaction
  enlistment; conditional explicit-transaction assignment fixes them. Undo 6/6
  and the full integrated Data suite pass. [Agent evidence](../../artifacts/dep03/evidence.json).
- **DEP-05 — PASS:** seven baseline destination regressions failed; all 31
  destination tests now pass, and Import passes 104/104. Final URI validation
  checks normalized origin, effective port, userinfo and fragments before
  authentication. The production factory disables automatic redirects; ten real
  loopback cases cover same/foreign-origin 301/302/303/307/308 with zero requests
  to a second origin. GET-only behavior, timeout, retries/cancellation and raw
  attachment fallback are retained. [Agent evidence](../../artifacts/dep05/evidence.json).
- **DEP-11 — PASS:** four baseline safety cases failed; all nine new schema
  cases and ten existing migration/list cases pass. Future versions, including
  integers above Int32.MaxValue, are rejected before journal changes. File bytes,
  schema and user rows remain unchanged; failed handles are released. Supported
  versions 1–4 preserve data, backups restore/re-upgrade, and reopening is
  idempotent. Normal launch and ShareTarget map rejection to localized guidance;
  the startup window closes without placement writes. Guest startup gate 5/5
  and its warning capture passed visual review. [Agent evidence](../../artifacts/dep11-schema/evidence.json).
- **Integrated validation — PASS:** Debug/Release x64 builds have zero warnings
  and errors. MTP passes 455/455 with zero skips (Core 212, Data 106, Import 104,
  Sync 33). WintodoTest passes schema 5/5, M13 11/11, import 20/20 and M8 17/17;
  reviewed all generated captures, including four contrast palettes. Vendored
  WinUI code review found nothing above informational. Gitleaks 8.30.1 scanned
  all 410 current tracked/new source files with zero findings. Local redacted
  [integration evidence](../../artifacts/deployment-batch02/evidence.json) links
  the source, counters and private artifact locations.
- **Limits:** the batch has not been republished as Native AOT/MSIX or tested
  as final signed packages. Live Radicale/Nextcloud, the actual conflict chooser
  UI and final deployment gates remain DEP-19 work. DEP-04 synced moves, DEP-06
  CalDAV redirects and the other pending packages remain open. Native/tool
  advisory coverage, owner policy decisions and M9 deferral are unchanged.
  Fresh [GitHub CI run 42428523717](https://github.com/Dvaderfun/wintodo/actions/runs/42428523717)
  passed for `6342f82`; downloaded TRX counters confirm all four suites,
  455/455 passed, zero failed/skipped. Implementation source is committed and
  pushed. DEP-20 release publication remains pending.

Third parallel batch evidence (2026-11-08):

- **Source:** baseline `da94519`; DEP-06/08/09 are integrated in one local
  M10 preparation commit. [Integrated redacted evidence](../../artifacts/dep-batch3/evidence.json)
  records the final source identity and hashes. Raw logs/TRX/coverage remain
  private. Fresh GitHub CI for this batch is pending; earlier CI is not proof
  for the new workflow or source.
- **DEP-06 — PASS:** the real-handler baseline executed 41 regressions:
  37 security assertions failed and four direct/TLS/cancellation controls
  passed. Default 307/308 handling replayed exact task bytes to a second origin;
  Basic headers were dropped, so no password-leak claim is made. The production
  factory disables redirects and every response path rejects 3xx before body
  reads and disposes the response. All 41 cases now pass; loopback and HTTPS
  controls pass. TLS fixtures use disposable keys without certificate trust
  installation. M13 VM 11/11 and five individual captures pass review.
  [Agent evidence](../../artifacts/dep06/evidence.json). Streamed-body limits
  and deadlines remain DEP-07; redirected providers require canonical URLs.
- **DEP-08 — PASS:** nine Core marker/status cases and two real sync
  result/event cases pass; both sync regressions failed against the baseline.
  Raw app exceptions, provider descriptions and native binding tracing are
  replaced or disabled at their output boundaries. Localized retry guidance
  remains. Debug import traces pass their private-marker check. Release
  formatting passes 9/9, and Release JIT guest startup/error/no-file checks
  pass 6/6, with both captures reviewed. CI exports fixed suite names and
  validated counters; actual four-suite 518/518 reports and marker plus five
  invalid-input regressions pass. Those exporter regressions now run in CI.
  IMP-25 is closed after the complete import gate and capture review.
  [Agent evidence](../../artifacts/dep08/evidence.json). External debugger,
  dump/build/raw-test logs remain private; binary metadata and final public
  output verification remain DEP-14/15/18/19, not a claim from these checks.
- **DEP-09 — PASS:** the untouched-client lifetime regression failed at
  runtime; 11 new tests now cover revocation, cancel/wait, late sign-in,
  expiration/fresh transport, callback generations, reset, idempotent teardown,
  owned fixture disposal and real SQLite commit-before-close. The wizard ends
  authenticated connections on success/expiration and clears sensitive state
  on final dismissal/reset/shutdown, awaiting already-started saving. Window
  closing awaits cleanup and safely handles dialog failure. Import VM 25/25
  passes, including idle close, busy cancellation and navigation; all 13 final
  captures pass review. The first run was FAIL 24/25 because the harness omitted
  WinApp's system-key opt-in for guest Alt+F4; after that script-only correction,
  the full rerun passed. A pre-existing stale reconnect hint remains an
  informational capture note. [Agent evidence](../../artifacts/dep09/evidence.json).
- **Integrated validation — PASS:** final Debug/Release x64 builds have zero
  warnings/errors; MTP is 518/518 with zero failures/skips (Core 221, Data 106,
  Import 115, Sync 76). WintodoTest: M13 11/11, import 25/25, M8 17/17,
  Release JIT diagnostics 6/6. All 49 final captures were reviewed individually
  (M13 five, import 13, M8 29, Release two); capture limits remain in the local
  review records. Vendored WinUI review has no issue above informational.
  A staged source-only Gitleaks scan passes; identifying attribution and final
  artifact checks remain their separate packages. No fresh AOT/MSIX, production
  signing, live sign-in/server, final installed-package or M9 perf claim is made.

Fourth parallel batch evidence (2026-11-08):

- **Source:** baseline `79ce9b1` plus reviewed working-file overlays. The final
  local M10 commit, overlay hashes and candidate hashes are recorded in
  [integrated redacted evidence](../../artifacts/dep-batch4/evidence.json).
  Fresh-checkout GitHub CI remains pending; these builds are not a claim of
  reproducible final packages from a clean release commit.
- **DEP-07 — PASS:** untouched Graph/staging regressions failed 14/16 and
  untouched CalDAV regressions failed 7/10. Final suites include 49 new cases:
  Core six, Import 29 and Sync 14. Headers-first bounded streams enforce
  actual/declared/decoded byte budgets and deadlines through retries, bodies,
  XML parsing and background base64 decoding. Staging protects existing files
  and removes newly created partial files; filesystem-denied removal remains
  an explicit limit. Real production-handler loopback tests cover slow, chunked,
  unknown-length and truncated bodies. [Agent evidence](../../artifacts/dep07/evidence.json).
- **DEP-14 — PRE-SIGNING PASS; FINAL SIGNED-PACKAGE PENDING:** Release source
  mapping and the verified native `/PDBALTPATH:Wintodo.pdb` policy fix the
  actual PE CodeView path. Private native/reference symbols remain in build
  outputs; the published payload excludes them. Summaries use relative locations.
  The first symbol-copy attempt exposed four reference PDBs and was repaired
  at the payload seam. Eighteen scanner regressions pass. Both final integrated
  AOT layouts and summary pass across 649 files, zero findings; an isolated
  native-overlay unsigned x64 MSIX and summary pass across 322 files. Vendor
  nonidentifying CodeView paths are counted separately. Final signed x64/ARM64
  bytes must be rescanned after DEP-16/18. [Agent evidence](../../artifacts/dep14/evidence.json).
- **DEP-04 — BASELINE FAIL; DESIGN PROPOSED:** seven deterministic SQLite/
  synthetic-transport regressions fail against unchanged `79ce9b1`, including
  single/bulk, steps, local/cross-account cleanup and restart. No production
  move fix or migration changed. [ADR-017](../adr/ADR-017-recoverable-synced-task-moves.md)
  and [the proposal](dep04-synced-moves.md) await acknowledgement for the next
  batch. Failing regressions stay in ignored scratch. [Agent evidence](../../artifacts/dep04/evidence.json).
- **Integration — PASS:** final Debug/Release x64 builds have zero warnings/
  errors; MTP is 567/567, zero failed/skipped (Core 227, Data 106, Import 144,
  Sync 90). Fresh x64/ARM64 AOT publishes pass. WintodoTest passes import 25/25,
  Debug M13 11/11 and published x64 AOT M13 11/11. All 23 final captures were
  viewed individually (13 import, five each M13 mode), with no new visual
  regression. WinUI review finds nothing above informational in changed code.
  The existing stale reconnect hint and AOT title-bar placeholder remain noted;
  the latter matches the October AOT baseline and final branding is DEP-17.
- **Initial import run — FAIL 23/25:** the rendered removal dialog lacked its
  expected button IDs; a template-application override now sets local IDs without
  replacing styles. A separate launch-readiness failure occurred before sign-in;
  its trace reached interactive and its capture used an old HWND. The harness
  now validates current window/PID ownership and cleans failed launches; four
  mocked lifecycle checks pass. Initial artifact copying failed on an open
  scratch DB; recovery yielded an unreadable structured result, so the original
  console failure is retained. The original window-acquisition trigger remains
  unproven. The complete clean rerun, valid results and all captures pass.
- **Limits:** no production signing, final installed-MSIX, native ARM64 runtime,
  live server/sign-in, source-attribution approval, publication or M9 performance
  claim. DEP-10 attachment provenance and DEP-15 CI hardening are ready next;
  DEP-04 implementation still requires its recorded decision. Raw logs, symbols,
  coverage, scratch data and screenshots remain private/ignored.

Fifth parallel batch evidence (2026-11-08):

- **Source:** baseline `812dd73` plus reviewed working-file overlays. Final
  local commit, source/candidate hashes and safe counters are recorded in
  [integrated redacted evidence](../../artifacts/dep-batch5/evidence.json).
  No push, fresh remote CI or publication is claimed for this batch.
- **DEP-10 — IMPLEMENTED; GUEST ACCEPTANCE PENDING:** six baseline provenance
  assertions failed; the two existing cleanup controls passed. All 11 focused
  regressions now pass, covering stage/final markers and byte fidelity,
  hostile filenames, marking IO/permission failures, owned-file rollback and
  preservation of existing files. The BCL-only helper writes ZoneId 3 without
  source/account metadata. Final copies reapply it; attachment opens retain
  Windows Launcher and request its untrusted warning. No dependency/schema/
  accepted-ADR change. NTFS is exercised; unsupported filesystems are not.
- **Guest preflight — FAIL; UI TESTS NOT RUN:** the focused WintodoTest gate
  stopped while building its test prerequisite. A separate read-only diagnostic
  confirms SDK 10.0.401 is installed, but the requested minimum is 10.0.403.
  The guest remains unchanged and powered off. Opening, delete/restart,
  failed-wizard-import, screenshots and the existing import gate remain
  pending after owner-invoked setup; no previous PASS is reused for this source.
- **DEP-15 — LOCAL PASS; FULL ACCEPTANCE PENDING:** independent review verified
  four Action commit pins and the Gitleaks 8.30.1 ZIP digest against upstream.
  Actionlint/PowerShell syntax and 27 scanner/privacy regressions pass. Local
  reachable-history/source-archive scans have zero findings. Solution/app
  direct/transitive audits pass with zero advisories; sanitized SDK/native/CLI
  and target-specific AOT/compiler/runtime evidence passes. Workflow audits
  the final restored graph after packaging, checks exact unsigned bytes before
  explicit 14-day uploads, and gives PR code no signing authority. Initial
  concurrent audit output contention was fixed with per-run private folders.
  Fresh remote CI, lockfiles, native/tool advisory review and the HTTP 403
  protection-plan decision remain pending.
- **DEP-12 — INVENTORY COMPLETE; POLICY PENDING:** the concrete proposal lists
  ordinary Debug/Release/AOT and ShareTarget behavior, source evidence and gate
  dependencies. Recommended policy retains/documents established overrides;
  the alternative uses separate test builds. Database overrides move SQLite
  only; shares use LocalState and real time in both configurations. All 26
  proposal links resolve. No production behavior or policy changed.
- **Host integration — PASS:** Debug/Release x64 builds have zero warnings/
  errors. All four MTP suites pass 578/578, zero failed/skipped (Core 227,
  Data 106, Import 155, Sync 90). Fresh x64/ARM64 AOT publishes pass; both
  layouts and relative summary pass the privacy scan across 649 files, zero
  findings. Existing safe-TRX marker/invalid-input regressions pass. Changed
  attachment code has no WinUI review issue above informational. AOT runtime
  UI, final signed/installed packages, ARM64 runtime, live services, M9 perf,
  owner policy decisions and publication remain pending.

Sixth parallel batch evidence (2026-11-08):

- **Source:** baseline `7d2fa21` plus reviewed sixth-batch overlays. The final
  local source commit, reviewed tree and changed-file hashes are recorded in
  [integrated redacted evidence](../../artifacts/dep-batch6/evidence.json).
- **DEP-15 — LOCKED RESTORE/LOCAL PASS:** twenty graph-specific NuGet locks
  preserve ordinary and AOT dependencies across x64/ARM64. Seventeen focused
  checks pass, including eight supported restore forms, unchanged lock hashes,
  rejected requested-version/content-hash drift, missing direct/reference locks,
  locked force-evaluate, explicit update mode and unreviewed RID rejection.
  The initial shared-RID lock failed `NU1004`; separate target graphs fix it.
  NuGet initially created a missing lock despite locked mode; `WTDLOCK001`
  now rejects it before generation, and `WTDLOCK002` requires explicit update
  mode for force-evaluate. Actionlint/XML review passes. SDK download/runtime
  dependencies and serviced native tools remain outside full lock guarantees.
- **DEP-13 — INDEPENDENT CHECKS PASS:** 23 ignore expectations pass; reviewed
  baseline archive preserves all 19 fixtures and required notices exactly.
  The raw attachment blob/archive/checkout hashes agree. All 17 raster assets
  pass bounded visual/metadata privacy review; automated OCR was unavailable,
  and no screenshot is currently tracked. Attribution and eight photographic
  backgrounds' redistribution rights remain pending. Exact final-release source,
  future screenshots and signed packages require their own checks.
- **DEP-02 — REVIEWED WITH LIMITS:** all 103 root AOT payload DLLs match restored
  package/runtime-MSIX content. Actual SQLite `3.53.4` matches its upstream
  source identity and vendor patch records. WinApp's HarfBuzz `14.2.0` is in
  the upstream advisory range; native-SDK project packaging has no direct
  subsetting calls in the bounded review, but complete applicability remains
  unproven. No stable patched release was identified. CLI embedded-runtime
  provenance and exact native vendor mappings remain pending. Hosted Visual
  Studio inventory maps to patched `17.14.44`; actual runner evidence remains
  required. Local `18.8.0` has a servicing/coverage gap, without a proven
  compiler vulnerability. Native/licensing findings are in the linked review.
- **Host integration — PASS:** final Debug/Release x64 warnings-as-errors builds
  have zero warnings/errors. All four MTP suites pass 578/578, zero failed/skipped
  (Core 227, Data 106, Import 155, Sync 90). All 27 scanner/privacy regressions
  pass, and the final solution NuGet audit covers nine projects with zero
  advisories. Changed build/CI/export rules pass focused review; no product
  XAML/C# changed. Integrated source/archive scan results are recorded in the
  local evidence: the canonical staged archive has 468 files, all 19 fixtures
  and notices match Git bytes, and Gitleaks finds zero secrets. Archive proof
  explicitly disables checkout line-ending conversion without changing global
  Git settings. No new AOT publish, UI gate or final signed package is claimed.
- **Remote/owner limits:** latest passing GitHub run `42429107588` is for
  `da94519`, before the recent fixes and this batch. Both protection/ruleset
  reads still return HTTP 403 with the plan-support requirement. Fresh CI,
  enforcement, ADR-017 acknowledgement, DEP-12 policy, guest SDK setup,
  identity/signing/licensing choices and final deployment remain pending.
  No remote setting, machine setup, certificate trust or publication changed.

DEP-04 implementation batch evidence (2026-11-08):

- **Decision/source:** owner acknowledgement of ADR-017 is recorded. Baseline
  `3615b31` plus reviewed Core/Data/Sync/App/test overlays; final local commit,
  source hashes, architecture hashes and redacted counters are in
  [integration evidence](../../artifacts/dep04-integration/evidence.json).
- **Data/Sync — AUTOMATED PASS:** migration 005 records durable grouped moves;
  atomic attempt/phase/restore/completion transactions retain source identity,
  payloads, ETags and acknowledgement boundaries. Source credentials require
  approved binding proof. Conditional target creation/recovery precedes source
  cleanup; later edits, deletes, restores, moves and undo remain guarded.
  Source tombstones and held known versions have internal provenance metadata.
  All three conflict choices preserve the intended versions and subtree scope.
- **Regression proof — PASS:** independent 71-case SQLite/CalDAV matrix passes
  without skips, including lost responses, restart, both account/collection
  orders, foreign/altered same-UID resources, parent/step choice handling,
  prospective collections and restoration during cleanup. Source baseline was
  3/19 passed and 16 failed; intermediate 38/51, 50/51 and 57/62 results remain
  preserved. Focused Data is 23/23 and Core serialization is 4/4. A newly authored
  missing-note/due/importance case failed before the move-only writer fix.
- **Integration — PASS:** Debug/Release x64 builds have zero warnings/errors.
  Full MTP passes 678/678, zero failed/skipped (Core 231, Data 130, Import 155,
  Sync 162). Initial integrated 673/677 is retained: ordinary deleted-parent
  KeepBoth now retains the original step UID, and move races assert durable
  deferral instead of a stale generic chooser while retaining data/token checks.
  Generic cached choices require fresh remote verification; a new offline
  regression proves no copy/overwrite on verification failure. All 27 script
  regressions pass. Final nine-project NuGet audit has zero advisories.
- **Native AOT/privacy — PASS:** fresh x64/ARM64 publishes use runtime/compiler
  10.0.14; both layouts and relative summary pass across 649 files, zero findings.
  These are local unsigned layouts, not final signed installation candidates.
- **Review/limits:** changed UI paths use existing commands, dialogs, AutomationIds
  and localized guidance; focused WinUI review has no issue above informational.
  Schema-6 seed generation preserves the earlier seed, and both gate scripts
  parse cleanly. WintodoTest UI/visual gates remain pending on its SDK 10.0.401;
  no guest setup or host UI automation occurred. Live providers, final installed
  packages, native advisory/provenance dispositions, other owner decisions and
  publication remain pending. No remote settings or publication changed.

Parallel release-preparation follow-up evidence (2026-11-08):

- **Source:** clean baseline `4fa223d` plus reviewed tooling/documentation
  overlays. The local integration commit, changed-file hashes and safe counters
  are recorded in [integration evidence](../../artifacts/dep-next/evidence.json).
  No product code, package references, lockfiles or accepted policy changed.
- **DEP-02 — FOLLOW-UP REVIEWED; ACCEPTANCE PENDING:** pinned WinApp native-SDK
  unsigned packaging delegates to `dotnet publish` without font-generation or
  SVG-rendering calls in the reviewed path. Matching Svg.Skia/HarfBuzz binding
  source maps shaping/variation calls; the recommended bounded advisory
  disposition remains owner-pending. Stable WinApp 0.8.0 includes signing/PKCS
  fixes but retains the same graphics versions. Embedded runtime/compiler
  provenance and native mappings remain unverified; no CLI upgrade was applied.
- **DEP-16 — STRUCTURAL PREPARATION PASS:** 24 synthetic regressions verify
  required native/runtime/assets, source-manifest expectations, declarations,
  malformed ZIP/XML, sensitive payload rejection, hashes and report integrity.
  Independent review found and verified fixes for candidate-output aliases,
  four malformed-manifest false passes and stale PASS reports after failure.
  A legacy x64 package passes structural checks and correctly fails its two
  known binary-privacy findings; it is unchanged and predates current source.
  The current CI invocation passes with a synthetic MSIX. Actual hosted CI,
  approved identity/payload, full schema/signature/runtime and final packages
  remain pending; vendor resource architectures are counted separately.
- **DEP-17 — DRAFTS PREPARED:** source-backed privacy/installation/recovery
  drafts retain their unpublished status and outstanding decision fields.
  All 40 local links across the four new preparation documents resolve; draft
  paths are tracked rather than hidden by the generic Release-output ignore.
- **Integration — PASS:** Debug and Release x64 warnings-as-errors builds have
  zero warnings/errors; MTP passes 678/678 with zero failed/skipped. All 51
  script regressions pass (24 package, 27 existing scanner/privacy checks).
  Actionlint passes. Raw logs and synthetic packages remain private/ignored.
- **Limits:** latest remote CI still covers `da94519`, before the newer local
  fixes. No push, remote settings, machine setup, UI/AOT publish, certificate
  trust, signing, install/update/uninstall, live sign-in/server or publication
  was performed. DEP-12 and distribution choices remain pending; M9 stays deferred.

Clean-source candidate/toolchain/notices batch evidence (2026-11-08):

- **Source:** both unsigned candidates use exact clean source `965d86b`, archive
  SHA-256 `4f5ac51702027cc744d8fec6620cd1cedd1bc32161aa7ef70bf0e8232bb0b7a0`.
  All 495 archived source files and 20 reviewed locks remained unchanged. The
  current integration adds only exporter/workflow/testing/documentation changes.
- **DEP-16 — LOCAL UNSIGNED PASS:** target locked restores, warnings-as-errors
  x64/ARM64 AOT publishes, repaired SDK-native packaging, exact-MSIX structure/
  privacy and final app NuGet audits pass. Packages have 321/317 files; all
  311/307 files shared with their respective AOT layouts match byte-for-byte.
  Package SHA-256 values and source/runtime limits are in
  [the candidate validation](dep16-local-candidate-validation.md).
  Original `WTDLOCK001` failure remains preserved; no lock guard was weakened.
- **DEP-02/15 — EXACT METADATA LOCAL PASS:** seven new synthetic tests and both
  ordinary x64/ARM64 exporter runs pass. Binary numeric versions/hashes distinguish
  available MSVC/installed SDK tools from MSBuild-evaluated restored packaging
  tools. [The tool evidence](dep15-exact-toolchain-evidence.md) retains actual
  invocation, embedded-runtime/advisory and hosted-runner limitations.
- **DEP-17 — NOTICE ADDITIONS PREPARED:** all 103 DLLs and both vendor restart
  executables match reviewed package sources. Both MSIX files contain the same
  unchanged 38,748-byte current notice. The
  [notice review](dep17-third-party-notices-review.md) identifies missing inherited
  .NET/Windows App SDK/ML/WebView2 texts and prepares nine deduplicated additions
  (856,256 bytes), with source hashes and conditional retained-code scope.
  Proposed additions pass privacy scanning; they are not integrated or approved
  as a complete legal/SBOM closure.
- **Integration — PASS:** Debug x64 build has zero warnings/errors; all four
  MTP suites pass 678/678, zero failed/skipped. All 58 Python-discovered script
  tests, Actionlint, exporter parsing and local document-link checks pass.
  Independent review corrected local per-architecture restore/package ordering.
  Private evidence is `artifacts/dep-next-integration/evidence.json` and the
  three separate agent evidence directories; raw logs remain ignored.
- **Pending:** current remote main/latest CI remains `da945199` (run
  `42429107588`), before this reviewed source. DEP-12/distribution/identity/
  first-party licensing/signing choices, notice integration, image rights,
  full SBOM, native advisory/invocation provenance, guest SDK setup and exact
  signed-package/live/native-ARM64 gates remain open. No push, remote settings,
  machine setup, certificate trust, UI automation, signing, install or
  publication occurred. M9 stays owner-deferred.

### 2026-11-08 — Notice integration, package SBOM and unsigned handoff batch

- **DEP-17 bounded notice inclusion — PASS:** nine original supplier legal
  bodies and the prior Windows notice prefix remain intact. New clean-source
  `9106040bc1eb460c1cd74fa7122d95460aa38a80` x64/ARM64 packages contain the exact
  898,198-byte notice file, SHA-256
  `ba042235866020798b50fcc24483abaea5a61c827340ca8db943a6819cac98fa`.
  [The package record](dep17-notice-package-validation.md) confirms all 500
  archived files/20 locks unchanged, warnings-as-errors AOT/package success,
  actual structure/privacy PASS and 311/307 common-layout matches.
- **Exact unsigned package hashes:** x64
  `4a5d345feb0b4a2e2bac454809436c9bacecf754f67dee5afad683bc2c247d5f`;
  ARM64 `3c0b688ec46288c5bedcc1c182a3dc0815535c1baf32fca18fa58e3e1be8405c`.
  Whole-package/summary privacy covers 322/318 files, zero findings;
  app-only NuGet audits have zero vulnerable packages/advisories.
- **DEP-16 bounded SBOM — PASS, full closure INCOMPLETE:**
  [the exporter](dep16-sbom-evidence.md) hash-matches all 105 vendor binary
  files to original package bytes. Both BOMs pass official CycloneDX 1.6
  schema/privacy checks. Unverified restored/SDK/build inputs are metadata
  evidence, not declared runtime dependencies. Signed NuGet raw/normalized
  hashes bind through qualified local metadata; upstream signatures and full
  retained-code/vendor/projection closure are not established. Twenty-two
  regressions pass. CI exports this qualified evidence without signing authority.
- **DEP-18 bounded unsigned handoff — PASS:**
  [the checker](dep18-candidate-handoff.md) pins both MSIX and eight reports,
  recomputes actual entry hashes and compares source/report/identity consistency.
  Fresh-pair and 24 regressions pass; report privacy is clear. It always leaves
  `release_authorized: false` and ten production/provenance gates pending.
- **Integration — PASS:** Debug x64 solution build zero warnings/errors;
  four MTP suites 678/678, zero failed/skipped; full Python discovery 104/104;
  Actionlint, whitespace and local link checks pass. Vendored WinUI review is
  scoped to unchanged UI/runtime code and the inherited static legal asset;
  no issue above informational. No affected UI behavior was introduced.
- **Private evidence:** `artifacts/dep-notice-integration/`,
  `artifacts/dep-notice-candidates/`, `artifacts/dep-sbom/`,
  `artifacts/dep18-handoff/` and integrated review records. Raw native/restore
  output, private assets graphs and cache inputs remain outside release uploads.
- **Still pending:** DEP-12 policy, distribution/identity/version/signing,
  first-party/image rights and public attribution/contact, vendor legal terms,
  complete SBOM/provenance/native advisory disposition, current hosted CI and
  protection, owner-invoked guest SDK setup, final signed/deployment/live/ARM64
  runtime gates and explicit publication. M9 stays owner-deferred. No remote
  setting, machine setup, host UI, certificate trust, signing, installation,
  draft release or publication occurred.

### 2026-11-09 — Audit graph binding, native closure and deployment gate inventory

- **DEP-15/18 bounded binding — LOCAL PASS:** target app audits observe HEAD
  and exact assets bytes around the no-restore NuGet command and publish their
  safe summary only after stable binding checks. Tool exports hash the exact
  parsed assets bytes; handoff rejects missing or mismatched bindings. The
  unsigned CI matrix supplies each architecture and privacy-checks the audit.
  Thirteen wrapper, 28 handoff and ten toolchain regressions pass. Legacy
  unbound evidence remains historical and cannot satisfy the new handoff.
- **Actual targets — LOCAL PASS:** matching locked x64/ARM64 AOT restores,
  bound app audits and tool exports have zero NuGet vulnerabilities/advisories
  and agreeing source/target/graph hashes. Observed HEAD is
  `8d54cc15a2b2bd0e2e2a8cf847942ebb1ca73145` with this reviewed script overlay;
  no candidate was rebuilt. Assets hashes are
  `eaeee37da8d910384573bfa160ed5cd38cc4b698a41b8dfb6164a0308852cadf` (x64),
  `f23ba2044d2349508413c00c0e6ad9c2139d7bd8140d586eb7388d1f5877e01b` (ARM64).
  Ordinary x64 development restore was restored afterward; locks are unchanged.
- **DEP-02/16 retained-code follow-up — REVIEWED:** private PDB GUID/age match
  exact `9106040` MSIX executables on both architectures and address-bearing
  symbols identify retained SQLite/Toolkit/WinRT/projection/runtime code.
  Primary advisory refresh is recorded. ONNX suffix and DirectML same-version
  binaries do not establish vendor build/patch equivalence. Full closure stays
  INCOMPLETE and WinApp runtime/vendor/HarfBuzz disposition stays PENDING.
- **DEP-19 inventory — PASS, final gates PENDING:** every requirement now maps
  to an existing assertion or explicit installed-package gap. Metadata-only VM
  checks show the development VM Off with its existing clean checkpoint.
  Final signed install/update/uninstall and native ARM64 need their own
  baseline/target; guest SDK preflight and manual/live rows remain pending.
- **Integration — PASS with initial failure retained:** Debug x64 build zero
  warnings/errors; final script discovery 124/124; Actionlint and link/whitespace
  checks pass. Initial MTP run was 677/678 with a disposed SQLite handle during
  DefaultList fixture startup. The affected test passes alone and a full rerun
  passes 678/678, zero failed/skipped. No runtime change was made for that
  nonreproduced failure; its cause is not established.
- **Evidence and boundaries:** new linked review records plus private
  `artifacts/dep-audit-binding-integration/`,
  `artifacts/dep-native-closure-followup/`, `artifacts/dep-audit-binding/` and
  `artifacts/dep19-gate-inventory/`. Read-only GitHub checks still show latest CI
  at `da945199`, not current source. Owner decisions, hosted/protected execution,
  signing, fresh bound candidate pair and final deployment remain pending.
  No guest start/provisioning, host UI, trust, installation, remote write,
  signing or publication occurred; M9 remains owner-deferred.

### 2026-11-09 — Fresh bound candidates, retained-code exporter and installed smoke

- **DEP-16/18 fresh unsigned pair — LOCAL PASS:** clean source
  `f1b9af570882ba67ef7ccecf18d7c47321a9e366` produces both warnings-as-errors
  AOT/MSIX targets. Exact structure/source manifest, layout/package/public
  privacy, notices, bound app audits/tool graphs and qualified SBOM/schema
  checks pass. All 511 archived files and 20 locks remain byte-identical;
  common layout/package files match 311/311 x64 and 307/307 ARM64. The current
  hash-pinned handoff passes with release authorization false and nine pending
  production/provenance gates. Exact hashes are in
  [the bound candidate record](dep16-bound-candidate-validation.md).
- **DEP-02/16 reproducible observations — LOCAL PASS:** the
  [retained-code exporter](dep16-retained-code-evidence.md) passes 16 focused
  regressions and both historical/fresh architecture pairs. MSIX/EXE/PDB hashes,
  CodeView GUID/age, DBI machine and final-section mapping fields agree. Fixed
  family observations, unknown/unmapped record counts and sampled code hashes
  remain qualified; absent symbols never prove removal. Full dependency
  closure stays INCOMPLETE and vendor/version/advisory/attestation gates stay
  pending. Four reports plus the review document pass privacy, zero findings.
- **DEP-19 runner — PREPARED, runtime PENDING:** the
  [installed-smoke runner](dep19-installed-package-smoke.md) defaults to
  guest-only preflight and checks signature, registration and exact executable/
  process association before explicit automation. Twelve mocked/static checks
  pass; actual guest metadata, Windows APIs, UI and visual results are pending.
  It does not install, activate, update or uninstall. Early mocked tests ran
  two process-only host UI opt-in assignments contrary to policy. Both were
  removed; corrected tests never set the opt-in and no actual UI command ran.
- **Integration — PASS:** Debug x64 build has zero warnings/errors; all four
  MTP suites pass 678/678, zero failed/skipped; Python script discovery passes
  152/152. Review found no remaining issue above informational in the added
  tooling; no app XAML/C# changed. Public-document/privacy/link/whitespace
  checks are recorded in the integration evidence.
- **Limits and evidence:** private evidence is in
  `artifacts/dep-bound-candidates/`, `artifacts/dep-retained-code-export/`,
  `artifacts/dep-installed-smoke/` and `artifacts/dep-next-integration/`.
  Read-only GitHub checks still show latest CI/main at `da945199`; protection
  is unavailable under the current private-repository plan. No guest start/
  provisioning, actual host UI, installation, trust, signing, remote write or
  publication occurred. Production decisions, vendor/advisory/full closure,
  hosted/protected execution, final signed/deployment/native ARM64 runtime
  acceptance remain pending; M9 remains owner-deferred.

### 2026-11-09 — Original archive signatures, signing continuity and owner decisions

- **DEP-02/16 original archives — LOCAL PASS:** all 45 unique original NuGet
  archives in the two fresh `f1b9af5` BOM inventories pass signature verification,
  have repository signatures and no NuGet diagnostic codes, and preserve exact
  raw SHA-256 values matching those inventories. Both BOM inputs remain unchanged.
  Verification uses existing host NuGet trust/settings; it does not attest
  embedded vendor builds or complete retention/patch/licence closure. See
  [the native closure next-actions record](dep02-native-closure-next-actions.md).
- **DEP-18 signed payload continuity — PREPARED / LOCAL PASS:** the new read-only
  checker and 19 synthetic regressions preserve every original entry except
  semantic content-type updates, allowing only the exact signature/optional CI
  catalog additions. A historical test-signed ARM64 pair passes (316 original
  entries byte-identical); the historical x64 filenames fail because the alleged
  unsigned input already contains a signature. No files are replaced. This is
  historical tool qualification, not current production/signature/runtime
  acceptance. See [the continuity record](dep18-signed-package-continuity.md).
- **Owner decisions — PROPOSAL / PENDING:**
  [the seven-choice packet](deployment-owner-decisions.md) provides reviewable
  route/visibility, branding/rights, identity/data continuity, version, signing,
  legal attribution/contact and production-override options. No recommendation
  is adopted. Vendor evidence requests are prepared; no external contact occurs.
- **Integration — PASS:** against `0b7272f` with the reviewed new tooling/doc
  overlay, Debug x64 build has zero warnings/errors, all four MTP suites pass
  678/678 with zero failed/skipped, and full Python discovery passes 171/171.
  Tooling review, public-document/privacy, links and whitespace checks pass.
  No app XAML/C# or dependency selection changes.
- **Evidence and remaining gates:** ignored `artifacts/dep-native-next-actions/`,
  `artifacts/dep-signed-continuity/` and `artifacts/dep-next-release-integration/`.
  Read-only GitHub checks still show latest hosted CI at `da945199`, not local
  HEAD. Owner choices, native/vendor/advisory closure, hosted/protected execution,
  signing, actual installed update/uninstall/native ARM64 and publication remain
  pending. No guest start/provisioning, host UI, installation, certificate trust,
  signing or remote write occurred. M9 remains owner-deferred.

### 2026-11-09 — Selected launch policy and strict release-ref preparation

- **Decisions — SELECTED:** ADR-018 records direct MSIX first; DEP-12 A retains
  established local controls. Exact identity/publisher, first version, signer,
  legal/contact/asset rights and visibility/protection values remain pending.
- **DEP-12 host policy — PASS / runtime PENDING:** the platform-free helper
  preserves argument parsing, lazy database precedence, onboarding short circuit
  and real/file clock selection. Twenty-six linked-production-source tests pass.
  App retains Debug notification and fixture/trace boundaries; broker shares
  are untouched. Default Windows/guest/Release/AOT/final-package rows stay pending.
- **DEP-16/18 ref gate — LOCAL PASS / WIRED:** 31 real-Git tests pass for exact
  source/main/tag/HEAD/ancestry and manifest/version bindings, ordinary clean
  index, line-ending preservation, negative events/refs/history and safe reports.
  Tag CI requires caller-supplied `RELEASE_REVIEWED_MAIN_SHA`; optional prior
  version checking does not close unsupported-update decisions. Full history,
  manifest LF and explicit policy/privacy uploads are wired. No repository
  variable, protection, tag, signing or release is created.
- **Integration — PASS:** `db6c448` plus this reviewed phase builds Debug and
  Release x64 with zero warnings/errors. All 704 MTP tests pass, zero failed/
  skipped; all 202 Python script tests pass. Actionlint, document links/whitespace
  and independent review pass. Evidence is ignored under
  `artifacts/dep12-policy/`, `artifacts/dep-release-ref-policy/` and
  `artifacts/dep-production-next-integration/`.
- **Separate evaluation — IN PROGRESS:** new vendor evidence and a compatible
  current ML package are being checked in an isolated `db6c448` source. Root
  dependency/lock/notice payloads are unchanged in this policy/ref commit. These
  results do not close hosted/protected execution, vendor/legal closure, actual
  guest/AOT runtime, production signing or final deployment. M9 remains deferred.

### 2026-11-09 — Supported ML servicing and SQLite fixture isolation

- **DEP-02/16 primary vendor evidence — ESTABLISHED:** the digest-matched
  official WinApp 0.7.1 release ZIP contains the exact reviewed executable.
  Microsoft explicitly associates ML 2.1.74 with ORT 1.24.6/source `800ac32`.
  Its current-support policy identifies ML 2.5.96 as the supported servicing
  choice within the existing SDK 2.5.1 dependency range. Exact embedded build/
  backport/DirectML/runtime attestations remain pending. See
  [the vendor/servicing record](dep02-vendor-provenance-resolution.md).
- **ML 2.5.96 — INTEGRATED / LOCAL PASS:** the isolated `db6c448` overlay passes
  Debug, 678 unit tests, both warnings-as-errors AOT/MSIX targets, exact structure/
  privacy and bound zero-advisory app audits, and four locked restore shapes.
  Integration after `703757b` changes only the intended central/direct reference,
  four app locks and seven notice-origin labels. All 16 other locks and inherited
  legal-text bodies remain exact. Current-helper Debug/Release builds and both
  AOT publishes pass; these are integration outputs, not final production packages.
  Their layout/summary privacy scan passes 649 files with zero findings. Locked
  restores rewrote line endings; after complete graph comparisons, 16 unchanged
  locks and the four reviewed app-lock byte records are restored exactly.
- **DEP-15 initial unit failure — FAIL, diagnosis/fix/rerun PASS:** the first
  updated-source unit run was 703/704 with a disposed SQLite handle in TaskBatch
  Delete's helper query. The isolated class passed 17/17. Exact vendor source,
  a public-API stress probe (8 pooled query failures; unpooled control 0) and a
  failing actual-fixture isolation check establish a reachable global-cleanup
  mechanism. Five fixture cleanup calls now target only owned pools; 14 helpers
  are unpooled. Three deterministic regressions and Data 133/133 pass. The original
  interleaving is unrecorded; production pooling/provider/timer behavior is not
  changed or declared fixed. See [the isolation record](dep15-sqlite-test-pool-isolation.md).
- **Integration — PASS:** final Debug/Release x64 builds have zero warnings/
  errors; all 707 MTP tests pass, zero failed/skipped; all 202 script tests pass.
  Review, Actionlint, links/whitespace and public-doc privacy checks pass. Ignored
  evidence: `artifacts/dep-vendor-resolution/`, `artifacts/dep-ml-upgrade-evaluation/`,
  `artifacts/dep-sqlite-pool-investigation/`, `artifacts/dep-production-next-integration/`.
- **Limits:** prior clean bound candidates retain their historical status; a
  final reviewed-source candidate/SBOM must be regenerated after approved identity/
  version/signing inputs. Current hosted/protected execution, owner fields,
  vendor/legal closure, actual guest/native runtime, final signing/deployment and
  publication remain pending. No signing, trust or guest/UI action ran. M9 stays
  owner-deferred; remote CI verification follows the reviewed integration commit.

### 2026-11-09 — First hosted run and Windows path-spelling correction

- **Hosted push CI — FAIL:** run `42536237534` at `8e562d8` failed exactly one
  of 202 script tests, the safe report/stale-PASS case. Build, restore/audit and
  unit stages were skipped; test export/upload also failed because unit evidence
  was absent. None of those skipped stages is reclassified as passing.
- **Cause and correction — LOCAL PASS:** a real Windows short-name alias of
  the same disposable Git directory reproduced the output path's lexical mismatch
  with the canonical repository. Directory identity now establishes the root;
  raw traversal/link/junction checks precede canonical containment and ignored/
  tracked/hard-link validation. Existing-leaf and extended-length aliases are
  covered. Root/app/SDK/identity/signing behavior does not change.
- **Verification — PASS:** 33 focused and 204 full script tests pass on Python
  3.13.14 with zero skips; privacy/review/link checks pass. The corrected source
  will run full hosted main maintenance, including unsigned x64/ARM64 AOT targets.
  Actual hosted/protected acceptance remains pending until its results exist.
- **Evidence:** ignored `artifacts/dep-release-ref-policy/alias-reproduction.json`,
  `alias-final-313.log`, and `artifacts/dep-production-next-integration/` contain
  the original hosted failure and successful local rerun. Local Debug/Release,
  707 unit tests and both AOT/privacy checks remain valid; no app source changed.

### 2026-11-09 — Hosted maintenance and downloaded unsigned pair

- **Hosted — PASS:** manual main run `42537269138` at exact source
  `c5610bbb8f44c23c6fb6ceb6865389aca021b134` passes build/security, all 204
  scripts, all 707 unit tests (zero failed/skipped/unresolved), and both
  warnings-as-errors AOT/MSIX structure/privacy/audit/dependency-export jobs.
- **Downloaded evidence — PASS:** both hash-pinned artifacts agree with source,
  requested architectures and bound app/tool graphs. The current unsigned handoff
  passes with release authorization false and nine gates pending. Qualified BOMs
  pass the official CycloneDX 1.6 schema, zero errors; evidence privacy passes.
  Exact package/graph/BOM hashes are in
  [the hosted candidate record](dep16-hosted-candidate-validation.md).
- **Limits — PENDING:** the maintenance trigger skips protected-tag eligibility;
  existing placeholder identity/version, production signer, legal/asset values,
  actual protection, full vendor/linked/projection closure and signed installed
  deployment/native ARM64 runtime acceptance remain open. GitHub's forced action
  runtime/deprecation notice is retained for maintenance. Initial failed hosted
  run `42536237534` remains FAIL, with its skipped stages explicit. M9 is deferred.
- **Evidence:** ignored `artifacts/dep-production-next-integration/hosted-42537269138/`.
  Source commits were pushed only to existing private main; no repository visibility,
  tag/protection/signing setting, publication, guest, certificate trust or UI changed.

### 2026-11-09 — Owner production choices and signing/licensing research

- **DECISIONS RECORDED:** retain Wintodo/current artwork; public source after
  privacy/attribution/licensing review; fresh first production installation;
  future updates preserve user data; stable tag mapping with first release
  `v1.0.0` / `1.0.0.0` on a new production family. The test family `1.0.0.5`
  remains separate; no manifest, package or installed profile changed.
- **RESEARCH PREPARED:** [the signing/licensing recommendation](deployment-signing-licensing-recommendation.md)
  uses current Microsoft and licence-author sources. Store-managed MSIX signing,
  Artifact Signing and commercial CA options are compared. GPLv3, MIT and
  source-available terms distinguish commercial fork rights while supporting
  the owner's freemium/free-personal-build objective.
- **DOCUMENTATION — PASS:** seven affected documents, 173 relative local links,
  balanced code fences and whitespace checks pass. No build/unit/UI gate was
  rerun for these documentation-only changes.
- **LIMITS — PENDING:** exact legal publisher spelling/identity, first-party
  ownership/licence/contact, retained artwork rights and selected signer remain
  unresolved. Store-first is a proposal requiring a new acknowledged ADR;
  accepted direct-MSIX-first ADR-018 is unchanged. No licence, billing, Store
  reservation, remote protection/visibility, signing, trust, guest/UI or
  publication action ran. All existing final-package gates and deferred M9
  retain their dispositions. Baseline source: `44a4ef6`.

### 2026-11-09 — Store-first acknowledgement, publisher/contact and source policy

- **DECISIONS RECORDED:** explicit Store-first acknowledgement, Univera LLC as
  official publisher, support@univera.app contact and reserved commercial
  redistribution with free self-builds. ADR-019 supersedes ADR-018's first
  route only; DEP-12 A and all blocking release/runtime/privacy gates remain.
- **PREPARATION COMPLETE:** [Store-first handoff](deployment-store-first-preparation.md)
  names the exact Partner Center identity fields, actual signed delivery/update
  evidence and staged publication prerequisites. [The source-available draft](wintodo-source-available-license-draft.md)
  awaits copyright authority and free-sharing scope; no root licence is applied.
  The privacy/install drafts and M10/selected-route gates now match Store-first.
- **DOCUMENTATION — PASS:** 14 changed/new Markdown files, 240 relative local
  links, code fences and whitespace checks pass. No build/unit/UI or Store
  acceptance gate was run for these documentation changes.
- **ARTWORK — PENDING:** existing controls render Windows icon glyphs, with no
  tracked font files. The reviewed package logos remain scaffold placeholders;
  eight JPEG sources/licences are unknown. Microsoft's font-use guidance does
  not establish those assets' redistribution rights. No artwork changed.
- **LIMITS:** exact reserved Store identity, legal/asset/attribution closure,
  actual protected submission/certification, final signed packages/customer
  tests and publication remain pending. No reservation/submission, purchase,
  signing, visibility/protection, guest/trust/UI or publication ran. Baseline
  `44a4ef6`; M9 remains owner-deferred.

### 2026-11-19 hosted restore failure

This records the pre-fix state at `44a4ef6`. See the [post-fix checks](#2026-11-19--pr-ci-and-local-store-candidates) for current results.

- **DEP-15 — hosted FAIL.** The [16 November scheduled run](https://github.com/Dvaderfun/wintodo/actions/runs/43878071848)
  on `windows-2025` at `44a4ef6` installed SDK `10.0.404`. All 204 script tests
  and the source scan passed. Locked restore then failed with `NU1004`:
  the SDK requested `Microsoft.NET.ILLink.Tasks` `10.0.15`; the reviewed locks
  specify `10.0.14`. Dependency audit, build, unit tests and release packaging
  were skipped. The test summary export also failed after unit tests were skipped.
- **Comparison — PASS in the earlier environment.** The [9 November scheduled run](https://github.com/Dvaderfun/wintodo/actions/runs/42629918468)
  at the same commit passed 707 unit tests, 204 script tests and both unsigned
  x64/ARM64 AOT/MSIX jobs with SDK `10.0.403`. A forced local x64 solution
  restore with locked mode, full NuGet audit settings and warnings as errors
  passed on `10.0.403` on 19 November. No new candidate was produced.
- **Next engineering gate:** reconcile the serviced SDK and reviewed lockfiles
  for all supported restore shapes, retain locked mode and auditing, then
  obtain a passing hosted build, unit tests and both architecture jobs.
  GitHub reports private `main` unprotected, rulesets unavailable on its current
  plan (HTTP 403), no `v*` tags and no published release. A protected submission
  path still needs to be established before any production tag or upload.
- **Store release — PENDING.** The manifest still uses the test publisher and
  `1.0.0.5`. The owner can supply the [reserved identity and rights evidence](deployment-store-first-preparation.md#owner-supplied-identity-handoff)
  while CI is repaired. Review and commit the Store-first decisions; align the
  M10 checklist's direct-signing/attachment wording with ADR-019. Final native
  dependency evidence, certification and tests of the actual Store-signed
  install, update and uninstall on x64 and native ARM64 remain open.

### 2026-11-19 — Serviced SDK, reserved Store family and English listing

- **DEP-15 — local PASS at the serviced toolchain.** Reproduced the old locks'
  `NU1004` on SDK 10.0.404. Regenerated all 20 locks in isolation: only ILLink
  and Native AOT compiler packages changed from 10.0.14 to 10.0.15. Both
  architectures' .NET licence and notice bodies still match the original
  packages byte for byte. Debug/Release x64 builds had zero warnings/errors;
  707/707 unit tests and 206/206 Python script tests passed. Both CI jobs now
  request the exact SDK; scheduled CI warns about servicing updates.
- **DEP-16/18 — identity applied; protection checked.** Package source
  `a567c88` contains the Partner Center identity for Store ID `9PF4DX2BSF0X`,
  version `1.0.0.0`, and a separate toast COM class for the production family.
  The tag job rejects an unprotected ref or unavailable main protection evidence.
  Positive and negative guard tests passed; GitHub returned HTTP 403 for private
  rulesets. No production tag, submission, visibility change or signing took place.
- **DEP-17 — gallery prepared.** [Five desktop screenshots with English captions
  and five promotional renders](../releases/store/en-US/README.md) use reviewed
  synthetic test captures. All ten are 1920 × 1080 RGB PNGs with no ancillary
  metadata. The Store images use no app background JPEGs. Rights for the artwork
  still shipped in the app and final legal text remain separate obligations.
- **DEP-19/20 — PENDING.** The owner will perform the final Store-signed install,
  update and uninstall checks. The [controlled handoff](deployment-store-first-preparation.md#controlled-handoff)
  describes protective settings, approved uploads and evidence to retain. M9
  remains deferred.

### 2026-11-19 — PR CI and local Store candidates

[PR #5](https://github.com/Dvaderfun/wintodo/pull/5) passed first at
[`9d61dd5`](https://github.com/Dvaderfun/wintodo/actions/runs/44590559062) and again
on the [exact package source `c2c8919`](https://github.com/Dvaderfun/wintodo/actions/runs/44593397992) on `windows-2025`:
locked restore with SDK `10.0.404`, nine-project NuGet audit, source/history scans,
x64 Debug build with zero warnings/errors, 707 unit tests (zero skipped) and
206 script tests. The release matrix was **skipped by design on the PR**.
After review and merge, run the existing manual or scheduled **main** workflow
to produce the hosted x64/ARM64 candidates without creating a release tag.

Both local Native AOT packages below were built at `c2c8919985517b6349fb9972078cca3b3e36640a`
with no tracked changes. Both include the [nine matching template icons](dep13-source-privacy-review.md#2026-11-19--source-of-the-nine-package-icons)
and the updated notice (SHA-256 `cd610e2ea7a190e41f4ea5937633d5a2c3bdc130c74f4374d2b9d7291301a9f1`).
Compared with the earlier `a567c88` packages, only the notice, the rebuilt
native executable and the MSIX block map changed in either payload.
The packaged manifests declare the reserved Name and Publisher, display
`Univera LLC`, derive PFN `47371DermenzhyStudio.Wintodo_rg7xtn0gdgdnp`
and carry version `1.0.0.0`. Neither MSIX contains a signature.

| Architecture | Unsigned MSIX SHA-256 | Files / native binaries | Privacy scan | NuGet advisories |
|---|---|---:|---|---:|
| x64 | `29be8f5e2983bc2846ffdb1fe35d6dda312106d3e32f0d8906c61efe26475597` | 321 / 54 | 650 files, 0 findings | 0 |
| ARM64 | `13f34367896d9eaf825fdd40f7e19a0e912a8d05c7fbdccee32065973444ea39` | 317 / 53 | 642 files, 0 findings | 0 |

Both package inspections and CycloneDX 1.6 schema checks passed. The 45
original NuGet archives have verified repository signatures and matching hashes.
On the earlier pair an initial descriptor used the wrong ARM64 label and was
rejected; the corrected descriptor passed. The current handoff matched both
packages to their reports: `PASS`, `release_authorized: false`. The two MSIX,
Store artwork ZIP and evidence ZIP are under `artifacts/store-1.0.0-rc/`.
A clean NuGet audit does not settle the
[remaining native component and licence questions](dep02-vendor-provenance-resolution.md#remaining-actions-and-evidence).

Next: establish effective protection of `main` and release tags, settle the
[default package icons](../adr/ADR-021-store-package-icon-proposal.md),
rights for the eight background JPEGs, licensing and the public privacy URL,
then prepare the [first private-audience submission](deployment-store-first-preparation.md#controlled-handoff).
Record the signed results before testing clean install,
higher-version update with data preserved, uninstall and actual native ARM64
execution. The [manual checklist](../guides/release-checklist.md) covers the
remaining functional and live-account checks. No tag or submission was created.

Record each result with work ID, PASS/FAIL/PENDING/SKIPPED/DEFERRED status,
source commit, candidate hash where relevant, environment, evidence location and
limitations. Keep sensitive/raw evidence private and public records sanitized.
Update this register and PROGRESS after execution, not from intention alone.

## 7. Completion condition

Release preparation is complete when DEP-01 through DEP-19 have their required
selected-route acceptance evidence, no unresolved release blocker remains, the
owner's decisions are recorded, and the reviewed Store publication/source draft
is bound to the actual approved Store-signed packages and tested deployment.
Publication is complete only after DEP-20's authorized delivery and verification.
Deferred performance or unsupported environments must remain honestly described.

This plan supplements the existing milestone and release checklist. It does not
silently supersede an ADR, change the product scope or claim any gate has passed.
