# ADR-021 — Original package icons for the Store

- **Status:** accepted — Glide
- **Date:** 2026-11-19
- **Scope:** the eight package PNGs and `AppIcon.ico` only.

The previous MSIX candidates shipped the default WinUI template icons.
Microsoft's [desktop branding validation](https://learn.microsoft.com/en-us/windows/uwp/debug-test-perf/windows-desktop-bridge-app-tests#22-branding-validation)
checks for default images and recommends replacing them.

The owner asked for a more open Fluent silhouette and Store images without
decorative gradients. The earlier check on stacked tiles is retired.

**Decision:** use [Glide](../plans/assets/store-icon-glide.svg), the original
teal check with a split wingtip, at all nine package asset paths. The owner
selected it because it reads more clearly; Lift's W suggested Microsoft Word.
The [review comparison](../plans/assets/store-icon-proposal.png) records the
exact choices at 16, 24, 32 and 48 pixels on light and dark surfaces.

The eight PNGs (including a white lock-screen silhouette) and six-frame ICO
are the approved review files, byte for byte. This is the artwork exception to
[ADR-019](ADR-019-store-first-source-available-release.md) for those files only.
Remove only the template-artwork notice that accompanied them, preserving
every other supplier notice. Keep the package family, version and asset paths
stable. Installed appearance on the Store-signed builds and rights to the
eight list background JPEGs are separate release checks.
