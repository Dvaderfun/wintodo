$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$appDirectory = Join-Path $repositoryRoot (
    "src\Wintodo.App\bin\x64\Debug\" +
    "net10.0-windows10.0.26100.0\win-x64"
)
$artifactDirectory = Join-Path $PSScriptRoot (
    "artifacts\m8\run-" + [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
)
$databasePath = Join-Path $artifactDirectory "m8-gate.db"

New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null

$launch = winapp run $appDirectory `
    --detach `
    --json `
    -- `
    --database-path $databasePath |
    ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $launch.ProcessId) {
    throw "Wintodo M8 gate launch failed."
}

$appPid = [int]$launch.ProcessId
try {
    & (Join-Path $PSScriptRoot "m8-design-accessibility.ps1") `
        -AppPid $appPid `
        -ArtifactDirectory $artifactDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "M8 UI gate failed."
    }
} finally {
    Stop-Process -Id $appPid -ErrorAction SilentlyContinue
}

Write-Host "M8 UI gate: PASS"
Write-Host "Artifacts: $artifactDirectory"
