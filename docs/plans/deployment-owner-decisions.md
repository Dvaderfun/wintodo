# Deployment owner decision packet

Prepared: 2026-11-09 against source `0b7272f`.
Updated 2026-11-20 after the owner confirmed personal ownership, publisher authority and draft artwork.
Status: **PARTIALLY DECIDED — Wintodo and the Glide package icon selected; public
source after review; fresh production installation with future data-preserving
updates; stable tag mapping and first release 1.0.0 selected; Store-first under
Univera LLC, commercial redistribution reserved, support@univera.app selected.**
DEP-12 policy A remains selected. The reserved identity, first version and [first-party source licence](../../LICENSE.md) are applied. External artwork rights and effective protection remain pending.

[ADR-019](../adr/ADR-019-store-first-source-available-release.md) supersedes
ADR-018's first distribution route with the owner's explicit Store-first answer.
ADR-018's preserved launch-control policy continues unchanged.

The next rights step is to document the eight background sources or replace them before public distribution, while
native provenance and deployment tooling progress independently. The
[latest hosted unsigned pair](dep16-hosted-candidate-validation.md) already
passes bounded checks; protected execution, full vendor/advisory closure,
Store certification/signing and final installed-package acceptance remain pending.
[Store preparation](deployment-store-first-preparation.md) provides the exact
identity handoff and selected-route gates. [ADR-023](../adr/ADR-023-first-party-rights-and-draft-artwork.md)
records the owner's authorization to retain the backgrounds in the draft.

## Decisions that can be answered together now

| ID | Required answer | Concrete proposal and constraints | Unblocks |
|---|---|---|---|
| D1 — Release route | **Store-first under Univera LLC selected (ADR-019); public source after review selected**; actual protection pending | The owner acknowledged Store-first explicitly. Microsoft supplies Store MSIX signing; direct production downloads/CA purchase are outside this first route. The repository remains private until final review and the concrete transition. The reservation is confirmed. Submission and the visibility transition are pending. | DEP-15/16/18/20 |
| D2 — Name and assets | **Wintodo and Glide selected**; background rights pending | [ADR-021](../adr/ADR-021-store-package-icon-proposal.md) replaces all nine template icons. The eight retained background JPEGs still need a source and commercial redistribution terms. | DEP-13/16/17 |
| D3 — Identity and installed-data continuity | **Reserved identity supplied and applied** | [Partner Center fields and PFN](deployment-store-first-preparation.md#owner-supplied-identity-handoff) match the production manifest. The old test family stays separate. Future updates must preserve tasks, attachments, settings and credential access. | DEP-16/18/19 |
| D4 — Version policy | **Stable tags `vMAJOR.MINOR.PATCH` → `MAJOR.MINOR.PATCH.0`; first release `v1.0.0` / `1.0.0.0` selected** | Use the new production identity from D3 and monotonic subsequent versions. Existing `1.0.0.5` test installations are outside this first production update line. Tags do not modify the manifest; the production manifest already uses `1.0.0.0`. Prerelease mapping remains a separate policy. | DEP-16/18/19 |
| D5 — Signing ownership | **Store-managed MSIX signing selected**; actual certification/signed-runtime evidence pending | No purchased Windows certificate or direct-signing secret is required for the first Store route. Bind actual Store-delivered signed packages to the reviewed uploads. Future direct-download signing is a separate decision. No purchase, submission or signing action ran. | DEP-18/19 |
| D6 — Public legal identity | **The individual author is the first-party rights holder; Univera LLC is the authorized publisher. Contact support@univera.app.** | The [licence](../../LICENSE.md) allows free self-builds and free unbranded redistribution; it reserves the official name and commercial rights. Third-party and Store terms remain separate. Public Git attribution still needs review. | DEP-13/17/20 |
| D7 — Production local overrides | [DEP-12](dep12-production-overrides.md) policy A selected | **Selected A: retain and document established Release/AOT local overrides.** A custom database path moves SQLite only; fixtures and durable trace output remain Debug-only; broker shares use package LocalState and real time. Executable startup-policy tests and documented runtime gates accompany the supported behavior. Whole-profile isolation still requires the disposable package/guest. | DEP-12/17/19 |

Unselected recommended options remain review proposals. Exact
publisher, licence and support-contact values must be supplied rather than
derived from local account information. Record accepted answers in the owning
plan/guide and, where an accepted decision changes, an ADR with acknowledgement
before dependent implementation.

Remaining answers can be supplied together:

```text
D1 Store-first; reservation confirmed. Protection/submission pending:
D2 source and redistribution licence for the eight retained background JPEGs:
D3 exact reserved identity applied; final installed-family test pending:
D4 accepted v1.0.0 -> 1.0.0.0 on a new family; future updates preserve data:
D5 Store signing selected; actual certified/signed-package evidence pending:
D6 individual rights holder; free unbranded community copies; official branding and monetization reserved:
D6 support@univera.app selected; public Git attribution review pending:
D7 selected A retained; runtime acceptance pending:
```

The reserved identity settles D3 in source. It does not establish artwork rights, ownership of the code or successful installation.

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
4. Complete approved Store submission/certification preparation and verify the
   resulting Store-signed packages/provenance. Run them through approved clean customer
   install/update/uninstall sessions and the remaining functional/manual gates.
   Cross-compilation cannot close native ARM64 runtime acceptance.
5. Prepare the reviewed Store publication state and GitHub source/release draft,
   containing approved metadata/evidence and known limitations. Obtain explicit publication/submission
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
The updated packet's relative local links and whitespace are checked alongside
the signing/licensing recommendation. These documentation checks do not close
any release gate or authorize the proposed distribution/licence choices.
