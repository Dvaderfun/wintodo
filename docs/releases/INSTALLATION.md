# Installation and recovery — review draft

**DRAFT — no production release is available from this document.** Prepared
2026-11-08 from source `4fa223d`. Final download/listing links, app name,
publisher, signing instructions and supported update versions are pending.
The steps below must be checked against the exact final signed packages.

## Windows and architecture

The current package requires Windows 11 24H2 or a later compatible desktop
version. Check **Settings → System → About** for your Windows version and
system type. Select x64 for an x64-based Intel/AMD PC and ARM64 for an ARM-based
PC. An x64 build running through emulation does not establish native ARM64
compatibility.

Release packages are intended to contain the Native AOT app, Windows App SDK
and SQLite runtime files. A .NET SDK, Visual Studio or Developer Mode should
not be part of the customer installation instructions. Clean-machine dependency
acceptance is still pending; any separately required component must be listed
after that check, rather than assumed from a development machine.

## Obtain and verify the package

For direct MSIX distribution, use the approved release download and select your
architecture. Download the signed MSIX and its published SHA-256 checksum.
Unsigned Actions artifacts and development test signatures are preparation
outputs, not production installation choices.

Before installation, compare the downloaded bytes with the published checksum:

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath '.\downloaded-package.msix'
Get-AuthenticodeSignature -LiteralPath '.\downloaded-package.msix' |
    Select-Object Status, StatusMessage, SignerCertificate
```

Confirm the exact SHA-256, a valid signature and the final publisher stated in
the approved release. A matching checksum establishes that the bytes match the
release record; it does not replace signature/publisher verification. If Windows
reports an invalid or untrusted signature, stop and use the approved support
instructions. The signing provider and any distribution-specific trust steps
have not been selected; this draft does not instruct you to trust a development
certificate.

If Microsoft Store distribution is selected, use the approved Store listing and
its installation/update path. Store submission requirements and the final listing
are separate from producing a local MSIX file. The route remains owner-pending.

## Install and start

For a verified direct package, open the MSIX with Windows App Installer, check
the name and publisher, and choose Install. A PowerShell installation alternative
is:

```powershell
Add-AppxPackage -Path '.\downloaded-package.msix'
```

Start the installed app from the Start menu. On a fresh profile, onboarding lets
you begin with local tasks or start a one-way Microsoft To Do import. CalDAV is
optional. Do not launch the packaged executable directly or use an extracted
development layout to validate a customer installation.

Work/school Microsoft sign-in can depend on an organization's consent policy.
Production registration, publisher verification and administrator-consent flows
still require an authorized live verification session. An import copies tasks;
it does not make Microsoft To Do an ongoing synchronization provider.

## Update and preserve data

Back up before installing an update. Use a verified higher-version package with
the supported identity and publisher, then install through the same approved
distribution path. Windows updates with a retained compatible package identity
are intended to preserve app data. The release must name the prior versions
actually tested; that update matrix is currently pending.

Changing package identity creates a different package data location and needs a
documented transfer strategy. Do not uninstall an existing installation to work
around an identity/signature conflict until its data has been preserved and the
approved migration instructions are available.

After update, launch from Start and check existing lists, a sample task and its
attachment. Keep the backup until restart and the applicable sync/import
behavior have been verified. Versions are forward-moving; replacing a published
version with different bytes is not a supported update procedure.

## Back up and recover

The normal data folder is
`%LOCALAPPDATA%\Packages\<package-family-name>\LocalState`. The final release
must provide its exact package family name. Local task data is in `wintodo.db`;
managed attachments are under `attachments`. Account secrets live separately
in PasswordVault and are not included in a file copy of LocalState.

Close the app completely before making a filesystem copy. Preserve the entire
LocalState folder, including any remaining SQLite sidecar files and migration
backups, in a private location. If a custom SQLite path is in use, preserve that
database and its sidecars/backups separately as well: a database override does
not move attachments or credentials. Do not copy a live database file by itself
and assume it is a consistent backup.

An iCalendar export is useful for task interchange but is not a complete profile
backup: it does not substitute for attachment files, preferences, account
configuration or credential recovery. Keep exported tasks private and reconnect
accounts through their supported sign-in/credential flow when needed.

Existing-database upgrades create one-time `.bak-N` copies before each forward
schema migration, beside the database. These copies precede the respective
migration and can omit edits made afterward. They do not include an independent
copy of attached files or PasswordVault credentials.

If the app says the database belongs to a newer version, preserve it and use an
app version that supports its schema. The app rejects newer unsupported schemas
before ordinary writes. Installing an older binary does not downgrade the
database safely. Verify a backup at a separate disposable location before
replacing any current data; restoration and rollback instructions for the final
release still require the recorded deployment checks.

## Remove the app

Back up the data you need, then use **Settings → Apps → Installed apps →
Uninstall** for the final app name, or the distribution's supported removal
path. Intentionally created exports, external backups and custom database
locations need separate management. Removing the local app does not erase
Microsoft To Do or CalDAV server data.

The final release must report tested package-data, PasswordVault, startup,
jump-list and notification cleanup behavior. Those checks remain pending; this
draft does not guarantee a clean reinstall or complete erasure after uninstall.

## Current release limits

Final identity/version/signing, clean install/update/uninstall, native ARM64
execution, live provider/consent checks and affected guest gates remain open.
The existing development VM checkpoint cannot establish clean-machine
installation. M9 performance measurement remains owner-deferred.

The [deployment plan](../plans/deployment-readiness.md) and
[release checklist](../guides/release-checklist.md) retain the actual gate
statuses. Review this draft alongside the [privacy draft](PRIVACY.md) and
[DEP-17 evidence](../plans/dep17-release-docs.md) before preparing final release
instructions.
