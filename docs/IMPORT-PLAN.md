# Import plan — onboarding and Microsoft To Do migration

Status: **implemented** (2026-10-19). [ADR-016](adr/ADR-016-microsoft-todo-import.md)
is accepted. The Entra app (IMP-00) is registered: "Wintodo", client id
`038f1bee-9c89-4f6c-9061-15210945a03e` in the owner's directory. Everything
is gated against recorded Graph fixtures; live import passed the owner's
manual gate on a personal account, and a work/school account is pending. See
§10 for how the build differs from this plan. §11 is the polish plan
(IMP-08 onwards) that follows a review of the as-built flow.

Goal: a first-run onboarding screen that offers to bring tasks over from
another task manager, starting with **Microsoft To Do**. The same import must
be available later from **Settings → Data**, and running it again must not
create duplicates.

Work packages are `IMP-xx` (§8). They follow the same rules as
[UX-PLAN.md](UX-PLAN.md): claim one in the status table, stay in scope, and
meet the `AGENTS.md` definition of done.

---

## 1. What the user sees

### 1.1 First run (onboarding)

The shell paints straight away, as DESIGN §13 requires (no splash screen). On
a fresh database the list pane shows a deferred (`x:Load`) **Welcome** surface
instead of the empty Tasks list:

```
Welcome to Wintodo
Bring your tasks with you, or start with an empty list.

  [ Microsoft To Do ]   Lists, tasks, steps, notes, due dates,
                        reminders, repeats and attachments.
  [ iCalendar file ]    Nextcloud, Tasks.org, Thunderbird, Apple Reminders
                        (.ics). Uses the existing M6 import.

                                            [ Start fresh ]
```

- "Fresh" means `onboarding.completed` is not set **and** the store holds only
  the empty default list. Existing users who upgrade never see onboarding.
  Their database already has tasks, so the key is set silently.
- **Start fresh** and a finished import both set `onboarding.completed=true`.
  Closing the app mid-wizard keeps onboarding for the next launch.
- Each source is an `IImportSource`, so adding Todoist, TickTick or Google
  Tasks later means adding one more card, with no change to the flow.

### 1.2 The Microsoft To Do wizard (same component in onboarding and Settings)

| Step | Content |
|---|---|
| 1. Connect | "Wintodo only reads your tasks. Microsoft's sign-in asks for permission to edit them (§10). Nothing in Microsoft To Do is changed or deleted. Your sign-in is not saved." Button **Sign in with Microsoft**. Works for personal and work/school accounts. |
| 2. Choose | Account shown (name + email). One checkbox per To Do list with its task count; shared lists are marked "shared". Options: **Include completed tasks** (on), **Include attachments** (on), **Include Flagged email list** (off). An InfoBar lists what To Do does not let apps read (§3.3). |
| 3. Review | A dry-run preview: "12 lists (3 merge with lists you already have) · 1,180 new tasks · 20 updated · 940 already imported, skipped". On a second run this step does most of the work: it shows that nothing will be duplicated. |
| 4. Import | Determinate `ProgressBar` with phases: reading lists → reading tasks (n/N) → downloading attachments → saving. **Cancel** is available until saving starts. Saving is one transaction, so a cancel or failure leaves nothing half-imported. |
| 5. Done | "Imported 12 lists, 1,180 tasks, 230 steps, 14 attachments." Skipped and kept-local counts, with **Copy details**. Button **Go to my tasks**. |

Errors use an `InfoBar` inside the step: offline, sign-in cancelled, admin
consent required (work account), or throttled (retrying automatically).

### 1.3 Settings → Data

A new card, **Import from Microsoft To Do**, opens the same wizard. Below it:
"Last imported from someone@outlook.com on 19 Oct 2026". The existing
**Import .ics** action stays where it is.

---

## 2. How the data gets out of Microsoft To Do

To Do has **no file export** for consumers. Its only supported export is a PST
through Microsoft Purview eDiscovery, which is admin-only and not usable here.
Microsoft documents only one programmatic path, which this plan uses: the
**Microsoft Graph To Do API** (`/me/todo/lists`, `/tasks`, `checklistItems`,
`linkedResources`, `attachments`).

- **Permission:** delegated `Tasks.ReadWrite` and `User.Read`. The plan was
  `Tasks.Read`, but the list delta call (§10) accepts only `Tasks.ReadWrite`
  (ADR-016 amendment 1). Wintodo still sends GET requests only. `User.Read`
  gives a stable account id and email for dedupe and display.
- **Sign-in:** the Windows App SDK `OAuth2Manager`
  (`Microsoft.Security.Authentication.OAuth`) does authorization code + PKCE in
  the system browser. **It is already in our SDK** (`winapp find-api` resolves it
  in WindowsAppSDK.Foundation 2.3.12), so it adds no package and is AOT-native.
  The alternative is MSAL.NET + WAM broker, which gives a Windows account
  picker and silent SSO but adds a dependency that must pass the PLAN §3/AOT
  check (ADR-016 compares the two).
- **Tokens stay in memory** for the duration of one import, and nothing is
  written to SQLite or `PasswordVault`. A re-import means one more sign-in;
  browser SSO makes that a click.
- **Entra app registration** (owner, outside the repo): a public client for
  "personal + work/school accounts", a redirect URI matching the chosen
  activation path (§7.3), and the scopes `Tasks.ReadWrite` and `User.Read`. The
  client ID is public and can live in source. Until the app has publisher
  verification, Microsoft's consent screen shows it as **unverified**. Some
  work tenants block user consent entirely, and their admins must approve.

---

## 3. Field mapping — "don't lose anything"

Every value either maps to a modelled field or is kept verbatim in
`ical_unknown` as an `X-MSTODO-*` property. That follows the existing
"never destroy what you don't understand" rule, so the values survive `.ics`
export and CalDAV sync.

### 3.1 Lists (`todoTaskList`)

| Graph | Wintodo |
|---|---|
| `displayName` | `display_name`. If it starts with an emoji grapheme, split that into `icon_emoji` via `ListEmoji` (**verify** with a live fixture that To Do stores the emoji this way). |
| `wellknownListName = defaultList` | **Merge into the local default "Tasks" list**, never "Tasks (2)" |
| `wellknownListName = flaggedEmails` | Imported only when the user opts in. The email link goes to `url`. |
| `isShared` / `isOwner` | Imported as a local snapshot. The preview labels it "shared", and members are not imported (§3.3). |
| `id` | Import link (§4), never the local UID |

### 3.2 Tasks (`todoTask`)

| Graph | Wintodo | Notes |
|---|---|---|
| `title` | `summary` | Empty titles become the localised "Untitled task" (the store rejects empty summaries) |
| `body` (text) | `description` | |
| `body` (html) | `description` = plain text; original HTML kept as `X-ALT-DESC;FMTTYPE=text/html` | Converter in Core, BCL only |
| `status` | `notStarted`→NEEDS-ACTION, `inProgress`→IN-PROCESS, `completed`→COMPLETED. `waitingOnOthers`/`deferred`→NEEDS-ACTION + `X-MSTODO-STATUS` | Raw value is kept |
| `importance` | `high`→PRIORITY 1 (starred), `normal`→0, `low`→9 | Matches DATA-MODEL §4 |
| `dueDateTime` | `due_utc` with `due_is_date_only=1` | To Do due dates are calendar dates. Convert from the value's own `timeZone` and keep its **date**, never the UTC instant, or tasks shift a day west of UTC. Covered by DST/offset tests. |
| `startDateTime` | `start_utc` | |
| `reminderDateTime` + `isReminderOn` | one absolute `VALARM` | Only when `isReminderOn`. A past reminder, or one on a completed task, is imported as acknowledged, so the import never fires a burst of toasts. |
| `recurrence` (`patternedRecurrence`) | `rrule` | Pure converter: daily / weekly(+days) / absoluteMonthly / relativeMonthly / absoluteYearly / relativeYearly, `interval`, `firstDayOfWeek`→`WKST`, range `noEnd` / `endDate`→`UNTIL` / `numbered`→`COUNT`. To Do advances on completion, the same as ADR-006. An unconvertible pattern keeps the raw JSON in `X-MSTODO-RECURRENCE` and is reported. |
| `completedDateTime` | `completed_utc` | |
| `createdDateTime`, `lastModifiedDateTime` | `created_utc`, `modified_utc` | Preserved, not reset to "now" |
| `categories` | `Categories` (`CATEGORIES`) | Becomes tags when M12 lands |
| `linkedResources` | first `webUrl` → `url`; all kept as `X-MSTODO-LINK` | |
| `attachments` (`taskFileAttachment`) | files under `attachments\<uid>\` + `attachments` rows | Only for tasks with `hasAttachments`. Name collisions are made unique. |
| `checklistItems` | steps: child `TodoTask` with `parent_uid` | `displayName`, `isChecked`→status, `checkedDateTime`→`completed_utc`, `createdDateTime`. Order follows the API's return order. |
| `id`, list `id` | import links (§4) + `X-MSTODO-ID` | |

### 3.3 Things Microsoft does not expose (shown to the user before importing)

| Lost | Why | Mitigation |
|---|---|---|
| **My Day** membership | Not a property in Graph v1.0 or beta | My Day resets daily anyway. The InfoBar says "Add today's tasks to My Day after importing." |
| List colour / background theme | Not exposed | Default colour. The user can change it. |
| List groups (folders) | Not exposed | Lists arrive ungrouped |
| Manual order of lists and tasks | No position property | Keep API return order as `sort_order`, best effort |
| Shared-list members and assignees | Not exposed | Snapshot of the tasks only |

These are hard platform limits. The wizard states them up front so that
"not losing data" is an honest promise.

---

## 4. Duplicate protection (re-running the import)

### 4.1 Identity

- **Source account key** = `provider` + the Graph `/me` `id`. Two different
  Microsoft accounts never dedupe against each other. The same account always
  does.
- **Import links** (new migration `004_import_links.sql`):

```sql
CREATE TABLE import_sources (
    id              INTEGER PRIMARY KEY,
    provider        TEXT NOT NULL,            -- 'mstodo'
    account_key     TEXT NOT NULL,            -- Graph user id
    display_name    TEXT NOT NULL,            -- email shown in Settings
    last_import_utc TEXT NULL,
    UNIQUE (provider, account_key)
);
CREATE TABLE import_links (
    source_id         INTEGER NOT NULL REFERENCES import_sources(id) ON DELETE CASCADE,
    entity_type       INTEGER NOT NULL,       -- 0 task, 1 list, 2 step, 3 attachment
    external_id       TEXT    NOT NULL,
    local_uid         TEXT    NOT NULL,
    fingerprint       TEXT    NOT NULL,       -- created_utc + normalised title
    external_modified TEXT    NULL,           -- Graph lastModifiedDateTime at import
    local_modified    TEXT    NOT NULL,       -- our modified_utc right after import
    imported_utc      TEXT    NOT NULL,
    PRIMARY KEY (source_id, entity_type, external_id)
) WITHOUT ROWID;
CREATE INDEX ix_import_links_fingerprint ON import_links(source_id, entity_type, fingerprint);
```

- **Deterministic local UIDs**: UUIDv5(namespace, account key + external id).
  Even if the link table were lost, the existing UID-unique import path still
  updates rather than duplicates.

### 4.2 Matching order, per item

1. **Link by external id.** This is the normal re-run case.
2. **Link by fingerprint.** Graph documents that a task's id **changes when it
   is moved to another list**. A task moved in To Do since the last import is
   recognised by `createdDateTime` + title, and is moved locally instead of
   duplicated.
3. **Lists:** `defaultList` maps to the local default list. Otherwise a
   case-insensitive name match with an existing list merges into it. The
   preview says "adds to your existing list 'Groceries'".
4. **Possible duplicates against tasks typed by hand** (same list, title and
   due date, no link) are counted in the preview and **skipped by default**.

### 4.3 What a re-run does with a matched item

| Source since last import | Local since last import | Action |
|---|---|---|
| unchanged | any | skip ("already imported") |
| changed | unchanged | update from To Do |
| changed | changed | **keep local**, report as "kept your edits". The preview offers "Overwrite my edits with To Do" (off). |
| any | soft-deleted locally | **don't resurrect**, report |
| gone from To Do | any | **never delete locally**, report "no longer in To Do" |

The import is one-way and additive. It never writes to Microsoft To Do and
never hard-deletes anything locally.

---

## 5. Correctness and safety rules

- One writer transaction for the whole save, with change-log rows and
  `is_dirty=1`, so CalDAV pushes the imported tasks when sync is on, and a
  single `StoreChanged`. Attachments download to a staging folder first and
  move into place only after the commit.
- Parameterised SQL only. Graph JSON is parsed with a source-generated
  `JsonSerializerContext`.
- Graph paging via `@odata.nextLink`, with `$top=100`. Throttling (`429`/`503`)
  honours `Retry-After` with capped retries. Request
  `$expand=checklistItems,linkedResources` on the task list (**verify**
  support on that endpoint; fall back to per-task requests).
- Reminders: after commit, the existing rescheduling sweep schedules only
  future, unacknowledged alarms.
- Undo: the import is too large for the 20-entry session undo. The Done step
  instead offers **Remove what was just imported**, which soft-deletes the
  run's created items (tracked by `imported_utc`) as one batch.
- Privacy: no telemetry and no token persistence. Network calls go only to
  `login.microsoftonline.com` and `graph.microsoft.com`.

---

## 6. Architecture placement

```
Wintodo.App ──► Wintodo.Import ──► Wintodo.Core      (new project, like Wintodo.Sync)
Wintodo.App ──► Wintodo.Data   ──► Wintodo.Core
```

| Where | What |
|---|---|
| `Wintodo.Core/Import/` | Source-neutral `ImportSnapshot` (lists, tasks, steps, attachments + external ids), `ImportOptions`, `ImportPreview`, `ImportResult`, the `IImportStore` interface, fingerprint + UUIDv5 helpers |
| `Wintodo.Import/MicrosoftTodo/` | `GraphTodoClient` (HttpClient, paging, retry), DTOs + `GraphJsonContext`, `GraphTodoMapper` (Graph → snapshot), `PatternedRecurrenceConverter`, `HtmlNoteConverter`, timezone conversion (Windows TZ ids via `TimeZoneInfo`) |
| `Wintodo.Data/SqliteTaskStore.Import.cs` + `Migrations/004_import_links.sql` | `PreviewImportAsync`, `ApplyImportAsync`, `RemoveImportRunAsync` |
| `Wintodo.App/Services/MicrosoftSignInService.cs` | `OAuth2Manager` flow and the in-memory token |
| `Wintodo.App/ViewModels/ImportWizardViewModel*.cs`, `Controls/ImportWizardView.xaml` | The wizard (§1.2), one control hosted by onboarding and by Settings |
| `Wintodo.App/Controls/WelcomeView.xaml` | Onboarding source cards (§1.1), `x:Load`-deferred in `ShellPage` |
| `Strings/en-US/Resources.resw` | New `<!-- IMP -->` block at the end |
| `Package.appxmanifest` | Protocol extension, only if the protocol redirect is chosen (§7.3) |

The `.ics` importer can migrate onto `ImportSnapshot` later. That is out of
scope here.

---

## 7. Testing

1. **Core unit tests:** recurrence conversion (every pattern type, range, WKST,
   leap day, month-end); status/importance mapping; due-date timezone → date
   across DST and ±14h offsets; HTML → text; emoji split; fingerprint; UUIDv5
   stability.
2. **`Wintodo.Import.Tests`:** recorded, anonymised Graph JSON fixtures in
   `tests/fixtures/mstodo/` (with a README on provenance, like the iCal corpus),
   covering paging, `429` retry, missing optional fields, html bodies,
   attachments, a shared list and a flagged-email list.
3. **`Wintodo.Data.Tests` idempotency suite:** import twice → zero creates on
   the second run; a source edit → update; a local edit → kept; a local delete
   → not resurrected; moved task with a new id → moved, not duplicated;
   default-list and name merges; a failure mid-save rolls back with no rows,
   files or notifications; change log, FTS and alarms correct.
4. **UI gate `tests/ui/run-import-gate.ps1`** in the `WintodoTest` VM (ADR-013).
   A Debug-only `--import-fixture <dir>` launch argument (like `--clock-file`)
   swaps Graph and sign-in for a fixture source. Checks: onboarding on a fresh
   DB, never on a populated one; wizard end to end; second run previews
   "0 new" and changes nothing; the Settings entry; cancel; offline error.
   M8 accessibility re-run for the new surfaces.
5. **Manual owner gate:** a real personal account and a real work account,
   seeded with known data. Verify counts, dates, reminders and recurrence
   against To Do, then re-run and confirm there are no duplicates.

---

## 8. Work packages

| ID | Title | Size | Depends on | Status |
|---|---|---|---|---|
| IMP-00 | Owner sign-off: ADR-016, DESIGN §10/§12 additions, Entra app registration | — | — | done 2026-10-19 (app "Wintodo", client id `038f1bee-9c89-4f6c-9061-15210945a03e`, public client `http://localhost`, delegated `Tasks.ReadWrite` + `User.Read` (amendment 1), no secret) |
| IMP-01 | Core import model, mapping helpers, recurrence/timezone/HTML converters + tests | M | IMP-00 | done |
| IMP-02 | Migration 004, `PreviewImportAsync`/`ApplyImportAsync`/`RemoveImportRunAsync`, idempotency suite | L | IMP-01 | done |
| IMP-03 | `Wintodo.Import` project: Graph client, DTOs, mapper, fixtures + tests | M | IMP-01 | done |
| IMP-04 | Sign-in spike → `MicrosoftSignInService` (activation routing, consent errors) | M | IMP-00 | code done (loopback redirect); personal-account sign-in, consent and token exchange passed 2026-10-19; **work/school account pending** |
| IMP-05 | Import wizard control + Settings → Data entry + `--import-fixture` seam | L | IMP-02, IMP-03, IMP-04 | done |
| IMP-06 | Onboarding Welcome surface (first-run detection, `.ics` card reuses M6) | M | IMP-05 | done |
| IMP-07 | Import UI gate in the VM, M8 re-run, `/winui-code-review`, docs, PROGRESS, owner manual gate | M | IMP-06 | automated parts done; personal-account owner gate passed 2026-10-19 (see §10); expired-sign-in finding fixed; **work/school account open** |

IMP-02/03/04 can run in parallel once IMP-01 lands. Skills: `winui-design`
before IMP-05/06 XAML, `winui-ui-testing` for IMP-07, and `winui-code-review`
before each UI commit.

Polish packages IMP-08 to IMP-24 are tracked in §11.1, not in this table.

---

## 9. Decisions for the owner

1. **Use Microsoft Graph for a one-way, read-only import.** `AGENTS.md` says
   the app is built "instead of Microsoft Graph". ADR-016 keeps Graph out of
   storage and sync and confines it to `Wintodo.Import`.
2. **Onboarding is beyond To Do parity** (PLAN §1, "don't invent navigation
   patterns"). The proposal is an in-pane surface, not a new page or window.
3. **Sign-in technology:** `OAuth2Manager` (no new dependency; system browser)
   or MSAL + WAM (Windows account picker; new dependency, AOT unverified).
   Recommendation: `OAuth2Manager`.
4. **Redirect handling for `OAuth2Manager`:** custom-scheme protocol activation
   needs single-instance redirection in `Program.cs`, which the app doesn't
   have yet. A loopback `http://localhost` listener needs no manifest change.
   IMP-04 spikes both and picks one.
5. **Entra registration and publisher verification**, plus where the client ID
   lives. Done by the owner, outside the repo.
6. **Defaults:** include completed tasks (on), attachments (on), Flagged email
   (off). On a re-run, local edits win.

---

## 10. As built (2026-10-19)

- **IMP-09:** Preview returns pending attachment ids; only those files download.
  Raw and contentBytes transfers stream into staging with byte progress.
- **IMP-08:** Error redirects show the failed sign-in page. Both browser pages
  have an encoded localised title/message, dark mode and no external resources.

- **Redirect (decision 4):** a loopback `http://localhost:<port>/` listener
  (`TcpListener` on 127.0.0.1, any port; Entra ignores the port for public
  clients). It needs no manifest change and no single-instance routing. The
  Entra registration must list `http://localhost` under "Mobile and desktop
  applications".
- **Welcome cards** are a `SettingsCard` with an action button, not a
  clickable card. The clickable `SettingsCard` exposed no Invoke pattern to
  UI Automation (found by the gate), which is an accessibility defect.
- **Scratch databases** (`--database-path`) skip onboarding unless
  `--show-onboarding` is passed, so the existing UI gates are unaffected.
- **Counts:** `ExistingLists` replaces "merged lists". Updated, unchanged,
  kept-local and deleted counts cover root tasks; steps are counted only as
  new.
- **Step change detection:** checklist items have no modified time, so a step
  uses its parent task's `lastModifiedDateTime`.
- **Possible duplicates** are skipped and not linked, so they are reported
  again on each run until the person deletes one copy.
- **Reopening the wizard:** the wizard and Settings are hosted with `x:Load`,
  so each open creates a fresh `ImportWizardView` over the one shared view
  model. A discarded view now stops its bindings and drops its repeaters'
  `ItemsSource` on `Unloaded`; before that, the second open in a session
  threw inside the button's command (the gate's "Cancel from Settings" row).
- **Sign-in listener:** the loopback listener keeps waiting after a request
  that is malformed, stalled (10 s per request) or rejected by
  `CompleteAuthRequest` (wrong `state`), so another local process cannot
  end the sign-in early.
- **Timeouts:** an `HttpClient` timeout is reported as offline instead of
  being treated as the person cancelling. `Retry-After` dates are measured
  with `IClock`.
- **Lists come from `me/todo/lists/delta`.** On the owner's personal account the
  plain `me/todo/lists` call (v1.0 and beta) returns an empty `value`, and
  `$filter` returns only some lists, while the delta function returns all of
  them, with paging. This is a known Graph behaviour for consumer accounts. Delta requires `Tasks.ReadWrite` ("access denied" with `Tasks.Read`), so sign-in requests it (ADR-016 amendment 1).
  `@removed` delta entries are skipped. Tasks still use the plain list-tasks
  call; the owner's open-task count comparison (below) passed for this account.
- **Verified live, personal account (owner gate, 2026-10-19):**
  - Sign-in, consent, token exchange, every list shown with its emoji, and
    import completion. Per-list open-task counts compared on six lists equal
    Microsoft To Do, so the plain list-tasks call returns complete data.
  - `$expand=checklistItems,linkedResources` on list tasks works: all 16
    lists traced `expand=success`; no fallback was needed.
  - Attachments download through raw `$value`: 24 of 24 traced
    `attachment=raw` (14 KB to 7.2 MB); the `contentBytes` fallback was not
    needed.
  - Re-import from Settings → Data into the same database: the second run
    added only the 3 tasks and 1 step created in To Do since the first run.
    The database holds 16 lists, 897 tasks, 296 steps and 24 attachments,
    each linked exactly once, with no duplicate lists or attachments.
  - Field fidelity: the owner spot-checked imported tasks against To Do and
    reported the content correct. Fields were not itemised one by one.
- **Expired sign-in (found live, fixed):** the access token is taken once at
  sign-in and never refreshed. When the person waited on the review step
  longer than the token lifetime, the attachment download got HTTP 401 and
  the wizard showed `Import_ErrorAccess` ("…may need an administrator's
  approval"); Retry reused the same token and failed again. Now any Graph
  401 is `GraphAuthorizationException.IsSignInExpired`: the wizard says
  `Import_ErrorSignInExpired`, returns to the Connect step and drops the
  stale reader; signing in again re-reads To Do and keeps the list choices
  and options. A 403 still shows `Import_ErrorAccess`. The import gate
  reproduces it with the Debug-only `--import-fixture-expire-once` (first
  session's raw attachment requests return 401). No silent token refresh:
  the token lives in memory for one sign-in only (ADR-016).
- **Not verified yet:**
  - **Work/school account:** sign-in, consent and import; tenant policy may
    require administrator approval for `Tasks.ReadWrite`.
  - **Non-migratable by design:** My Day, list colours/backgrounds, groups
    and manual order (ADR-016); these are not field-fidelity failures.

Owner launch (Debug; a scratch database keeps real data out of the default
profile):

```powershell
.\BuildAndRun.ps1 --args "--database-path C:\temp\imp-test.db --show-onboarding --trace-file C:\temp\imp-trace.log"
```

The trace's `WINTODO_IMPORT_READ` lines report `expand=success/fallback` with
per-list counts and `attachment=raw/contentBytes` with byte sizes; they hold
no names, ids or URLs.

---

## 11. Polish plan (review of the as-built flow, 2026-10-19)

The import is correct and idempotent; the personal-account gate shows that.
The wizard around it is still MVP-grade. Two outcomes behave wrongly
(IMP-08 and IMP-11), some work is done twice (IMP-09 and IMP-10), a
destructive action has no confirmation (IMP-12), and the steps lack the
orientation, detail and recovery paths the rest of the app has. These
packages close those gaps without changing the data rules in §3–§5.

Rules: the same as §8 (claim a row, stay in scope, AGENTS.md definition of
done, `winui-design` before XAML, `winui-code-review` before each UI commit).
Every package must keep `run-import-gate.ps1` passing. Existing
`Todo.Import.*` and `Todo.Welcome.*` AutomationIds are API: add new ones, but
don't rename existing ones. 🔒 marks a package that changes an accepted
decision (ADR-016) or goes beyond the spec, so it needs an ADR amendment and
the owner's acknowledgement before work starts.

The work/school account gate is still open under IMP-07 and is not repeated
here. IMP-18 needs a work account to verify the admin-consent path.

### 11.1 Status

| ID | Title | Phase | Size | Depends on | Touches (hot files) | Status |
|---|---|---|---|---|---|---|
| IMP-08 | The browser page says "signed in" after a cancelled or failed sign-in | P1 fix | S | — | MicrosoftSignInService | done (924ef77; import 12/12) |
| IMP-09 | A re-import downloads only new attachments, streamed to disk | P1 fix | M | — | MicrosoftTodoReader, GraphTodoClient, Data import, ImportWizardVM | done (IMP-09 commit; import 12/12) |
| IMP-10 | An expired sign-in keeps what was read and resumes at Review | P1 fix | S | IMP-09 | ImportWizardVM | open |
| IMP-11 | The Welcome `.ics` card keeps onboarding if the file picker is cancelled | P1 fix | S | — | ShellVM.Import, ShellVM `.ics` import | open |
| IMP-12 | Confirm before "Remove what was just imported" | P1 fix | S | — | ImportWizardView, ImportWizardVM, ShellPage dialogs | open |
| IMP-13 | Report what was kept but not converted | P1 fix | S | — | Core `ImportOutcome`, GraphTodoMapper, Data import | open |
| IMP-14 | Wizard frame: step indicator, fixed action bar, focus, keyboard | P2 UX | M | — | ImportWizardView(.xaml.cs), ImportWizardVM, DESIGN §10.2 | open |
| IMP-15 | Choose step: open/completed counts, select all, merge hints, sizes | P2 UX | M | IMP-14 | ImportWizardView, ImportWizardVM, ImportListOptionVM | open |
| IMP-16 | Review step: grouped summary, duplicates you can see, preview progress | P2 UX | M | IMP-13, IMP-14 | ImportWizardView, ImportWizardVM, Data import | open |
| IMP-17 | Done step: success state, Copy details, My Day hand-off | P2 UX | S | IMP-16 | ImportWizardView, ImportWizardVM, ShellVM.Import | open |
| IMP-18 | Errors you can act on; visible throttling | P2 UX | M | IMP-14 | GraphTodoClient, MicrosoftTodoReader, ImportWizardVM/View | open |
| IMP-19 | Settings → Data shows every imported account | P2 UX | S | — | SettingsView, ShellVM settings, ImportWizardVM | open |
| IMP-20 | Faster read: bounded parallel requests, measured | P3 perf | M | IMP-09 | MicrosoftTodoReader, GraphTodoClient, `docs/PERF.md` | open |
| IMP-21 | Interrupted imports leave nothing behind | P3 robustness | S | IMP-09 | App startup, ImportWizardVM, Data import | open |
| IMP-22 | Gate rows, M8 re-run, code review and docs for the polish | P3 | M | IMP-08…21 | `tests/ui/run-import-gate.ps1`, DESIGN §10.2/§12, PROGRESS | open |
| IMP-23 | 🔒 Remove an earlier import from Settings | P4 | M | IMP-12, IMP-19 | ADR-016 amendment, SettingsView, Data import | awaiting owner |
| IMP-24 | 🔒 Refresh the token in memory during long imports | P4 | S | IMP-10 | ADR-016 amendment, MicrosoftSignInService | awaiting owner |
| IMP-25 | Sanitize existing import exception traces | follow-up | S | — | ImportWizardVM, shell import traces | open (found during polish; outside IMP-08…22) |

Suggested order: the P1 fixes first (they are small and independent; IMP-10
builds on IMP-09). Then IMP-14, because it changes the view's skeleton that
IMP-15 to IMP-18 build on. IMP-19 to IMP-21 can run at any point, and IMP-22
comes last.

### 11.2 P1 — fixes found in review

**IMP-08 — Wrong text on the browser page after a cancelled sign-in.**
`AcceptRedirectAsync` treats a redirect carrying `error=` like one carrying
`code=`, and `WriteResponseAsync` always writes `Import_SignInCompletePage`
("You're signed in…") (`MicrosoftSignInService.cs:106-110`, `:160-165`).
When the person declines consent, the browser tells them they're signed in
while the wizard says the sign-in was cancelled. Change:
- Use a separate page string for `error=` (`Import_SignInFailedPage`: "Sign-in
  didn't finish. Go back to Wintodo to try again.").
- Style the page for dark mode (`prefers-color-scheme`) and give it a
  localised `<title>`. The page is HTML outside XAML, so the no-hardcoded-
  colours rule doesn't apply. Keep it minimal: system font, no external
  resources.
- Pull the response builder out as a pure function and unit-test both pages and
  the HTML encoding.

**IMP-09 — A re-import downloads every attachment again.** The mapper gives
every attachment a `SourcePath` (`GraphTodoMapper.cs:271-278`), so the reader
downloads all of them (`MicrosoftTodoReader.cs:82-99`), and only afterwards
does the store skip the ones already linked (`SqliteTaskStore.Import.cs:745-750`).
On the owner's account, every re-run downloads all 24 files again (up to
7.2 MB each). `GetAttachmentContentAsync` also buffers each file in a
`byte[]` (`GraphTodoClient.cs:142`). Change:
- The preview returns the external ids of the attachments it would create
  (`ImportOutcome.PendingAttachmentIds`; ids only, never stored).
  `DownloadAttachmentsAsync` takes that set and fetches nothing else. A
  second run with no new attachments makes zero `$value` requests.
- Stream `$value` straight to the staging file. The `contentBytes` fallback
  decodes from the stream into the file. Report progress in bytes, using the
  sizes from the metadata.
- Data tests: the second run's pending set is empty, and an attachment on a
  task deleted locally is not pending. Import tests: the fixture handler
  counts zero attachment requests on a re-run.

**IMP-10 — An expired sign-in throws away the whole read.** On a 401 the
wizard drops `_reader` and `_export` (`ImportWizardViewModel.cs:266-274`), so
signing in again reads every list again (16 lists and 897 tasks on the owner's
account) before the person can press Import a second time. Change:
- Keep `_export`, the list choices and the IMP-09 pending set. After the new
  sign-in, call `GetMeAsync` only. If it is the same account id, swap the
  client into the reader and return to **Review** with the message "Signed in
  again. Select Import to continue." If it is a different account, do a full
  read as today.
- The `--import-fixture-expire-once` gate row also checks that no second list
  read happens (by trace counts).

**IMP-11 — The Welcome `.ics` card ends onboarding before the file is
chosen.** `WelcomeImportIcalAsync` sets `onboarding.completed`, then opens the
picker (`ShellViewModel.Import.cs:108-113`). If the picker is cancelled,
Welcome is gone for good on an empty database. Change: open the picker from the
Welcome surface, and mark onboarding complete only after an `.ics` import is
applied. A cancelled picker leaves Welcome showing. Add a gate row for this.

**IMP-12 — Removing an import doesn't ask first.** **Remove what was just
imported** soft-deletes the run's lists, tasks and steps immediately
(`ImportWizardView.xaml:249-253` → `RemoveImportRunAsync`). After a first
import that can be hundreds of tasks, and the 20-entry undo cannot take them
back. Change:
- Show a `ContentDialog` that states the counts: "Remove 897 tasks and 16
  lists imported just now? Tasks you already had aren't affected." It should
  follow the delete-confirmation styling from UX-02. Because the action can't
  be undone, it always asks, whatever the confirm-before-delete setting is.
- Say plainly that updates the run made to existing tasks stay. Removal only
  undoes what the run created (§5).
- AutomationIds: `Todo.Import.RemoveConfirm`, `Todo.Import.RemoveConfirm.Primary`.

**IMP-13 — Values kept but not converted are never reported.** §3.2 says an
unconvertible recurrence "is reported". The mapper keeps it in
`X-MSTODO-RECURRENCE` (`GraphTodoMapper.cs:197-202`), but `ImportOutcome` has
no field for it, so the person never learns that a task lost its repeat.
Change: add counts to `ImportOutcome`/`ImportSnapshot` (repeats kept as text,
empty titles renamed to "Untitled task", reminders imported as already
acknowledged). Review and Done show each count that isn't zero, for example
"Repeats Wintodo can't use yet, kept with the task: 2". Add Core and Import
unit tests for each count.

### 11.3 P2 — wizard UX

**IMP-14 — Wizard frame.** Today the title never changes
(`ImportWizardView.xaml:24-30`), and only Choose and Review have headings.
Nothing tells the person how far through they are. The action buttons scroll
away under a long list of lists, and the code-behind never moves focus when
the step changes. A keyboard or Narrator user is left on a control that has
just collapsed. Change:
- Under the title, a caption "Step {0} of 4 · {1}" (Sign in · Choose · Review ·
  Import), announced with `LiveSetting="Polite"`, plus a Level2 heading for
  each step. AutomationId `Todo.Import.Step`.
- A fixed action bar in a third grid row, outside the `ScrollViewer`,
  bottom-aligned like a dialog footer. It holds the current step's primary
  and secondary commands, so Continue and Import are always visible.
- When `Step` changes, the view moves focus to the step's primary target with
  `FocusState.Programmatic`: Sign in, the first list checkbox, Import, or Go
  to my tasks. This is view code reacting to a property, not a click handler.
- `KeyboardAccelerator`s: Escape runs Cancel (or Back on Review), and the
  primary button is the default for Enter.
- Update DESIGN §10.2 and rerun M8 for the new surfaces.

**IMP-15 — Choose step.** Each list's detail line is `Tasks: {count of all
tasks}` (`ImportWizardViewModel.cs:392`), so turning off **Include completed
tasks** visibly changes nothing. Change:
- The detail line reads "12 open · 40 completed", and a footer total updates
  live as lists and the completed toggle change: "Importing 9 lists ·
  1,180 tasks".
- A three-state **Select all** checkbox above the lists
  (`Todo.Import.SelectAll`).
- Hints from the link table and list names. A list already imported reads
  "Imported before". A list that will merge reads "Adds to your list
  'Groceries'" (the hint §4.2 promised; today it is only the
  `ExistingLists` count).
- Flagged email gets its own description ("Emails you flagged in Outlook.
  Off by default.") so it is clear why it starts unchecked.
- The attachments card description shows "24 attachments · 38 MB" (from the
  metadata already read), so the person knows the cost before downloading.
- When no list is selected, an inline caption says why Continue is disabled.

**IMP-16 — Review step.** The summary is a flat list of same-weight lines in
a fixed order that mixes additions and skips
(`ImportWizardView.xaml:209-225`). Running the preview shows no progress and
cannot be cancelled: `ContinueAsync` only sets `IsBusy`, so the Choose step
seems to freeze on a large account. Change:
- Group the counts into **Will add** (lists, tasks, steps, attachments with
  size), **Will update** and **Left as they are** (already imported, your
  edits kept, deleted here, possible duplicates, plus the IMP-13 counts).
  Each group is a `SettingsCard`/`SettingsExpander`; the per-list breakdown
  goes in an expander.
- Possible duplicates are named, not just counted: up to 50 titles in an
  expander, with an **Import them anyway** toggle, which is the option §4.2
  step 4 implies. Titles stay in memory and never go into a trace.
- While the preview runs, show an inline `ProgressRing` and "Checking what's
  already here…", with Cancel wired to a token passed to
  `PreviewImportAsync`.
- Measure preview time on a 1,000-task fixture and record the row in
  `docs/PERF.md`.

**IMP-17 — Done step.** Change:
- Show the result in a `Success` `InfoBar` (or an `Informational` one for
  "Already up to date") above the grouped summary from IMP-16, so the import
  clearly ends.
- **Copy details** (promised in §1.2, not built) copies the counts, the date,
  the account display name and the app version. No task or list names.
  AutomationId `Todo.Import.CopyDetails`.
- My Day hand-off: My Day can't be migrated (§3.3), so add a secondary
  **Open My Day** button with the existing hint "Add today's tasks to My Day".
  It is ordinary navigation, so no new pattern is invented.

**IMP-18 — Errors you can act on; visible throttling.** The error `InfoBar`
(`ImportWizardView.xaml:45-49`) has no title and no action. On a 429/503,
`SendAsync` waits up to 60 s for each of 5 retries
(`GraphTodoClient.cs:248-257`) while the progress text stays frozen.
Change:
- Add an `InfoBar.ActionButton` for each failure: **Try again** (offline or
  read error, repeating the failed step), **Sign in again** (expired) and,
  for a 403 on a work account, **Copy link for your administrator** (the
  tenant admin-consent URL for the client id; **verify** the URL shape with
  the work-account gate). AutomationId `Todo.Import.ErrorAction`.
- `GraphTodoClient` reports retries through a callback, and the progress text
  shows "Microsoft is busy. Trying again in 30 s…". Add an Import test using
  a fixture 429 with `Retry-After` and a gate row.

**IMP-19 — Settings → Data shows every imported account.** §1.3 promised
"Last imported from … on …" under the Settings card. The card has only a
static description (`SettingsView.xaml:375-386`), and the wizard shows only
`sources.FirstOrDefault()` (`ImportWizardViewModel.cs:161`), which hides a
second account. Change: the card description lists one line per imported
account ("someone@outlook.com · 19 Oct 2026"), and the Connect step lists
them too. AutomationId `Todo.Settings.Data.MicrosoftTodoAccounts`.

### 11.4 P3 — performance and robustness

**IMP-20 — Faster read.** `ReadAsync` reads lists one at a time, and for each
task with attachments it makes one more metadata request
(`MicrosoftTodoReader.cs:52-64`). Attachment downloads are sequential too.
Change: bounded concurrency (start at 4 lists and 2 downloads), with a shared
throttle gate so that one 429 pauses every worker. Keep the output in list
order so `sort_order` stays stable. Progress counts completed lists.
"Measure, don't assume": record time to the Choose step and total import
time, before and after, in `docs/PERF.md`. Use the fixture handler with
injected latency in the VM, and the owner's account if the owner runs it.
Add no concurrency the numbers don't justify.

**IMP-21 — Interrupted imports leave nothing behind.**
- The staging folder `import-staging\<guid>` is deleted only in a `finally`
  (`ImportWizardViewModel.cs:246`, `:286`). If the app is killed mid-download
  the folder stays under the attachment root forever. Sweep `import-staging`
  on startup, off the UI thread.
- The build copies staged files into `attachments\<uid>\` *inside* the
  transaction, before the commit (`CopyStagedAttachments`,
  `SqliteTaskStore.Import.cs:817-848`). §5 says "after the commit". A
  failure rolls the files back, but a process killed after the copy and
  before the commit leaves files that no row references. Either update §5 to
  match and have the startup sweep remove unreferenced files under folders
  whose task has no attachment rows, or copy under a temporary name and
  rename after the commit. Add a Data test either way.
- Closing the window while the wizard is busy asks "An import is in progress.
  Stop it and close?" During Saving the close waits for the transaction to
  finish.

**IMP-22 — Gate, review, docs.** New rows in `run-import-gate.ps1`:
- a re-run makes zero attachment requests (trace);
- an expired sign-in resumes at Review with no second read;
- the remove confirmation;
- a cancelled Welcome `.ics` picker keeps Welcome;
- focus lands on each step's primary target, and Escape cancels;
- the error action buttons;
- the throttle message;
- Settings lists accounts;
- Select all and the live totals.

Then rerun M8 for the changed surfaces, run `/winui-code-review` with nothing
above informational, update DESIGN §10.2/§12, update this section's status
table, and add a PROGRESS entry.

### 11.5 P4 — needs the owner first (🔒)

**IMP-25 — Existing exception traces may contain private values.**
`ImportWizardViewModel.ShowError` writes the complete exception, which can
include Graph response messages or file paths. Shell import exception traces
have the same pattern. Replace these with type/category/count-only diagnostics
and add regression coverage in a separate package; this finding is not folded
into IMP-09 or the authorized polish packages.

**IMP-23 — Remove an earlier import from Settings.** Today removal exists
only on the Done step of the same session, because `_lastRunId` is held in
memory. Proposal: a **Remove imported tasks** action for each account in
Settings → Data. It soft-deletes what that source created and the person
hasn't edited since, with a confirmation that states the counts. This goes
beyond the spec (a new destructive action), so it needs an ADR-016 amendment.

**IMP-24 — Refresh the token in memory.** ADR-016 rules out silent refresh.
Requesting `offline_access` and keeping the refresh token **in memory only**
for the wizard's lifetime would remove the expired-sign-in detour completely.
Nothing would be persisted. Recommendation: ship IMP-10 first, and take this
only if a second sign-in still happens in practice.
