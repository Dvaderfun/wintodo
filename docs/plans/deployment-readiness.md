# Deployment readiness and GitHub release plan

Prepared: 2026-11-08. Audit baseline: `d8c2ac3`.

Status: **IN PROGRESS — DEP-01, DEP-03, DEP-05, DEP-06, DEP-07, DEP-08, DEP-09 and DEP-11 automated acceptance passed. DEP-14 pre-signing checks passed; final signed-package acceptance and remaining release work are pending.**

This plan turns the deployment-readiness audit into work packages for a safe
GitHub release and GitHub Actions CI/CD. It covers data integrity, API security,
credentials, personal-data exclusion, packaging, signing, documentation and
deployment verification. Writing this plan does not authorize publication,
changes to accepted decisions, certificate trust or machine provisioning.

## 1. Outcome and boundaries

The intended outcome is reviewed, production-signed, self-contained Native AOT
MSIX packages for x64 and ARM64, produced from one clean source commit, with
passing CI, verified privacy, reproducible provenance and tested installation,
updates and removal. Store delivery is included if selected by the owner.

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

| Decision | Required record | Blocks |
|---|---|---|
| App name and branding | Final display name, asset ownership and branding direction | DEP-16, DEP-17 |
| Distribution | Direct MSIX, Microsoft Store, or both; source repository visibility | DEP-16, DEP-18, DEP-20 |
| Package identity and publisher | Exact final identity, signing subject and preservation strategy for existing installations | DEP-16, DEP-18, DEP-19 |
| Versioning | Valid tag syntax, four-part MSIX mapping, monotonic updates and Store requirements if applicable | DEP-16, DEP-18 |
| Signing | Production provider, timestamp service, credential custody and renewal policy | DEP-18 |
| Public attribution and licence | Approved publisher/repository attribution, first-party licence and organizational support contact | DEP-13, DEP-17 |
| Production overrides | Keep/document or gate database/environment/clock overrides without breaking AOT test coverage | DEP-12 |
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
| DEP-02 | Patch and verify release toolchain | Security | — | Codex / dep02_toolchain_review | REVIEWED WITH LIMITS; native-tool advisory/provenance disposition and hosted acceptance PENDING |
| DEP-03 | Protect local edits during sync pull | Blocker | DEP-01 | Codex / dep03_sync_pull | COMPLETE; automated acceptance PASS |
| DEP-04 | Implement recoverable synced task moves | Blocker | DEP-03; data/ADR decision if needed | Codex / dep04_move_design | BASELINE FAIL 7/7; ADR-017 proposed; implementation PENDING acknowledgement/next batch |
| DEP-05 | Validate Graph request destinations | Blocker | DEP-01 | Codex / dep05_graph_origin | COMPLETE; automated acceptance PASS |
| DEP-06 | Enforce CalDAV redirect origin policy | Security | DEP-01 | Codex / dep06_caldav_redirects | COMPLETE; automated acceptance PASS |
| DEP-07 | Bound network bodies and attachment downloads | Security | DEP-05, DEP-06 | Codex / dep07_network_limits | COMPLETE; automated acceptance PASS |
| DEP-08 | Sanitize all diagnostic paths | Privacy blocker | DEP-01 | Codex / dep08_diagnostics | COMPLETE; automated app/CI privacy acceptance PASS |
| DEP-09 | End sensitive import sessions correctly | Security | DEP-01 | Codex / dep09_import_lifecycle | COMPLETE; automated acceptance PASS |
| DEP-10 | Preserve untrusted attachment provenance | Security | DEP-07; API/policy review | Codex / dep10_provenance | IMPLEMENTED; host/AOT PASS; guest preflight FAIL, UI acceptance PENDING |
| DEP-11 | Reject unsupported database schemas | Data safety | DEP-01 | Codex / dep11_schema_guard | COMPLETE; automated acceptance PASS |
| DEP-12 | Resolve production test/storage overrides | Policy | Owner decision | Codex / dep12_overrides | INVENTORY COMPLETE; production policy/acceptance PENDING |
| DEP-13 | Verify repository/history and source privacy | Privacy blocker | Attribution decision | Codex / dep13_archive_review | ARCHIVE/FIXTURE/ASSET CHECKS PASS; attribution/licensing/final-source acceptance PENDING |
| DEP-14 | Remove identifying binary and metadata paths | Privacy blocker | DEP-02 | Codex / dep14_binary_privacy | PRE-SIGNING PASS; final signed-package acceptance PENDING DEP-16/18/19 |
| DEP-15 | Harden and reproduce CI builds | Blocker | DEP-01, DEP-02 | Codex / dep15_locked_restore | LOCKED RESTORE/LOCAL CHECKS PASS; remote CI/protection/native advisory disposition PENDING |
| DEP-16 | Finalize identity, version and package contents | Blocker | Identity/version decisions; DEP-14 | Unassigned | PENDING |
| DEP-17 | Complete licensing, privacy, branding and sign-in docs | Release | Owner decisions | Unassigned | PENDING |
| DEP-18 | Add protected signing and GitHub release stages | Blocker | DEP-13–17; signing decision | Unassigned | PENDING |
| DEP-19 | Verify final deployment and functional gates | Blocker | DEP-03–12, DEP-16–18 | Unassigned | PENDING |
| DEP-20 | Publish approved assets and maintain releases | Release | DEP-19; explicit publication authorization | Unassigned | PENDING |

Execution sequence:

1. Establish decisions and repair CI; select a patched compatible toolchain.
2. Fix data/API/privacy defects and add meaningful regression coverage.
   Independent work packages may proceed concurrently with clear ownership.
3. Harden CI, sanitize outputs and complete identity/documentation/assets.
4. Produce and sign one candidate, then test those exact signed packages.
5. Publish the approved verified files and establish ongoing release maintenance.

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

## 5. Detailed work packages

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
- [ ] Define recoverable source removal and destination creation, including
      ETags, UID preservation, offline retries and restart recovery. Do not
      simply clear the source URL and leave an orphan remote copy.
- [ ] Handle single/bulk moves, children, same-account collection moves,
      cross-account moves, local-to-synced and synced-to-local moves.
- [ ] Protect against a source pull moving the task back and against premature
      acknowledgment or loss when either remote operation fails/conflicts.
- [ ] Use an acknowledged ADR and forward migration if durable move intent
      requires a schema/protocol decision.
- [ ] Test two sync cycles and second-client convergence, including failure
      between remote operations and resume after process restart.

Acceptance: destination and source membership match the intended move, no task
version is silently lost, and retries converge without unexplained duplicates.

The fourth batch reproduced seven deterministic baseline failures against
`79ce9b1`; no production fix is claimed. [ADR-017](../adr/ADR-017-recoverable-synced-task-moves.md)
and [the detailed proposal](dep04-synced-moves.md) define durable recovery for
the next batch. Owner acknowledgement remains pending.

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

- [ ] Apply the approved name/identity/publisher and data-preservation strategy.
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

### DEP-17 — Complete release documentation, assets and sign-in setup

Targets: README, packaging/testing/release guides, legal/privacy documents,
app assets/resources and Microsoft To Do deployment documentation.

- [ ] Add the selected first-party licence and verify third-party notices
      against the actual shipped transitive/native closure. Check image/icon
      provenance; preserve required attribution and permitted redistribution.
- [ ] Replace approved placeholder icons at required scales/target sizes;
      validate manifest references and final title/tile/splash appearance.
- [ ] Produce final screenshots and listing copy using synthetic tasks/accounts.
      Add an organizational support/security contact and reporting instructions.
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

### DEP-18 — Add protected signing and GitHub release stages

Targets: GitHub workflow files, signing/release scripts and repository settings.

The intended stages are:

| Stage | Trigger/input | Authority | Output |
|---|---|---|---|
| Validate | PR/main source | Read-only, no production credentials | Build/tests, redacted security/dependency results |
| Candidate | Valid protected release tag on reviewed main ancestry | No signing secrets; narrowly scoped provenance permission if used | Inspected unsigned x64/ARM64 packages, sanitized manifest/SBOM |
| Sign and verify | Approved candidate IDs/hashes | Protected environment; short-lived signer identity or protected signing secrets | Timestamped production-signed packages and final verification |
| Draft release | Both verified architectures and required gates | `contents: write` only for the release job | Draft with signed assets, final checksums and release notes |
| Publish | Verified draft plus explicit owner authorization | Approved release operator/job | Public release or selected distribution submission |

- [ ] Validate event, tag syntax, source ancestry, version and candidate hashes;
      do not interpolate untrusted ref/body values directly into shell scripts.
- [ ] Verify environment protection works with the repository's GitHub plan.
      Define an equivalent authorized gate if unavailable; never silently weaken it.
- [ ] Prefer supported short-lived signing authority. If PFX is selected, use
      protected secrets, ephemeral files, restrictive access and cleanup on every
      outcome. Never upload keys/passwords or trust a dev certificate in CI.
- [ ] Match certificate subject and manifest publisher; timestamp direct
      signatures with the approved service and verify signature/trust chains.
- [ ] Limit credential/publication permissions by job and trusted trigger;
      validate downloaded artifact provenance and protect cancellation/retries.
- [ ] Attach only approved signed MSIX/bundles, final SHA-256 checksums, sanitized
      provenance/SBOM and reviewed release notes. No wholesale `publish/` upload.
- [ ] Support safe reruns without overwriting a published version with different
      code. Compute hashes after signing; verify both architectures before release.
- [ ] For Store delivery, implement the SDK/Partner Center submission artifact
      and validation path separately; WinApp packaging is not Store submission.

Acceptance: a complete authorized dry run produces a verified draft and correct
assets, and negative tests demonstrate that PRs, malformed tags, missing gates
and mismatched artifacts cannot sign or publish.

### DEP-19 — Verify final deployment and functional gates

Use the exact signed candidates; record commit, final SHA-256, architecture,
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

### DEP-20 — Publish approved assets and maintain releases

- [ ] Present the complete reviewed draft, final hashes and gate results to the
      owner. Obtain publication/submission authorization after preparation.
- [ ] Publish those exact verified files; confirm download hashes/signatures
      and installation instructions against the public/customer-facing artifacts.
- [ ] Define withdrawal/replacement and hotfix procedures. Prefer a new higher
      signed version; never replace published bytes under an existing version.
- [ ] Preserve last-known-good artifacts and migration backups. An older
      binary is not a safe rollback if it cannot read the current schema.
- [ ] Establish dependency/Actions advisory monitoring, scheduled trusted AOT
      checks, signing expiry/renewal alerts and subsequent release verification.
- [ ] Publish limitations and security/support reporting instructions; record
      final release evidence in PROGRESS and mark only completed rows done.

Acceptance: the approved release is downloadable with matching signatures/hashes,
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
| Fresh-checkout CI | Exact commit; all four test suites; no unexplained skips | Previous batch PASS (`6342f82`, GitHub run 42428523717, 455/455); third/fourth-batch GitHub CI PENDING |
| Pull concurrency | Newer edits/deletes survive or conflict; no false acknowledgment | PASS (`97e7107`, 33 Sync tests); final-package/live chooser proof remains DEP-19 |
| Synced moves | Correct source/destination membership and retry/restart convergence | BASELINE FAIL 7/7; ADR-017 proposed; implementation/acceptance PENDING |
| Graph origin/CalDAV redirects | Real request boundaries; no foreign token/body disclosure | Graph PASS (`0d5dc94`, 31 destination tests); CalDAV PASS (41 regressions, M13 VM 11/11) |
| Network limits | Declared/unknown/decoded lengths; bounded reads and staging cleanup | PASS (49 new cases; import VM 25/25, Debug/AOT M13 11/11 each); final installed-package gates DEP-19 PENDING |
| Diagnostic privacy | Synthetic private markers absent from all safe output routes | Automated app/CI PASS (Debug import 25/25, Release formatter 9/9 and guest 6/6); final output gates DEP-14/15/19 PENDING |
| Import session and attachment trust | Final dismissal, expiration/resume and provenance policy | Session PASS (11 new unit tests, import VM 25/25); attachment provenance DEP-10 PENDING |
| Schema/update recovery | Supported upgrades/backups; newer-schema rejection | Schema/backup PASS (`015a6a8`, 9 new cases + VM 5/5); final installed-update DEP-19 PENDING |
| Production override policy | Approved Release behavior and broker isolation | PENDING |
| Dependency/AOT security | Fresh resolved graph; reviewed patched compiler/runtime | LOCAL x64/ARM64 AOT/NuGet PASS (`2e149ca`); runner/native review PENDING |
| Repository/history/archive privacy | Redacted scanner findings and reviewed attribution | PRELIMINARY SCANS PASS (`2e149ca`); policy/final review PENDING |
| Binary/public metadata privacy | Both final signed architectures; no identifying build paths | PRE-SIGNING PASS (649 integrated x64/ARM64 AOT files; isolated unsigned x64 MSIX 322 files); final signed x64/ARM64 PENDING |
| Package identity/version/content | Tag mapping, native/runtime/assets/capabilities and SBOM | PENDING |
| Signing and pipeline protection | Final chain/timestamp; trusted inputs; negative permission tests | PENDING |
| Licence/privacy/assets/support docs | Complete approved customer-facing material | PENDING |
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

Record each result with work ID, PASS/FAIL/PENDING/SKIPPED/DEFERRED status,
source commit, candidate hash where relevant, environment, evidence location and
limitations. Keep sensitive/raw evidence private and public records sanitized.
Update this register and PROGRESS after execution, not from intention alone.

## 7. Completion condition

Release preparation is complete when DEP-01 through DEP-19 have their required
acceptance evidence, no unresolved release blocker remains, the owner's decisions
are recorded, and a verified draft contains the exact approved signed packages.
Publication is complete only after DEP-20's authorized delivery and verification.
Deferred performance or unsupported environments must remain honestly described.

This plan supplements the existing milestone and release checklist. It does not
silently supersede an ADR, change the product scope or claim any gate has passed.
