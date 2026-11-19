# ADR-021 — Original package icons for the Store

- **Status:** proposed; owner rejected the first sketch. Selection of the new artwork is pending.
- **Date:** 2026-11-19
- **Scope:** the eight package PNGs and `AppIcon.ico` only.

Wintodo ships the exact default WinUI template icons. Their MIT licence is
documented, but Microsoft's [desktop branding validation](https://learn.microsoft.com/en-us/windows/uwp/debug-test-perf/windows-desktop-bridge-app-tests#22-branding-validation)
checks for default images and recommends replacing them. The certification
test has not been run on these packages.

The owner asked for a more open Fluent silhouette and Store images without
decorative gradients. The earlier check on stacked tiles is retired.

**Proposal:** [Lift](../plans/assets/store-icon-proposal.svg), an open W that
folds into a check. [Glide](../plans/assets/store-icon-glide.svg) is a simpler
green check with a split wingtip. Both are original transparent vectors built
from solid faces. The [comparison](../plans/assets/store-icon-proposal.png)
shows each on light and dark and at 16, 24, 32 and 48 pixels.

Each design has its own complete set of eight correctly sized PNGs (including
a white lock-screen silhouette) and one six-frame ICO. The local review kits
are `artifacts/store-1.0.0-rc/Wintodo-package-icon-proposal.zip` (Lift) and
`Wintodo-package-icon-Glide.zip` in the same directory. Neither changes the
inspected MSIX candidates. The Store screenshot ZIP contains neither icon kit.

If accepted, this permits the package-icon replacement under
[ADR-019](ADR-019-store-first-source-available-release.md), keeping the family
and asset paths stable. Rebuild both MSIX files and verify the new hashes,
artwork and installed appearance. The decision on the eight list background
JPEGs is separate.
