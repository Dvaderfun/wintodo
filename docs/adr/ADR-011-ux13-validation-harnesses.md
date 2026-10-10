# ADR-011 — UX-13 validation with corrected harnesses

- **Status:** accepted
- **Date:** 2026-10-11
- **Supersedes:** UX-13's literal requirement that all UI gate scripts remain unchanged
- **Owner acknowledgement:** the owner replied "Yes" on 2026-10-11 to accepting this ADR and running reduced-motion validation with verified setting restoration.

## Context

UX-13 splits source files while preserving application behavior. Its acceptance
list says "All UI gates pass unchanged." Four existing harnesses failed against
both the pre-refactor application and the refactored application. The owner
requested separate harness fixes, shipped in `40a1a34`, and those suites now pass:
M6 7/7, list icons 10/10, Tasks settings 9/9, and composer 3/3. Application source
was unchanged by those fixes. The literal script criterion still prevents closure.

The fresh completion timing comparison also passes the existing strict mean and
median thresholds. Reduced-motion validation remains pending permission for its
temporary Windows setting change. Neither is waived by this proposal.

## Decision

For UX-13, accept the separately corrected harnesses in `40a1a34` as validation
of unchanged application behavior. Existing functional assertions and budgets
remain mandatory. Require the full reduced-motion test and verified restoration
of the original Windows setting before closing the package.

## Consequences

After acknowledgement, record this narrow exception in UX-PLAN.md and retain
all earlier failed gate/timing results. No product change, weakened assertion,
performance tolerance, skipped gate acceptance, or Native AOT claim is authorized.

## Alternatives considered

- Keep the literal script criterion: baseline harness failures prevent closure
  despite corrected functional checks passing against unchanged app behavior.
- Waive affected gates: rejected because it would remove functional proof.
