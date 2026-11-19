# Store-first release preparation

Prepared: 2026-11-09 against source `44a4ef6` plus recorded owner decisions.
Updated 2026-11-19: **Store reservation confirmed in both unsigned MSIX files.** The English
[gallery and listing copy](../releases/store/en-US/README.md) are prepared.
Asset rights, legal text, controlled submission and customer tests remain open.
Authority: [ADR-019](../adr/ADR-019-store-first-source-available-release.md).

## Selected inputs

| Input | Value |
|---|---|
| Product display name | Wintodo |
| Official publisher | Univera LLC |
| First distribution route | Microsoft Store MSIX; Microsoft supplies Store signing |
| Support / security / privacy contact | support@univera.app |
| First release tag / MSIX version | `v1.0.0` / `1.0.0.0` on the new reserved production family |
| Updates | Stable production family and increasing versions; actual data-preservation tests required |
| First-party source policy | Source-available: free self-builds, commercial redistribution reserved; legal text not yet applied |
| Repository visibility | Public after final privacy, attribution and licence review |
| Artwork | Retain only with established rights; current background sources unknown |

## Owner-supplied identity handoff

The owner supplied these values from Wintodo's **Product identity** page:

| Field | Reserved value |
|---|---|
| Package/Identity/Name | `47371DermenzhyStudio.Wintodo` |
| Package/Identity/Publisher | `CN=03D194EC-2260-464A-80F9-EF22C07F62D0` |
| Package/Properties/PublisherDisplayName | Univera LLC |
| Package family name | `47371DermenzhyStudio.Wintodo_rg7xtn0gdgdnp` |
| Package SID (Store/WNS reference) | `S-1-15-2-1435222867-471200497-1038821137-3538736367-1189327619-1405212333-575449426` |
| Store ID / destination | [9PF4DX2BSF0X](https://apps.microsoft.com/detail/9PF4DX2BSF0X) |
| Windows Store link | `ms-windows-store://pdp/?productid=9PF4DX2BSF0X` |
| MSA app ID (Store registration) | `992a758a-6a5a-4c8b-9a10-db62233a7eb4` |

Both [inspected packages](deployment-readiness.md#2026-11-19--pr-ci-and-local-store-candidates) carry the three identity fields and `1.0.0.0`. The
assigned Store URL does not establish public availability. The package SID and
MSA app ID are not manifest fields; the Store association does not replace the
separate Entra client used for the optional Microsoft To Do import.

## Preparation and acceptance order

1. Confirm first-party ownership/licence text, retained asset
   rights and public attribution. Finalize the privacy/install/support drafts
   using the selected contact. Keep unknown artwork rights as blocking evidence.
2. Inspect the generated packages against the identity above, desktop version
   and existing Windows integrations. Leave the old development family installed.
3. Build the clean reviewed source for x64 and ARM64 using SDK-native packaging.
   Partner Center's [Packages page](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/upload-app-packages)
   accepts individual `.msix` files for both architectures and recommends
   `.msixupload` for Windows submissions. If an upload container is used,
   hash it separately and verify the embedded MSIX matches the reviewed bytes.
   Successful local packaging does not establish portal acceptance.
   Preserve exact upload hashes, source/tool/dependency evidence and privacy
   reports. Keep private symbols/recipes out of public source-release assets;
   separately review any optional private symbol submission.
4. Prepare the Store listing, permitted logos/screenshots with synthetic tasks,
   age ratings, privacy/customer licence text, support information and review
   notes. Prepare the first private-audience submission for owner approval
   as described below. An unsigned handoff PASS does not certify
   the Store upload or grant submission/publication authority.
5. Use an approved staged/certified Store path to obtain and inspect the actual
   signed packages. Record submission/certification IDs, delivered version/PFN,
   architecture, package/signature information and source/content binding.
   Microsoft processing may alter bytes; verify those resulting packages rather
   than reusing unsigned hashes or direct-signing continuity results.
6. Run clean customer install/update/uninstall and all remaining functional,
   privacy, integration, live-account/manual and native ARM64 gates on the
   actual signed Store builds. Prepare a higher-version staged update to prove
   task/attachment/settings/credential continuity; the first `1.0.0.0` package
   alone cannot prove future update behavior. Keep unrun gates pending and M9
   performance owner-deferred.
7. Prepare the complete reviewed Store submission/publication state and GitHub
   source/release metadata. Obtain publication and concrete source-visibility
   authorization after final review, then publish only those reviewed results.
   Verify the customer Store listing/version and public source metadata.

Direct GitHub production MSIX downloads, production CA purchase and signing
secrets are deferred to a separate route. Development test signing remains an
independent test concern. DEP-18/19 still require authenticated release inputs,
Store-signed resulting packages and actual deployment evidence.

## Controlled handoff

On an eligible GitHub plan, require a reviewed PR and the `build-and-test`
status on `main`, block deletion and force-push, and restrict creating,
rewriting or deleting `v*` tags to the release operator. Verify the effective
rules before setting the reviewed main SHA. The private repository's ruleset
API returned HTTP 403 on 19 November; the tag job rejects unprotected refs.
The read-only CI workflow has no Partner Center credential or upload step.

For the first submission of Store ID `9PF4DX2BSF0X`:

1. In **Pricing and availability → Visibility → Audience**, choose
   [**Private audience**](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/visibility-options#private-audience)
   and the known user group for the x64/ARM64 testers' personal Microsoft
   accounts. Leave **Make this product public on** unset and select manual
   publication. A public listing with **Direct link only** still permits others
   with the link to acquire the app.
2. Check the source SHA, both upload hashes, manifest and dependency reports.
   Attach the [English listing and certification notes](../releases/store/en-US/README.md),
   approved privacy URL and age ratings. Record the submission ID. After
   certification and approval for this private session, publish **to that
   group** so the testers can install the Store-signed `1.0.0.0` baseline.
   A submission merely held for publication cannot provide that installation.
3. Record the delivered versions, PFN, signatures and hashes on clean x64
   and native ARM64 systems. Microsoft makes [package flights](https://learn.microsoft.com/en-us/windows/apps/publish/package-flights#create-a-new-package-flight)
   available **after** the first published submission. Use a higher-version
   flight for a subset of the private audience to test update continuity;
   keep the non-flighted `1.0.0.0` available for clean-install checks.
   Move the listing to a public audience only after the remaining gates
   pass and the public submission is approved.

## Artwork evidence from the current repository

| Area | Observed evidence | Disposition |
|---|---|---|
| In-app icons | Existing `ShellPage.xaml` / `TaskListView.xaml` use `FontIcon` and `SymbolIcon`; Windows font rendering is supported by Microsoft's guidance | Keep OS/font control use. No tracked `.ttf`, `.otf`, `.woff` or `.woff2` files found. Do not copy/redistribute a Windows font file without its own rights. |
| Package logos | All eight PNGs and the ICO match the [Microsoft WinUI template](dep13-source-privacy-review.md#2026-11-19--source-of-the-nine-package-icons) byte for byte. The required MIT notice is included. | Microsoft checks for default template artwork. The owner declined the first proposal; [Lift and Glide](../adr/ADR-021-store-package-icon-proposal.md) are available as complete review sets. Select the exact mark before the next package build. |
| Backgrounds | [Coast, Field, Forest, Graphite, Indigo, Mist, Petals and Sunrise](../../src/Wintodo.App/Assets/ListBackgrounds) entered in `1813855` without a source or licence record | Supply their origin and commercial redistribution terms, or select replacements before submission. |

The existing [asset privacy review](dep13-source-privacy-review.md) remains
privacy evidence, not licensing proof. The Store screenshots use synthetic
test captures; the hero illustration is new. Neither relies on the eight
backgrounds shipped in the app. Their redistribution rights remain open.

## Sources and evidence limits

- [Microsoft Store product identity fields](https://learn.microsoft.com/en-us/windows/apps/publish/view-app-identity-details)
- [Store MSIX package requirements and signing](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements)
- [Microsoft Windows signing options](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)
- [Windows font use and redistribution FAQ](https://learn.microsoft.com/en-us/typography/fonts/font-faq)
- [Segoe MDL2 icon control use](https://learn.microsoft.com/en-us/windows/apps/design/iconography/segoe-ui-symbol-font)

For the hosted test results and exact local package hashes, see the
[deployment register](deployment-readiness.md#2026-11-19--pr-ci-and-local-store-candidates).
The unsigned candidates do not establish customer installation, licensing or
Store certification. No submission or public release has taken place.
