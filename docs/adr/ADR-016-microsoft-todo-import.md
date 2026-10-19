# ADR-016 — One-way Microsoft To Do import through Microsoft Graph

- **Status:** accepted
- **Date:** 2026-10-19
- **Supersedes:** nothing. It adds an exception to the "instead of Microsoft
  Graph" positioning in `AGENTS.md` and PLAN §1.
- **Owner acknowledgement:** on 2026-10-19, after reviewing IMPORT-PLAN and
  this ADR, the owner said "Lets try to do what possible to deliver this". The
  recommended options in IMPORT-PLAN §9 were taken: `OAuth2Manager`, the
  in-pane Welcome surface, and the listed defaults.

## Context

New users who come from Microsoft To Do need to bring their lists, tasks,
steps, notes, dates, reminders, repeats and attachments with them. Re-running
the import must not duplicate anything. The owner wants this on a first-run
onboarding screen and in Settings.

Microsoft To Do has no consumer file export. The PST export through Purview
eDiscovery is admin-only. The only supported programmatic path is the
Microsoft Graph To Do API. It supports delegated `Tasks.Read` for both personal
and work/school accounts. Graph does not expose My Day membership, list
colours or themes, list groups, or manual ordering. It also documents that a
task's `id` changes when the task moves between lists.

The app is Native AOT and packaged, and bans reflection-based JSON and
unvetted dependencies (PLAN §3). The Windows App SDK in use already includes
`Microsoft.Security.Authentication.OAuth.OAuth2Manager`, which does
authorization code + PKCE.

## Decision

- Add a **one-way, read-only import** from Microsoft To Do via Graph v1.0
  using the scopes `Tasks.Read` and `User.Read`. Wintodo never writes to Graph,
  and Graph never becomes a storage format or sync backend. ADR-001 (`VTODO`
  canonical) and ADR-002 (CalDAV sync) are unchanged.
- Put all Graph code in a new AOT-compatible `Wintodo.Import` project that
  depends only on Core. It maps Graph data to a source-neutral
  `ImportSnapshot`, and Data persists that snapshot.
- Sign in with `OAuth2Manager` in the system browser. The redirect is a
  loopback `http://localhost:<port>/` that the app listens on (RFC 8252), so
  no protocol activation or single-instance redirection is needed. Keep the
  access token in memory for one import only, and never persist it.
- Make the import idempotent through a new `import_sources`/`import_links`
  schema (migration 004). It matches by external id, then by fingerprint
  (`createdDateTime` + title, for moved tasks), then by list name. Local
  UIDs are deterministic (UUIDv5). Local edits and local deletes win.
  Nothing is deleted locally because it disappeared from To Do.
- Keep every unmodelled Graph value as an `X-MSTODO-*` property in
  `ical_unknown`.
- Show the fields Graph cannot provide (My Day, list themes, groups, order,
  shared members) in the wizard before the import starts.
- Add an in-pane onboarding Welcome surface for fresh databases only. It is
  hosted in `ShellPage` like `SettingsView`, and adds no new window or
  navigation item. Databases opened with `--database-path` (UI-gate scratch
  databases) skip it unless `--show-onboarding` is also passed.

Full design: [docs/IMPORT-PLAN.md](../IMPORT-PLAN.md).

## Consequences

- The owner must register an Entra public-client application. Until
  publisher verification is done, users see an "unverified app" consent
  screen. Some work tenants require admin consent.
- The app gains its first non-CalDAV network endpoint, `graph.microsoft.com`,
  used only when the user starts an import.
- My Day membership and list cosmetics can't be migrated. This is stated
  honestly in the UI.
- The new project, schema and UI surfaces need unit tests, a VM UI gate and a
  manual gate against real accounts.

## Alternatives considered

- **MSAL.NET + WAM broker.** It offers a better account picker and silent SSO,
  but adds a dependency whose Native AOT/trim cleanliness must be verified
  against PLAN §3. It can be revisited if `OAuth2Manager` UX tests poorly.
- **Device-code flow.** It needs no redirect, but the user must copy a code
  into a browser. That is not "easy".
- **PST / Outlook export.** Admin-only (Purview) or Outlook-desktop-only, with a
  binary format to parse. Rejected.
- **Graph delta queries for re-import.** Delta is efficient, but it misses the
  id-change-on-move case and needs stored delta tokens. A full read plus
  link/fingerprint comparison is simpler and correct at personal-task volumes.
