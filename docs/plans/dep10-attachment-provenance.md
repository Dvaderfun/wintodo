# DEP-10 — Downloaded attachment provenance

Implemented on 2026-11-08; focused automated checks pass. Guest opening and
final Native AOT acceptance are tracked separately in the deployment plan.

## Observed gap

`MicrosoftTodoReader.DownloadAttachmentsAsync` wrote response bytes to an
ordinary file and `SqliteTaskStore.CopyStagedAttachments` copied that file into
the attachment directory. Neither boundary applied an Internet zone. The
detail command already used Windows `Launcher.LaunchFileAsync`, with path
confinement and Windows file-type restrictions, but did not explicitly request
the Windows untrusted-content warning. Six marker checks failed before the fix;
the two existing cleanup checks passed.

This establishes missing provenance, not an executable exploit. The change
retains the Windows Launcher boundary and does not add shell execution.

## Opening and storage policy

- A completed Graph download receives a `Zone.Identifier` named stream with
  exactly `[ZoneTransfer]\r\nZoneId=3\r\n` on Windows filesystems supporting
  named streams. Zone 3 is Internet origin. No source URL, referrer, account,
  bearer token or machine path is written into provenance metadata.
- The final attachment copy receives the same marker explicitly. Storage does
  not rely solely on an intermediate copy preserving named streams. This also
  marks staged imports made before this fix if they reach the current copy
  boundary. Existing committed files are not rewritten in a migration.
- `LauncherOptions.TreatAsUntrusted = true` applies to every attachment open,
  including locally picked and previously imported attachments. Windows
  supplies its warning and existing file-type/handler restrictions. The app
  does not introduce its own extension allowlist or bypass a Windows denial.
  A false launch result retains the existing localized failure feedback.
- Local `StorageFile.CopyAsync` retains its existing path; the opening warning
  does not depend on assuming that every filesystem preserves its metadata.
- Non-Windows library callers do not write Windows named streams. On Windows,
  only exact `HRESULT_FROM_WIN32(ERROR_INVALID_FUNCTION)` (`0x80070001`) and
  `HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED)` (`0x80070032`) allow the named-stream
  operation to be omitted. The explicit Launcher warning still applies.
  Other IO or permission failures fail the import; no catch converts them to
  successful provenance preservation.
- Marking requires an already existing completed file. This prevents creation
  of an empty attachment through a named-stream write on a missing base file.
  The staging failure path removes its newly created numbered file. The final
  copy is registered for rollback cleanup before marking; a marking failure
  therefore cannot bypass the existing owned-copy cleanup. Deleting a base
  file also removes its named streams. Filesystem/permission failures can
  still prevent physical cleanup, as documented for the existing import path.

The helper uses BCL file IO only, with an `OperatingSystem.IsWindows()` guard.
No Windows dependency is added to Core, no schema changes, COM activation,
reflection, new NuGet packages or amendments to accepted ADRs are required.

## API evidence

Project-scoped `winapp find-api` against the restored `src/Wintodo.App`
references confirmed both `LaunchFileAsync(IStorageFile, LauncherOptions)` and
the writable Boolean `LauncherOptions.TreatAsUntrusted`. Searches for
AttachmentServices and AttachmentExecute returned no matching project WinRT
API. This result does not assert that Win32 `IAttachmentExecute` is absent
from Windows; using it here would require a separate native COM integration
and review, which this implementation does not add.

Microsoft's documented contracts:

- [LauncherOptions.TreatAsUntrusted](https://learn.microsoft.com/en-us/uwp/api/windows.system.launcheroptions.treatasuntrusted?view=winrt-26100)
  requests the potentially unsafe file/URI warning when launching a handler.
- [Windows file streams](https://learn.microsoft.com/en-us/windows/win32/fileio/file-streams)
  describes the `filename:stream:$DATA` named-stream form.
- [IAttachmentExecute](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nn-shobjidl_core-iattachmentexecute)
  describes the richer native attachment policy API considered during review.

## Verification and limits

`AttachmentProvenanceTests` passes 11/11 with zero failed/skipped tests using
synthetic bytes and fresh SQLite stores. Coverage checks stage/final zone
contents and byte fidelity, reapplying lost staging metadata, `.txt`/`.pdf`/
`.csv` copies, hostile filename confinement, failed-download cleanup,
failed-save rollback, missing-file no-create, read-only permission failure,
and a deterministic locked-zone IO failure with owned-file cleanup and
preservation of an unrelated existing attachment. The baseline and passing
TRX files are private ignored evidence under `tests/ui/artifacts/dep10/`.

The real filesystem exercised so far is NTFS. No FAT/exFAT/ReFS volume was
mounted to exercise an unsupported-filesystem response; that limitation
remains explicit. The 11/11 count is Windows proof. Windows-only assertions
do not establish named-stream behavior on non-Windows hosts.

The focused guest gate is:

```powershell
.\scripts\Invoke-VmGate.ps1 -Gate run-attachment-provenance-gate.ps1
```

It runs the provenance regressions in the guest, then imports a synthetic
`.txt` file through the real wizard, checks final metadata/path confinement,
opens it through the app command and an existing Windows document handler,
removes it and checks restart persistence, and verifies failed wizard-download
cleanup. Its screenshots require individual visual review. It does not install
a document handler or change file associations; if the guest cannot open the
document, the opening row remains failed/pending with the actual reason.

Solution builds, both fresh AOT publishes, the existing import gate and this
focused guest gate are owned by integration. No guest or final-package PASS
is claimed by the focused library tests or API lookup alone.
