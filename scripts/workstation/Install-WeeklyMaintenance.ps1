# Installs a snapshot of reviewed scripts under the current user's profile. No elevation.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$RepoPath)
$ErrorActionPreference = 'Stop'
$checkout = (Resolve-Path -LiteralPath $RepoPath).ProviderPath
if (!(Test-Path -LiteralPath (Join-Path $checkout 'Wintodo.sln'))) { throw 'Expected a Wintodo checkout' }
$parent = Join-Path $env:LOCALAPPDATA 'WintodoMaintenance'
$null = New-Item -ItemType Directory -Force -Path $parent
$scriptSource = Split-Path $PSScriptRoot -Parent
$sources = @('Invoke-WorkstationMaintenance.ps1','Update-CodexPlugins.ps1','Update-CodeGraph.py','Show-MaintenanceNotice.ps1') |
    ForEach-Object { Join-Path $PSScriptRoot $_ }
$sources += Join-Path $scriptSource 'Check-ProjectHealth.py'
$sha = [Security.Cryptography.SHA256]::Create()
$hash = [Convert]::ToHexString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes((@($sources | ForEach-Object { (Get-FileHash -LiteralPath $_).Hash }) -join ':')+'|'+[string]$env:CODEX_HOME))).Substring(0,16)
$dest = Join-Path $parent "runners/$hash"
$null = New-Item -Force -ItemType Directory -Path $dest
foreach ($src in $sources) { Copy-Item -LiteralPath $src -Destination $dest -Force }
@{codexProfile=[string]$env:CODEX_HOME} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dest 'local-profile.json') -Encoding utf8
$job = Join-Path $dest 'Invoke-WorkstationMaintenance.ps1'
if (($job+$checkout) -match '["\r\n]') { throw 'Unsupported task path' }
$argsText = '-NoProfile -NonInteractive -WindowStyle Hidden -File "'+$job+'" -RepoPath "'+$checkout+'" -Apply -IfDue -Notify'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
$action = New-ScheduledTaskAction -Execute (Get-Command pwsh -CommandType Application | Select-Object -First 1).Source -Argument $argsText -WorkingDirectory $dest
$triggers = @((New-ScheduledTaskTrigger -Weekly -WeeksInterval 1 -DaysOfWeek Saturday -At 10:00), (New-ScheduledTaskTrigger -AtLogOn -User $identity))
$principal = New-ScheduledTaskPrincipal -UserId $identity -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Hours 2)
$taskName = 'Wintodo-Weekly-Maintenance'
$existing = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if ($existing -and (@($existing.Actions).Count -ne 1 -or !$existing.Actions[0].Arguments.Contains((Join-Path $parent 'runners')+'\'))) {
    throw 'A different task already owns this name; nothing was replaced.'
}
$noticeKey = 'HKCU:\Software\Classes\AppUserModelId\Wintodo.Workstation.Maintenance'
$null = New-Item -Path $noticeKey -Force
$null = New-ItemProperty -LiteralPath $noticeKey -Name DisplayName -Value 'Wintodo development maintenance' -PropertyType String -Force
$null = Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $triggers -Principal $principal -Settings $settings -Force -Description 'Service existing Wintodo developer tools; report repository changes for review.'
Get-ScheduledTaskInfo -TaskName $taskName | Select-Object TaskName,NextRunTime
Write-Output "Installed runner: $job"
