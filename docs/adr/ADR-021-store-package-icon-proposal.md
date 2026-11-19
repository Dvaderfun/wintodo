# ADR-021 — Original package icons for the Store

- **Status:** proposed; owner acknowledgement pending
- **Date:** 2026-11-19
- **Scope:** the eight package PNGs and `AppIcon.ico` only.

Wintodo ships the exact default WinUI template icons. Their MIT licence is
documented, but Microsoft's [desktop branding validation](https://learn.microsoft.com/en-us/windows/uwp/debug-test-perf/windows-desktop-bridge-app-tests#22-branding-validation)
checks for default images and recommends replacing them. The certification
test has not been run on these packages.

**Proposal:** use the [folded-check artwork](../plans/assets/store-icon-proposal.svg)
shown in the [light, dark and taskbar preview](../plans/assets/store-icon-proposal.png).
All nine replacement files were prepared with the current dimensions and ICO
frame sizes. The local review bundle is
`artifacts/store-1.0.0-rc/Wintodo-package-icon-proposal.zip`; it is separate
from the inspected MSIX candidates and from the English listing kit.

If accepted, this permits the package-icon replacement under
[ADR-019](ADR-019-store-first-source-available-release.md), keeping the family
and asset paths stable. Rebuild both MSIX files and verify the new hashes,
artwork and installed appearance. The decision on the eight list background
JPEGs is separate.
