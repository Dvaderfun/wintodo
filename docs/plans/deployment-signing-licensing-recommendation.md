# Wintodo signing, distribution and licensing recommendation

**20 November decision:** the individual author is the rights holder, Univera
LLC is the authorized publisher, and the [source licence](../../LICENSE.md)
is active. [ADR-023](../adr/ADR-023-first-party-rights-and-draft-artwork.md)
records the artwork exception for the private draft. The research below
describes the options as they stood on 9 November.

Prepared: 2026-11-09 against source `44a4ef6`.
Updated after the owner's follow-up decisions: **Store-first under Univera LLC,
Store-managed MSIX signing and source-available commercial-redistribution
restrictions SELECTED; copyright authority, exact licence and asset rights
remain pending.** See [ADR-019](../adr/ADR-019-store-first-source-available-release.md).
No production licence or package identity is applied here.

## Recorded owner choices

- Keep **Wintodo**, describing a native Windows to-do app. Keep the current
  icons and eight backgrounds; their source/redistribution evidence remains
  required by DEP-17. No artwork replacement is selected.
- Make the repository public after privacy, attribution and licensing review.
  The visibility transition and enforced protection are not executed here.
- A fresh first production installation is acceptable. Existing test packages
  need not update into it. Future production updates must preserve users' data.
- Use stable tags `vMAJOR.MINOR.PATCH`, mapped to `MAJOR.MINOR.PATCH.0`, starting
  with `v1.0.0` / `1.0.0.0` on a new production identity. This is not a downgrade
  of the existing test identity/version `1.0.0.5`.
- Aim for freemium official distribution while allowing free personal builds
  from source. Exact premium features and entitlement implementation are a
  separate product decision, outside this release-readiness research.
- The selected official publisher is **Univera LLC**, with public support,
  security and privacy contact **support@univera.app**. Copyright ownership and
  public Git attribution remain to be confirmed; publisher selection is not a
  transfer of rights to the company.

## Distribution and signing

**Selected: Store-first MSIX under Univera LLC, with public source after review
and direct production downloads considered separately.**
This fits an existing organization Store account and avoids purchasing a
Windows signing certificate for the Store release. It changes the first route
selected in [ADR-018](../adr/ADR-018-direct-msix-first-release.md); the owner
acknowledged this change explicitly and ADR-019 records it. Its launch-control
policy and blocking final-package requirements remain preserved.

| Route | Signing and cost | Requirements / tradeoffs |
|---|---|---|
| Microsoft Store MSIX | Microsoft signs the certified package; no purchased CA certificate needed for submission | Reserve Wintodo in Partner Center, use its exact identity/publisher, prepare Store assets and certification/deployment evidence. This does not provide a signing identity for independently built GitHub downloads. |
| Direct MSIX with Azure Artifact Signing | Basic currently documented at USD 9.99/account/month, including 5,000 signatures; overage USD 0.005/signature | Public Trust identity validation, eligible legal entity, Azure subscription and protected signer access. Prices/eligibility must be confirmed before purchase. |
| Direct MSIX with a commercial CA | Obtain a current OV code-signing quote including token/cloud-HSM and CI access costs | Useful if Artifact Signing eligibility fails. Publicly trusted code-signing keys require compliant hardware/cloud protection; do not assume an exportable PFX is available. |

An Android APK signing key does not by itself provide a Windows-trusted
code-signing certificate. MSIX public delivery needs an appropriate trusted
code-signing chain and an exact publisher/subject match, or Store signing.
No existing Android key or development certificate was inspected or reused.

The current Artifact Signing service quickstart lists organizations in the
United States, Canada, EU, UK, Australia, New Zealand, Japan, South Korea,
Singapore, Switzerland, Norway and Israel; individuals are limited to the US
and Canada. Entity location and successful verification remain unknown here.
The Windows signing overview has a narrower region summary, and a related
Windows overview still mentions a three-year tax-history requirement which
the fetched current service quickstart/FAQ do not state. Confirm current
validation conditions for the selected entity rather than declaring it eligible
from these inconsistent summaries. No Azure resource or billing change ran.

For direct delivery, select the timestamp service, credential custodian and
renewal owner before signing. For Store delivery, obtain the actual Partner
Center identity; the human-readable LLC name is not a substitute for its
manifest publisher identifier. Neither route removes the remaining privacy,
dependency, native ARM64 and installed-update acceptance requirements.

## Licence and freemium

**Selected: free self-builds with commercial redistribution reserved to the
authorized rights holder.** GPLv3/MIT were not selected. The
[source-available licence draft](wintodo-source-available-license-draft.md)
provides reviewable terms; copyright authority, free community sharing and
component/Store compatibility remain unresolved before applying it.

| Choice | Personal builds | Other distributors | Fit |
|---|---|---|---|
| GPLv3 | Free; private modifications need not be published | May sell copies/forks; distributed covered derivatives must provide corresponding source and GPL rights | Preserves open-source derivatives while allowing your official paid/freemium releases. It does not reserve commercial distribution to you. |
| MIT | Free | May sell and produce closed-source forks with the required notice | Simplest permissive choice if closed commercial forks are acceptable. |
| Source-available personal-use / commercial licence | Can explicitly permit free personal builds | Can reserve specified commercial redistribution rights | Fits exclusive commercial distribution, but is not an OSI open-source licence. Exact terms and the boundary between personal, work/internal and commercial use need drafting and review. |

Public code and monetization are compatible. Under GPL/MIT, other people can
build and distribute a free version, including permitted changes to local
premium gates. The business advantage would be official trusted distribution,
updates, support and any separately provided service value. Do not promise
exclusive paid local features while granting those source rights.

GPLv3 does not require publishing private/internal modifications solely because
they are used. When distributing a covered binary, corresponding-source and
licence obligations apply. The official Store EULA/DRM and shipped third-party
terms must be reviewed for compatibility; Store acceptance alone does not prove
GPL compliance. A separate commercial licence for owner-controlled code is
possible only with the necessary rights over contributions; it cannot override
third-party obligations. No contributor agreement or alternative licence is
selected in this research.

The owner selected the commercial redistribution restriction. Free community
sharing and the authority to grant commercial licences remain to be settled.
Artwork has its own rights and must not be silently relicensed with first-party
code; its unknown source is still a DEP-17 release blocker.

## Identity, version and update acceptance

The new production package family must be selected before release `1.0.0.0`.
For Store use its reserved identity; for direct MSIX use the approved identity
and validated signing subject. Future releases retain that package family and
increment versions, with forward schema migrations and actual installed-update
tests covering tasks, attachments, settings and credential access. A certificate
renewal must also preserve the approved identity/update path.

Fresh installation permission does not authorize uninstalling the owner's test
app, copying its profile, or forcing a lower version onto its existing family.
Cross-route identity/data continuity must be explicitly designed and tested if
both Store and direct production channels are later offered. The requirement
to preserve data on updates does not imply preserving it after uninstall.

## Sources and verification

First-party documentation retrieved on 2026-11-09:

- [Microsoft Windows code-signing options](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)
- [Microsoft MSIX signing overview](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview)
- [Artifact Signing quickstart and current eligibility](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart)
- [Artifact Signing FAQ and validated publisher names](https://learn.microsoft.com/en-us/azure/artifact-signing/faq)
- [Artifact Signing pricing tiers](https://learn.microsoft.com/en-us/azure/artifact-signing/how-to-change-sku)
- [Microsoft Store setup and non-gaming commerce choices](https://learn.microsoft.com/en-us/windows/apps/publish/get-started)
- [MSIX app update constraints](https://learn.microsoft.com/en-us/windows/msix/app-package-updates)
- [GNU GPLv3 terms](https://www.gnu.org/licenses/gpl-3.0.en.html)
- [GNU FAQ: charging for GPL copies](https://www.gnu.org/licenses/gpl-faq.html#DoesTheGPLAllowMoney)
- [GNU FAQ: private modifications](https://www.gnu.org/licenses/gpl-faq.html#GPLRequireSourcePostedPublic)
- [MIT licence text](https://choosealicense.com/licenses/mit/)
- [OSI definition: commercial use and redistribution](https://opensource.org/osd)

Research and documentation checks are separate from release acceptance. No
build, runtime/UI gate, installation, trust, signing, Store reservation, remote
setting, publication or expenditure occurred. M9 remains owner-deferred.
