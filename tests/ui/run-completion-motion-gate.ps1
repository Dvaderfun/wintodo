param([Parameter(Mandatory)][string]$BaselineTimings, [switch]$TestReducedMotion, [string]$ArtifactDirectory)
# Local desktop input requires explicit owner permission for this session.
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1') {
    throw 'Local UI automation is disabled on this main machine. Use an isolated VM/Sandbox, or obtain owner permission for this session before setting WINTODO_ALLOW_LOCAL_UI=1. See docs/guides/testing.md.'
}

$ErrorActionPreference = 'Stop'
$BaselineTimings = (Resolve-Path -LiteralPath $BaselineTimings).Path
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $ArtifactDirectory) {
    $ArtifactDirectory = Join-Path $PSScriptRoot ('artifacts\completion-motion\run-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
}
$ArtifactDirectory = [IO.Path]::GetFullPath($ArtifactDirectory)
New-Item -ItemType Directory -Force -Path $ArtifactDirectory | Out-Null
$db = Join-Path $ArtifactDirectory 'motion.db'
if (Test-Path $db) { throw 'Motion gate requires a fresh database.' }
$env:WINTODO_PERF_DB_PATH = $db
$env:WINTODO_PERF_TASK_COUNT = '200'
$env:WINTODO_PERF_LIST_COUNT = '20'
try {
    dotnet test --project (Join-Path $root 'tests\Wintodo.Data.Tests') -p:Platform=x64 --filter-class '*PerformanceFixtureTests'
    if ($LASTEXITCODE -ne 0) { throw 'Motion fixture setup failed.' }
} finally {
    Remove-Item Env:\WINTODO_PERF_DB_PATH, Env:\WINTODO_PERF_TASK_COUNT, Env:\WINTODO_PERF_LIST_COUNT -ErrorAction SilentlyContinue
}
$app = Join-Path $root 'src\Wintodo.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64'
$launch = winapp run $app --detach --json -- --database-path $db | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $launch.ProcessId) { throw 'Motion gate launch failed.' }
try {
    & (Join-Path $PSScriptRoot 'completion-motion.ps1') -AppPid $launch.ProcessId -ArtifactDirectory $ArtifactDirectory -BaselineTimings $BaselineTimings -TestReducedMotion:$TestReducedMotion
    if ($LASTEXITCODE -ne 0) { throw 'Completion motion gate failed.' }
} finally { Stop-Process -Id $launch.ProcessId -ErrorAction SilentlyContinue }
Write-Host "Completion motion gate: PASS — $ArtifactDirectory"
