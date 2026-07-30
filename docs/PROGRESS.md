# Progress log

One entry per milestone. Append, never rewrite history.

Template:

```
## M<n> — <name>          <YYYY-MM-DD>

**Gate:** PASS / FAIL (which criteria)

**Shipped**
- ...

**Deviations from PLAN.md**
- <what> — <why> — <ADR if the deviation is permanent>

**Known issues carried forward**
- ...
```

---

_No milestones completed yet. M0 has not started._

## M0 — Setup and scaffold          2026-07-30

**Gate:** PASS — `BuildAndRun.ps1` built and launched the packaged x64 app
without an app exception; the M0 UI smoke suite passed 4/4; and
`dotnet build Wintodo.sln -p:Platform=x64 -warnaserror` completed with zero
warnings and zero errors across all five projects.

**Shipped**
- Initialized CodeGraph and Git with the `main` branch.
- Installed and enabled `winui@microsoft-winui` 0.3.0.
- Verified .NET SDK 10.0.302, WinApp CLI 0.5.0, current WinUI templates, and
  Developer Mode.
- Scaffolded the packaged WinUI app, Core and Data libraries, and both xUnit
  test projects with the required one-way project references.
- Added central SDK, package, analyzer, architecture, formatting, and ignore
  configuration.
- Added localized app identity resources and an empty, rule-compliant template
  window.
- Added the attached build/run workflow and repeatable M0 UI smoke test.
- Completed the WinUI code review with no actionable findings.

**Deviations from PLAN.md**
- `BuildAndRun.ps1` searches parent directories for an existing
  `Directory.Build.props` — the bundled script otherwise created a temporary
  project-level props file that shadowed the repository-wide target framework.

**Known issues carried forward**
- None.
