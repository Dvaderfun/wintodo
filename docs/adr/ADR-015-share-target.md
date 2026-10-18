# ADR-015 — Complete the Windows text and URL share target

- **Status:** proposed
- **Date:** 2026-10-18
- **Supersedes:** nothing; extends M7's existing text-share groundwork
- **Owner acknowledgement:** owner selected UX-18 and skipped UX-16/17 for now;
  acknowledgement of this concrete proposal is pending before implementation.

## Context

UX-18 imports text and URLs through the Windows Share UI into the default list.
M7 already declares a Text share target in Package.appxmanifest and handles
ShareTargetActivatedEventArgs in App.OnLaunched. The current handler ignores
WebLink and source-title metadata, and initializes the full main window,
Windows integration and CalDAV graph before saving. It then reports completion
and opens the newly created task. No real-share gate currently proves this path.

The app is packaged WinUI 3 / Native AOT, with a local default Tasks list and
parameterized store writes. Share activation is an existing Windows activation
contract, not a reason to introduce UWP XAML or change the app's navigation.
Windows documentation says ShareOperation is not agile and its UWP guidance
requires an activated target CoreWindow. This app uses WinUI 3 HWND windows;
the corresponding native-window lifecycle must be verified through actual
broker activation. The proposed path activates a minimal WinUI window and
keeps operation calls on its DispatcherQueue; API lookup alone does not prove
that behavior works.

## Decision

### Supported content and resulting task

- Extend the existing manifest share target to Text and WebLink. Keep package
  identity, publisher, permissions, toast/startup declarations and architectures.
- Save automatically when the user selects Wintodo in the Windows Share UI.
  The target is the local default Tasks list returned by EnsureDefaultListAsync,
  regardless of the list currently selected in any main window.
- Plain text keeps the current first-nonempty-line title and remaining-text note
  convention. Normalize line endings consistently; retain Unicode and note lines.
- For a supplied HTTP(S) WebLink, use a nonempty source title when available,
  otherwise the first nonempty text line, otherwise the URL itself as the task
  title. Preserve accompanying text in the note and append the URL if it is not
  already present as an exact note line. A text-only HTTP(S) URL is also retained
  in the note. Do not fetch, open, execute or rewrite a shared URL.
- Reject unsupported or empty payloads with a localized share error. Do not
  truncate long text silently or infer dates, tags, list names or importance.
- Use a small BCL-only Core mapping helper for title/note policy, tested with
  plain/multiline text, Unicode, missing titles, text-only URLs, combined formats
  and duplicate URL lines. Windows objects remain in App.

### Activation and lifetime

- Detect ShareTarget before creating the normal MainWindow and integration/sync
  services. Initialize only localization, DispatcherQueue, IClock, resources,
  a minimal share window and SqliteTaskStore.
- Activate the minimal native WinUI share window before operation calls. It
  reports progress and localized errors using standard controls/theme resources;
  it introduces no list picker, configuration page or new main-app navigation.
- Keep ShareOperation on the activation/UI thread; await data retrieval there.
  ReportStarted, retrieve/copy supported content, then ReportDataRetrieved.
  Create one task through the existing store path and report ReportCompleted
  only after the write commits. On failure report ReportError; offer Close and
  never retry a committed write automatically.
- Successful completion closes the share-only window, disposes the store and
  exits that process. Avoid a hidden resident process and full-shell background
  sync/reminder work. A share operation has one save attempt; repeated separate
  user shares intentionally create separate tasks.
- Reuse the current packaged multi-instance activation behavior: each share is
  processed by its own short-lived process. Do not introduce general single-
  instance redirection, custom protocol activation or a background service.
  An already-open main window must observe the new SQLite task when the user
  returns to it. Its current activation path refreshes dates/integration, but
  does not reload externally written task rows; add the narrow data refresh
  needed there, preserving selection and avoiding refresh over pending edits.
- Keep normal launch, toast and startup-task behavior unchanged. The existing
  post-full-shell text-share handler is replaced after the new path is validated.

### Packaging, AOT and verification

- M10 retains final name/publisher/signing and Store assets. UX-18 changes only
  share-format declarations and verifies them in generated packaged output.
- No new dependency, schema or credential storage. New bindings use x:Bind with
  explicit modes; strings live in Resources.resw; commands handle user actions.
- Validate API shapes against the restored app and use existing projected WinRT
  contracts without reflection. Full-trust share activation must be proved in
  the published x64 AOT artifact, not inferred from a Release JIT build.
- Add a real-share source fixture and gate inside WintodoTest. Exercise Windows
  target discovery and broker activation with Text/WebLink, cold and open-main-
  window cases, repeated shares, unsupported/empty inputs and restart persistence.
  Verify exact default list/title/note and one task per successful operation in
  scratch databases; any broker-activation storage override is DEBUG-only and
  excluded from Release. Published AOT tests use the disposable guest package's
  actual LocalState database, never the owner's data or a production override.
- Build/test and affected launch/Windows-integration regression checks remain
  required. Record unsupported VM broker scenarios as pending, never substitute
  a direct helper call for successful Windows share integration.
- Publish both architectures when the toolchain supports them; x64 real-share
  runtime is required. ARM64 runtime/performance remains unclaimed on this x64 VM.

## Consequences

Selecting Wintodo in Share saves a task directly and returns to the source app.
The main task manager is no longer opened automatically by a successful share.
The user can open Wintodo to edit the saved task. Sharing needs no CalDAV account
and keeps all payload handling local; normal sync can upload the task later when
the user has configured the destination list for sync.

A minimal share window is needed for the platform lifecycle even though task
creation is automatic. Windows broker and Native AOT behavior require real VM
evidence, especially when another main window is already open.

## Alternatives considered

- Keep the full-shell startup path: unnecessary services and lifetime for one save.
- New share-specific list picker/editor: outside UX-18's default-list scope.
- Add global single-instance redirection: broad activation redesign with extra
  coupling to toast/background paths; not required for this package.
- Register a custom protocol to simulate shares: does not prove the Share contract.

## Platform references checked

- [Windows App SDK rich activation](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/applifecycle/applifecycle-rich-activation)
- [ShareTarget manifest contract](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-uap-sharetarget)
- [ShareOperation lifecycle and threading](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.datatransfer.sharetarget.shareoperation)
- [StandardDataFormats.WebLink](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.datatransfer.standarddataformats.weblink)
