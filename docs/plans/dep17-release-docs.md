# DEP-17 — Release documentation preparation

Prepared: 2026-11-08. Source baseline: `4fa223d0210bdf744b28ccb1a2eba7c45e9866cb`.
Status on 2026-11-20: the owner accepted the privacy policy for the Store draft. The complete text was read back from Partner Center. Installation guidance, final-package acceptance and the other release gates remain open.

## Delivered scope

[PRIVACY.md](../releases/PRIVACY.md) explains local storage, optional service
connections, Graph token lifetime, attachments, diagnostics and retention.
[INSTALLATION.md](../releases/INSTALLATION.md) prepares Windows/architecture
selection, direct-package verification, conditional Store delivery, update,
backup/recovery and removal guidance. The installation guide remains an unpublished review draft.

This stream owns these two drafts and this evidence record only. It leaves
shared tracking, existing operational guides, application files and legal/brand
decisions to integration or the owner. No runtime behavior changed, and no new
build, unit suite, UI gate, live sign-in, installation or network capture was run
for a documentation-only change.

## Source evidence and claim boundaries

CodeGraph was used before locating/reading implementation. Current-source
exploration and scoped searches support the following descriptions:

| Draft claim | Reviewed source / documentation | Boundary |
|---|---|---|
| Default SQLite and import attachment roots; database-only overrides | [App.xaml.cs](../../src/Wintodo.App/App.xaml.cs), `ResolveDatabasePath` / `CreateImportEnvironment`; [DEP-12](dep12-production-overrides.md) | Current behavior only; production override policy and runtime acceptance remain pending. |
| Broker Share uses package-local storage and real time | [App.Share.cs](../../src/Wintodo.App/App.Share.cs); [architecture](../../ARCHITECTURE.md) | Does not claim final-package Share acceptance. |
| Task/preferences/account metadata and import linkage/history | [data model](../DATA-MODEL.md) §§3.1/3.6/3.7; [Import.cs](../../src/Wintodo.Data/SqliteTaskStore.Import.cs), `GetImportSourcesAsync` | Account/source identifiers and display email remain persisted after session disposal. |
| PasswordVault stores CalDAV secrets; disconnect removes matching credential | [PasswordVaultCredentialStore.cs](../../src/Wintodo.App/Services/PasswordVaultCredentialStore.cs); [CalDavSyncService.cs](../../src/Wintodo.Sync/CalDavSyncService.cs), `DisconnectAsync` | Pending move obligations can block disconnect; uninstall credential cleanup is not verified. |
| Microsoft PKCE/system browser/loopback and exact scopes | [MicrosoftSignInService.cs](../../src/Wintodo.App/Services/MicrosoftSignInService.cs); [ADR-016](../adr/ADR-016-microsoft-todo-import.md) | Keep `Tasks.ReadWrite` / `User.Read`; no narrower-scope or live production-registration claim. |
| Read-only Graph import and in-memory token ownership | [GraphTodoClient.cs](../../src/Wintodo.Import/MicrosoftTodo/GraphTodoClient.cs); [MicrosoftTodoImportSession.cs](../../src/Wintodo.Import/MicrosoftTodo/MicrosoftTodoImportSession.cs); [DEP-09 evidence](deployment-readiness.md) | Session disposal is distinct from browser sign-out, consent revocation and deletion of already imported data. |
| Attachments copied locally, Windows file opening and explicit removal | [TaskDetailViewModel.Attachments.cs](../../src/Wintodo.App/ViewModels/TaskDetailViewModel.Attachments.cs); [DEP-10](dep10-attachment-provenance.md) | No promise of secure erasure, server attachment cleanup or final guest-opening acceptance. |
| Safe trace content and Debug-only durable trace listener | [diagnostics guide](../guides/diagnostics.md); [App.xaml.cs](../../src/Wintodo.App/App.xaml.cs) | External debugger/dump/tool output is outside the helper's sanitization boundary. |
| No configured publisher analytics/advertising/crash-upload service found | Scoped source search across App/Sync/Import and [central package declarations](../../Directory.Packages.props); [architecture](../../ARCHITECTURE.md) | A bounded source/dependency observation; final signed-package network capture and platform/vendor behavior are not established. |
| Forward backups and newer-schema rejection | [SqliteTaskStore.cs](../../src/Wintodo.Data/SqliteTaskStore.cs), `ApplyMigrations` / `BackupDatabase` / `ReadSupportedSchemaVersion`; [migration guide](../guides/migrations.md) | `.bak-N` is database-only and predates its migration; no schema downgrade or later-edit recovery guarantee. |
| Windows 11 24H2+, x64/ARM64, AOT/package contents and unfinished delivery | [packaging guide](../guides/packaging.md); [deployment plan](deployment-readiness.md) DEP-16/19; [release checklist](../guides/release-checklist.md) | Cross-compilation is distinct from native ARM64 execution; existing dev checkpoint is distinct from a clean installation baseline. |

Review searches covered `telemetry`, `analytics`, `ApplicationInsights`, `Sentry`,
`Crashlytics` and HTTP/credential call sites in the three product layers and
central dependency declarations. No runtime absence-of-network guarantee is
inferred from those searches.

## Documentation verification

All 35 relative local links across the three new documents resolve. All three
carry draft status, and a line-by-line check found no trailing whitespace.
`git check-ignore` confirms the final `docs/releases/` paths are not ignored;
the initially proposed singular directory matched the existing generic Release
build-output exclusion, so the two owned drafts were moved without changing
ignore policy. No application, guide, shared tracking or dependency file changed.

## Required decisions and completion

| Needed record / evidence | Draft portion it closes |
|---|---|
| Approved app name, publisher attribution, first-party licence and organizational privacy/security/support contact | Draft headers, privacy responsibility/contact and installation publisher verification. |
| Direct MSIX, Store or both; exact package identity/publisher/version and tested prior-version strategy | Download/listing paths, certificate/trust instructions, app family name and update procedure. |
| Production signing provider and timestamp/trust policy | Expected signer and route-specific verification instructions. |
| DEP-12 production override choice and acceptance | Database/clock override support and backup scope. |
| Asset redistribution evidence and final approved captures/copy | Background/icon notices, branding and listing screenshots; no art was regenerated or relicensed here. |
| Production Entra registration and authorized work/school/admin-consent verification | Final sign-in requirements and organization support guidance. |
| Exact signed-package install/update/uninstall, credential/integration cleanup and network observations | Dependency requirements, cleanup/retention statements and final telemetry wording. |
| Owner's retained/deferred gate dispositions | Accurate supported-environment and performance limitations. |

Integration should link the drafts from shared tracking without marking DEP-17
complete. Final customer documents must be reconciled against the exact release
candidate, replace undecided fields, remove internal review wording, and receive
owner review before publication. Required notices must describe the actual
shipped dependency/runtime closure, not merely the current direct packages.

The subsequent [shipped dependency notices review](dep17-third-party-notices-review.md)
identifies exact inherited runtime/native legal texts missing from the current
payload and prepares a deduplicated source/hash list. Those proposed additions
remain separate from the owner's first-party licence and image-rights decisions;
they must be integrated and rechecked in rebuilt final packages.
