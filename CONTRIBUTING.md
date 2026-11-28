# Contributing to Wintodo

Thank you for your interest in contributing to Wintodo!

## Licence for contributions

Wintodo's first-party source code and documentation are licensed under the
[Mozilla Public License 2.0 (MPL-2.0)](LICENSE).

By submitting a pull request, patch, or other contribution to this repository,
you represent that you have the right to submit it and agree that your
contribution is licensed under the **MPL-2.0** (no separate Contributor License
Agreement is required; you retain copyright to your contributions).

Brand names, icons, and visual assets are excluded from MPL-2.0 and governed by
[TRADEMARKS.md](TRADEMARKS.md).

## Before you start

1. Read [ARCHITECTURE.md](ARCHITECTURE.md) and [AGENTS.md](AGENTS.md) for the
   project layout, hard rules, and coding conventions.
2. Follow [docs/guides/build-and-run.md](docs/guides/build-and-run.md) to set
   up the pinned .NET SDK and WinApp CLI.
3. Verify your changes build cleanly with warnings as errors and pass tests:

```powershell
dotnet build Wintodo.sln -p:Platform=x64 -warnaserror
dotnet test  Wintodo.sln -p:Platform=x64
```

For larger features or design changes, please open an issue first so we can
align on scope and UX.
