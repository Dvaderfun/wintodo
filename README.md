# Wintodo

A native Windows task manager: a packaged WinUI 3 app that starts as a faithful
Microsoft To Do clone, built on open standards (iCalendar `VTODO`, CalDAV)
instead of a proprietary backend. Native AOT, x64 and ARM64. Wintodo is
reserved in Microsoft Store under Univera LLC; publication is pending.

The [source licence](LICENSE.md) allows free self-builds and free unbranded
community distributions. The individual author retains the Wintodo brand and
commercial licensing rights. Univera LLC publishes the official app.

## Build and run

Prerequisites: the exact .NET SDK in `global.json`, WinApp CLI 0.7+, Developer Mode.

```powershell
dotnet build Wintodo.sln -p:Platform=x64 -warnaserror
dotnet test  Wintodo.sln -p:Platform=x64
.\BuildAndRun.ps1
```

Details: [docs/guides/build-and-run.md](docs/guides/build-and-run.md).

## Where to read next

| You want | Read |
|---|---|
| How the code is organised | [ARCHITECTURE.md](ARCHITECTURE.md) |
| Product scope, milestones, budgets | [PLAN.md](PLAN.md) |
| What is done and what is open | [docs/PROGRESS.md](docs/PROGRESS.md) — "Current state" |
| Every other doc | [docs/README.md](docs/README.md) |
| Rules for AI coding agents | [AGENTS.md](AGENTS.md) |
