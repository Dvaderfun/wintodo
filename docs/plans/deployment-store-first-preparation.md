# Store-first release preparation

Prepared: 2026-11-09 against source `44a4ef6` plus recorded owner decisions.
Updated 2026-11-20: **All five required sections of the [Partner Center draft](#partner-center-draft--20-november-2026) are Complete.** The listing is configured as Free, public and discoverable. **Submit for certification** is enabled; nothing has been submitted and publication remains manual.
Authority: [ADR-019](../adr/ADR-019-store-first-source-available-release.md) and the owner's [public launch decision](../adr/ADR-022-public-free-store-launch.md).

## Selected inputs

| Input | Value |
|---|---|
| Product display name | Wintodo |
| Official publisher | Univera LLC |
| First distribution route | Microsoft Store MSIX; Microsoft supplies Store signing |
| Store pricing and audience | Free for version 1.0; public and discoverable, without a tester group. No in-app purchases. |
| Support / security / privacy contact | support@univera.app |
| First release tag / MSIX version | `v1.0.0` / `1.0.0.0` on the new reserved production family |
| Updates | Stable production family and increasing versions; actual data-preservation tests required |
| First-party source policy | [Source-available](../../LICENSE.md): the individual author retains the official brand and monetization; free self-builds and free unbranded community copies permitted. Univera LLC is the authorized publisher. |
| Repository visibility | Public after final privacy, attribution and licence review |
| Artwork | Original Glide icons; [owner authorized keeping eight backgrounds in the draft](../adr/ADR-023-first-party-rights-and-draft-artwork.md). Their origins and redistribution terms are unverified. |

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

Both [inspected packages](deployment-readiness.md#2026-11-19--glide-packages-and-store-gallery) carry the three identity fields and `1.0.0.0`. The
assigned Store URL does not establish public availability. The package SID and
MSA app ID are not manifest fields; the Store association does not replace the
separate Entra client used for the optional Microsoft To Do import.

## Preparation and acceptance order

1. Apply the first-party source licence and preserve supplier notices. The
   owner's draft-upload decision does not supply missing artwork rights for
   public distribution. Keep the privacy text accurate for the final build.
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
   notes. Prepare the public, free submission for owner approval
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

1. Keep **Public audience**, **available and discoverable**, retail price `0`, and manual publication. There is no first-release tester group.
2. Compare the final reviewed source, upload hashes, manifest and dependency reports. Check the [English listing and certification notes](../releases/store/en-US/README.md), saved [privacy policy](../releases/PRIVACY.md), and saved age ratings against those packages. The owner separately submits the reviewed app for certification and decides when to publish.
3. When the certified packages are available, record their versions, PFN, signatures and hashes on clean x64 and native ARM64 systems. Test a higher-version update against this baseline. A public draft or a certified submission held for publication cannot by itself prove installation from the customer Store.

## Reviewed main packages — 20 November 2026

[PR #5](https://github.com/Dvaderfun/wintodo/pull/5) merged as
`98476b1be027637a50e404143f2c2611c84bf9f6` after its [final CI check](https://github.com/Dvaderfun/wintodo/actions/runs/44628952053).
The reviewed tree and merge tree are identical. Local Debug and Release x64 builds
were clean; CI confirmed 707/707 unit tests, 206 script tests, zero NuGet
advisories across nine projects and no source/history secret-scan findings.

The [manual main run](https://github.com/Dvaderfun/wintodo/actions/runs/44630077895)
is the source of the final unsigned x64 and ARM64 candidates. Each architecture's
`SafeTestResults/release-package.json` binds its package SHA-256; its dependency
and toolchain reports bind the source and target. The [PR handoff](https://github.com/Dvaderfun/wintodo/pull/5)
records the comparison with the uploaded candidates and the portal disposition.
The code, binary assets, lockfiles and packaging inputs match the earlier
`aa59ff4` build, so the English gallery does not need new captures.

If the final bytes differ, replace only the two MSIX in the existing
**Submission 1 → Packages**. Use the filenames containing `98476b1` to
distinguish the uploads, verify x64 and ARM64 at `1.0.0.0`, and save the
Desktop selection. Preserve the listings, IARC ratings and manual publication
hold. An Actions result or a local hash is not a readback from Partner Center.
The owner controls **Submit for certification** and subsequent publication.

## Partner Center draft — 20 November 2026

[Submission 1](https://partner.microsoft.com/en-us/dashboard/products/9PF4DX2BSF0X/overview) (`1152921505702452815`) remains **In draft**. The owner chose a free public launch without a private pilot. No certification or publication action was taken.

| Section | Readback after saving |
|---|---|
| Pricing and availability — **Complete** | Retail price `0`, **Public audience**, **available and discoverable in the Microsoft Store**. |
| Properties — **Complete** | Productivity. The portal's **Provide privacy policy text** option contains the [approved English policy](../releases/PRIVACY.md). All 4,556 characters matched after reopening. No external policy URL is needed for this draft. |
| Packages and Store listings — **Complete** | Desktop x64/ARM64 `1.0.0.0` candidates and both verified English galleries; see the [package and listing readbacks](#partner-center-draft--19-november-2026). |
| Age ratings — **Complete** | The publisher accepted and saved the IARC declaration. The reloaded submission overview confirms completion; the saved ratings include **PEGI 3** and **ESRB E (Everyone)**. All Other App Types; nine factual responses, no physical distribution or separate board ratings. |
| Submission options | Reopened after changing the audience and price: **Don't publish this submission until I select Publish now** is on; automatic publication is off. |

This version has no native chat, published user feed, promoted content catalogue or in-app purchases. Imported tasks, private attachments and sync through the user's own provider are distinct from those features. Revisit the relevant questionnaire answers and privacy text if a later version adds paid digital goods or changes these data flows.

At this pre-merge readback, **Submit for certification** was enabled. Both local upload hashes matched the original handoff, and both MSIX contained the exact eight JPEGs listed below. Remote `main` (`44a4ef6`) still used the old test-family manifest. The uploaded production candidates were from `aa59ff4`; PR checks passed and skipped release packaging by design. See the [later main build and upload handoff](#reviewed-main-packages--20-november-2026).

## Partner Center draft — 19 November 2026

On 19 November, [Wintodo in Partner Center](https://partner.microsoft.com/en-us/dashboard/products/9PF4DX2BSF0X/overview) showed **Submission 1** (`1152921505702452815`) **In draft**. The following is the historical snapshot before the owner chose a free public launch:

| Portal section | Saved state | Needed before certification |
|---|---|---|
| Packages — **Complete** | `Wintodo-x64.msix` and `Wintodo-arm64.msix`, both `1.0.0.0`. Desktop is on; Xbox, Mobile, Team, Mixed Reality and automatic future device families are off. The upload showed four Xbox compatibility warnings for the startup-task and toast-activation extensions. | These are the [unsigned candidates from `aa59ff4`](deployment-readiness.md#2026-11-19--glide-packages-and-store-gallery). Compare the upload with the final reviewed `main` artifacts and replace it if the bytes differ. |
| Store listings — **Complete** | `en-US` and `en` each have the [approved English copy](../releases/store/en-US/README.md), five desktop screenshots with captions, the Glide logo and checklist hero. The server export matched 20 text/settings fields and contained seven distinct hosted images per locale. The five thumbnails were checked against the originals in both reopened editors. | Recheck artwork against the final build if its UI changes. |
| Pricing and availability — **Incomplete** | **Private audience**, no tester group selected; **Make this product public on** is off. No valid base price is configured. | Set the owner's Store price and choose the named tester group. Until a group is selected, no customers can acquire the private release. |
| Properties — **Incomplete** | Primary category **Productivity**. The declared capabilities require a privacy policy; the URL field is empty. | Supply the approved policy. The [local privacy text](../releases/PRIVACY.md) is still a review draft. |
| Age ratings — **Not started** | The portal offers the IARC questionnaire or an existing IARC certificate/global rating ID. | Obtain the publisher's approved answers or an applicable existing rating. |
| Submission options and testing notes | **Don't publish this submission until I select Publish now** was selected after reopening. **Additional Testing Information** returned the exact 497-character English reviewer note after reload. | The owner controls submission for certification and subsequent publication. |

The uploads used an authenticated Partner Center browser session. No `pcenter` credentials or account permissions were changed. The portal statuses describe this draft only. The eight background-image rights, first-party licence, reviewed release artifacts and signed customer install/update checks remain separate release gates.

## Artwork evidence from the current repository

| Area | Observed evidence | Disposition |
|---|---|---|
| In-app icons | Existing `ShellPage.xaml` / `TaskListView.xaml` use `FontIcon` and `SymbolIcon`; Windows font rendering is supported by Microsoft's guidance | Keep OS/font control use. No tracked `.ttf`, `.otf`, `.woff` or `.woff2` files found. Do not copy/redistribute a Windows font file without its own rights. |
| Package logos | Both unsigned MSIX contain the eight approved PNGs and six-frame ICO for [Glide](../adr/ADR-021-store-package-icon-proposal.md). The previous template icons and their notice are retired; other supplier notices are preserved. | Check actual Start, taskbar and splash presentation with the Store-delivered packages. |
| Backgrounds | [Coast, Field, Forest, Graphite, Indigo, Mist, Petals and Sunrise](../../src/Wintodo.App/Assets/ListBackgrounds) entered in `1813855` without a source or licence record | Keep in the private draft by owner instruction. No licence or origin has been established. Handle complaints through support@univera.app; see [ADR-023](../adr/ADR-023-first-party-rights-and-draft-artwork.md). |

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

For exact Glide package hashes and the earlier hosted restore evidence, see the
[deployment register](deployment-readiness.md#2026-11-19--glide-packages-and-store-gallery).
The unsigned candidates do not establish customer installation, licensing or
Store certification. The saved draft has not been submitted for certification or published.
