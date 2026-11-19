# Installation and recovery — review draft

**DRAFT — no production release is available from this document.** Prepared
2026-11-08 from source `4fa223d`; updated 2026-11-09 against `db6c448` for the
production override policy; now aligned with selected Store-first delivery under
ADR-019. Wintodo is published by Univera LLC; contact support@univera.app.
Wintodo is reserved as [9PF4DX2BSF0X](https://apps.microsoft.com/detail/9PF4DX2BSF0X).
Version `1.0.0.0` uses `47371DermenzhyStudio.Wintodo_rg7xtn0gdgdnp`;
certification and installed update tests are pending.
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

## Obtain the app

After publication, open [Wintodo in Microsoft Store](https://apps.microsoft.com/detail/9PF4DX2BSF0X)
and confirm **Univera LLC** as publisher. A reserved link does not mean the app is available.
Microsoft Store handles package delivery and signing and selects a compatible
architecture from the published packages.

Unsigned Actions artifacts and development test signatures are preparation
outputs. A Store release does not require installing a development certificate.
Direct production downloads are deferred to a separately reviewed route.

## Install and start

On the approved Microsoft Store listing, choose Install and wait for completion.

Start the installed app from the Start menu. On a fresh profile, onboarding lets
you begin with local tasks or start a one-way Microsoft To Do import. CalDAV is
optional. Do not launch the packaged executable directly or use an extracted
development layout to validate a customer installation.

Work/school Microsoft sign-in can depend on an organization's consent policy.
Production registration, publisher verification and administrator-consent flows
still require an authorized live verification session. An import copies tasks;
it does not make Microsoft To Do an ongoing synchronization provider.

## Update and preserve data

Back up before an update. Use Microsoft Store's update path for the approved
Wintodo listing; updates retain the production identity and increase its version.
Windows updates with a retained compatible package identity
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

## Advanced local launch controls

The selected production policy retains the existing database and clock overrides
in Release, including Native AOT. Normal Start activation uses the package-local
database and real time. An explicit `--database-path <file>` selects another
SQLite file and takes precedence over the process environment variable
`WINTODO_DATABASE_PATH`; otherwise the environment variable selects the file.
These inputs apply to that launch, rather than saving a preferred location for
future Start or notification activations.

A custom database carries its database-backed preferences and onboarding state.
Attachments and import staging still use the installed package's LocalState;
credentials stay in PasswordVault. Back up both storage locations as described
above. Windows Share always saves to the normal package database using real time,
so shares can appear in a different database from an explicitly overridden shell.

`--clock-file <file>` reads a locally controlled time value on each use. It can
change task timestamps and reminder/date behavior; use it with synthetic data.
It keeps Windows' local timezone. Only a command-line database override skips
fresh-database onboarding by default; `--show-onboarding` allows the ordinary
fresh-database checks. An environment-only database override does not skip them.

Fixture imports and durable `--trace-file` output remain Debug-only and are
ignored by production Release/AOT builds. Numeric scroll-output controls remain
available for explicit diagnostics. The [supported option table](../guides/build-and-run.md#supported-local-launch-overrides-dep-12)
documents parsing and the complete scope. These controls do not establish a
portable profile or change the selected customer installation path.

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

Store certification/signing, clean install/update/uninstall, native ARM64
execution, live provider/consent checks and affected guest gates remain open.
The existing development VM checkpoint cannot establish clean-machine
installation. M9 performance measurement remains owner-deferred.

The [deployment plan](../plans/deployment-readiness.md) and
[release checklist](../guides/release-checklist.md) retain the actual gate
statuses. Review this draft alongside the [privacy draft](PRIVACY.md) and
[DEP-17 evidence](../plans/dep17-release-docs.md) before preparing final release
instructions.
