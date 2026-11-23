# Maintain the installed Codex plugins

Run the workstation adapter in the intended user profile. It uses the configured
Codex CLI and inherits its profile selection. For a scheduled task with a
different PATH, pass the trusted CLI's full path with `-CodexCommand`.

```powershell
# Read the currently installed plugins and cached catalog.
pwsh -NoProfile -File scripts/workstation/Update-CodexPlugins.ps1 -Json
# Refresh approved Git catalogs and apply an available update to enabled Floorplan.
pwsh -NoProfile -File scripts/workstation/Update-CodexPlugins.ps1 -Apply -Json
```

The adapter compares the configured source with the actual Git origin before
refreshing the named Floorplan, Microsoft Azure skills and Microsoft Windows
development skills marketplaces. It verifies the CLI's selected marketplace,
changed roots and errors. An empty changed-roots array with no errors means
the CLI had nothing to refresh. Unknown Git sources need review.

The [official Codex CLI reference](https://developers.openai.com/codex/cli/reference)
documents `plugin list --json`, `plugin marketplace upgrade` and `plugin add`.
The CLI has no standalone plugin-update command. For a newer numeric Floorplan
release, the adapter can re-add the exact **already installed, enabled**
selector from its verified existing marketplace and confirm the resulting
version. Disabled plugins stay disabled. Runtime-distributed plugins report
the installed version only; their local catalog does not establish a public
latest version. New Codex sessions pick up refreshed plugin capabilities
([OpenAI plugin documentation](https://developers.openai.com/codex/plugins)).

Exit **0** means no action remains for supported plugins, **1** means review,
and **2** means verification was incomplete. Check-only results reflect the
cached marketplace; use apply mode for an upstream refresh. The script
checks that the installed plugin IDs and enabled states match before and
after. It never installs an unselected plugin or rewrites Codex configuration
or plugin caches directly.
