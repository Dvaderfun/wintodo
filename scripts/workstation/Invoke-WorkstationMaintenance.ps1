# Requires PowerShell 7 on Windows. Check by default; -Apply services existing installations.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RepoPath,
    [string]$ReportDirectory = (Join-Path $env:LOCALAPPDATA 'WintodoMaintenance/reports'),
    [switch]$Apply, [switch]$IfDue, [switch]$Notify,
    [ValidateSet('Manual','Scheduled')][string]$RunSource
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$central = Join-Path $env:USERPROFILE '.codex/maintenance/DeveloperEnvironment.ps1'
if (Test-Path -LiteralPath $central -PathType Leaf) { . $central }
$profileFile = Join-Path $PSScriptRoot 'local-profile.json'
if (Test-Path -LiteralPath $profileFile) {
    $selection = Get-Content -Raw -LiteralPath $profileFile | ConvertFrom-Json
    if ($selection.codexProfile) { [Environment]::SetEnvironmentVariable('CODEX_HOME',[string]$selection.codexProfile,'Process') }
}
$null = New-Item -ItemType Directory -Force -Path $ReportDirectory
$ReportDirectory = (Get-Item -LiteralPath $ReportDirectory).FullName
$RepoPath = [IO.Path]::TrimEndingDirectorySeparator((Resolve-Path -LiteralPath $RepoPath).ProviderPath)
$latest = Join-Path $ReportDirectory 'latest.json'
$runnerId = Split-Path $PSScriptRoot -Leaf
function Get-ReusableMaintenanceReport($Path, $Repo, $Runner, [bool]$WantApply, [DateTimeOffset]$Now) {
    try {
        $prior = [IO.File]::ReadAllText($Path) | ConvertFrom-Json -ErrorAction Stop
        if ($prior.completed -isnot [bool] -or !$prior.completed -or !$prior.components -or
            $prior.applied -isnot [bool] -or ($WantApply -and !$prior.applied) -or
            $prior.repo -ne $Repo -or $prior.runnerId -cne $Runner -or
            ($prior.exitCode -isnot [long] -and $prior.exitCode -isnot [int]) -or $prior.exitCode -notin @(0,1,2)) { return }
        $finished = [DateTimeOffset]$prior.checkedAt
        $retryDays = if ($prior.exitCode -eq 2) { 1 } else { 6.5 }
        if ($finished -le $Now -and $finished -gt $Now.AddDays(-$retryDays)) { return $prior }
    } catch { } # Missing, partial and obsolete reports do not suppress a run.
}
function Open-MaintenanceReportLock($Directory) {
    # FileShare.None covers manual and scheduled sessions; Windows releases it if the process dies.
    [IO.File]::Open((Join-Path $Directory '.maintenance.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
}
function Get-MaintenanceNotification([bool]$Enabled, [int]$Code, [scriptblock]$Request) {
    if (!$Enabled) { return @{state='not requested'; detail='Notifications were not enabled for this run.'} }
    if (!$Code) { return @{state='not needed'; detail='No review or incomplete checks to report.'} }
    try {
        & $Request
        return @{state='requested'; detail='Windows accepted the request; display and receipt are not confirmed.'}
    } catch { return @{state='failed'; detail=$_.Exception.Message} }
}
function Write-MaintenanceReport($Directory, $Report) {
    $id = ([DateTimeOffset]$Report.checkedAt).ToUniversalTime().ToString('yyyyMMddTHHmmssfffZ')+'-'+[guid]::NewGuid().ToString('N')
    $Report.history = "history/$id/summary.md"
    $mode = if ($Report.applied) {'Update eligible tools'} else {'Check only'}
    $summary = @('# Wintodo weekly maintenance','',("Checked (UTC): "+$Report.checkedAt),
        ("Mode: "+$mode),("Invocation: "+$Report.runSource),("Result code: "+$Report.exitCode),
        ("Notification: "+$Report.notification.state+" — "+$Report.notification.detail),'') +
        @($Report.components | ForEach-Object { '- **'+$_.name+'**: '+$_.state+' — '+$_.detail })
    $historyDir = Join-Path $Directory 'history'
    $stage = Join-Path $historyDir ('.writing-'+$id)
    $null = [IO.Directory]::CreateDirectory($stage)
    $json = $Report | ConvertTo-Json -Depth 5
    $md = ($summary + @('','Component details that name log files refer to the live reports folder; those files can change on the next attempt.')) -join [Environment]::NewLine
    # Publish both dated summaries together. If this fails the previous latest summary remains.
    [IO.File]::WriteAllText((Join-Path $stage 'summary.json'), $json)
    [IO.File]::WriteAllText((Join-Path $stage 'summary.md'), $md)
    [IO.Directory]::Move($stage, (Join-Path $historyDir $id))
    $currentMd = $md + [Environment]::NewLine + [Environment]::NewLine +
        "[Dated summary]($($Report.history)) · Latest attempt: [project details](project.md) · [plugin inventory](plugins.log)."
    foreach ($entry in @(@('latest.md',$currentMd),@('latest.json',$json))) {
        $target = Join-Path $Directory $entry[0]
        [IO.File]::WriteAllText("$target.new", $entry[1])
        [IO.File]::Move("$target.new", $target, $true)
    }
}

try { $reportLock = Open-MaintenanceReportLock $ReportDirectory }
catch { Write-Warning "Cannot acquire the maintenance report lock; no checks started: $($_.Exception.Message)"; exit 2 }
try {
if ($IfDue) {
    $prior = Get-ReusableMaintenanceReport $latest $RepoPath $runnerId ([bool]$Apply) ([DateTimeOffset]::UtcNow)
    if ($prior) { Write-Output "Not due; reusing the completed report: $latest"; exit $prior.exitCode }
}

$rows = [Collections.Generic.List[object]]::new()
function Add-Result($name, $state, $detail) { $rows.Add([ordered]@{name=$name; state=$state; detail=$detail}) }
function Get-ManualSkillState([string]$Directory, [string[]]$Entries, [bool]$RecordApplied) {
    $sorted = [string[]]@($Entries | Sort-Object -Unique)
    [IO.File]::WriteAllLines((Join-Path $Directory 'manual-skills.txt'), $sorted)
    $marker = Join-Path $Directory 'manual-skills.applied.sha256'
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes(($sorted -join "`n"))))
    $changed = if (Test-Path -LiteralPath $marker -PathType Leaf) {
        [IO.File]::ReadAllText($marker).Trim() -cne $hash
    } else { $sorted.Count -gt 0 }
    if ($RecordApplied) {
        [IO.File]::WriteAllText("$marker.new", $hash)
        [IO.File]::Move("$marker.new", $marker, $true)
    }
    if ($changed) { return 'review' }
    if ($sorted.Count) { return 'manual' }
    return 'checked'
}
function Invoke-Component($name, [scriptblock]$action) {
    try { & $action } catch { Add-Result $name 'incomplete' $_.Exception.Message }
}
function Version-Number([string]$value) {
    if ($value -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') { throw 'Unexpected stable version' }
    $v = [version]$value
    [version]::new($v.Major,$v.Minor,$v.Build,[Math]::Max(0,$v.Revision))
}
function Stable-Tag([string]$repo) {
    $res = Invoke-WebRequest -Uri "https://github.com/$repo/releases/latest" -Method Head -TimeoutSec 30
    $uri = $res.BaseResponse.RequestMessage.RequestUri
    if ($uri.Host -ne 'github.com' -or $uri.AbsolutePath -notmatch ('^/'+[regex]::Escape($repo)+'/releases/tag/v(\d+\.\d+\.\d+)$')) {
        throw 'Stable release could not be verified'
    }
    $Matches[1]
}
function Checked-Command([scriptblock]$action) {
    $value = & $action
    if ($LASTEXITCODE -ne 0) { throw "Command exited with $LASTEXITCODE" }
    $value
}
function Compare-Install($name, $wanted, [scriptblock]$read, [scriptblock]$install) {
    $before = & $read
    if ((Version-Number $before) -ge (Version-Number $wanted)) { Add-Result $name 'current' "$before (stable $wanted)"; return }
    if (!$Apply) { Add-Result $name 'review' "$before -> $wanted"; return }
    & $install
    $after = & $read
    if ((Version-Number $after) -lt (Version-Number $wanted)) { throw "Updater returned without providing $wanted; observed $after" }
    Add-Result $name 'updated' "$before -> $after"
}

Invoke-Component 'Node and npm' {
    $nodeResult = & (Join-Path $PSScriptRoot 'Update-AgentNode.ps1') -Apply:$Apply
    if ($nodeResult.state -notin @('current','review','updated')) { throw 'Invalid Node maintenance result' }
    Add-Result 'Node and npm' $nodeResult.state $nodeResult.detail
}
Invoke-Component 'Codex CLI' {
    $wanted = (Invoke-RestMethod 'https://registry.npmjs.org/@openai%2fcodex/latest' -TimeoutSec 30).version
    $read = {
        $line = (Checked-Command { codex --version } | Out-String).Trim()
        if ($line -notmatch '^codex-cli (\d+\.\d+\.\d+)$') { throw 'Codex is not a stable CLI build' }; $Matches[1]
    }
    Compare-Install 'Codex CLI' $wanted $read {
        $pkg = Checked-Command { npm ls --global --depth=0 --json '@openai/codex' } | Out-String | ConvertFrom-Json
        if (!$pkg.dependencies.'@openai/codex'.version) { throw 'Active Codex is not npm-managed; use its installation manager' }
        $cmdRoot = Split-Path (Get-Command codex).Source
        $npmRoot = (Checked-Command { npm prefix --global } | Out-String).Trim()
        if ([IO.Path]::GetFullPath($cmdRoot) -ne [IO.Path]::GetFullPath($npmRoot)) { throw 'Active CLI and npm prefix differ; installation left unchanged' }
        $null = Checked-Command { npm install --global "@openai/codex@$wanted" --registry=https://registry.npmjs.org --no-audit --no-fund }
    }
}
Invoke-Component 'CodeGraph' {
    $cgArgs = @((Join-Path $PSScriptRoot 'Update-CodeGraph.py'))
    if ($Apply) { $cgArgs += '--apply' }
    $msg = (& python @cgArgs 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -notin @(0,1) -or ($LASTEXITCODE -eq 1 -and ($Apply -or $msg -notmatch '^CodeGraph [0-9.]+ -> [0-9.]+$'))) { throw 'CodeGraph installation or release verification failed' }
    $state = if ($LASTEXITCODE -ne 0) { 'review' } elseif ($msg -match ' -> ') { 'updated' } else { 'current' }
    Add-Result 'CodeGraph' $state $msg
}
Invoke-Component 'WinApp CLI' {
    Compare-Install 'WinApp CLI' (Stable-Tag 'microsoft/WinAppCli') { (Checked-Command { winapp --version } | Out-String).Trim() } {
        if (!(Get-AppxPackage -Name winapp | Where-Object PublisherId -eq '8wekyb3d8bbwe')) { throw 'No existing Microsoft MSIX; use its installation manager' }
        $null = Checked-Command { winget install --id Microsoft.WinAppCli --exact --version $wanted --source winget --silent --disable-interactivity --accept-source-agreements --accept-package-agreements }
    }
}
Invoke-Component 'WinUI Gallery' {
    Compare-Install 'WinUI Gallery' (Stable-Tag 'microsoft/WinUI-Gallery') {
        $p = Get-AppxPackage -Name Microsoft.WinUI3ControlsGallery | Where-Object PublisherId -eq '8wekyb3d8bbwe' | Select-Object -First 1
        if (!$p) { throw 'Gallery is not installed for this user' }; [string]$p.Version
    } {
        $null = Checked-Command { winget upgrade --id 9P3JFPWWDZRC --exact --source msstore --silent --disable-interactivity --accept-source-agreements --accept-package-agreements }
    }
}
Invoke-Component '.NET 10 LTS SDK' {
    $info = Invoke-RestMethod 'https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json' -TimeoutSec 30
    $wanted = $info.'latest-sdk'
    if ($wanted -notmatch '^10\.0\.\d+$') { throw 'Unexpected LTS version' }
    $pin = (Get-Content -LiteralPath (Join-Path $RepoPath 'global.json') -Raw | ConvertFrom-Json).sdk.version
    $read = {
        $sdkRoot = Join-Path (Get-Item -LiteralPath (Get-Command dotnet).Source).Directory.FullName 'sdk'
        $sdks = @(Checked-Command { dotnet --list-sdks } | ForEach-Object {
            if ($_ -match '^(10\.0\.\d+) \[(.+)\]$' -and [IO.Path]::GetFullPath($Matches[2]) -eq [IO.Path]::GetFullPath($sdkRoot)) { $Matches[1] }
        })
        if (!$sdks) { throw 'The current .NET installation has no .NET 10 SDK' }
        @($sdks | Sort-Object { [version]$_ } -Descending)[0]
    }
    Compare-Install '.NET 10 LTS SDK' $wanted $read {
        $targetDir = (Get-Item -LiteralPath (Get-Command dotnet).Source).Directory.FullName
        if (!$targetDir.StartsWith([IO.Path]::GetFullPath($env:USERPROFILE)+'\',[StringComparison]::OrdinalIgnoreCase)) {
            throw 'System-managed SDK needs the system administrator or its package manager'
        }
        $installer = Join-Path $ReportDirectory ('dotnet-install-'+[guid]::NewGuid().ToString('N')+'.ps1')
        try {
            Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer -TimeoutSec 90
            $sig = Get-AuthenticodeSignature -LiteralPath $installer
            if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Subject -notmatch '(?:^|, )O=Microsoft Corporation(?:,|$)') {
                throw 'Microsoft installer signature could not be verified; SDK was not changed'
            }
            $null = Checked-Command { pwsh -NoProfile -NonInteractive -File $installer -Version $wanted -InstallDir $targetDir -SkipNonVersionedFiles -NoPath }
        } finally { if (Test-Path -LiteralPath $installer) { Remove-Item -LiteralPath $installer } }
    }
    if (!(@(Checked-Command { dotnet --list-sdks }) -match ('^'+[regex]::Escape($pin)+' \['))) { Add-Result 'Exact project SDK' 'review' "$pin is not installed" }
}
Invoke-Component 'Installed skill sources' {
    $publisher = Join-Path $env:USERPROFILE '.codex/skills/skill-discovery/scripts/Update-Skills.ps1'
    if (!(Test-Path -LiteralPath $publisher -PathType Leaf)) { Add-Result 'Installed skill sources' 'review' 'The registered publisher manager is missing; restore it or review local skill ownership. Project skills are checked separately.'; return }
    $log = Join-Path $ReportDirectory 'skill-publishers.log'
    & pwsh -NoProfile -NonInteractive -File $publisher *> $log
    $code = $LASTEXITCODE
    if ($code -notin @(0,2)) { throw 'Publisher inventory failed; see skill-publishers.log' }
    if ($code -eq 2 -and $Apply) {
        & pwsh -NoProfile -NonInteractive -File $publisher -Apply *> $log
        if ($LASTEXITCODE -ne 0) { throw 'Publisher update needs review; see skill-publishers.log' }
        Add-Result 'Installed skill sources' 'updated' 'Applied registered publisher revisions; local edits protected.'
    } else {
        Add-Result 'Installed skill sources' $(if ($code -eq 2) {'review'} else {'current'}) 'Registered publishers checked. Private skills without a publisher are not overwritten.'
    }
}
Invoke-Component 'Other local skills' {
    $statePath = Join-Path $env:USERPROFILE '.agents/skill-sources/upstream-state.json'
    $managed = if (Test-Path -LiteralPath $statePath) { (Get-Content -Raw -LiteralPath $statePath | ConvertFrom-Json -AsHashtable).files } else { @{} }
    $manual = @(foreach ($relative in @('.agents/skills','.codex/skills')) {
        $dir = Join-Path $env:USERPROFILE $relative
        if (Test-Path -LiteralPath $dir) {
            foreach ($item in Get-ChildItem -LiteralPath $dir -Directory) {
                if ($item.Name -match '^(?:\.|codex-primary-runtime$|caveman|cavecrew|cavemem|ultracave|megacave)') { continue }
                if (!(Test-Path -LiteralPath (Join-Path $item.FullName 'SKILL.md') -PathType Leaf)) { continue }
                if ($relative -eq '.agents/skills' -and $managed.Contains(($item.Name+'/SKILL.md'))) { continue }
                "$relative/$($item.Name)"
            }
        }
    })
    $state = Get-ManualSkillState $ReportDirectory $manual ([bool]$Apply)
    $detail = "$($manual.Count) local skill locations without an owning updater; see manual-skills.txt. Their upstream versions are not asserted."
    if ($state -eq 'review') { $detail = "Manual skill inventory is new or changed. " + $detail }
    Add-Result 'Other local skills' $state $detail
}
Invoke-Component 'Codex plugins' {
    $pArgs = @('-NoProfile','-NonInteractive','-File',(Join-Path $PSScriptRoot 'Update-CodexPlugins.ps1'),'-Json')
    if ($Apply) { $pArgs += '-Apply' }
    & pwsh @pArgs *> (Join-Path $ReportDirectory 'plugins.log')
    $code=$LASTEXITCODE
    if ($code -notin @(0,1)) { throw 'Plugin inventory/refresh incomplete; see plugins.log' }
    Add-Result 'Codex plugins' $(if ($code) {'review'} else {'checked'}) 'See plugins.log for publisher-managed, enabled and disabled components.'
}
Invoke-Component 'Claude Code' {
    $adapter = @((Join-Path $PSScriptRoot 'Update-ClaudeTools.py'), '--repo', $RepoPath)
    if ($Apply) { $adapter += '--apply' }
    $result = (& python @adapter | Out-String)
    $code = $LASTEXITCODE
    $verified = $result | ConvertFrom-Json
    if ($code -notin @(0,1,2) -or $verified.exitCode -ne $code -or !$verified.components) { throw 'Invalid Claude maintenance result' }
    $result | Set-Content -LiteralPath (Join-Path $ReportDirectory 'claude.json') -Encoding utf8
    foreach ($row in $verified.components) { Add-Result $row.name $row.state $row.detail }
}
Invoke-Component 'Repository pins' {
    $checker = Join-Path $PSScriptRoot 'Check-ProjectHealth.py'
    if (!(Test-Path -LiteralPath $checker)) { $checker = Join-Path (Split-Path $PSScriptRoot -Parent) 'Check-ProjectHealth.py' }
    & python $checker --repo $RepoPath --json (Join-Path $ReportDirectory 'project.json') --markdown (Join-Path $ReportDirectory 'project.md') *> (Join-Path $ReportDirectory 'project.log')
    $code=$LASTEXITCODE
    if ($code -notin @(0,1)) { throw 'At least one project feed or pin could not be verified; see project.md / project.log' }
    Add-Result 'Repository pins' $(if ($code) {'review'} else {'current'}) 'See project.md; reviewed dependency updates never write to this checkout.'
}
$states=@($rows | ForEach-Object { $_.state })
$statusCode=if ($states -contains 'incomplete') {2} elseif ($states -contains 'review') {1} else {0}
$notice = Get-MaintenanceNotification ([bool]$Notify) $statusCode {
    $notifyFile=Join-Path $PSScriptRoot 'Show-MaintenanceNotice.ps1'
    & "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -WindowStyle Hidden -File $notifyFile *> $null
    if ($LASTEXITCODE -ne 0) { throw "Windows notification helper exited with code $LASTEXITCODE; open the report directly." }
}
if ($notice.state -eq 'failed') { $statusCode = 2 }
$sourceLabel = if ($RunSource) {$RunSource} elseif ($IfDue) {'Unspecified'} else {'Manual'}
$report=[ordered]@{checkedAt=[DateTimeOffset]::UtcNow.ToString('o'); completed=$true; runnerId=$runnerId; applied=[bool]$Apply;
    runSource=$sourceLabel; repo=$RepoPath; exitCode=$statusCode; notification=$notice; components=$rows}
Write-MaintenanceReport $ReportDirectory $report
Write-Output (Join-Path $ReportDirectory 'latest.md')
exit $statusCode
} finally { $reportLock.Dispose() }
