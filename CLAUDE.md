# CLAUDE.md

All project instructions live in AGENTS.md (shared by every agent). It is imported below.

@AGENTS.md

## Claude Code specifics

- WinUI skills are in `.claude/skills/` (a generated mirror of `.agents/skills/`; never
  edit by hand). If the `winui@win-dev-skills` user plugin is also installed, prefer the
  project copies — they are pinned to what this repo uses.
- Project MCP servers are in `.mcp.json` (Microsoft Learn docs).
- Run `BuildAndRun.ps1` and attached `winapp run` in the background — they stay
  attached to the app until it exits.
- The PowerShell tool is the default shell here; scripts in `scripts/` and `tests/ui/`
  are PowerShell 7.
