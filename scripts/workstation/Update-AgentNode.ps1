# Service the user-managed Windows x64 Node runtime which owns this workstation's npm Codex CLI.
# Outputs one status object; an incomplete verification throws. Existing versions remain available.
[CmdletBinding()]
param([switch]$Apply)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
function Report([string]$state, [string]$detail) { [pscustomobject]@{state=$state; detail=$detail} }
function Stable([string]$value) {
    if ($value -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a stable numeric version.' }
    $value
}
function ReadVersion($path) {
    $s = (& $path --version | Out-String).Trim()
    if ($LASTEXITCODE) { throw 'Could not verify installed runtime/tool.' }
    Stable ($s -creplace '^v','')
}
function SamePath($left,$right) {
    [string]::Equals([IO.Path]::GetFullPath($left),[IO.Path]::GetFullPath($right),[StringComparison]::OrdinalIgnoreCase)
}
$active = (Get-Command node -CommandType Application | Select-Object -First 1).Source
$old = Split-Path -Parent $active
$node = ReadVersion $active
$npmCmd = (Get-Command npm.cmd -CommandType Application | Select-Object -First 1).Source
$npm = ReadVersion $npmCmd
$base = Join-Path $env:USERPROFILE '.local/tools/node'
$owner = Join-Path $env:USERPROFILE '.codex/maintenance'
$manifest = Join-Path $owner 'tools.json'
# A scheduled process does not necessarily inherit npm's interactive prefix.
# Only select it for this process when the active Codex and Node shims are
# already owned by the exact versioned prefix in the developer manifest.
if ((SamePath $old (Join-Path $base "$node/node-v$node-win-x64")) -and (Test-Path -LiteralPath $manifest) -and
    (Test-Path -LiteralPath (Join-Path $old 'node_modules/@openai/codex/package.json')) -and
    (Get-Command codex -ErrorAction SilentlyContinue) -and (SamePath (Split-Path (Get-Command codex).Source) $old) -and
    @((Get-Content -Raw -LiteralPath $manifest | ConvertFrom-Json).binPaths | Where-Object { SamePath $_ $old }).Count -eq 1 -and
    (!$env:NPM_CONFIG_PREFIX -or [IO.Path]::GetFullPath($env:NPM_CONFIG_PREFIX).StartsWith([IO.Path]::GetFullPath($base)+'\',[StringComparison]::OrdinalIgnoreCase))) {
    $env:NPM_CONFIG_PREFIX = $old
}
$major = ([version]$node).Major
$lines = Invoke-RestMethod 'https://nodejs.org/dist/index.json' -TimeoutSec 40
$line = @($lines | Where-Object { $_.lts -is [string] -and $_.version -match "^v$major\.\d+\.\d+$" } |
    Sort-Object { [version]$_.version.Substring(1) } -Descending | Select-Object -First 1)
if (!$line.Count) { Report 'review' "Node $node is not on a published LTS line; review its installation."; return }
$wanted = Stable ($line[0].version.Substring(1))
$ltsSchedule = Invoke-RestMethod 'https://raw.githubusercontent.com/nodejs/Release/main/schedule.json' -TimeoutSec 25
$entry = $ltsSchedule."v$major"
if (!$entry.end -or [datetime]::ParseExact($entry.end,'yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture).Date -lt [datetime]::UtcNow.Date) {
    Report 'review' "Node $node requires selection of a supported LTS line."; return
}
$wantedNpm = Stable ((Invoke-RestMethod 'https://registry.npmjs.org/npm/latest' -TimeoutSec 25).version)
$desc = "Node $node (LTS $wanted), npm $npm (stable $wantedNpm)"
if ([version]$node -ge [version]$wanted -and [version]$npm -ge [version]$wantedNpm) { Report 'current' $desc; return }
if (!$Apply) { Report 'review' $desc; return }
if (!(SamePath $old (Join-Path $base "$node/node-v$node-win-x64")) -or
    !(SamePath $npmCmd (Join-Path $old 'npm.cmd')) -or !(Test-Path -LiteralPath $manifest) -or
    !(Test-Path -LiteralPath (Join-Path $owner 'Sync-DeveloperEnvironment.ps1'))) {
    Report 'review' "$desc; this installer does not own the active prefix."; return
}
$configText = [IO.File]::ReadAllText($manifest)
$config = $configText | ConvertFrom-Json
$ownEntries = @($config.binPaths | Where-Object { SamePath $_ $old })
if ($ownEntries.Count -ne 1) { Report 'review' "$desc; developer manifest and active prefix differ."; return }
$existingPrefix = (& $npmCmd prefix --global | Out-String).Trim()
if ($LASTEXITCODE -or !(SamePath $existingPrefix $old)) { Report 'review' "$desc; npm has a separate global prefix."; return }
$mods = (& $npmCmd ls --global --depth=0 --json | Out-String | ConvertFrom-Json -AsHashtable).dependencies
if ($LASTEXITCODE -or !$mods.'@openai/codex' -or @($mods.Keys | Where-Object { $_ -notin @('@openai/codex','npm') }).Count) {
    Report 'review' "$desc; the global prefix contains other packages or is not the Codex toolchain."; return
}
$codex = Stable $mods.'@openai/codex'.version
$dest = $old
$originalPrefix = $env:NPM_CONFIG_PREFIX
$originalPath = $env:PATH
$switched = $false
try {
    if ([version]$node -lt [version]$wanted) {
        $releaseDir = Join-Path $base $wanted
        $dest = Join-Path $releaseDir "node-v$wanted-win-x64"
        if (Test-Path -LiteralPath $dest) { Report 'review' "$desc; the target directory already exists outside the active manifest."; return }
        $null = New-Item -ItemType Directory -Force -Path $releaseDir
        $zipName = "node-v$wanted-win-x64.zip"
        $release = "https://nodejs.org/dist/v$wanted"
        $checks = (Invoke-WebRequest "$release/SHASUMS256.txt" -TimeoutSec 40).Content
        if ($checks -is [byte[]]) { $checks = [Text.Encoding]::UTF8.GetString($checks) }
        $linePattern = '(?m)^([0-9a-f]{64})  '+[regex]::Escape($zipName)+'\r?$'
        $matchesFound = [regex]::Matches($checks,$linePattern)
        if ($matchesFound.Count -ne 1) { throw 'Missing unique upstream Node checksum.' }
        $archive = Join-Path $releaseDir $zipName
        Invoke-WebRequest "$release/$zipName" -OutFile $archive -TimeoutSec 180
        if ((Get-FileHash -LiteralPath $archive).Hash -ne $matchesFound[0].Groups[1].Value) { throw 'Node archive checksum mismatch.' }
        Expand-Archive -LiteralPath $archive -DestinationPath $releaseDir
        $exe = Join-Path $dest 'node.exe'
        $signature = Get-AuthenticodeSignature -LiteralPath $exe
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(?:^|, )O=OpenJS Foundation(?:,|$)') {
            throw 'Could not verify OpenJS signature; active prefix unchanged.'
        }
        if ((ReadVersion $exe) -ne $wanted) { throw 'Staged Node version did not match.' }
    }
    $env:NPM_CONFIG_PREFIX = $dest
    $env:PATH = $dest+';'+$originalPath
    $npmTool = Join-Path $dest 'npm.cmd'
    $targetNpm = if ([version]$npm -gt [version]$wantedNpm) {$npm} else {$wantedNpm}
    $npmArgs = @('install','--global','--engine-strict','--no-fund','--no-audit','--registry=https://registry.npmjs.org')
    $null = & $npmTool @npmArgs "npm@$targetNpm"
    if ($LASTEXITCODE -or (ReadVersion $npmTool) -ne $targetNpm) { throw 'npm update or compatibility check failed.' }
    if (!(SamePath $old $dest)) {
        $null = & $npmTool @npmArgs "@openai/codex@$codex"
        if ($LASTEXITCODE) { throw 'Staged Codex restore failed.' }
        $ver = (& (Join-Path $dest 'codex.cmd') --version | Out-String).Trim()
        if ($LASTEXITCODE -or $ver -ne "codex-cli $codex") { throw 'Staged Codex version differed; active prefix unchanged.' }
        $backupRoot = Join-Path $owner ('backups/node-'+[guid]::NewGuid().ToString('N'))
        $null = New-Item -ItemType Directory -Path $backupRoot
        Copy-Item -LiteralPath $manifest -Destination $backupRoot
        if ([IO.File]::ReadAllText($manifest) -cne $configText) { throw 'Developer manifest changed during staging.' }
        $config.binPaths = @($config.binPaths | ForEach-Object { if (SamePath $_ $old) { $dest.Replace('\','/') } else { $_ } })
        $tmp = "$manifest.$([guid]::NewGuid().ToString('N')).tmp"
        [IO.File]::WriteAllText($tmp, ($config | ConvertTo-Json -Depth 40)+[Environment]::NewLine)
        [IO.File]::Move($tmp,$manifest,$true)
        try {
            $null = & (Join-Path $owner 'Sync-DeveloperEnvironment.ps1') -Apply
        } catch {
            Copy-Item -LiteralPath (Join-Path $backupRoot 'tools.json') -Destination $manifest -Force
            $null = & (Join-Path $owner 'Sync-DeveloperEnvironment.ps1') -Apply
            throw
        }
        $switched = $true
    }
    Report 'updated' "Node $node → $(ReadVersion (Join-Path $dest 'node.exe')); npm $npm → $targetNpm. Codex $codex preserved."
} finally {
    if (!$switched) { $env:PATH = $originalPath; $env:NPM_CONFIG_PREFIX = $originalPrefix }
}
