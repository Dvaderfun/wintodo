$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$appDirectory = Join-Path $repositoryRoot (
    "src\Wintodo.App\bin\x64\Debug\" +
    "net10.0-windows10.0.26100.0\win-x64"
)
$artifactDirectory = Join-Path $PSScriptRoot (
    "artifacts\m5\run-" + [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
)
$databasePath = Join-Path $artifactDirectory "m5-gate.db"
$clockPath = Join-Path $artifactDirectory "clock.txt"

New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null
[System.IO.File]::WriteAllText(
    $clockPath,
    "2026-07-31T09:00:00Z",
    [System.Text.Encoding]::UTF8)

$env:WINTODO_M5_GATE_DB_PATH = $databasePath
try {
    dotnet test (Join-Path $repositoryRoot "tests\Wintodo.Data.Tests") `
        --filter "FullyQualifiedName~M5GateDatabaseTests" `
        --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "M5 database seeding failed."
    }
} finally {
    Remove-Item Env:\WINTODO_M5_GATE_DB_PATH -ErrorAction SilentlyContinue
}

function Start-GateApp {
    $launch = winapp run $appDirectory `
        --detach `
        --json `
        -- `
        --database-path $databasePath `
        --clock-file $clockPath |
        ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $launch.ProcessId) {
        throw "Wintodo M5 gate launch failed."
    }

    return [int]$launch.ProcessId
}

$firstPid = Start-GateApp
try {
    & (Join-Path $PSScriptRoot "m5-smart-search.ps1") `
        -AppPid $firstPid `
        -ArtifactDirectory $artifactDirectory `
        -ClockPath $clockPath
    if ($LASTEXITCODE -ne 0) {
        throw "M5 phase-one UI gate failed."
    }
} finally {
    Stop-Process -Id $firstPid -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
}

$restartPid = Start-GateApp
try {
    & (Join-Path $PSScriptRoot "m5-restart.ps1") `
        -AppPid $restartPid `
        -ArtifactDirectory $artifactDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "M5 restart UI gate failed."
    }
} finally {
    Stop-Process -Id $restartPid -ErrorAction SilentlyContinue
}

Write-Host "M5 UI gate: PASS"
Write-Host "Artifacts: $artifactDirectory"
