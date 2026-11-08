# DEP-02/16 reproducible retained-code observations

Prepared 2026-11-09. **Bounded exporter and synthetic acceptance PASS; complete
native dependency closure remains INCOMPLETE.**
[Export-RetainedCodeEvidence.py](../../scripts/Export-RetainedCodeEvidence.py)
makes the positive observations in [the native closure review](dep02-native-closure-followup.md)
reproducible against an exact MSIX and its private native PDB. It adds separate
evidence alongside [the qualified SBOM](dep16-sbom-evidence.md); it does not
alter that BOM, dependency declarations, notices or production decisions.

## Supported proof and limits

The caller pins both complete MSIX and PDB SHA-256 values and supplies a full
source commit. The source identifier is caller-declared, not build attestation.
The exporter reads the root packaged `Wintodo.exe`, requires the selected native
x64/ARM64 PE machine and no CLR header, and binds its one basename-only CodeView
GUID/age to MSF7 PDB information and the matching DBI age/machine.

The supported PDB address model uses final PE sections directly. The exporter
requires matching section RVA, virtual size, raw-file offset/size and flags in
the PDB's final-section stream and packaged PE. It rejects OMAP transformations
and unsupported/missing mapping information. This is equality of mapping fields,
not all 40 bytes of each section header. Section names and other private/raw
symbol values are not emitted.

Every symbol record's framing is parsed to the exact end of the DBI symbol
stream. Only `S_PUB32` public-symbol semantics are supported; other record kinds
are counted separately. A supported record qualifies for an executable
observation only when its segment and offset resolve into actual raw bytes of
an executable PE section. Records without those raw-byte mappings remain
explicitly counted as unmapped, including synthetic segments and addresses in
virtual/uninitialized data. This does not prove that those records are corrupt,
or that all other PDB metadata or retained code has been interpreted.

Eight fixed symbol families are reported. Package/projection families use token
containment; `Rhp` requires a leading token, excluding wrapper names that only
contain it. Each family gets a record count and at most two samples with numeric
segment/offset/RVA, sampled byte count and SHA-256. Raw names, arbitrary family
labels and paths never enter output. Counts can overlap and include generated
code; they are not unique methods, binary sizes or performance measurements.

Absent observations never imply component removal. Symbol tokens and matching
PDBs do not independently prove package versions, source ownership, vendor
embedded libraries, compiler execution, licensing or build authenticity.
Every successful report retains `full_dependency_closure: INCOMPLETE`, pending
component versions/vendor/attestation, and explicit observation limits.

## Commands and input preservation

Keep native PDBs in private build outputs, associated with exact executable
hashes. They must not become MSIX contents or public release assets. Preserve
each architecture's matching PDB when producing candidates.

```powershell
python -B scripts/Export-RetainedCodeEvidence.py '<exact-x64.msix>' `
  --pdb '<matching-private-native.pdb>' --architecture x64 `
  --source-commit '<full-reviewed-commit>' `
  --expected-package-sha256 '<exact-msix-sha256>' `
  --expected-pdb-sha256 '<exact-private-pdb-sha256>' `
  --output artifacts/dep-retained-code-export/x64-evidence.json
python -B scripts/Test-ReleasePrivacy.py artifacts/dep-retained-code-export/x64-evidence.json `
  --private-root $PWD --output artifacts/dep-retained-code-export/x64-privacy.json
python -B -m unittest discover -s tests/scripts -p test_retained_code_evidence.py
```

Use `ARM64` for the other native architecture. No package is extracted,
registered, launched or modified. Size limits apply before initial input hashing:
1 GiB per complete MSIX/PDB, 256 MiB per package entry, 128 MiB per PDB stream,
4,096 ZIP entries and 4,000,000 framed symbol records. The MSF directory supports
one directory-block-map page, with explicit supported block sizes and bounded
stream counts. Unsupported structures fail with fixed-category diagnostics.
Both complete files are rehashed after export to detect intervening changes.

JSON output cannot alias either input or be hardlinked. Reports are replaced
atomically; a rejected rerun writes `FAIL` when the destination is safe. An
unsafe or unwritable output is preserved, so consumers must honor the nonzero
exit code. Failure text does not expose inputs or exception values.

## Observed verification

Sixteen [synthetic regressions](../../tests/scripts/test_retained_code_evidence.py)
pass. They cover both machines, GUID/age/machine mismatch, OMAP/section mismatch,
unmapped and unknown records, absence limits, framing/name corruption, MSF
directory/page/stream bounds, pre-hash PDB size rejection, unsafe/duplicate ZIP
entries, stale-result invalidation, input/hardlink preservation, late mutation
temporary-file cleanup after failed atomic replacement, and argument-error
redaction.

Both earlier clean notice-containing `9106040bc1eb460c1cd74fa7122d95460aa38a80`
unsigned packages and their matching native PDBs pass. Exact package/executable/
PDB hashes agree with [the previous review](dep02-native-closure-followup.md).

| Positive family records | x64 | ARM64 |
|---|---:|---:|
| Microsoft_Data_Sqlite | 349 | 343 |
| SQLitePCLRaw_core | 104 | 104 |
| SQLitePCLRaw_provider | 79 | 82 |
| CommunityToolkit_Mvvm | 230 | 230 |
| CommunityToolkit_WinUI | 1,249 | 1,249 |
| Microsoft_Windows_SDK_NET | 3,098 | 3,098 |
| WinRT_Runtime | 5,696 | 5,707 |
| Rhp, leading token only | 102 | 99 |

The seven package/projection counts reproduce the earlier review. The smaller
Rhp count reflects the documented narrower recognition rule. The x64 parser
frames 371,370 records, including 313,617 public records and 64,345 executable
public observations; 41,835 public records have no supported raw-byte mapping.
ARM64 frames 368,884 records, including 310,592 public records and 64,126
executable public observations; 41,823 have no supported raw-byte mapping.

Private evidence is `artifacts/dep-retained-code-export/earlier-x64-evidence.json`
and `earlier-arm64-evidence.json`; raw PDBs remain in their original private
build outputs. The corresponding report SHA-256 values are
`2c6392989bc7589b9e6ab690ece83cb98e7679e3cf1435af3c93fe27933f18cc` and
`fcc7dfa192ac76e297efba859089a9448fcb3e26a8947431ddc5ba28c5d07be4`.

The [fresh bound candidate pair](dep16-bound-candidate-validation.md) from clean
`f1b9af570882ba67ef7ccecf18d7c47321a9e366` also passes both architectures' exact
PDB/PE binding, final-section mapping and positive export checks. All eight
family counts agree with the historical table above. The fresh x64 symbol
stream frames 371,371 records, including 313,618 public records, 64,345
executable observations and 41,829 public records without a raw-byte mapping.
Fresh ARM64 frames 368,884 records, including 310,592 public records, 64,126
executable observations and 41,819 without a raw-byte mapping.

| Fresh input | x64 SHA-256 | ARM64 SHA-256 |
|---|---|---|
| Unsigned MSIX | `3d4e8b44e7310af1e8e480a8e28a7cf4bbf6d48e450bb9af9cf877d267316862` | `a6882cda602f170c37a4ca4888e3700a820bebd1605d1caf23568559a7377952` |
| Private native PDB | `42c99ad042c99df3d75ac12a7eff629d6044e53320a00fe107c28b74642501d8` | `419010831f21a21de997b9f01bb79d3cd98aa4992548bc0733e709ecc560a5a1` |
| Observation report | `275910aabe6375809dc449ca9b9bdabc2fdde4a7c3caf062945113af8f47a063` | `22cd8247ee7d8ed2dfef875977ac9339ec2526a4652ed99f0595b72ba95df20d` |

Fresh reports are `artifacts/dep-retained-code-export/fresh-x64-evidence.json`
and `fresh-arm64-evidence.json`. The release privacy scanner passes all four
observation reports plus this document: five files, zero findings, in
`public-evidence-privacy.json`. This exporter changes no candidate or private
PDB. Final signed candidates require matching new observations. Vendor build
mapping, WinApp runtime/advisory disposition, full retained-code/SBOM/legal
closure, hosted execution, signing and deployment acceptance remain pending.
