$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$appDirectory = Join-Path $repositoryRoot "src\Wintodo.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64"
$artifactDirectory = Join-Path $PSScriptRoot ("artifacts\m3\run-" + [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss"))
$databasePath = Join-Path $artifactDirectory "m3-gate.db"
New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null

# The M3 loop predates optional delete confirmation and exercises immediate
# delete/undo. Seed that preference in a new scratch database before activation.
$env:WINTODO_M3_GATE_DB_PATH = $databasePath
try {
    dotnet test --project (Join-Path $repositoryRoot "tests\Wintodo.Data.Tests") `
        -p:Platform=x64 --filter-class "*M3GateDatabaseTests"
    if ($LASTEXITCODE -ne 0) { throw "M3 database setup failed." }
} finally {
    Remove-Item Env:\WINTODO_M3_GATE_DB_PATH -ErrorAction SilentlyContinue
}

$launch = winapp run $appDirectory --detach --json -- --database-path $databasePath | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $launch.ProcessId) { throw "M3 gate launch failed." }
try {
    & (Join-Path $PSScriptRoot "m3-lists-tasks.ps1") -AppPid $launch.ProcessId -ArtifactDirectory $artifactDirectory
    if ($LASTEXITCODE -ne 0) { throw "M3 UI gate failed." }
} finally {
    Stop-Process -Id $launch.ProcessId -ErrorAction SilentlyContinue
}
$restart = winapp run $appDirectory --detach --json -- --database-path $databasePath | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $restart.ProcessId) { throw "M3 restart launch failed." }
try {
    & (Join-Path $PSScriptRoot "m3-restart.ps1") -AppPid $restart.ProcessId -ArtifactDirectory $artifactDirectory
    if ($LASTEXITCODE -ne 0) { throw "M3 restart gate failed." }
} finally {
    Stop-Process -Id $restart.ProcessId -ErrorAction SilentlyContinue
}
Write-Host "M3 UI gate: PASS"
Write-Host "Artifacts: $artifactDirectory"
