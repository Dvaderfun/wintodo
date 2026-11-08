# ADR-018 — Direct MSIX first release

- **Status:** accepted
- **Date:** 2026-11-09
- **Supersedes:** nothing
- **Owner acknowledgement:** the owner's “Lets do it” response to the proposed
  direct-MSIX-first release sequence and retain/document production-override
  policy on 2026-11-09. Exact identity, signing and legal values remain pending.

## Context

M10 already prepares self-contained Native AOT MSIX packages for x64 and ARM64,
with local structural, privacy, dependency and unsigned-handoff checks. The
[deployment plan](../plans/deployment-readiness.md) requires production signing,
protected execution and exact installed-package verification before delivery.
The distribution route was undecided; the proposed first route uses these paired
packages. Store delivery adds reserved identity and submission requirements.

## Decision

Target direct MSIX distribution for the first production release. Prepare and
verify one x64/ARM64 pair from the approved source, identity and version, then
production-sign and test those exact packages before preparing the release draft.
Store delivery remains a separate future work package requiring its own decision
and Partner Center identity/submission path.

Repository visibility and protection are separate from this distribution choice.
The source repository remains private until an explicit visibility decision and
privacy/attribution review. Required protection must work under the selected
GitHub plan; local source/ref checks do not replace enforced remote protection.

The existing local launch controls follow selected
[DEP-12 policy A](../plans/dep12-production-overrides.md): retain and document
Release/AOT database and clock overrides, preserving Debug-only fixture/trace
behavior and broker-share LocalState/real-time defaults. This preserves the
accepted onboarding and guest-only testing contracts in ADR-016 and ADR-013.

## Consequences

- Exact production name, publisher/identity, version, supported previous
  installations, signing provider/timestamp/custody, licence, public contacts
  and asset rights still require supplied owner values. This ADR selects none.
- Tags must satisfy the reviewed release-ref/version contract; a tag does not
  rewrite the manifest or authorize signing. Production identity/version and
  remote protection remain independent acceptance gates.
- Final install, activation, persistence, update/recovery and uninstall checks
  use a separately approved clean customer baseline and the exact signed bytes.
  Native ARM64 runtime needs a native ARM64 target.
- Custom database selection moves SQLite only. Attachments, credentials and
  broker-share state retain their existing locations. Final tests also verify
  ordinary LocalState and real time.
- Certificate trust, provisioning, remote configuration and publication retain
  their applicable approvals. This decision authorizes release preparation;
  publication follows review of the complete verified draft.
- Vendor/advisory/legal closure and existing manual gates remain visible. M9
  performance remains owner-deferred.

## Alternatives considered

- Store first: requires reserved identity and Store-specific artifacts before
  the existing paired-package path can become the first distribution channel.
- Both routes together: adds two delivery/identity/trust validation paths to the
  first release.
- Restrict local overrides to test builds: changes established Release/AOT
  behavior and would require separately adapted production/default-state gates.

See [the owner decision packet](../plans/deployment-owner-decisions.md) for the
remaining values and operational sequence.
