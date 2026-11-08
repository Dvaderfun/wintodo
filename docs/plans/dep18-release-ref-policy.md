# DEP-16/18 local release-ref and version eligibility

Prepared 2026-11-09 against `db6c448` with the new checker/test overlay.
**Implementation and 31/31 focused real-Git regressions PASS.
Production identity, first version, protected hosted execution and signing remain
PENDING.** The stable-tag mapping is a preparation contract; this work changes no
manifest version, production publisher, repository protection or release tag.

[Test-ReleaseRefPolicy.py](../../scripts/Test-ReleaseRefPolicy.py) checks the
source/ref/version portion of [DEP-18](deployment-readiness.md). The existing
[unsigned candidate checker](dep18-candidate-handoff.md) checks package/report
consistency; this helper supplies separate local Git evidence. A passing result
has `scope: local_release_ref_eligibility` and `release_authorized: false`.
Local agreement cannot authenticate the supplied owner/main pin, enforce remote
ref protection or prove which source produced an MSIX.

## Supported inputs and checks

The caller supplies the event, complete tag ref, exact source commit, independently
reviewed main commit and one fixed main ref. Only `push` is eligible here; PR,
manual and scheduled runs remain separate build/maintenance workflows.

- Tags must be canonical `refs/tags/vMAJOR.MINOR.PATCH`, without leading zeros,
  prerelease/build suffixes or extra components. The mapped manifest version is
  `MAJOR.MINOR.PATCH.0`; each component is at most 65535 and major is nonzero.
- Source/main pins are nonzero lowercase 40-character SHA-1 commit identifiers.
  They must resolve to those exact Git commit objects. `HEAD` and the peeled tag
  must equal the source pin; `refs/heads/main` or `refs/remotes/origin/main` must
  equal the reviewed main pin. Other main ref names are rejected.
- The source must be an ancestor of, or equal to, the pinned main commit.
  Lightweight and annotated tags are supported. Annotated-tag signatures are
  not verified; raw tag-object identity is rechecked to catch replacement even
  when a replacement still points to the same commit.
- The repository must be a complete non-shallow worktree. Replace refs, graft
  files and configured partial clones reject the check. Git runs with inherited
  `GIT_*` variables discarded, global/system Git settings disabled, lazy fetching
  and replacement objects disabled, optional writes disabled and fsmonitor off.
  Existing local configuration is not modified.
- Tracked changes and unignored untracked files reject eligibility. Ignored
  evidence directories remain allowed. Assume-unchanged/skip-worktree and other
  incomplete index states are rejected. Every index entry must be an ordinary
  cached `H` entry at stage zero with mode 100644 or 100755; sparse/hidden entries,
  symlinks and submodule gitlinks are unsupported. This is Git's clean-worktree check;
  it does not prove every working file's physical bytes match its source blob.
- The committed fixed manifest path `src/Wintodo.App/Package.appxmanifest` must
  contain one identity and the exact mapped version. Its UTF-8 XML is bounded
  to 1 MiB, with DTD/entity declarations rejected. The physical worktree manifest
  must match the committed blob byte-for-byte, including line endings. Use a
  checkout preserving those bytes; Git-normalized CRLF equality is insufficient.
- If supplied, `--previous-version` must be a valid four-part MSIX version lower
  than the mapped candidate. Omission records `previous_version_check: PENDING`;
  it does not select or waive the supported update baseline.

Git calls use argument arrays with `shell=False`, no stdin, a 20-second deadline
per call and a 2 MiB stdout limit. Raw stderr is captured privately and never
printed. Source/ref/worktree snapshots and manifest bytes are checked again
before success. No fetch, checkout, tag creation, registration or signing runs.
The complete history requirement cannot establish remote authority by itself.

## Invocation and output boundary

Run after checking out the exact reviewed tag source. Pins below are independently
reviewed inputs, not values to infer from the same check being approved:

```powershell
python -B scripts/Test-ReleaseRefPolicy.py --repository . `
  --event-name push --ref 'refs/tags/v1.0.1' `
  --source-commit '<full exact source commit>' `
  --reviewed-main-commit '<full independently reviewed main commit>' `
  --main-ref refs/remotes/origin/main `
  --previous-version '<supported previous four-part version>' `
  --output SafeTestResults/release-ref-policy.json
```

The example tag is illustrative; it does not choose the first release or modify
the current `1.0.0.5` manifest. A current-source invocation with that version
must fail until the approved mapped version is committed. A future tag-only CI
step can supply event/ref/source through environment variables and the reviewed
main pin through explicit repository policy. A missing/malformed pin fails
closed. Schedule/manual main candidate builds do not gain release authority.

JSON output is optional; stdout is always sanitized. A report file must have a
`.json` suffix, an existing parent, and an ignored/untracked location inside the
repository. Paths traversing `..`, outside paths, hard links, symlink/junction
ancestors and tracked destinations are rejected. `SafeTestResults/` and
`artifacts/` are supported when ignored by the repository. Reports are replaced
atomically; safe failed reruns replace stale PASS with FAIL. An unwritable/rejected
output exits nonzero and cannot turn an older report into current evidence.

Reports contain validated commits, mapped version, committed-manifest SHA-256,
fixed scope/check statuses and fixed finding categories. They omit repository
paths, raw ref/input values, XML contents, Git errors and public identity values.
The required pending gates include owner identity/first version, hosted/ref
protection, source-to-package provenance, vendor/legal closure, production
signing/trust/timestamp, final signed deployment and publication authorization.

## Verification and remaining work

[The focused regressions](../../tests/scripts/test_release_ref_policy.py) use
disposable real local Git repositories with synthetic identities and no network.
They cover lightweight/annotated tags, pinned ancestry, ref/HEAD/main mismatches,
unrelated roots, dirty/untracked/ignored worktrees, shallow/grafted/replaced/
partial history, injected refs/environment, manifest/version/line-ending mismatch,
monotonicity, ref mutation, safe/stale/aliased output and sanitized failures.

```powershell
python -B -m unittest discover -s tests/scripts -p test_release_ref_policy.py
```

Private results are retained in `artifacts/dep-release-ref-policy/`. Parent
integration owns any CI wiring, shared tracking and final commit. Production
identity/version decisions, remote protection/hosted evidence, cryptographic
tag/build provenance, signing and installed-package gates retain their separate
acceptance. No root-repository tag, first-version decision, machine setting,
remote write, signing or publication occurs in this stream.

The final focused run passes 31/31 with zero skips. The real autocrlf=true
checkout preserves exact manifest bytes with the committed LF attribute, and
the source/ancestry checks pass for both lightweight and annotated tags.
Earlier CRLF mutation fixtures expected the byte-mismatch category, while Git
correctly rejected their changed worktree first; the regression now accepts
either fail-closed path without claiming Git status was clean. No checker rule
was weakened. Four local document links resolve, whitespace checks pass, and
the public-document privacy scan has one file and zero findings. These local
results do not close hosted/protected execution or production acceptance.
