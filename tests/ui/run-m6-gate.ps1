
# Local desktop input requires explicit owner permission for this session.
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1') {
    throw 'Local UI automation is disabled on this main machine. Use an isolated VM/Sandbox, or obtain owner permission for this session before setting WINTODO_ALLOW_LOCAL_UI=1. See docs/guides/testing.md.'
}
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$appDirectory = Join-Path $repositoryRoot (
    "src\Wintodo.App\bin\x64\Debug\" +
    "net10.0-windows10.0.26100.0\win-x64"
)
$artifactDirectory = Join-Path $PSScriptRoot (
    "artifacts\m6\run-" + [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
)
$databasePath = Join-Path $artifactDirectory "m6-gate.db"
$fixtureDirectory = Join-Path $repositoryRoot "tests\fixtures\ical"

New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null

$launch = winapp run $appDirectory `
    --detach `
    --json `
    -- `
    --database-path $databasePath |
    ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $launch.ProcessId) {
    throw "Wintodo M6 gate launch failed."
}

$appPid = [int]$launch.ProcessId
try {
    & (Join-Path $PSScriptRoot "m6-ical.ps1") `
        -AppPid $appPid `
        -ArtifactDirectory $artifactDirectory `
        -FixtureDirectory $fixtureDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "M6 UI gate failed."
    }
} finally {
    Stop-Process -Id $appPid -ErrorAction SilentlyContinue
}

Write-Host "M6 UI gate: PASS"
Write-Host "Artifacts: $artifactDirectory"
