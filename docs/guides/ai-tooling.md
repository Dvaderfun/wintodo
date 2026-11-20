# AI tooling

The repo is set up so any agent — Claude Code, Codex, GitHub Copilot, Cursor,
Gemini — gets the same instructions, skills and tools from a fresh clone.

## Instruction files

| File | Read by | Holds |
|---|---|---|
| `AGENTS.md` | Codex, Copilot, Cursor, Gemini, Zed, Claude Code | Canonical rules: read order, commands, definition of done, hard rules, where things are |
| `CLAUDE.md` | Claude Code | `@AGENTS.md` import plus Claude-only notes |
| `ARCHITECTURE.md`, `docs/**` | Everyone, on demand | Details. `AGENTS.md` links to them; nobody reads them all every session. |

Keep `AGENTS.md` short and pointing outward. A rule that only matters for one
area belongs in that area's doc or guide.

## Vendored skills

The WinUI skills from the [Microsoft WinApp CLI plugin](https://github.com/microsoft/WinAppCli/tree/v0.8.0/plugins/winui)
are committed at a pinned release tag:

- `.agents/skills/winui-*` — Agent Skills location read by Codex, Copilot,
  Cursor, Gemini and others.
- `.claude/skills/winui-*` — identical mirror for Claude Code.
- `.agents/skills/VENDORED.md` — tag, commit and date.

| Skill | Use it when |
|---|---|
| `winui-setup` | Owner asks to set up or repair the toolchain. Never auto-invoke. |
| `winui-dev-workflow` | Building, running, diagnosing build errors or crashes, AOT runs |
| `winui-design` | Before any new XAML, theming or control choice — `winapp find-ui` / `find-api` |
| `winui-code-review` | Before committing a milestone or a UI-heavy change |
| `winui-ui-testing` | Writing or running a `winapp ui` suite |
| `winui-packaging` | MSIX, signing, CI release, Store (M10); AOT source-generation patterns |

Not vendored: `winui-wpf-migration` (no WPF here) and `winui-session-report`
(user-invoked agent-session diagnostics; available from the user-level plugin).

**Updating:** after checking the stable release and its full commit on the Microsoft repository, use
`.\scripts\Update-AgentSkills.ps1 -Tag vX.Y.Z -ExpectedCommit <full-commit>`.
Update the two defaults in the script in the same change. Review the upstream
release notes, update these guides if commands changed, then commit. The former
`microsoft/win-dev-skills` repository is now a catalog, not the source of these files.
Minor releases can break things
(0.7 removed `BuildAndRun.ps1` and the bundled analyzer). Never hand-edit the
vendored folders.

If you also have the `winui@win-dev-skills` user plugin installed in Claude
Code, each skill appears twice. Prefer the project copy; it matches the repo.

## WinApp CLI (`winapp`)

Microsoft's CLI for Windows apps. The current vendored skills target 0.8+.

```powershell
winapp run src\Wintodo.App\Wintodo.csproj --detach --json   # build + register + launch
winapp find-ui "navigation view with footer"                 # WinUI Gallery / Toolkit samples
winapp find-ui --id <id-1> --id <id-2> --json                # full XAML + C# for chosen samples
winapp find-api InfoBar --json --project-dir src\Wintodo.App # APIs as referenced by THIS project
winapp ui inspect -a <pid> --interactive --json              # UIA tree of the running app
winapp ui screenshot -a <pid> -o shot.png
```

Front-load lookups: one batch of `find-ui` queries for every control the
surface needs, `--id` the best few, then write XAML. Don't interleave
searching with coding. Run `find-api` from the app project so it reflects our
actual package versions, not a sample's newer ones.

## MCP servers

`.mcp.json` registers:

| Server | Endpoint | Use for |
|---|---|---|
| `microsoft-learn` | `https://learn.microsoft.com/api/mcp` (no auth) | Windows App SDK, WinUI, .NET, MSIX docs and code samples. Prefer it over memory for any API newer than your training data. |

Codex: add to `~/.codex/config.toml`:

```toml
[mcp_servers.microsoft-learn]
url = "https://learn.microsoft.com/api/mcp"
```

## CodeGraph

The repo has a local CodeGraph index (`.codegraph/`, database gitignored). When
available, `codegraph explore "<symbols or question>"` (or the
`codegraph_explore` MCP tool) returns the source plus callers and blast radius in
one call — use it before grep-and-read loops.

## Suggested session flow

1. `AGENTS.md` → `ARCHITECTURE.md` → `docs/PROGRESS.md` "Current state".
2. The spec for the area (`DESIGN.md` / `DATA-MODEL.md`) and the guide for the task.
3. Plan; if it changes a decision, draft an ADR and get the owner's acknowledgement.
4. Implement → `dotnet build -warnaserror` → `dotnet test` → the relevant UI gate.
5. `/winui-code-review` for UI work. Update docs the change touched. Log it in
   `docs/PROGRESS.md`.
