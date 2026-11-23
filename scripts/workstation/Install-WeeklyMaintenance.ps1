# Installs a snapshot of reviewed scripts under the current user's profile. No elevation.
[CmdletBinding(SupportsShouldProcess)]
param([Parameter(Mandatory)][string]$RepoPath,
      [ValidateSet('Preserve','CheckOnly','Apply')][string]$ScheduleMode = 'Preserve')
$ErrorActionPreference = 'Stop'
$checkout = (Resolve-Path -LiteralPath $RepoPath).ProviderPath
if (!(Test-Path -LiteralPath (Join-Path $checkout 'Wintodo.sln'))) { throw 'Expected a Wintodo checkout' }
$parent = Join-Path $env:LOCALAPPDATA 'WintodoMaintenance'
$taskName = 'Wintodo-Weekly-Maintenance'
$existing = Get-ScheduledTask -TaskPath '\' -ErrorAction Stop | Where-Object TaskName -eq $taskName
$profileSelection = [string]$env:CODEX_HOME
$previouslyApplies = $false
$ownerIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
if ($existing) {
    $who = [string]$existing.Principal.UserId
    $taskSid = if ($who -match '^S-\d-') { $who } else {
        ([Security.Principal.NTAccount]::new($who)).Translate([Security.Principal.SecurityIdentifier]).Value
    }
    if ($taskSid -ne $ownerIdentity.User.Value -or $existing.Principal.RunLevel -ne 'Limited' -or $existing.Principal.LogonType -ne 'Interactive') {
        throw 'Task must belong to the current user with limited interactive permissions; no changes made.'
    }
    $oldAction = @($existing.Actions)[0]
    $oldPattern = '^-NoProfile -NonInteractive -WindowStyle Hidden -File "(?<script>'+[regex]::Escape((Join-Path $parent 'runners')+'\')+'[0-9A-F]{16}\\Invoke-WorkstationMaintenance\.ps1)" -RepoPath "[^"\r\n]+"(?<apply> -Apply)? -IfDue -Notify(?: -RunSource Scheduled)?$'
    if (@($existing.Actions).Count -ne 1 -or [IO.Path]::GetFileName($oldAction.Execute) -ine 'pwsh.exe' -or
        $oldAction.Arguments -inotmatch $oldPattern) { throw 'Unrecognized task action; no changes made.' }
    $previouslyApplies = [bool]$Matches['apply']
    $oldFolder = Split-Path -Parent $Matches['script']
    if ($oldAction.WorkingDirectory -ine $oldFolder) { throw 'Unrecognized task working directory; no changes made.' }
    $profileSelection = [string](Get-Content -LiteralPath (Join-Path $oldFolder 'local-profile.json') -Raw | ConvertFrom-Json).codexProfile
    if ($existing.State -eq 'Running') { throw 'The current job is running. Retry after it finishes.' }
}
$autoApply = $ScheduleMode -eq 'Apply' -or ($ScheduleMode -eq 'Preserve' -and $previouslyApplies)
$modeLabel = if ($autoApply) {'check and apply eligible updates'} else {'check only'}
if (!$PSCmdlet.ShouldProcess($taskName,"Install reviewed runner ($modeLabel) for $checkout")) { return }
$null = New-Item -ItemType Directory -Force -Path $parent
$scriptSource = Split-Path $PSScriptRoot -Parent
$sources = @('Invoke-WorkstationMaintenance.ps1','Update-AgentNode.ps1','Update-CodexPlugins.ps1','Update-ClaudeTools.py','Update-CodeGraph.py','Show-MaintenanceNotice.ps1') |
    ForEach-Object { Join-Path $PSScriptRoot $_ }
$sources += Join-Path $scriptSource 'Check-ProjectHealth.py'
$sha = [Security.Cryptography.SHA256]::Create()
$hash = [Convert]::ToHexString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes((@($sources | ForEach-Object { (Get-FileHash -LiteralPath $_).Hash }) -join ':')+'|'+$profileSelection))).Substring(0,16)
$dest = Join-Path $parent "runners/$hash"
$null = New-Item -Force -ItemType Directory -Path $dest
foreach ($src in $sources) { Copy-Item -LiteralPath $src -Destination $dest -Force }
@{codexProfile=$profileSelection} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dest 'local-profile.json') -Encoding utf8
$job = Join-Path $dest 'Invoke-WorkstationMaintenance.ps1'
if (($job+$checkout) -match '["\r\n]') { throw 'Unsupported task path' }
$applyFlag = if ($autoApply) {' -Apply'} else {''}
$argsText = '-NoProfile -NonInteractive -WindowStyle Hidden -File "'+$job+'" -RepoPath "'+$checkout+'"'+$applyFlag+' -IfDue -Notify -RunSource Scheduled'
$action = New-ScheduledTaskAction -Execute (Get-Command pwsh -CommandType Application | Select-Object -First 1).Source -Argument $argsText -WorkingDirectory $dest
$noticeKey = 'HKCU:\Software\Classes\AppUserModelId\Wintodo.Workstation.Maintenance'
if (!(Test-Path -LiteralPath $noticeKey)) {
    $null = New-Item -Path $noticeKey
    $null = New-ItemProperty -LiteralPath $noticeKey -Name DisplayName -Value 'Wintodo development maintenance' -PropertyType String
}
if ($existing) {
    if ($oldAction.Arguments -cne $argsText -or $oldAction.WorkingDirectory -ine $dest) {
        $backupDir = New-Item -ItemType Directory -Force -Path (Join-Path $parent 'task-backups')
        $backupPath = Join-Path $backupDir.FullName "$taskName-$([guid]::NewGuid().ToString('N')).xml"
        [IO.File]::WriteAllText($backupPath,(Export-ScheduledTask -TaskName $taskName -TaskPath '\'),[Text.Encoding]::Unicode)
        $null = Set-ScheduledTask -TaskName $taskName -TaskPath '\' -Action $action
        Write-Output "Previous task definition: $backupPath"
    }
} else {
    $triggers = @((New-ScheduledTaskTrigger -Weekly -WeeksInterval 1 -DaysOfWeek Saturday -At 10:00), (New-ScheduledTaskTrigger -AtLogOn -User $ownerIdentity.Name))
    $principal = New-ScheduledTaskPrincipal -UserId $ownerIdentity.Name -LogonType Interactive -RunLevel Limited
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Hours 2)
    $null = Register-ScheduledTask -TaskName $taskName -TaskPath '\' -Action $action -Trigger $triggers -Principal $principal -Settings $settings -Description 'Check Wintodo developer tools and write a local maintenance report.'
}
$registered = Get-ScheduledTask -TaskPath '\' -TaskName $taskName -ErrorAction Stop
if (@($registered.Actions).Count -ne 1 -or $registered.Actions[0].Arguments -cne $argsText -or
    ($existing -and [bool]$registered.Settings.Enabled -ne [bool]$existing.Settings.Enabled)) {
    throw 'Registered action or enabled state differed from the requested configuration. Inspect Task Scheduler and the backup.'
}
foreach ($name in 'Maintenance.ps1','Maintenance.cmd') {
    $sourcePath = Join-Path $PSScriptRoot $name
    $controlPath = Join-Path $parent $name
    [IO.File]::WriteAllBytes("$controlPath.new",[IO.File]::ReadAllBytes($sourcePath))
    [IO.File]::Move("$controlPath.new",$controlPath,$true)
}
Write-Output "Installed runner ($modeLabel): $job"
Write-Output ('Open for status and controls: '+(Join-Path $parent 'Maintenance.cmd'))
