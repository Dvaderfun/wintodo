param([switch]$AppOnly, [string]$Architecture)
$ErrorActionPreference = 'Stop'

function Get-CiDependencyAuditBinding {
    param([Parameter(Mandatory)][string]$RepositoryRoot, [Parameter(Mandatory)][string]$Architecture)

    try {
        if ($Architecture -cnotin @('x64', 'arm64')) { throw 'Unsupported target.' }
        $project = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot 'src/Wintodo.App/Wintodo.csproj'))
        $assetsPath = Join-Path $RepositoryRoot 'src/Wintodo.App/obj/project.assets.json'
        $reader = [IO.BinaryReader]::new([IO.File]::OpenRead($assetsPath))
        try { $bytes = $reader.ReadBytes(16 * 1024 * 1024 + 1) } finally { $reader.Dispose() }
        if ($bytes.Length -gt 16 * 1024 * 1024) { throw 'Oversized assets.' }
        $assets = [Text.UTF8Encoding]::new($false, $true).GetString($bytes).TrimStart([char]0xFEFF) |
            ConvertFrom-Json -AsHashtable
        if ($assets -isnot [System.Collections.IDictionary] -or
            $assets.targets -isnot [System.Collections.IDictionary] -or
            $assets.project.frameworks -isnot [System.Collections.IDictionary] -or
            $assets.project.frameworks.Count -ne 1 -or
            $assets.project.restore.projectPath -isnot [string] -or
            -not [IO.Path]::GetFullPath($assets.project.restore.projectPath).Equals($project, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Unexpected app assets.'
        }
        $framework = @($assets.project.frameworks.Keys)[0]
        $targets = @($assets.targets.Keys)
        $originalFrameworks = @($assets.project.restore.originalTargetFrameworks)
        if ($framework -notmatch '^net[0-9]+\.[0-9]+-windows[0-9]+(?:\.[0-9]+){3}$' -or
            $targets.Count -ne 2 -or $framework -cnotin $targets -or "$framework/win-$Architecture" -cnotin $targets -or
            $originalFrameworks.Count -ne 1 -or $originalFrameworks[0] -cne $framework) {
            throw 'Target-specific app graph is missing or inconsistent.'
        }
        $commit = (& git -C $RepositoryRoot rev-parse HEAD 2>$null) -join ''
        if ($LASTEXITCODE -ne 0 -or $commit -cnotmatch '^[a-f0-9]{40}$' -or $commit -eq ('0' * 40)) {
            throw 'Source commit unavailable.'
        }
        return [ordered]@{
            source_commit = $commit
            architecture = $Architecture
            assets_sha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
            project_set = 'app'
            scope = 'app_assets_observed_before_and_after_audit'
        }
    } catch {
        throw 'WINTODO_DEPENDENCY_BINDING_INVALID'
    }
}

function Assert-CiDependencyAuditBindingUnchanged {
    param([Parameter(Mandatory)]$Before, [Parameter(Mandatory)]$After)

    foreach ($name in @('source_commit', 'architecture', 'assets_sha256', 'project_set', 'scope')) {
        if ($Before[$name] -isnot [string] -or $Before[$name] -cne $After[$name]) {
            throw 'WINTODO_DEPENDENCY_BINDING_CHANGED'
        }
    }
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$privateRoot = Join-Path $repositoryRoot ('artifacts/ci-security/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $privateRoot | Out-Null
$project = if ($AppOnly) { 'src/Wintodo.App/Wintodo.csproj' } else { 'Wintodo.sln' }
$projectSet = if ($AppOnly) { 'app' } else { 'solution' }
$report = Join-Path $privateRoot "dependencies-$projectSet.json"
try {
    $summary = Join-Path $repositoryRoot 'SafeTestResults/dependencies.json'
    if (Test-Path -LiteralPath $summary) { Remove-Item -LiteralPath $summary }
    if ($Architecture -and -not $AppOnly) { throw 'Binding requires app-only scope.' }
    $binding = if ($Architecture) {
        Get-CiDependencyAuditBinding -RepositoryRoot $repositoryRoot -Architecture $Architecture
    } else { $null }
    dotnet package list --project (Join-Path $repositoryRoot $project) --vulnerable --include-transitive `
        --format json --output-version 1 --no-restore 1> $report 2> (Join-Path $privateRoot 'dependencies.log')
    $auditExit = $LASTEXITCODE
    if ($binding) {
        $after = Get-CiDependencyAuditBinding -RepositoryRoot $repositoryRoot -Architecture $Architecture
        Assert-CiDependencyAuditBindingUnchanged -Before $binding -After $after
    }
    # Keep intermediate PASS private until the audit's graph/source checks finish.
    $privateSummary = Join-Path $privateRoot 'summary.json'
    python (Join-Path $PSScriptRoot 'Test-CiSecurity.py') dependencies $report --exit-code $auditExit `
        --project-set $projectSet --output $privateSummary `
        1> (Join-Path $privateRoot 'summary.log') 2> (Join-Path $privateRoot 'summary-errors.log')
    if ($LASTEXITCODE -ne 0) { throw 'Dependency audit did not pass.' }
    if ($binding) {
        $after = Get-CiDependencyAuditBinding -RepositoryRoot $repositoryRoot -Architecture $Architecture
        Assert-CiDependencyAuditBindingUnchanged -Before $binding -After $after
        $result = Get-Content -LiteralPath $privateSummary -Raw | ConvertFrom-Json -AsHashtable
        $result.binding = $binding
        $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $privateSummary -Encoding utf8
    }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $summary) | Out-Null
    Move-Item -LiteralPath $privateSummary -Destination $summary
    Write-Host 'CI dependency audit passed; evidence: SafeTestResults/dependencies.json'
} catch {
    Write-Host 'CI dependency audit failed; review private report.'
    exit 1
}
