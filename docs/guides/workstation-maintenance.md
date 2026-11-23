# Keeping Wintodo and its workstation current

There are two different jobs. A dependency update changes the build that could reach customers; a local tool update changes the developer's machine.

**Open the controls:** double-click `%LOCALAPPDATA%\WintodoMaintenance\Maintenance.cmd` (or [Maintenance.cmd](../../scripts/workstation/Maintenance.cmd) in this checkout). Opening it only reads local status. For each job, compare **what the registered action may do**, **Windows' last start/result and next trigger**, and **when, how and with what result the tool inventory completed**. These are separate facts. A manual inventory or a skipped trigger is not evidence of a successful scheduled check.

For Wintodo, the dated report is the evidence. If the `latest.md` or `latest.json` shortcut could not be replaced, the menu opens the dated report and flags the stale shortcut. A corrupt newest report is reported as such. Windows does not have to allow overwriting an open file for you to see how the check went.

| In the menu | Effect |
|---|---|
| **1 / 2**, **H** | Open the Wintodo or developer-tools report, or the history folder. |
| **N — Check Wintodo now** | Run the current registered Wintodo runner once in inventory mode. This button never passes update flags, even when automatic updates are scheduled. The Sunday inventory has its own command below. |
| **C — Checks only** | Keep the triggers, remove unattended installation flags from the selected registrations and retain a backup of the prior task definitions. Checking writes local reports and can query publisher feeds; it does not install the listed updates. |
| **P — Pause**, **R — Resume** | Disable or enable future triggers. Pausing does not interrupt a running process. Resuming an update-enabled task calls this out explicitly. |
| **S — Refresh**, **Enter — Close** | Read the current state again, or exit. Closing does not change either job. |

From PowerShell, `.\scripts\workstation\Maintenance.ps1 -Json` is read-only. Use `-Action Pause -Job Wintodo -WhatIf` to preview pausing just Saturday’s job. `-Action CheckOnly -WhatIf` previews removing unattended updates from both. Task Scheduler itself remains available at **Task Scheduler Library → task name → Triggers / Actions**.

**What actually runs on this PC.** Windows Task Scheduler is the alarm clock. When the Saturday trigger fires, it starts a hidden PowerShell process as the signed-in user. The runner is a snapshot in the user's local app data. It compares installed versions with configured publisher sources, applies eligible updates only if its registered mode permits them, checks the results, writes a report and exits. It does not need Wintodo or an AI chat open. It checks repository pins from the real checkout but does not edit or push them. Signing in can catch up on a missed run: the same runner/checkout skips a successful or review-needed check less than 6½ days old; an incomplete check is eligible again at the next trigger after one day. A second copy will not run at the same time. There is no always-running Wintodo maintenance service.

The job has ordinary user privileges, not a sandbox: it can change files and developer tools belonging to that user. Some standalone skill publishers track the tip of their default branch. An expected publisher and a backup make an update traceable and recoverable; they do not mean each new instruction or script was reviewed. The Windows scheduler supplies the timing and permissions, **not** an endorsement of the code it runs.

| Owner | What happens without asking |
|---|---|
| GitHub Dependabot | Weekly review PRs for Actions, direct NuGet dependencies (including native SQLite) and the exact .NET SDK in global.json. SDK/provider major migrations are deliberate decisions. Immutable Actions pins, reviewed locks, AOT, notices and guest checks still apply. |
| Scheduled project-health workflow | Compares official stable versions, release commits and scanner checksums. A failed feed is reported separately from a newer version. A job summary identifies the pin that needs attention. Schedules and Dependabot configuration run from the default branch after the change is merged. [CI guide](ci.md) · [Dependabot ecosystem reference](https://docs.github.com/en/code-security/dependabot/working-with-dependabot/dependabot-options-reference) |
| Windows Task Scheduler, current user | Each Saturday at 10:00 local, and at sign-in if due, check existing user tools (or service them in update mode), check configured plugins, run the registered skill publisher, and check the chosen checkout. No elevation, pushes, task-data access or Store releases. Reports persist even if an individual service fails. |

## Install or inspect the local job

Use PowerShell 7 and Python 3.12 or later. In the **reviewed maintenance checkout**, run:

~~~powershell
.\scripts\workstation\Install-WeeklyMaintenance.ps1 -RepoPath C:\path\to\wintodo
.\scripts\workstation\Maintenance.ps1
~~~

The installer stores a snapshot of the scripts under **%LOCALAPPDATA%\WintodoMaintenance\runners**, saves the selected Codex profile, and registers a limited, interactive-user task. The snapshot can read pins from a different checkout without executing that checkout's scripts. Reinstall from a subsequently reviewed revision to change the runner. The previous general developer-tools inventory is independent. If the machine is asleep or the user is logged out, the weekly job catches up on an eligible sign-in. A failed check is eligible to retry at a subsequent sign-in after a day.

Open **%LOCALAPPDATA%\WintodoMaintenance\reports\latest.md** for the component results, **project.md** for every repository pin and **plugins.log** for plugin results. A scheduled run that makes changes, needs review or encounters an error requests a Windows notification; Windows notification preferences can suppress display. Exit code 0 means completed, 1 means review is needed, 2 means a check or notification request was incomplete. To explicitly run the Wintodo checks now **without installing updates**:

~~~powershell
.\scripts\workstation\Invoke-WorkstationMaintenance.ps1 -RepoPath C:\path\to\wintodo
~~~

Running a scheduled task manually from Windows runs its **registered policy**, which may permit updates; use the runner directly without `-Apply` for an intentional inventory. For the separate Sunday tool inventory, run `& "$env:USERPROFILE\.codex\maintenance\Check-DeveloperUpdates.ps1"` without switches. Review a report before invoking a tool's updater. To explicitly apply all eligible Wintodo tool updates once, add `-Apply` to the direct command above; this does not change the schedule.

A new Saturday registration defaults to check-only. Reinstalling preserves an existing task's update policy, customized triggers, enabled/paused state, and Codex profile. To explicitly choose a policy, the Saturday installer accepts `-ScheduleMode CheckOnly` or `-ScheduleMode Apply`; use `-WhatIf` to preview. The control menu can switch both known registrations to checks only. It does not install a new scheduled job. An explicit reinstallation from the separate Sunday tool's original installer may re-enable Git/CLI updates; the status display reads the actual flags each time.

Each completed Wintodo run writes a dated Markdown and JSON summary under `reports\history\`, labeled **Manual** (direct command) or **Scheduled** (the registered action) when known. Trigger time and a human pressing **Run** on the registered action cannot be distinguished by that label. The summaries retain outcomes; per-component logs at the top level can be replaced on the next attempt. An overlapping run is refused before checking tools. A due-date skip reuses a prior completed report; a check-only report cannot suppress an update-enabled run. The notification field records whether Windows accepted a request, not whether you saw it. If a PC is off, a task never starts, or a process exits before writing a report, inspect the Windows last start/result and the report's age. Old reports cannot be retroactively attributed to a trigger.

Remove the Saturday schedule with **Unregister-ScheduledTask -TaskName Wintodo-Weekly-Maintenance**; this does not uninstall tools. Local reports stay on this machine. Its notification identity is the current-user registry key **HKCU:\Software\Classes\AppUserModelId\Wintodo.Workstation.Maintenance**.

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

When update-enabled, the separate Sunday 10:00 task, `Codex-DeveloperTools-WeeklyCheck`, updates only this user's existing portable Git for Windows and GitHub CLI. It verifies each release archive's SHA-256 and the executable's publisher signature, and keeps the previous installation. Its other tools, including Go, Azure and other .NET runtimes, are inventory only. Read the per-tool results in `%USERPROFILE%\.codex\maintenance\reports\latest.json`.

## Recommended simpler arrangement (proposal only)

| What is being maintained | Suggested owner |
|---|---|
| Versions that build or ship Wintodo | Dependabot PRs, immutable pins, and CI on the actual x64/ARM64 packages. Dependabot checks NuGet and GitHub Actions on `main`. The `dev` branch also includes .NET SDK checks and the project health workflow that prepares vendor update patches. No automatic promotion to Store. |
| Installed tools with a supported servicing route | Let Microsoft Store and the tool's existing package manager/native updater service their installations where appropriate. WinGet can perform selected upgrades when invoked; installing WinGet alone does not schedule upgrades for every tool. |
| Agent plugins, skills and exceptional portable installations | Keep one user-level weekly inventory/notification for both tool lists. Prefer a reviewed update for agent instructions or executable plugin changes, particularly inputs that follow a branch tip. Keep only the unattended, publisher-verified tool updates the owner wants. |

Task Scheduler is the normal local Windows mechanism. The bespoke part is having **two** scripts with overlapping inventories and numerous publisher-specific update paths. For one PC, one scheduled check and a short exception list are easier to support. A fleet service or installing another dependency bot would add infrastructure; neither replaces checks against the plugins and tools actually installed in this profile. This proposal does not alter the two current tasks.

For a read-only look in Windows, open **Task Scheduler → Task Scheduler Library → Wintodo-Weekly-Maintenance → Triggers / Actions** or use `Get-ScheduledTaskInfo -TaskName Wintodo-Weekly-Maintenance`. The local Markdown report gives more context than the numeric task result. [Microsoft: Task Scheduler](https://learn.microsoft.com/windows/win32/taskschd/task-scheduler-start-page) · [WinGet upgrade](https://learn.microsoft.com/windows/package-manager/winget/upgrade) · [GitHub: Dependabot version updates](https://docs.github.com/en/code-security/how-tos/secure-your-supply-chain/secure-your-dependencies/configure-version-updates).
