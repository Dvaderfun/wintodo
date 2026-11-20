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

- **Codex CLI:** official npm stable, only if the active npm prefix owns the CLI. **WinApp:** Microsoft's existing MSIX via WinGet. **Gallery:** the existing Microsoft Store package. A package-manager failure is an incomplete update, not success.
- **.NET:** the latest stable SDK on the accepted .NET 10 LTS line, side by side in the existing user-owned dotnet installation. The Microsoft installer must have a valid Microsoft Authenticode signature; existing host files and SDKs are kept. A machine-wide installation remains with its administrator. The checkout's global.json is never changed by this job.
- **CodeGraph:** stable Windows x64 standalone archive, checked against the release checksum and executed for a version check before switching the installation. Older active MCP processes reload when their client next restarts; the task does not terminate them.
- **Codex plugins:** the selected profile and the original enabled states are preserved. [Plugin maintenance](codex-plugin-maintenance.md) explains which marketplaces can refresh safely and which runtime packages have no independently published update channel. [Official OpenAI plugin documentation](https://developers.openai.com/codex/plugins/).
- **Standalone skills:** if the workstation has a registered skill publisher, use its tracked source revisions, local-edit checks and backups. Skills without an established publisher are manual by design. No removed extensions are installed. For this project's six WinUI skills, use the separate [pinned, mirrored update](ai-tooling.md).

The workstation job services Wintodo and its agent tools. It does not install unrelated products, enable disabled components, upgrade the OS or Visual Studio, or auto-merge a dependency update into a release.
