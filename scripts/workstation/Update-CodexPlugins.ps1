#requires -Version 7.2
<#
.SYNOPSIS
Checks the current Codex profile's installed plugins; -Apply refreshes approved
Git marketplaces and advances an already-enabled Floorplan when a newer version
appears in its configured catalog. Does not install additional plugins.
.EXAMPLE
pwsh -NoProfile -File scripts/workstation/Update-CodexPlugins.ps1 -Apply -Json
#>
[CmdletBinding()]
param([switch]$Apply, [switch]$Json, [string]$CodexCommand = 'codex')

$ErrorActionPreference = 'Stop'
$allowed = @{
    'azure-skills' = 'https://github.com/microsoft/azure-skills.git'
    'floorplan' = 'https://github.com/haydenschmitty/floorplan.git'
    'win-dev-skills' = 'https://github.com/microsoft/win-dev-skills.git'
}
$report = [ordered]@{ mode = $(if ($Apply) {'apply'} else {'check'}); marketplaces = @(); plugins = @(); exitCode = 0 }
function Flag([int]$Severity) { $report.exitCode = [Math]::Max($report.exitCode, $Severity) }
function CodexJson([string[]]$CliArgs) {
    $raw = & $codexPath @CliArgs --json 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $raw) { throw 'Codex command failed.' }
    return (($raw -join "`n") | ConvertFrom-Json -AsHashtable)
}
function PathKey([string]$Value) {
    return [IO.Path]::GetFullPath($Value.Replace('\\?\', '')).TrimEnd([char[]]'\/')
}
function GitRoot([string]$Folder, [string]$Origin) {
    $root = & git --no-optional-locks -C $Folder rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -ne 0 -or (PathKey $root) -ne (PathKey $Folder)) { throw 'Unexpected Git root.' }
    $url = & git --no-optional-locks -C $Folder config --local --get remote.origin.url 2>$null
    if ($LASTEXITCODE -ne 0 -or $url -ne $Origin) { throw 'Unexpected Git origin.' }
}
function Snapshot {
    $j = CodexJson @('plugin','list')
    if ($j.installed -isnot [array]) { throw 'No installed inventory.' }
    $byId = @{}
    foreach ($p in $j.installed) {
        if ($p.installed -ne $true -or $p.enabled -isnot [bool] -or
            $p.pluginId -cnotmatch '^[a-zA-Z0-9._-]+@[a-zA-Z0-9._-]+$' -or
            $p.pluginId -cne "$($p.name)@$($p.marketplaceName)" -or $byId.ContainsKey($p.pluginId)) {
            throw 'Invalid installed identity.'
        }
        $byId[$p.pluginId] = $p
    }
    return $byId
}
function SameInventory($Before, $After) {
    if ($Before.Count -ne $After.Count) { throw 'Inventory changed.' }
    foreach ($id in $Before.Keys) {
        if (-not $After.ContainsKey($id) -or $Before[$id].enabled -ne $After[$id].enabled) {
            throw 'Installed or enabled state changed.'
        }
    }
}
function ReadFloorplan([string]$Root) {
    $path = Join-Path $Root '.codex-plugin/plugin.json'
    $m = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json -AsHashtable
    if ($m.name -cne 'floorplan' -or $m.version -cnotmatch '^\d+\.\d+\.\d+$') { throw 'Unexpected Floorplan manifest.' }
    return $m.version
}

# Avoid workspace-scoped marketplace discovery when run by a scheduled job.
Push-Location -LiteralPath ([Environment]::GetFolderPath('UserProfile'))
try {
    $codexPath = (Get-Command $CodexCommand -CommandType Application,ExternalScript -ErrorAction Stop | Select-Object -First 1).Source
    $before = Snapshot
    $sources = CodexJson @('plugin','marketplace','list')
    if ($sources.marketplaces -isnot [array]) { throw 'Missing marketplaces.' }
    $verified = @{}
    $runtime = @($sources.marketplaces | Where-Object name -eq 'openai-primary-runtime')
    foreach ($m in $sources.marketplaces) {
        if ($m.marketplaceSource.sourceType -ne 'git') { continue }
        $name = $m.name
        $state = 'unreviewed-source'
        if ($allowed.ContainsKey($name) -and $m.marketplaceSource.source -eq $allowed[$name]) {
            try {
                GitRoot $m.root $allowed[$name]
                if ($Apply) {
                    $u = CodexJson @('plugin','marketplace','upgrade',$name)
                    if ($u.selectedMarketplaces -isnot [array] -or $u.selectedMarketplaces.Count -ne 1 -or
                        $u.selectedMarketplaces[0] -ne $name -or $u.errors -isnot [array] -or $u.errors.Count -or
                        $u.upgradedRoots -isnot [array] -or $u.upgradedRoots.Count -gt 1 -or
                        ($u.upgradedRoots.Count -eq 1 -and (PathKey $u.upgradedRoots[0]) -ne (PathKey $m.root))) {
                        throw 'Unverified refresh.'
                    }
                    GitRoot $m.root $allowed[$name]
                }
                $verified[$name] = $m
                $state = if (-not $Apply) {'cached-only'} elseif ($u.upgradedRoots.Count) {'refreshed'} else {'checked-no-change'}
            } catch { $state = 'incomplete'; Flag 2 }
        } else { Flag 1 }
        $report.marketplaces += [ordered]@{ name = $name; state = $state }
    }
    $after = Snapshot
    SameInventory $before $after
    foreach ($id in ($before.Keys | Sort-Object)) {
        $p = $after[$id]
        $r = [ordered]@{ plugin = $id; enabled = $p.enabled; installed = $before[$id].version; available = $null; state = 'review' }
        try {
            if (-not $p.enabled) { $r.state = 'disabled-retained' }
            elseif ($p.marketplaceName -eq 'openai-primary-runtime' -and $p.marketplaceSource.sourceType -eq 'local') {
                if ($runtime.Count -ne 1 -or $p.source.source -ne 'local' -or
                    (PathKey $p.marketplaceSource.source) -ne (PathKey $runtime[0].root) -or
                    (PathKey $p.source.path) -ne (PathKey (Join-Path $runtime[0].root 'plugins' $p.name))) {
                    Flag 1
                } else { $r.state = 'runtime-managed' }
            } elseif ($id -ceq 'floorplan@floorplan' -and $verified.ContainsKey('floorplan')) {
                $root = $verified['floorplan'].root
                if ($p.marketplaceSource.sourceType -ne 'git' -or $p.marketplaceSource.source -ne $allowed.floorplan -or
                    $p.source.source -ne 'local' -or (PathKey $p.source.path) -ne (PathKey $root)) { throw 'Unexpected source.' }
                $r.available = ReadFloorplan $root
                if ($r.installed -notmatch '^\d+\.\d+\.\d+$') { throw 'Unknown installed version.' }
                if ([version]$r.installed -eq [version]$r.available) { $r.state = 'matches-catalog' }
                elseif ([version]$r.installed -gt [version]$r.available -or -not $Apply) { Flag 1 }
                else {
                    # The CLI has no plugin-update verb. Re-add only the already installed,
                    # enabled selector, never a catalog entry or a disabled installation.
                    $latest = Snapshot
                    SameInventory $before $latest
                    $added = CodexJson @('plugin','add','floorplan@floorplan')
                    if ($added.pluginId -cne $id -or $added.version -ne $r.available -or
                        (ReadFloorplan $added.installedPath) -ne $r.available) { throw 'Cache update unverified.' }
                    $r.installed = $added.version
                    $r.state = 'updated'
                }
            } else { Flag 1 }
        } catch { $r.state = 'incomplete'; Flag 2 }
        $report.plugins += $r
    }
    $final = Snapshot
    SameInventory $before $final
    foreach ($r in $report.plugins) {
        if ($r.state -in 'matches-catalog','updated' -and $final[$r.plugin].version -ne $r.installed) {
            $r.state = 'incomplete'; Flag 2
        }
    }
} catch {
    $report['error'] = 'Could not verify the selected Codex profile, inventory, or installed/enabled state.'
    Flag 2
} finally { Pop-Location }

if ($Json) { $report | ConvertTo-Json -Depth 5 -Compress }
else {
    $report.marketplaces | ForEach-Object { Write-Output "marketplace $($_.name): $($_.state)" }
    $report.plugins | ForEach-Object { Write-Output "$($_.plugin): installed=$($_.installed) $($_.state) catalog=$($_.available)" }
    if ($report.error) { Write-Output $report.error }
}
exit $report.exitCode
