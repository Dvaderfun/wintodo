#requires -Version 7.2
# Status reads only local records. Nothing here invokes an updater or starts a scheduled task.
[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('Status','Pause','Resume','CheckOnly')][string]$Action = 'Status',
    [ValidateSet('Both','Wintodo','DeveloperTools')][string]$Job = 'Both',
    [switch]$AllowAutomaticUpdates, [switch]$Json, [switch]$Menu
)
$ErrorActionPreference = 'Stop'
if ($Menu -and ($Action -ne 'Status' -or $Job -ne 'Both' -or $Json)) { throw 'Use -Menu on its own.' }
$locations = [ordered]@{
    Wintodo = @{name='Wintodo-Weekly-Maintenance'; dir=(Join-Path $env:LOCALAPPDATA 'WintodoMaintenance/reports')}
    DeveloperTools = @{name='Codex-DeveloperTools-WeeklyCheck'; dir=(Join-Path $env:USERPROFILE '.codex/maintenance/reports')}
}
$knownPwsh = @((Get-Command pwsh -All -CommandType Application).Source) + (Join-Path $PSHOME 'pwsh.exe')

function Read-Mode($key,$task) {
    $actions = @($task.Actions)
    if ($actions.Count -ne 1 -or $actions[0].Execute -notin $knownPwsh) { return 'Unknown' }
    $line = [string]$actions[0].Arguments
    if ($line -notmatch '^\s*(?:(?:-NoProfile|-NonInteractive|-NoLogo|-WindowStyle\s+Hidden)\s+)*-File\s+"(?<file>[^"]+)"(?<tail>.*)$') { return 'Unknown' }
    $file,$tail = $Matches.file,$Matches.tail.Trim()
    if ($key -eq 'Wintodo') {
        $dir = Join-Path $env:LOCALAPPDATA 'WintodoMaintenance\runners'
        if ($file -notmatch ('^'+[regex]::Escape($dir)+'\\[0-9A-F]{16}\\Invoke-WorkstationMaintenance\.ps1$') -or
            $tail -notmatch '^-RepoPath\s+"[^"]+"(?<flags>(?:\s+-(?:Apply|IfDue|Notify|RunSource\s+Scheduled))*)$') { return 'Unknown' }
        if ($Matches.flags -split '\s+' -contains '-Apply') { return 'Automatic tool updates' }
    } else {
        if ($file -ne (Join-Path $env:USERPROFILE '.codex\maintenance\Check-DeveloperUpdates.ps1') -or
            $tail -notmatch '^(?:(?:-ApplyScm|-RefreshSkills)(?:\s+|$))*$') { return 'Unknown' }
        if ($tail -match '-RefreshSkills') { return 'Automatic skill updates (inspect action)' }
        if ($tail -match '-ApplyScm') { return 'Automatic Git and GitHub CLI updates' }
    }
    return 'Check only'
}

function Read-Triggers($task) {
    foreach ($t in $task.Triggers) {
        $disabled = if ($t.Enabled -eq $false) { ' (disabled)' }
        switch ($t.CimClass.CimClassName) {
            'MSFT_TaskWeeklyTrigger' {
                $days = (0..6 | Where-Object { $t.DaysOfWeek -band (1 -shl $_) } | ForEach-Object { [DayOfWeek]$_ }) -join ','
                "Every $($t.WeeksInterval) week(s): $days $(([datetime]$t.StartBoundary).ToString('HH:mm'))$disabled"
            }
            'MSFT_TaskLogonTrigger' { "Sign-in$disabled" }
            default { "Other trigger: $($t.CimClass.CimClassName)$disabled" }
        }
    }
}

function Read-Report($key,$dir) {
    $path = Join-Path $dir 'latest.json'
    $data = [ordered]@{time=$null; source='not recorded'; freshness='Unknown'; outcome='No report'; notification=$null; reportPath=$path; folder=$dir}
    if ($key -eq 'Wintodo' -and (Test-Path -LiteralPath (Join-Path $dir 'history') -PathType Container)) { $data.folder = Join-Path $dir 'history' }
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { return $data }
    try {
        $r = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
        if (!$r.checkedAt) { throw 'missing timestamp' }
        $when = [DateTimeOffset]$r.checkedAt
        $data.time = $when.ToLocalTime().ToString('yyyy-MM-dd HH:mm:ss zzz')
        $age = [DateTimeOffset]::Now - $when
        $data.freshness = if ($age.TotalMinutes -lt -5) {'Future timestamp'} elseif ($age.TotalDays -gt 8) {'Stale (>8 days)'} else {'Recent'}
        if ($key -eq 'Wintodo') {
            if ($r.exitCode -notin @(0,1,2) -or !$r.components -or @($r.components | Where-Object { !$_.state }).Count) { throw 'missing component results' }
            if ($r.runSource -in @('Manual','Scheduled')) { $data.source = $r.runSource }
            if ($r.notification.state) { $data.notification = $r.notification.state }
            $outcome = @('Completed','Review needed','INCOMPLETE')[[int]$r.exitCode]
            $runMode = if ($r.applied -eq $true) {'updates allowed'} elseif ($r.applied -eq $false) {'check only'} else {'unknown mode'}
            $attention = @($r.components | Where-Object state -in @('incomplete','review','updated') | ForEach-Object { "$($_.name): $($_.state)" })
            $data.outcome = "$outcome ($runMode). " + ($attention -join '; ')
            $md = Join-Path $dir 'latest.md'
            if (Test-Path -LiteralPath $md -PathType Leaf) { $data.reportPath = $md }
        } else {
            if (!$r.tools -or @($r.tools | Where-Object { !$_.status }).Count) { throw 'missing tool results' }
            $summary = ($r.tools | Group-Object status | ForEach-Object { "$($_.Count) $($_.Name)" }) -join '; '
            if ($r.skillPublisherCheckFailed) { $summary += '; skill publisher check FAILED' }
            $data.outcome = $summary
        }
    } catch {
        $data.outcome = 'UNREADABLE or incomplete report; inspect the local file.'
        $data.freshness = 'Unknown'
    }
    return $data
}

function Get-Record($key) {
    $entry = $locations[$key]
    $record = [ordered]@{job=$key; task=$entry.name; state='Unknown'; mode='Unknown'; command=$null; access='Unknown'; limitedInteractive=$false; triggers='Unknown'; lastRun=$null; lastResult=$null; lastMeaning='unknown'; nextRun=$null; missed=$null; report=(Read-Report $key $entry.dir)}
    try {
        $t = Get-ScheduledTask -TaskPath '\' -TaskName $entry.name
        $record.state = [string]$t.State
        $record.mode = Read-Mode $key $t
        if ($record.mode -ne 'Unknown') { $record.command = '"'+$t.Actions[0].Execute+'" '+$t.Actions[0].Arguments }
        $record.access = "$($t.Principal.UserId), $($t.Principal.RunLevel), $($t.Principal.LogonType)"
        $record.limitedInteractive = "$($t.Principal.RunLevel)" -eq 'Limited' -and "$($t.Principal.LogonType)" -eq 'Interactive'
        $record.triggers = (Read-Triggers $t) -join '; '
        $i = Get-ScheduledTaskInfo -TaskPath '\' -TaskName $entry.name
        if ($i.LastRunTime -and $i.LastRunTime.Year -gt 2000) { $record.lastRun = $i.LastRunTime.ToString('yyyy-MM-dd HH:mm:ss') }
        if ($record.state -ne 'Disabled' -and $i.NextRunTime -and $i.NextRunTime.Year -gt 2000) { $record.nextRun = $i.NextRunTime.ToString('yyyy-MM-dd HH:mm:ss') }
        $record.lastResult = $i.LastTaskResult
        $record.lastMeaning = switch ($i.LastTaskResult) {
            0 { 'normal exit; may have skipped when not due' }
            267009 { 'running' }
            267011 { 'never started' }
            default { 'nonzero; compare report time before interpreting' }
        }
        $record.missed = $i.NumberOfMissedRuns
    } catch {
        if ($_.CategoryInfo.Category -eq 'ObjectNotFound' -and $record.state -eq 'Unknown') { $record.state = 'Not registered or not visible' }
        $record.error = "Could not read all scheduler details ($($_.CategoryInfo.Category))."
    }
    [pscustomobject]$record
}

function Write-Record($r) {
    "$($r.task)  |  $($r.state)  |  $($r.mode)"
    "  Runs as: $($r.access)   Triggers: $($r.triggers)"
    "  Registered action: $($r.command ?? 'unknown; inspect in Task Scheduler')"
    "  Windows last start: $($r.lastRun ?? 'unknown/never')   Last result: $($r.lastResult ?? 'unknown') ($($r.lastMeaning))   Missed triggers: $($r.missed ?? 'unknown')"
    "  Next scheduled time: $($r.nextRun ?? 'none reported')"
    "  Latest report: $($r.report.time ?? 'unknown') [$($r.report.freshness); source: $($r.report.source)] $($r.report.outcome)"
    "  Report: $($r.report.reportPath)"
    "  Reports folder: $($r.report.folder)"
    if ($r.report.notification) { "  Notification: $($r.report.notification)" }
    if ($r.error) { "  $($r.error)" }
    ''
}

function Set-ChecksOnly($records,$caller) {
    $changes = @(foreach ($r in $records) {
        $t = Get-ScheduledTask -TaskPath '\' -TaskName $r.task
        $mode = Read-Mode $r.job $t
        if ($r.error -or $mode -eq 'Unknown' -or !$r.limitedInteractive) { throw "Unrecognized or unavailable: $($r.task). Nothing changed." }
        if ("$($t.State)" -eq 'Running') { throw "$($r.task) is already running. Try again after it finishes. Nothing changed." }
        $exec = $t.Actions[0]
        if ($exec.Arguments -notmatch '^(?<head>.*?(?:-RepoPath|-File)\s+"[^"]+")(?<tail>(?:\s+-(?:ApplyScm|RefreshSkills|Apply|Notify|IfDue|RunSource\s+Scheduled))*)\s*$') { throw "Unrecognized arguments: $($r.task)." }
        $head,$tail = $Matches.head,$Matches.tail
        if ($r.job -eq 'Wintodo' -and $tail -notmatch '(?:^|\s)-RunSource\s+Scheduled(?:$|\s)') {
            throw 'Install the reviewed Wintodo runner with Install-WeeklyMaintenance.ps1 first. The old runner cannot honor the weekly due interval in check-only mode. Nothing changed.'
        }
        if ($mode -ne 'Check only') {
            [pscustomobject]@{record=$r; exec=$exec; old=$exec.Arguments; new=($head+($tail -replace '\s+-(?:ApplyScm|RefreshSkills|Apply)(?=\s|$)','')); xml=$null }
        }
    })
    $changes = @($changes | Where-Object { $caller.ShouldProcess($_.record.task,'Back up registration and remove unattended update flags') })
    if (!$changes.Count) { return }
    $backup = Join-Path $env:LOCALAPPDATA ('WintodoMaintenance/task-backups/'+[datetime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N'))
    $null = New-Item -ItemType Directory -Path $backup -Force
    # Export and validate every original action before changing either one.
    foreach ($c in $changes) {
        $original = Export-ScheduledTask -TaskPath '\' -TaskName $c.record.task
        $c.xml = [xml]$original
        $arg = $c.xml.SelectSingleNode("/*[local-name()='Task']/*[local-name()='Actions']/*[local-name()='Exec']/*[local-name()='Arguments']")
        if (!$arg -or $arg.InnerText -cne $c.old) { throw "Action changed during preview. Nothing changed. Backups: $backup" }
        # Task Scheduler exports UTF-16 XML.
        [IO.File]::WriteAllText((Join-Path $backup ($c.record.task+'.xml')), $original, [Text.Encoding]::Unicode)
        $arg.InnerText = $c.new
    }
    Write-Verbose "Saved original task XML: $backup"
    foreach ($c in $changes) {
        $taskName = $c.record.task
        try {
            $argsForAction = @{Execute=$c.exec.Execute; Argument=$c.new}
            if ($c.exec.WorkingDirectory) { $argsForAction.WorkingDirectory = $c.exec.WorkingDirectory }
            if ($c.exec.Id) { $argsForAction.Id = $c.exec.Id }
            $updated = New-ScheduledTaskAction @argsForAction
            Set-ScheduledTask -TaskName $taskName -TaskPath '\' -Action $updated | Out-Null
            $nowTask = Get-ScheduledTask -TaskName $taskName -TaskPath '\'
            if ($c.record.state -eq 'Disabled' -and "$($nowTask.State)" -ne 'Disabled') {
                Disable-ScheduledTask -TaskName $taskName -TaskPath '\' | Out-Null
                $nowTask = Get-ScheduledTask -TaskName $taskName -TaskPath '\'
            }
            $afterXml = [xml](Export-ScheduledTask -TaskPath '\' -TaskName $taskName)
            foreach ($section in 'Actions','Settings','Principals','Triggers') {
                $xpath = "/*[local-name()='Task']/*[local-name()='$section']"
                if ($afterXml.SelectSingleNode($xpath).OuterXml -cne $c.xml.SelectSingleNode($xpath).OuterXml) { throw "Readback differs in $section." }
            }
            if ((Read-Mode $c.record.job $nowTask) -ne 'Check only' -or ("$($nowTask.State)" -eq 'Disabled') -ne ($c.record.state -eq 'Disabled')) { throw 'Mode or enabled state differs on readback.' }
        } catch { throw "Could not verify $taskName. Inspect the current registrations and original XML in $backup. $($_.Exception.Message)" }
    }
    if (!$Json) { Write-Information "Check-only changes verified. Previous task XML: $backup" -InformationAction Continue }
}

$keys = if ($Job -eq 'Both') { @($locations.Keys) } else { @($Job) }
do {
    $records = @($keys | ForEach-Object { Get-Record $_ })
    if ($Action -eq 'CheckOnly') {
        Set-ChecksOnly $records $PSCmdlet
        $records = @($keys | ForEach-Object { Get-Record $_ })
    } elseif ($Action -ne 'Status') {
        # Preflight the entire selection before enabling the first task.
        foreach ($r in $records) {
            if ($r.error) { throw "Cannot control $($r.task): $($r.error)" }
            if ($Action -eq 'Resume' -and $r.state -eq 'Disabled') {
                if ($r.mode -eq 'Unknown') { throw "Unrecognized action: inspect $($r.task) in Task Scheduler before enabling it." }
                if (!$r.limitedInteractive) { throw "$($r.task) does not have the expected limited interactive principal; inspect it in Task Scheduler." }
                if ($r.mode -ne 'Check only' -and !$AllowAutomaticUpdates) { throw "$($r.task) permits updates. To resume this policy explicitly, add -AllowAutomaticUpdates." }
            }
        }
        foreach ($r in $records) {
            if ($Action -eq 'Pause' -and $r.state -ne 'Disabled' -and $PSCmdlet.ShouldProcess($r.task,'Disable future triggers (a running job continues)')) {
                Disable-ScheduledTask -TaskPath '\' -TaskName $r.task | Out-Null
            }
            if ($Action -eq 'Resume' -and $r.state -eq 'Disabled' -and $PSCmdlet.ShouldProcess($r.task,"Enable future triggers: $($r.mode)")) {
                Enable-ScheduledTask -TaskPath '\' -TaskName $r.task | Out-Null
            }
        }
        $records = @($keys | ForEach-Object { Get-Record $_ })
    }
    if ($Json) { ConvertTo-Json -InputObject $records -Depth 4; break }
    "Local maintenance  |  $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    'Windows records task starts and exit codes. Latest reports can also come from manual checks.'
    'Wintodo report: 0 completed, 1 review needed, 2 incomplete. A Windows code alone is not a health check.'
    ''
    $records | ForEach-Object { Write-Record $_ }
    if (!$Menu) { break }
    $Action = 'Status'
    $AllowAutomaticUpdates = $false
    switch ((Read-Host '[S] Refresh  [1]/[2] Open report  [H] History  [C] Checks only  [P] Pause both  [R] Resume both  [Enter] Close').Trim().ToUpperInvariant()) {
        '' { return }
        '1' { if (Test-Path -LiteralPath $records[0].report.reportPath) { Invoke-Item -LiteralPath $records[0].report.reportPath } }
        '2' { if (Test-Path -LiteralPath $records[1].report.reportPath) { Invoke-Item -LiteralPath $records[1].report.reportPath } }
        'H' {
            $choice = Read-Host 'Open report folder: [1] Wintodo, [2] Developer tools, [Enter] Cancel'
            if ($choice -in @('1','2')) {
                $folder = $records[[int]$choice-1].report.folder
                if (Test-Path -LiteralPath $folder -PathType Container) { Invoke-Item -LiteralPath $folder }
            }
        }
        'P' { if ((Read-Host 'Type PAUSE to disable future triggers for both tasks').Trim() -ceq 'PAUSE') { $Action = 'Pause' } }
        'C' { if ((Read-Host 'Type CHECK ONLY to remove unattended updates from both schedules; preserve their triggers and enabled state').Trim() -ceq 'CHECK ONLY') { $Action = 'CheckOnly' } }
        'R' {
            if (@($records | Where-Object { $_.state -eq 'Disabled' -and $_.mode -ne 'Check only' }).Count) {
                if ((Read-Host 'One or more tasks may install updates. Type RESUME UPDATES to enable them').Trim() -ceq 'RESUME UPDATES') { $AllowAutomaticUpdates = $true; $Action = 'Resume' }
            } elseif ((Read-Host 'Type RESUME to enable future triggers for both tasks').Trim() -ceq 'RESUME') { $Action = 'Resume' }
        }
    }
} while ($Menu)
