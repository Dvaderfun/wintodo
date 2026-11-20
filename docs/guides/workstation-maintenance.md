# Keeping Wintodo and its workstation current

There are two different jobs. A dependency update changes the build that could reach customers; a local tool update changes the developer's machine.

| Owner | What happens without asking |
|---|---|
| GitHub Dependabot | Weekly review PRs for Actions, direct NuGet dependencies (including native SQLite) and the exact .NET SDK in global.json. SDK/provider major migrations are deliberate decisions. Immutable Actions pins, reviewed locks, AOT, notices and guest checks still apply. |
| Scheduled project-health workflow | Compares official stable versions, release commits and scanner checksums. A failed feed is reported separately from a newer version. A job summary identifies the pin that needs attention. Schedules and Dependabot configuration run from the default branch after the change is merged. [CI guide](ci.md) · [Dependabot ecosystem reference](https://docs.github.com/en/code-security/dependabot/working-with-dependabot/dependabot-options-reference) |
| Windows Task Scheduler, current user | Each Saturday at 10:00 local, and at sign-in if due, service existing user tools, check configured plugins, run the registered skill publisher, and check the chosen checkout. No elevation, pushes, task-data access or Store releases. Reports persist even if an individual service fails. |

## Install or inspect the local job

Use PowerShell 7 and Python 3.12 or later. In the **reviewed maintenance checkout**, run:

~~~powershell
.\scripts\workstation\Install-WeeklyMaintenance.ps1 -RepoPath C:\path\to\wintodo
Get-ScheduledTaskInfo -TaskName Wintodo-Weekly-Maintenance
Start-ScheduledTask -TaskName Wintodo-Weekly-Maintenance
~~~

The installer stores a snapshot of the scripts under **%LOCALAPPDATA%\WintodoMaintenance\runners**, saves the selected Codex profile, and registers a limited, interactive-user task. The snapshot can read pins from a different checkout without executing that checkout's scripts. Reinstall from a subsequently reviewed revision to change the runner. The previous general developer-tools inventory is independent. If the machine is asleep or the user is logged out, the weekly job catches up on an eligible sign-in. A failed check is eligible to retry at a subsequent sign-in after a day.

Open **%LOCALAPPDATA%\WintodoMaintenance\reports\latest.md** for the component results, **project.md** for every repository pin and **plugins.log** for installed plugin states. An outstanding review or error also requests a Windows notification; Focus Assist and Windows notification preferences can suppress it. Exit code 0 means checked, 1 means review is needed, 2 means at least one check was incomplete. To force an immediate run instead of the schedule's due-date guard:

~~~powershell
.\scripts\workstation\Invoke-WorkstationMaintenance.ps1 -RepoPath C:\path\to\wintodo -Apply
~~~

Omit **-Apply** for an inventory only. Remove the schedule with **Unregister-ScheduledTask -TaskName Wintodo-Weekly-Maintenance**; this does not uninstall tools. Local reports stay on this machine. Its notification identity is the current-user registry key **HKCU:\Software\Classes\AppUserModelId\Wintodo.Workstation.Maintenance**.

## What can be updated unattended?

- **Node and npm for Codex:** compare the installed Node major with its supported LTS releases and check npm's stable version. On this workstation the versioned prefix belongs to the user and contains only npm and Codex. Stage a new Node beside its predecessor, verify the official ZIP checksum, OpenJS executable signature, npm engine requirements and the existing Codex version before switching the developer manifest. Old versions and a manifest backup are retained. A different manager, unsupported LTS line or other global packages request review.
- **Codex CLI:** official npm stable, only if the active npm prefix owns the CLI. **WinApp:** Microsoft's existing MSIX via WinGet. **Gallery:** the existing Microsoft Store package. A package-manager failure is an incomplete update, not success.
- **.NET:** the latest stable SDK on the accepted .NET 10 LTS line, side by side in the existing user-owned dotnet installation. The Microsoft installer must have a valid Microsoft Authenticode signature; existing host files and SDKs are kept. A machine-wide installation remains with its administrator. The checkout's global.json is never changed by this job.
- **CodeGraph:** stable Windows x64 standalone archive, checked against the release checksum and executed for a version check before switching the installation. Older active MCP processes reload when their client next restarts; the task does not terminate them.
- **Codex plugins:** the selected profile and the original enabled states are preserved. [Plugin maintenance](codex-plugin-maintenance.md) explains which marketplaces can refresh safely and which runtime packages have no independently published update channel. [Official OpenAI plugin documentation](https://developers.openai.com/codex/plugins/).
- **Claude Code:** for the existing native installation, compare its version with the official `@anthropic-ai/claude-code` tag for its configured `latest` or `stable` channel. Apply mode runs the native updater only when behind. See [Anthropic's setup and update guide](https://code.claude.com/docs/en/setup).
- **Claude plugins:** inventory every scoped install. Only the four explicitly reviewed, already-enabled user plugins are eligible: Claude MD Management, Figma and Security Guidance from Anthropic's official marketplace, and Andrej Karpathy Skills from its original publisher. Apply mode refreshes those two verified catalogs, checks plugin source identities, and calls the publisher's scoped update command with noninteractive input. It neither installs nor enables plugins nor accepts marketplace-declared commands. Re-read the installed IDs, scopes, versions and enablement after each change. Check-only mode reports cached versions and does not claim remote freshness. The local `claude.json` reports versions and outcomes without command text or project paths. Restart Claude Code to load an updated plugin. [Plugin CLI reference](https://code.claude.com/docs/en/plugins-reference) · [Marketplaces](https://code.claude.com/docs/en/plugin-marketplaces).
- With Codex CLI 0.171.0, reinstalling a disabled plugin is **not** an inactive update: a disposable local plugin changed from v1.0.0/disabled to v1.0.1/**enabled** when re-added. The job refreshes those catalogs but does not re-add disabled plugins. The user's real disabled plugins were left in their original state.
- **Standalone skills:** if the workstation has a registered skill publisher, use its tracked source revisions, local-edit checks and backups. The local report inventories skill locations without an owning updater in **manual-skills.txt**; no remote source is inferred from a matching name. No removed extensions are installed. For this project's six WinUI skills, use the separate [pinned, mirrored update](ai-tooling.md).

The first nonempty manual skill inventory, and later additions or removals, request review. An unchanged set stays listed without repeated notifications. A check-only run previews changes and does not advance the last applied inventory. If the registered publisher manager is missing, each eligible run requests review until it is restored.

The workstation job services Wintodo and its agent tools. It does not install unrelated products, enable disabled components, upgrade the OS or Visual Studio, or auto-merge a dependency update into a release.

For the broader workstation inventory (including Go, Azure, other .NET runtimes and unrelated applications), this machine also has a separate Sunday developer-tools check. Its last report is at `%USERPROFILE%\.codex\maintenance\reports\latest.json`. That job is an inventory; a green Wintodo run does not imply every program on the PC is current.
