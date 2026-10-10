param([string]$ArtifactDirectory)
# Local desktop input requires explicit owner permission for this session.
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1') {
    throw 'Local UI automation is disabled on this main machine. Use an isolated VM/Sandbox, or obtain owner permission for this session before setting WINTODO_ALLOW_LOCAL_UI=1. See docs/guides/testing.md.'
}

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $ArtifactDirectory) {
    $ArtifactDirectory = Join-Path $PSScriptRoot ('artifacts\list-stability\run-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
}
New-Item -ItemType Directory -Force -Path $ArtifactDirectory | Out-Null
$db = Join-Path $ArtifactDirectory 'stability.db'
$env:WINTODO_STABILITY_DB_PATH = [IO.Path]::GetFullPath($db)
try {
    dotnet test --project (Join-Path $root 'tests\Wintodo.Data.Tests') -p:Platform=x64 --filter-class '*ListStabilityGateDatabaseTests'
    if ($LASTEXITCODE -ne 0) { throw 'Stability database setup failed.' }
} finally { Remove-Item Env:\WINTODO_STABILITY_DB_PATH -ErrorAction SilentlyContinue }
$app = Join-Path $root 'src\Wintodo.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64'
$launch = winapp run $app --detach --json -- --database-path $db | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $launch.ProcessId) { throw 'Stability gate launch failed.' }
try {
    & (Join-Path $PSScriptRoot 'list-stability.ps1') -AppPid $launch.ProcessId -ArtifactDirectory $ArtifactDirectory
    if ($LASTEXITCODE -ne 0) { throw 'List stability gate failed.' }
} finally { Stop-Process -Id $launch.ProcessId -ErrorAction SilentlyContinue }
Write-Host "List stability gate: PASS — $ArtifactDirectory"
