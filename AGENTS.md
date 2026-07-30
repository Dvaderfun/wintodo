# AGENTS.md — operating rules

Read this at the start of **every** session, before touching anything.

- **The plan is `PLAN.md`.** Work milestone by milestone. Do not skip ahead.
- **Design spec is `docs/DESIGN.md`.** Read it before writing any XAML.
- **Data spec is `docs/DATA-MODEL.md`.** Read it before writing model or storage code.
- **Log every milestone** in `docs/PROGRESS.md`: what shipped, what deviated from
  the plan, and why.

---

## Toolchain

This project depends on Microsoft's `win-dev-skills` plugin. If it is not
installed, install it first (`PLAN.md` §4). Codex has no "agents" concept —
invoke skills by name: `/winui-setup`, `/winui-design`, `/winui-dev-workflow`,
`/winui-code-review`, `/winui-ui-testing`, `/winui-packaging`.

| Situation | Skill |
|---|---|
| Machine prep, missing prerequisite | `/winui-setup` (user-invoked only) |
| Building, running, diagnosing a build error | `/winui-dev-workflow` |
| Any new page, any XAML, any theming question | `/winui-design` |
| Before committing a milestone | `/winui-code-review` |
| Verifying a milestone gate | `/winui-ui-testing` |
| MSIX, signing, CI, Store | `/winui-packaging` |

Before writing XAML for a new surface, run the `winui-search.exe` bundled with
`winui-design`: **one** batched `search` call listing every control you need
(one focused query each), then `get` the best 3 IDs, then write the XAML from
those real samples. Do not interleave searching with coding.

---

## Hard rules — never violate

**Build and run**
- Never run the packaged `.exe` directly. Always `BuildAndRun.ps1` or `winapp run`.
- Invoke `BuildAndRun.ps1` **async** — it stays attached to the running app.
- Never add `<WindowsPackageType>None</WindowsPackageType>` to work around a
  launch failure.
- Never delete `Package.appxmanifest`.
- Never target `AnyCPU`. x64 or ARM64 only.
- Never pass `--version` to `dotnet add package` — take the latest stable.
- When a build fails with no useful diagnostic, suspect the last-edited `.xaml`.
  The XAML compiler under `dotnet build` swallows those errors; that is a known
  gap, not a mystery. Do not thrash through unrelated guesses.

**Code**
- `{x:Bind}` only, with an explicit `Mode`. Zero `{Binding}`.
- Zero `IValueConverter`. Use `x:Bind` to static functions in code-behind.
- Never `Converter={x:Null}` — it crashes at runtime.
- `TextBox` two-way bindings **must** include `UpdateSourceTrigger=PropertyChanged`.
- Commands, not `Click`/`Tapped` handlers.
- `VisualStateManager` for visual state, never code-behind property setting.
- No reflection. No `Activator.CreateInstance`, no `Type.GetType`, no
  assembly scanning, no reflection-based JSON. This app ships Native AOT at M9;
  every reflective call is a time bomb.
- `[ObservableProperty]` on **partial properties**, not the legacy field-backed
  form — the WinUI analyzer flags the old pattern.
- `DispatcherQueue` for UI marshalling. Never `Dispatcher`, never `CoreDispatcher`,
  never `Window.Current`.
- No UWP-era namespaces leaking into the app (`Windows.UI.Xaml.*`) except the
  deliberate `Windows.UI.Notifications` scheduling path in M7 and
  `Windows.Security.Credentials` in M13.
- `TreatWarningsAsErrors` is on. Do not suppress a warning to get a green build —
  fix it, or write down in `docs/PROGRESS.md` why suppression is correct.

**Design**
- No hardcoded colours, corner radii, font sizes, or font weights.
- Every spacing value is a multiple of 4.
- Three theme dictionaries always: `Light`, `Dark`, `HighContrast`. Never
  `x:Key="Default"`.
- High-contrast dictionaries use only the 8 permitted `SystemColor*` brushes.
- `AutomationProperties.AutomationId` on every interactive element, named
  `Todo.<Area>.<Element>`. The UI test suite depends on these — treat them as API.

**Data**
- Never store a credential in SQLite. `PasswordVault` only.
- Never concatenate user input into SQL. Parameterised commands only.
- Never destroy an iCalendar property you don't understand — round-trip it.
- Soft delete plus a `change_log` row. Never hard-delete on a user action.

---

## Working style

- Batch fixes. When a build produces 12 errors, read all 12, fix all 12, rebuild
  once.
- Do not add a dependency without checking it against the banned list in
  `PLAN.md` §3 and confirming it is AOT-clean.
- Do not "improve" the plan mid-milestone. If the plan is wrong, say so, write an
  ADR in `docs/adr/`, get it acknowledged, then change it.
- Do not report a milestone complete until its gate actually passes. If part of a
  gate fails, say which part and why — a partial pass reported as a pass is worse
  than a failure.
- Measure, don't assume. Perf claims go in `docs/PERF.md` with real numbers from
  `scripts/Measure-Perf.ps1`.
- Commit per milestone at minimum, with a message naming the milestone.
- Ask the human before: enabling Developer Mode (UAC), installing Visual Studio,
  changing an ADR, or anything that touches their machine outside this repo.

---

## Project facts

- Working directory: `the repository root`
- `Wintodo` is a **placeholder name**. Keep every user-visible string in
  `Strings/en-US/Resources.resw` so the rename is one find-replace plus a new
  MSIX identity.
- .NET SDK 10.0.302 is installed. Windows App SDK 2.3.1+ stable.
- `winapp` CLI was **not** installed as of the last check — `/winui-setup` fixes that.
- v1 is a Microsoft To Do clone. Do not invent new navigation patterns in v1.
