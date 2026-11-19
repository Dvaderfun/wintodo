# ADR-019 — Store-first release with free self-builds

- **Status:** accepted
- **Date:** 2026-11-09
- **Supersedes:** ADR-018's direct-MSIX-first distribution and direct-download
  production-signing requirements for the first release only. ADR-018's DEP-12
  policy A and remaining safety/verification requirements continue unchanged.
- **Owner acknowledgement:** the owner's explicit answers: publisher
  “Univera LLC”, “Store-first”, “Reserve commercial redistribution to me”,
  artwork source unknown, and contact `support@univera.app`.

## Context

The owner has an organization Microsoft Store publishing option, no Windows
production signing certificate, and wants freemium official releases with free
self-builds. Microsoft signs certified MSIX Store submissions without requiring
a purchased CA certificate. GPL/MIT permit commercial redistribution by others
and do not implement the selected commercial restriction.

The name Wintodo, first tag `v1.0.0` mapped to MSIX `1.0.0.0`, a fresh production
installation, future data-preserving updates and public source after review were
already selected. Current packages still use the separate test identity/version.

## Decision

1. Prepare the first production release as Microsoft Store MSIX under
   **Univera LLC**, using the exact Partner Center identity and publisher values.
   The selected public support/security/privacy contact is
   **support@univera.app**. Neither a display name nor a screenshot supplies the
   reserved package identity; obtain the actual Product identity fields.
2. Use Store-managed signing for this route. Purchased signing certificates,
   direct signed GitHub downloads and their credential-bearing CI jobs are
   outside the first release. Revisit them in a separate approved work package
   if direct production distribution is later wanted.
3. Publish first-party source under source-available terms permitting free
   private self-builds and reserving commercial redistribution to the authorized
   rights holder. Exact legal text, copyright ownership and any free community
   redistribution grant require review; the policy selection is not an applied
   repository licence or a transfer of copyright to Univera LLC.
4. Retain the current artwork only with established redistribution rights.
   The owner's “must be free” assumption is not a licence. Existing Windows
   font glyph use, bundled logo assets and photographic backgrounds must have
   their respective evidence. No replacement or unverified asset publication
   is authorized by this ADR.
5. Use a new production package family for the fresh `1.0.0.0` line. Keep that
   family stable and prove data-preserving future updates with installed-package
   tests and forward migrations. Do not downgrade, uninstall or alter the
   owner's test installation to establish this path.

## Consequences

- Store name reservation, actual identity, package validation, listing assets,
  age ratings, accurate privacy/licensing terms and certification preparation
  become required DEP-16/17/18 work. A prepared private submission is not
  certification PASS, signed-runtime PASS or publication authorization.
- Bind uploaded x64/ARM64 artifacts to the reviewed source/hashes. Record the
  certification/submission identity and inspect the resulting Store-delivered
  signed versions. Store signing/processing can change package bytes; validate
  delivered provenance and content rather than assuming an unchanged file hash
  or treating a direct-signing continuity helper as Store acceptance.
- Preserve all blocking data/API/privacy/dependency, native ARM64, customer
  install/update/uninstall and manual gate requirements. Run actual signed
  Store package tests through an approved staged/flight path where available;
  keep unavailable evidence pending. M9 remains owner-deferred.
- Public GitHub source/release metadata requires final source/history, rights
  and attribution review plus actual enforced protection. A prepared GitHub
  draft can reference the approved Store listing and reviewed source metadata;
  direct production binaries are not required for this selected first route.
- Store reservations, submissions/flights, public visibility, machine/trust
  changes and publication still need their concrete scoped execution review.
  Configure controlled publication rather than silently publishing on approval.
- Freemium features, payments and entitlement logic are separate product work.
  This ADR does not add a billing service, change app data flows or promise
  payment enforcement on self-built versions.

## Alternatives considered

- Direct MSIX first with Artifact Signing or a commercial CA: remains a valid
  later route but adds signing eligibility, costs and independent distribution
  verification before the first release.
- GPLv3: permits paid official builds and free personal builds, but also permits
  commercial compliant forks. The owner chose to reserve that redistribution.
- MIT: additionally permits closed commercial forks; does not meet the selected
  commercial restriction.
- Treating unknown artwork as free: provides no redistribution evidence and
  does not satisfy DEP-17.

See [Store preparation](../plans/deployment-store-first-preparation.md),
[the licence draft](../plans/wintodo-source-available-license-draft.md) and
[the decision packet](../plans/deployment-owner-decisions.md).
