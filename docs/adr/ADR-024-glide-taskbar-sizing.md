# ADR-024 — Enlarge Glide at taskbar sizes

- **Status:** accepted
- **Date:** 2026-11-20
- **Supersedes:** [ADR-021](ADR-021-store-package-icon-proposal.md) only for the small shell artwork and ICO frames.
- **Owner acknowledgement:** the owner asked for Glide to look closer in size to Microsoft To Do on the taskbar.

The original 24 px Glide asset gives the check about 20 × 15 px of visible area. On the taskbar, it reads noticeably smaller than adjacent app marks.

Keep the Glide master and tighten the crop around its silhouette for taskbar sizes. Provide the unplated variants for light and dark taskbars at the [Windows taskbar sizes](https://learn.microsoft.com/en-us/windows/apps/design/style/iconography/app-icon-construction#app-icon). Use the same crop in the 16, 24, 32 and 48 px window ICO frames. The approved Store, tile, splash and lock screen images and the larger ICO frames retain their original artwork. Package identity and version remain unchanged.

Existing packages keep the icon they shipped with. Updated packages must be rebuilt and checked on an installed Windows system before release.
