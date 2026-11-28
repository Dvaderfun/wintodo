# ADR-027 — Relicence first-party source code under MPL-2.0, reserve brand assets, and replace list backgrounds with verified CC0 photographs

- **Status:** accepted (pending recorded external contributor consent in [#14](https://github.com/Dvaderfun/wintodo/issues/14) before public release / merging to `main`)
- **Date:** 2026-11-28
- **Supersedes:** the first-party source-available licence selection in [ADR-019](ADR-019-store-first-source-available-release.md) §3, and both the source-licence terms and the unverified-background-retention clause in [ADR-023](ADR-023-first-party-rights-and-draft-artwork.md). Store-first distribution under Univera LLC, individual author ownership of first-party rights, and reserved brand/visuals remain unchanged.
- **Owner acknowledgement:** the owner selected **Mozilla Public License 2.0 (`MPL-2.0`)** for Wintodo's first-party source code and documentation, with separate brand and visual asset reservation in [TRADEMARKS.md](../../TRADEMARKS.md), to support open-source adoption alongside a planned paid **Wintodo Cloud Sync** freemium service, and authorized replacing the eight unverified list backgrounds with verified-licence photographs and requesting contributor consent on GitHub.

## Context

[ADR-019](ADR-019-store-first-source-available-release.md) and [ADR-023](ADR-023-first-party-rights-and-draft-artwork.md) adopted a custom source-available licence (`Wintodo Source-Available Licence 1.0`) to protect the Wintodo brand and prevent third-party commercial clones, and retained eight list background JPEGs whose original provenance was unknown.

In practice, Wintodo's planned monetization is a **freemium hosted Cloud Sync service** (keeping the offline Windows app and bring-your-own CalDAV sync free). Under that model:

1. A bespoke non-OSI licence creates friction with privacy-minded Windows users, open-source directories, enterprise compliance checks, and potential contributors, without adding meaningful protection for a server-side Cloud Sync subscription.
2. Protecting the **Wintodo** name, **Glide** icons, and Store identity does not require a source-available code licence: visual assets are protected automatically by copyright and the name is protected by common-law trademark rights in commerce, both of which are explicitly excluded from `MPL-2.0` (§2.3).
3. Wintodo bundles Microsoft's Windows App SDK in a self-contained MSIX. Microsoft's Windows App SDK licence terms (`src/Wintodo.App/Assets/ThirdPartyNotices.txt`) include an anti-viral-copyleft clause prohibiting combining Microsoft distributables with licences that require Microsoft components or other parts of the software to be disclosed in source form. `MPL-2.0` is strictly file-level copyleft (§3.3 *Larger Works*), making it cleanly compatible with the Windows App SDK, the Microsoft Store, and any future private client/billing modules without a Contributor License Agreement (CLA) or custom linking exceptions.
4. Shipping a clean open-source repository and Microsoft Store release also requires replacing the eight unverified list background JPEGs from `1813855` with assets that have documented, verifiable redistribution rights.

## Decision

1. **Adopt `MPL-2.0` for first-party code and documentation.** Replace root `LICENSE.md` with the verbatim official Mozilla Public License Version 2.0 in [LICENSE](../../LICENSE) so GitHub and SPDX tooling detect `MPL-2.0` cleanly.
2. **Reserve brand and visuals separately.** Publish [TRADEMARKS.md](../../TRADEMARKS.md) explicitly reserving the **Wintodo** name, **Glide** icon/logo files, Store identity, and promotional visuals as All Rights Reserved to the individual author (with **Univera LLC** authorized as official Store publisher). Unofficial forks and builds must rename, swap icons/branding, and use a distinct MSIX package identity.
3. **Replace all eight list backgrounds with verified `CC0 1.0` photographs.** Replace `Coast.jpg`, `Field.jpg`, `Forest.jpg`, `Graphite.jpg`, `Indigo.jpg`, `Mist.jpg`, `Petals.jpg`, and `Sunrise.jpg` under `src/Wintodo.App/Assets/ListBackgrounds/` with verified **CC0 1.0 Universal** photographs from Wikimedia Commons, normalized to metadata-free `2560x1600 (16:10)` JPEGs (JFIF `0xFFE0` only, zero EXIF/IPTC/XMP), and record exact source links, credits, and SHA-256 hashes in [PROVENANCE.md](../../src/Wintodo.App/Assets/ListBackgrounds/PROVENANCE.md).
4. **Establish inbound = outbound `MPL-2.0` contributions.** Add [CONTRIBUTING.md](../../CONTRIBUTING.md) stating that contributions are licensed under `MPL-2.0` with no CLA required.
5. **Keep future paid Cloud Sync server code separate.** The planned hosted Wintodo Cloud Sync backend will live in a separate private repository; client-side sync integration may live in `Wintodo.Sync` alongside free local storage and BYO CalDAV.
6. **Blocking prerequisite before public release / merging to `main`:** record explicit written consent to relicense under `MPL-2.0` from external contributor `Chintalapati Venkata Rama Rahul` (`@ramarahul`, commits `6a67c4a`, `bfe05eb`, `9fb7711`, `a301e1c`). Consent has been requested in [GitHub issue #14](https://github.com/Dvaderfun/wintodo/issues/14).

## Consequences

- Wintodo qualifies as true OSI-approved open source (`MPL-2.0`), enabling standard SPDX tooling, OSS directory listings, and lower-friction community contributions.
- All eight list background assets now have verified `CC0 1.0` provenance and stripped metadata, closing the open background-artwork provenance item from [ADR-023](ADR-023-first-party-rights-and-draft-artwork.md) and DEP-13/17.
- Anyone modifying Wintodo's `MPL-2.0` source files and distributing binaries must publish their modifications to those files under `MPL-2.0`, while being barred from reusing the Wintodo name or Glide visuals.
- Unlike the previous source-available licence, compliant third parties may legally redistribute unbranded commercial forks built from the `MPL-2.0` code. Wintodo differentiates through official Store distribution, brand trust, and the managed Cloud Sync service.
- Do not push the relicensed tree to `main` or announce the `MPL-2.0` switch publicly until external contributor consent in [#14](https://github.com/Dvaderfun/wintodo/issues/14) is confirmed.

## Alternatives considered

- **Keep `Wintodo Source-Available Licence 1.0`:** retains legal prohibition on all commercial forks, but forfeits OSI open-source credibility, automated SPDX compliance, and contributor appeal while leaving bespoke legal edge cases.
- **`GPL-3.0-or-later` with Windows App SDK / Store §7 exception:** strongest anti-clone copyleft, but introduces linking-exception maintenance around Microsoft's Windows App SDK / Microsoft Store terms and requires a CLA if proprietary client modules are ever needed alongside Cloud Sync.
- **`MIT` / `Apache-2.0`:** maximum permissiveness, but allows closed-source ad-wrapped Microsoft Store clones with zero obligation to publish modifications back.
- **`FSL-1.1` (Functional Source License):** standardized source-available terms converting to Apache/MIT after two years, but still non-OSI and unnecessary when monetization lives in a hosted Cloud Sync service.
