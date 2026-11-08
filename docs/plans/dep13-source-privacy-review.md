# DEP-13 source archive and asset privacy review

Prepared: 2026-11-08. Source baseline: `7d2fa21`.

Status: **Independent archive, ignore-rule, fixture and current asset privacy
checks PASS. Attribution policy and final release-source/screenshot acceptance
remain PENDING.** This review does not authorize publication or history changes.

## Changes and proof

The root `.gitignore` now excludes explicitly named repo-local secrets, disposable
exports and scratch SQLite files, including WAL/SHM companions. Root environment
examples/templates stay visible. Rules are rooted so future intentional database
or key fixtures under `tests/fixtures/` are not hidden. All 23 positive/negative
`git check-ignore --no-index` checks pass; no sample secret or database was created.

The `.gitattributes` archive rule excludes only `.codegraph/`, a local indexing
scaffold with no build input. Shared agent skills and configuration remain in
the source archive. The current `.mcp.json` contains a public documentation
endpoint and no credential; its presence is not a secret finding. Required
third-party notices remain included.

`git archive --worktree-attributes HEAD` applies the reviewed archive rule to the
baseline source. Its 444 files differ from the ordinary 445-file baseline archive
only by omission of `.codegraph/.gitignore`. There are no named generated output,
secret, database or package entries. All 19 fixture files and the existing
`ThirdPartyNotices.txt` have identical bytes before/after archive filtering.
The raw Graph attachment SHA-256 is identical in the Git blob, archive and
checkout: `c7f22aaa2cc9c0db923c3e57b970e91969bb40a19cb585526738a02c53c88620`.

Pinned Gitleaks 8.30.1 finds zero secrets in this safely extracted reviewed
archive. Its executable was compared byte-for-byte with the executable in the
previously verified release ZIP, whose SHA-256 remains
`d29144deff3a68aa93ced33dddf84b7fdc26070add4aa0f4513094c8332afc4e`.
Scanner output was redacted and its result validated by `Test-CiSecurity.py`.
Existing reachable-history results remain separate evidence; this archive check
does not replace the integrated scan of newly staged work or the final commit.

## Source and fixture findings

The targeted current tracked-text review found no private-key header. All
observed identifying/credential-shaped markers have an explicit disposition:

| Inventory | Count | Disposition |
|---|---:|---|
| Absolute profile-path matches | 10 in six files | Nine synthetic negative-test markers and one scanner regex; no actual profile path found |
| Credential-bearing URL | 1 | Deliberately rejected synthetic Graph destination test |
| Email-shaped matches | 29 in eleven files | 24 reserved-example addresses, three URL-userinfo test inputs, two generic Outlook placeholders in the import plan |
| Distinct Git authors/committers | One each | Values withheld; owner public-attribution policy pending |

The iCalendar README identifies minimized, sanitized public-export/documentation
fixtures; the Graph README identifies hand-written responses with example-domain
identities. The perf fixture is SQL seed source and the share fixture is C++ test
source. Reviewed fixture content agrees with those descriptions. The Apple
fixture retains a public-landmark location as an interoperability test; it is
not evidence about a real account or device location. No installed database,
PasswordVault, private export or unrelated local/session data was inspected.

## Raster assets and remaining provenance work

All 17 tracked raster assets were inventoried and visually inspected in a
generated contact sheet: eight square photographic/texture backgrounds, eight
PNG placeholder logos and one ICO placeholder. No visible task/account data,
person, readable identifying text or watermark was observed. No screenshot
file is currently tracked. This reviews current assets, not future Store/release
screenshots or invisible/steganographic pixel content.

JPEG APP/comment, PNG text/EXIF and all six ICO frames were inspected structurally.
Nine assets contain no identifying metadata segments; eight PNGs contain only
the `Software=Figma` text field. No creator/contact/location/path metadata was
found in those fields. No OCR tool was available, and automated OCR was not run.
Any final screenshots still require their own visual/text/privacy review.

The eight backgrounds entered the repository in `1813855`; a recorded original
source, asset ownership statement or image-specific redistribution licence was
not found in the reviewed documentation/notices. Their privacy checks pass, but
their redistribution rights remain unverified for DEP-17. The scaffold ICO/PNG
placeholders also remain subject to DEP-17's final branding/ownership review.
Do not infer a licence from absent metadata, a commit or an existing dependency
notice. No image or required attribution was removed.

## Evidence and completion limits

Private evidence is under `artifacts/dep13-review/`: `evidence.json`,
`ignore-checks.json`, `image-metadata-summary.json`, `images.json`,
`image-review.png` and `source-scan-summary.json`. Raw marker/metadata inventories,
scanner logs and the source extraction remain ignored and are not release assets.

These checks use baseline `7d2fa21` plus the reviewed working `.gitattributes`
rule; the archive does not contain new sixth-batch documents or lockfiles.
Integration must scan the complete staged tree and archive the exact eventual
release commit again. Owner attribution/visibility and first-party licence
choices, background redistribution proof, future screenshot review and final
signed-package privacy checks remain open. No author/committer, history,
repository visibility, accepted ADR, machine configuration or remote setting
changed.
