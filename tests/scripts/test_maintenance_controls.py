"""Read status and exercise controls against fake scheduler commands and temporary reports only."""

import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[2] / "scripts/workstation/Maintenance.ps1"

HARNESS = r'''
param($ScriptPath,$Scratch)
$ErrorActionPreference='Stop'
$env:USERPROFILE=$env:LOCALAPPDATA=$Scratch
$work='Wintodo-Weekly-Maintenance'
$tools='Codex-DeveloperTools-WeeklyCheck'
$states=@{$work='Ready';$tools='Ready'}
$wfile=Join-Path $Scratch 'WintodoMaintenance\runners\1111222233334444\Invoke-WorkstationMaintenance.ps1'
$sfile=Join-Path $Scratch '.codex\maintenance\Check-DeveloperUpdates.ps1'
$lines=@{$work=('-NoProfile -File "'+$wfile+'" -RepoPath "C:\folder -Apply\repo" -IfDue -Notify -RunSource Scheduled');$tools=('-File "'+$sfile+'" -ApplyScm')}
$stateWrites=[Collections.Generic.List[string]]::new()
function Get-ScheduledTask($TaskPath,$TaskName){
    if($TaskPath -ne '\' -or !$states.ContainsKey($TaskName)){throw 'Unknown test job'}
    [pscustomobject]@{State=$states[$TaskName];Actions=@([pscustomobject]@{Execute=(Get-Command pwsh -CommandType Application | Select-Object -First 1).Source;Arguments=$lines[$TaskName];WorkingDirectory=$(if($TaskName -eq $work){Split-Path $wfile})});Triggers=@();Principal=@{UserId='fixture';LogonType='Interactive';RunLevel='Limited'}}
}
function Start-ScheduledTask { throw 'The controls must NEVER start the registered automatic action' }
function Get-ScheduledTaskInfo($TaskPath,$TaskName){[pscustomobject]@{LastRunTime=[datetime]'2026-01-01';NextRunTime=$null;LastTaskResult=1;NumberOfMissedRuns=2}}
function Disable-ScheduledTask($TaskPath,$TaskName){$stateWrites.Add("disable:$TaskPath$TaskName");$states[$TaskName]='Disabled'}
function Enable-ScheduledTask($TaskPath,$TaskName){$stateWrites.Add("enable:$TaskPath$TaskName");$states[$TaskName]='Ready'}
function Export-ScheduledTask($TaskPath,$TaskName){
    $escaped=[Security.SecurityElement]::Escape($lines[$TaskName])
    "<Task><Triggers><Weekly/></Triggers><Principals><Limited/></Principals><Settings><Enabled>$($states[$TaskName] -ne 'Disabled')</Enabled></Settings><Actions><Exec><Arguments>$escaped</Arguments></Exec></Actions></Task>"
}
function New-ScheduledTaskAction($Execute,$Argument,$WorkingDirectory,$Id){[pscustomobject]@{Execute=$Execute;Arguments=$Argument;WorkingDirectory=$WorkingDirectory;Id=$Id}}
function Set-ScheduledTask($TaskPath,$TaskName,$Action){$stateWrites.Add("set:$TaskPath$TaskName");$lines[$TaskName]=$Action.Arguments}
function Must($truth,$msg){if(!$truth){throw $msg}}
function Snapshot { & $ScriptPath -Json | ConvertFrom-Json }
function Refused($arguments,$reason) {
    $denied=$false
    try{ & $ScriptPath @arguments | Out-Null }catch{ $denied=$_.Exception.Message -like "*$reason*" }
    Must $denied "Did not refuse: $reason"
}
$wd=New-Item -ItemType Directory -Force (Join-Path $Scratch 'WintodoMaintenance\reports')
$sd=New-Item -ItemType Directory -Force (Join-Path $Scratch '.codex\maintenance\reports')
$old=@{checkedAt=[DateTimeOffset]::Now.AddDays(-12).ToString('o');runSource='Manual';applied=$false;exitCode=2;components=@(@{state='incomplete';name='fixture feed'})}
$old|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $wd 'latest.json')
'{broken'|Set-Content -LiteralPath (Join-Path $sd 'latest.json')
$r=Snapshot
Must ($r.Count -eq 2 -and $r[0].mode -eq 'Check only' -and $r[1].mode -like 'Automatic Git*') 'Incorrect registered modes'
Must ($r[0].report.freshness -like 'Stale*' -and $r[0].report.source -eq 'Manual' -and $r[0].report.outcome -like 'INCOMPLETE*fixture feed*') 'Stale, failed manual report not recognized'
Must ($r[1].report.outcome -like 'UNREADABLE*' -and $r[1].lastResult -eq 1 -and $r[1].missed -eq 2) 'Malformed report masked scheduler facts'
Must ($stateWrites.Count -eq 0) 'Status changed registrations'
& $ScriptPath Pause -Job DeveloperTools -WhatIf | Out-Null
Must ($stateWrites.Count -eq 0) 'WhatIf changed registrations'
& $ScriptPath Pause | Out-Null
Refused @{Action='Resume'} 'AllowAutomaticUpdates'
Must ($stateWrites.Count -eq 2) 'Preflight partially resumed the pair'
& $ScriptPath Resume -Job Wintodo | Out-Null
& $ScriptPath Resume -Job DeveloperTools -AllowAutomaticUpdates | Out-Null
Must (($stateWrites -join ',') -ceq "disable:\$work,disable:\$tools,enable:\$work,enable:\$tools") 'Unexpected targets'
$workCheck=$lines[$work]
$lines[$work]=$workCheck.Replace(' -IfDue',' -Apply -IfDue').Replace(' -RunSource Scheduled','')
Refused @{Action='CheckOnly'} 'old runner'
Must ($stateWrites.Count -eq 4) 'Legacy guard changed a task'
$lines[$work]+=' -RunSource Scheduled'
$scmAuto=$lines[$tools]
$lines[$tools]+=' -RefreshSkills'
& $ScriptPath CheckOnly -WhatIf | Out-Null
$backupRoot=Join-Path $Scratch 'WintodoMaintenance/task-backups'
Must (!(Test-Path -LiteralPath $backupRoot) -and $stateWrites.Count -eq 4) 'CheckOnly WhatIf wrote files or policies'
$states[$work]='Disabled'
& $ScriptPath CheckOnly | Out-Null
Must ($states[$work] -eq 'Disabled' -and $lines[$work] -ceq $workCheck -and $lines[$tools] -ceq $scmAuto.Replace(' -ApplyScm','')) 'Changed wrong flags, path or enabled state'
$backups=@(Get-ChildItem $backupRoot -File -Recurse)
Must ($backups.Count -eq 2 -and (Get-Content -Raw -LiteralPath ($backups|Where-Object Name -like 'Codex-*').FullName) -match '-ApplyScm -RefreshSkills') 'Missing original XML'
& $ScriptPath CheckOnly | Out-Null
Must ($stateWrites.Count -eq 6) 'Repeated check-only setting should be a no-op'
$lines[$tools]='-Command Get-Date';$states[$tools]='Disabled'
Refused @{Action='Resume';Job='DeveloperTools';AllowAutomaticUpdates=$true} 'Unrecognized action'
Remove-Item -LiteralPath (Join-Path $sd 'latest.json')
$states.Remove($tools)
$r=Snapshot
Must ($r[1].error -and $r[1].report.outcome -eq 'No report') 'Missing job/report masked'

# Real child process with a harmless stand-in for the installed snapshot, in a temporary user profile.
$sources=[ordered]@{'Invoke-WorkstationMaintenance.ps1'=@'
[CmdletBinding()]
param($RepoPath,$RunSource,[switch]$Apply,[switch]$IfDue,[switch]$Notify)
$ErrorActionPreference='Stop'
$PSBoundParameters|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $RepoPath 'observed.json')
$now=[DateTimeOffset]::UtcNow
$id=$now.ToString('yyyyMMddTHHmmssfffZ')+'-'+[guid]::NewGuid().ToString('N')
$dir=[IO.Directory]::CreateDirectory((Join-Path $env:LOCALAPPDATA "WintodoMaintenance/reports/history/$id"))
$code=if($env:WINTODO_FIXTURE_RESULT){[int]$env:WINTODO_FIXTURE_RESULT}else{1}
$r=@{checkedAt=$now.ToString('o');history="history/$id/summary.md";completed=$true;runnerId=(Split-Path $PSScriptRoot -Leaf);applied=[bool]$Apply;runSource=$RunSource;repo=$RepoPath;exitCode=$code;components=@(@{state=@('current','review','incomplete')[$code];name='fixture tool'})}
$r|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $dir.FullName 'summary.json')
'Fake inventory only'|Set-Content -LiteralPath (Join-Path $dir.FullName 'summary.md')
if($env:WINTODO_FIXTURE_PUBLISH -eq 'yes'){
    $latest=$dir.Parent.Parent.FullName
    $r|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $latest 'latest.json')
    "[Dated summary]($($r.history))"|Set-Content -LiteralPath (Join-Path $latest 'latest.md')
}
exit $code
'@}
foreach($n in 'Update-AgentNode.ps1','Update-CodexPlugins.ps1','Update-ClaudeTools.py','Update-CodeGraph.py','Show-MaintenanceNotice.ps1','Check-ProjectHealth.py') { $sources[$n]='# fixture' }
$hashes=foreach($content in $sources.Values){[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($content)))}
$id=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($hashes -join ':')+'|'))).Substring(0,16)
$stage=[IO.Directory]::CreateDirectory((Join-Path $Scratch "WintodoMaintenance/runners/$id")).FullName
foreach($n in $sources.Keys){[IO.File]::WriteAllText((Join-Path $stage $n),$sources[$n])}
[IO.File]::WriteAllText((Join-Path $stage 'local-profile.json'),'{"codexProfile":""}')
$wfile=Join-Path $stage 'Invoke-WorkstationMaintenance.ps1'
$repo=[IO.Directory]::CreateDirectory((Join-Path $Scratch 'repo -Apply [A] & B')).FullName
[IO.File]::WriteAllText((Join-Path $repo 'Wintodo.sln'),'fixture')
$lines[$work]='-NoProfile -File "'+$wfile+'" -RepoPath "'+$repo+'" -Apply -IfDue -Notify -RunSource Scheduled'
& $ScriptPath CheckNow -Job Wintodo -WhatIf|Out-Null
Must ($LASTEXITCODE -eq 0 -and !(Test-Path -LiteralPath (Join-Path $repo 'observed.json'))) 'WhatIf unexpectedly failed or ran the inventory'
$env:WINTODO_FIXTURE_PUBLISH='no'
$env:WINTODO_FIXTURE_RESULT='1'
& $ScriptPath CheckNow -Job Wintodo|Out-Null
Must ($LASTEXITCODE -eq 2) 'Partial publication must return 2 even when component review is 1'
$seen=Get-Content -Raw -LiteralPath (Join-Path $repo 'observed.json')|ConvertFrom-Json -AsHashtable
Must ($seen.Count -eq 2 -and $seen.RunSource -ceq 'Manual' -and $seen.RepoPath -ceq $repo) 'Check now forwarded automatic flags or changed the registered checkout'
$view=(Snapshot)[0].report
Must ($view.outcome -like 'Review needed*fixture tool*' -and $view.aliases -like '*stale*' -and $view.reportPath -like '*summary.md' -and $view.source -eq 'Manual') 'Did not surface new dated evidence over stale aliases'
$datedFolder=Split-Path $view.reportPath
$partial=@{exitCode=2;history=('history/'+$view.receipt+'/summary.md');unavailable=@(@{name='latest.md';detail='access denied'})}
$partial|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $datedFolder 'publication.json')
Must ((Snapshot)[0].report.aliases -like 'Partial publication*latest.md*exit 2*') 'Partial publication hidden'
[IO.File]::WriteAllText((Join-Path $datedFolder 'summary.json'),'{malformed')
Must ((Snapshot)[0].report.outcome -like 'INTEGRITY WARNING*') 'Corrupt newest receipt masked by earlier successful report'
$env:WINTODO_FIXTURE_PUBLISH='yes'
foreach($expected in 1,2,0){
    $env:WINTODO_FIXTURE_RESULT=[string]$expected
    & $ScriptPath CheckNow -Job Wintodo|Out-Null
    Must ($LASTEXITCODE -eq $expected) "Verified result $expected was not propagated"
}
[IO.File]::AppendAllText($wfile,'# local change')
& $ScriptPath CheckNow -Job Wintodo|Out-Null
Must ($LASTEXITCODE -eq 2) 'Unverified snapshot must return 2'
$choices=[Collections.Generic.Queue[string]]::new()
$choices.Enqueue('N');$choices.Enqueue('')
function Read-Host($Prompt){if(!$choices.Count){throw 'Unexpected menu prompt'};$choices.Dequeue()}
& $ScriptPath -Menu|Out-Null
Must ($choices.Count -eq 0) 'Menu closed on an incomplete check instead of offering controls again'
'''


@unittest.skipUnless(os.name == "nt" and shutil.which("pwsh"), "Windows and pwsh are required")
class MaintenanceControlTests(unittest.TestCase):
    def test_actual_cli_against_isolated_tasks_and_reports(self):
        with tempfile.TemporaryDirectory() as tmp:
            harness = Path(tmp) / "verify.ps1"
            harness.write_text(HARNESS, encoding="utf-8")
            subprocess.run(["pwsh", "-NoProfile", "-NonInteractive", "-File", str(harness), str(SCRIPT), tmp], check=True)
