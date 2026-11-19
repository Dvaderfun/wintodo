# ADR-020 — Service the .NET SDK and its lockfiles together

- **Status:** accepted
- **Date:** 2026-11-19
- **Owner request:** fix the hosted SDK/lock failure and prevent it recurring.

On 16 November, the same commit that previously passed CI failed locked restore
when setup-dotnet selected a newer SDK and therefore a different ILLink package.
Keep .NET 10 LTS and locked restore. Pin the exact stable SDK in `global.json`
with `rollForward: disable`. Both CI jobs read that file. Update the pin,
reviewed per-target lockfiles and applicable runtime notices together.

Weekly CI reports new .NET 10 SDK releases without installing them into the
build. A missing pinned SDK should fail rather than quietly select another
compiler. The hosted runner, NuGet advisory feed and native tools can still
change; continue checking them on each candidate.
