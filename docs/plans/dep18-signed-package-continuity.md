# DEP-18 signed-package payload continuity

Prepared 2026-11-09 against `0b7272f66fa8ba3f44fc0a8e17ce1da767d6f562`
with this new validator/test overlay. **Preparation and 19 synthetic regressions
PASS; one historical ARM64 comparison PASS. Current production signing,
signature trust, timestamp policy, provenance and deployment remain PENDING.**

[Test-SignedPackageContinuity.py](../../scripts/Test-SignedPackageContinuity.py)
closes the comparison gap between the
[unsigned candidate handoff](dep18-candidate-handoff.md) and the
[installed-package smoke runner](dep19-installed-package-smoke.md). It reads
an exact unsigned and signed MSIX, compares their contents without extraction,
and emits `scope: signed_package_payload_continuity`. Every result has
`release_authorized: false`. Signature presence establishes no cryptographic
validity, certificate trust, timestamp validity or authorization.

## Inputs and checked invariants

The caller supplies the unsigned MSIX hash from its independently reviewed
candidate handoff, the signed MSIX hash from the signing session, and `x64`
or `ARM64`. Pins identify bytes; computing pins alone approves nothing. The
validator does not consume or authenticate the unsigned handoff or signed build
provenance. Retain those separate records and associate their exact hashes.

Both inputs are held open throughout comparison and hashed before and after
inspection. Path file identity is rechecked before success. Packages are bounded
to 1 GiB, 4096 entries and 1 GiB total declared uncompressed content; individual
entries are bounded to 256 MiB. UTF-8 manifest/content-type XML is bounded to
1 MiB and newly added signing footprints to 16 MiB. Unsafe paths, case-insensitive
duplicates, directory/symlink entries, encrypted or unsupported compression,
decompression/CRC failures and length inconsistencies reject the comparison.

The original must lack `AppxSignature.p7x`. The signed file must add that exact
nonempty entry. It may also add the exact nonempty
`AppxMetadata/CodeIntegrity.cat` entry. No other addition or removal is allowed.
Every original filename remains exact, and every original member except
`[Content_Types].xml` remains byte-identical. This includes the manifest,
executable, resources, notices, `AppxBlockMap.xml`, and any original catalog.
The manifest has one identity with the supplied architecture and a valid
four-part version; full package structure/privacy approval remains a separate
[package inspector](../../scripts/Test-ReleasePackage.py) gate.

Content types are compared semantically: harmless XML formatting and declaration
order can change. Existing mappings and their exact attributes must remain.
Only these signing overrides can be added, each when its footprint exists:

| PartName | ContentType |
|---|---|
| `/AppxSignature.p7x` | `application/vnd.ms-appx.signature` |
| `/AppxMetadata/CodeIntegrity.cat` | `application/vnd.ms-pkiseccat` |

Duplicate mappings, unknown namespaces/tags/attributes, removed/changed mappings,
child elements/non-whitespace content, DTDs/entities and unsupported encodings
reject the comparison. New signature/catalog bytes are read and hash/CRC checked
but remain unauthenticated here. The block map's original bytes must survive
signing; this comparison does not independently validate its block hashes.

## Primary evidence for the signing footprint

Microsoft's [MSIX overview](https://learn.microsoft.com/en-us/windows/msix/overview)
identifies `AppxSignature.p7x` as the signature footprint and
`AppxMetadata\CodeIntegrity.cat` as optional code-integrity metadata. The
[signing overview](https://learn.microsoft.com/en-us/windows/msix/package/signing-package-overview)
describes the signature/block-map chain used for Windows integrity checks.
[SignTool package guidance](https://learn.microsoft.com/en-us/windows/win32/appxpkg/how-to-sign-a-package-using-signtool)
requires contents to validate against the block map and a rebuilt package when
that map is corrupt. These documents were read on 2026-11-09.

Microsoft's MSIX SDK
[ContentTypeWriter.cpp](https://github.com/microsoft/msix-packaging/blob/25a65f5c1690930813bcc10cdf1d59fa865f2bb1/src/msix/pack/ContentTypeWriter.cpp#L15)
shows both exact signature/catalog overrides. Its
[footprint definitions](https://github.com/microsoft/msix-packaging/blob/25a65f5c1690930813bcc10cdf1d59fa865f2bb1/MsixCore/msixmgr/FootprintFiles.hpp#L18)
include the CI catalog. These pinned primary sources support the allowed names
and MIME declarations; they do not attest to our chosen signer's output.

## Use after separately authorized signing

Run the read-only comparison on the host after the production signing session
has produced reviewed immutable outputs. Replace these placeholders with exact
pins; create the ignored evidence directory first.

```powershell
python -B scripts/Test-SignedPackageContinuity.py '<reviewed-unsigned.msix>' `
  '<exact-signed.msix>' --architecture x64 `
  --unsigned-sha256 '<reviewed unsigned lowercase SHA-256>' `
  --signed-sha256 '<reviewed signed lowercase SHA-256>' `
  --output artifacts/release-candidate/signed-x64-continuity.json
```

Repeat with `ARM64` and its own files/pins. Signed checksums differ from unsigned
checksums; always use the signed hash in final deployment results. Rerun final
structural/privacy checks on the exact signed bytes and verify approved signer,
cryptographic chain and timestamp with the selected Windows signing/trust tools.
Store delivery still needs its own source-to-delivery evidence and customer
installation route. This direct pair comparison alone cannot establish that path.

An optional JSON output is atomically replaced, including a sanitized FAIL on a
safe failed rerun. Output paths resolving to either input, including hard links,
are rejected before writing. If the report cannot be written, the command exits
nonzero and emits a fixed failure; an existing report cannot be treated as the
new run's evidence. Reports contain fixed categories and hashes/counts, with no
input paths, identity/publisher values or exception text. No application or VM
is launched and no signing, installation, trust or UI operation occurs.

## Observed verification and limits

[Nineteen focused regressions](../../tests/scripts/test_signed_package_continuity.py)
pass with synthetic MSIX ZIP files. Coverage includes both architectures,
signature/catalog boundaries, unchanged original payload/manifests/block map,
content-type changes/duplicates/unknown structure, path/case/symlink rejection,
CRC/encryption/bounds, supplied-hash/architecture mismatches, multiple identities,
input changes, output aliases/hard links, stale PASS replacement, write failure,
sanitized output and the pending trust/provenance/deployment fields.

```powershell
python -B -m unittest discover -s tests/scripts -p test_signed_package_continuity.py
```

A read-only historical local ARM64 comparison passes:

| Observation | Exact result |
|---|---|
| Unsigned MSIX SHA-256 | `d5ea2d320918883850ae34e6bb475635ff27d53373acf6313ee28e9fabee3f07` |
| Test-signed MSIX SHA-256 | `500320bcbcdd7f7f161ca4e26e1e85d7b64f6fc5fb9ac57ae184c0919ebccc95` |
| Original entries | 317; 316 remain byte-identical, including `AppxBlockMap.xml` |
| Changes | Signature and CI catalog added; content types only reformatted with their exact added overrides |
| Executable SHA-256 | `0d29546e7af8f334013cbfd985504f65d380239e08b66584b3b6ffbcb74cbef4` |

Private reports are under `artifacts/dep-signed-continuity/`. The historical x64
filenames are not a signing pair: the supposed unsigned file already contains
a signature, and its manifest/executable/block map differ from the other file.
The validator rejects it as `unsigned_input_contains_signature`; no file was
replaced. Filenames cannot establish candidate identity.

The ARM64 result qualifies the validator on existing test-signed bytes. It does
not establish current-source acceptance, a production signer, cryptographic
validity/trust, native ARM64 execution or final deployment. No production
decision, signing provider, installation/update/uninstall route, host UI or guest
action changed. Parent integration owns shared plan/progress records and final
review/commit.
