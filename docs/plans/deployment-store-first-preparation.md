# Store-first release preparation

Prepared: 2026-11-09 against source `44a4ef6` plus recorded owner decisions.
Updated 2026-11-19: **Store reservation confirmed and applied.** The English
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
| Store ID / destination | [9PF4DX2BSF0X](https://apps.microsoft.com/detail/9PF4DX2BSF0X) |

The desktop manifest carries the three identity fields and `1.0.0.0`. The
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
   notes. Select controlled publication and prepare the precise submission or
   flight session for owner approval. An unsigned handoff PASS does not certify
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

The release operator uses Partner Center's controlled submission/flight for
Store ID `9PF4DX2BSF0X`. Check the source SHA, both upload hashes, manifest
and dependency reports against the reviewed candidate; attach the approved
listing and privacy URL, choose **manual publication**, and record the
submission ID. After certification, retain the delivered identity, version
and hashes for the owner's install/update checks before public availability.

## Artwork evidence from the current repository

| Area | Observed evidence | Disposition |
|---|---|---|
| In-app icons | Existing `ShellPage.xaml` / `TaskListView.xaml` use `FontIcon` and `SymbolIcon`; Windows font rendering is supported by Microsoft's guidance | Keep OS/font control use. No tracked `.ttf`, `.otf`, `.woff` or `.woff2` files found. Do not copy/redistribute a Windows font file without its own rights. |
| Package logos | Eight PNGs and one ICO entered with scaffold commit `1b3d846`; reviewed contact sheet shows generic template placeholders, not proof of Segoe origin | Keep files during preparation; establish the actual template distribution licence and final Store asset acceptance. No logo redesign selected. |
| Backgrounds | Eight JPEGs entered in `1813855`; reviewed archive/metadata inventory has no source/licence record; owner does not know their source | Redistribution rights PENDING. Free availability or a clean privacy scan does not establish commercial redistribution permission. |

The existing [asset privacy review](dep13-source-privacy-review.md) remains
privacy evidence, not licensing proof. No image was altered, removed, copied
from another application or newly published in this preparation.

## Sources and evidence limits

- [Microsoft Store product identity fields](https://learn.microsoft.com/en-us/windows/apps/publish/view-app-identity-details)
- [Store MSIX package requirements and signing](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements)
- [Microsoft Windows signing options](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options)
- [Windows font use and redistribution FAQ](https://learn.microsoft.com/en-us/typography/fonts/font-faq)
- [Segoe MDL2 icon control use](https://learn.microsoft.com/en-us/windows/apps/design/iconography/segoe-ui-symbol-font)

Read-only code inspection used CodeGraph first; XAML/manifest and binary asset
inventory followed where the graph did not return the requested content. The
existing private asset contact sheet was reviewed. Documentation verification
does not close source ownership, asset rights, Store certification or runtime
acceptance. No build, gate, installation, trust, reservation, remote visibility,
submission, purchase or publication occurred.
