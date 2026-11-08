# Deployment owner decision packet

Prepared: 2026-11-09 against source `0b7272f`.
Status: **PROPOSAL — every answer below remains UNDECIDED.** This packet
records choices needed to advance DEP-12/13/16/17/18; it changes no policy,
accepted ADR, package identity, remote setting or release authorization.

The next useful step is to settle production policy and release identity while
native provenance and deployment tooling progress independently. Another build
of the same placeholder packages would not settle those blockers. The
[latest bound unsigned pair](dep16-bound-candidate-validation.md) already passes
local checks; hosted execution, full vendor/advisory closure, production signing
and final installed-package acceptance remain pending.

## Decisions that can be answered together now

| ID | Required answer | Concrete proposal and constraints | Unblocks |
|---|---|---|---|
| D1 — Release route | Direct MSIX, Store, or both; source repository public or private | **Proposed first route: direct MSIX**, with Store delivery deferred to a separate approved work package. This uses the existing paired-MSIX preparation path; a Store release additionally requires reserved Partner Center identity and submission artifacts. Visibility is a separate choice from distribution. Current private-repository protection reads returned a plan-related 403; retain enforced protection through an eligible private plan or approve public visibility after final privacy/attribution review. Verify actual plan/features before selecting either route. | DEP-15/16/18/20 |
| D2 — Name and assets | Final display name; approve a brand direction and provide ownership/redistribution evidence for icons and eight backgrounds, or authorize replacement | The current name is **Wintodo, a placeholder**. Final branding may retain that name only by explicit choice. [Asset review](dep13-source-privacy-review.md) found no identifying content but did not establish redistribution rights. Existing placeholder icons also need ownership/final-brand review. Do not infer rights from their presence in Git. | DEP-13/16/17 |
| D3 — Identity and installed-data continuity | Exact production `Identity.Name`, publisher subject/display attribution; which previous installations must update or migrate | Current [manifest](../../src/Wintodo.App/Package.appxmanifest) uses a GUID identity, `CN=AppPublisher` and `1.0.0.5`. For direct delivery the signer must match the approved manifest publisher; for Store use the exact reserved identity/publisher. Specify whether existing test-install data must transfer. A new identity changes the package data location; a publisher change may also change package-family/update compatibility. Preserve the owner's installation and prove any approved transition on disposable data. An SQLite-only transfer does not include attachments or credentials. | DEP-16/18/19 |
| D4 — Version policy | Strict release-tag format, first production version, supported prior update versions | **Proposed stable tags: `vMAJOR.MINOR.PATCH`, mapped to `MAJOR.MINOR.PATCH.0`**, with validated MSIX component limits and monotonic versions. If the current identity/update line is retained, `v1.0.1` → `1.0.1.0` is an example above the recorded `1.0.0.5`; `v1.0.0` → `1.0.0.0` would be lower. Confirm the greatest installed supported version before choosing the first release. Tags currently do not modify the manifest. Prerelease mapping requires a separate explicit policy if wanted. | DEP-16/18/19 |
| D5 — Signing ownership | Production signing provider, approved timestamp service/trust route, credential custodian and renewal owner | Prefer a supported provider with short-lived signing authority if available. A protected PFX workflow is an alternative only with explicit credential custody, ephemeral handling and cleanup. The existing development certificate is not a production selection. The signing subject must be compatible with D3; direct delivery needs the approved timestamp/trust policy. Provider feasibility and protection must be established before wiring a credential-bearing job. | DEP-18/19 |
| D6 — Public legal identity | First-party licence; permitted public author/publisher attribution; project support, security and privacy contact | Choose the licence explicitly; inherited supplier notices do not license first-party code or images. For a permissive open-source release, MIT is a candidate for review, subject to rights over the first-party work. Provide the exact public attribution/contact text and decide whether current Git attribution is publishable. Preserve legally required supplier attribution. Any history rewrite requires a separate concrete review and authorization. | DEP-13/17/20 |
| D7 — Production local overrides | Select A or B from [DEP-12](dep12-production-overrides.md) | **Recommend A: retain and document established Release/AOT local overrides.** A custom database path moves SQLite only; fixtures and durable trace output remain Debug-only; broker shares use package LocalState and real time. B restricts overrides to separate test builds and requires gate changes plus acknowledgement of any affected ADR-016 contract. Neither choice introduces portable profiles. | DEP-12/17/19 |

Recommended options are review proposals, not defaults or approvals. Exact
publisher, licence and support-contact values must be supplied rather than
derived from local account information. Record accepted answers in the owning
plan/guide and, where an accepted decision changes, an ADR with acknowledgement
before dependent implementation.

An owner can answer the whole packet in one response:

```text
D1 route / source visibility / protection plan:
D2 final name / brand direction / asset rights or replacement:
D3 identity / publisher / existing installations to preserve:
D4 tag mapping / first version / supported update versions:
D5 signing provider / timestamp / custody and renewal owner:
D6 licence / public attribution / support-security-privacy contact:
D7 A (keep/document) or B (separate test builds):
```

Unknown answers stay pending. D3 can be finalized after the selected signer or
Store reservation supplies its exact publisher requirements; do not guess those
values to start an identity change.

## Execution after the decisions

1. Record the choices. Implement/document/test D7; finalize approved identity,
   version mapping and assets under DEP-16/17. Complete exact-source privacy,
   attribution, legal terms and notice/payload approval. Finalize the
   [privacy](../releases/PRIVACY.md) and
   [installation/recovery](../releases/INSTALLATION.md) drafts with the supplied
   public fields and actual behavior.
2. In parallel, close vendor/advisory provenance evidence, prepare the missing
   installed update/uninstall gates, and validate reviewed source in hosted CI.
   Native code observations and a zero-advisory NuGet audit do not establish
   complete vendor closure. Remote operations require their applicable concrete
   authorization; a visibility answer alone does not publish the repository.
3. Build a clean paired candidate from the final reviewed source with its
   approved identity/version/assets. Bind checks to exact package bytes and
   source/tool/dependency evidence. Rebuilding becomes useful at this point
   because approved payload or release configuration has changed.
4. Complete protected signing and verify the resulting signatures and final
   hashes. Run the exact signed packages through approved clean customer
   install/update/uninstall sessions and the remaining functional/manual gates.
   Cross-compilation cannot close native ARM64 runtime acceptance.
5. Prepare the verified draft containing only approved assets, final hashes,
   evidence and known limitations. Obtain explicit publication/submission
   authorization for that concrete draft, then execute DEP-20.

This order follows [the deployment plan](deployment-readiness.md),
[M10](../../PLAN.md#m10--packaging-and-release),
[packaging](../guides/packaging.md) and
[release checklist](../guides/release-checklist.md). It does not weaken their
acceptance conditions or replace the detailed work packages.

## Operational approvals to request when ready

Product decisions above are separate from permission to change machines or
publish. Request the following only when the exact target, inputs, planned
changes and evidence path are concrete:

- **Guest setup and test sessions:** owner-invoked prerequisite setup if
  required; separately identified customer baseline; exact install/update/
  uninstall packages and prior versions; any justified certificate trust or
  dependency provisioning; native ARM64 target. The current development
  checkpoint has Developer Mode, runtimes and test trust and cannot establish
  the clean customer premise. Host UI automation remains forbidden without
  explicit test-session permission.
- **Live verification:** disposable work/school sign-in/admin consent and
  Radicale/Nextcloud accounts, final registration and package, and redacted
  capture plan. Personal-account manual PASS is already recorded; the existing
  public Entra client registration and ADR-016 `Tasks.ReadWrite`/`User.Read`,
  PKCE/loopback and GET-only design are established constraints. Verify
  production registration/publisher/consent suitability instead of reopening
  those accepted scope choices or introducing a desktop client secret.
- **Remote and signing operations:** exact repository/ref/workflow or
  protection changes, visibility transition if chosen, signing provider and
  protected credentials/environment configuration. Prepare and review the
  applicable result before requesting its final authorization.
- **Publication:** the completed draft, exact signed hashes and full gate
  report. Approval to prepare this packet or use subagents does not authorize
  publication, Store submission, public vendor contact or history rewriting.

M9 remains owner-deferred. M6 external-client import, M7 reboot reminder/visible
badge, M8 Narrator, M13 real-server/conflict UI and native ARM64/final deployment
rows remain visible until their required evidence exists; they cannot be
converted to PASS by a policy answer. Any proposed change to a release
requirement needs its own explicit owner decision.

## Verification of this packet

This is a documentation deliverable. Its constraints are grounded in the
deployment plan, current manifest, existing DEP-12 proposal, asset/notice
reviews, release drafts and final-deployment matrix. CodeGraph was consulted
before implementation inspection. No builds, runtime gates, installation,
trust changes, signing, remote writes or publication were performed for it.
All ten relative local links resolve, and the document has no trailing
whitespace. These documentation checks do not close any release gate.
