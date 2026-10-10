param(
    [switch]$VerifyToastCompletion,
    [switch]$VerifyToastSnooze,
    [string]$GateDatabasePath
)
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
    "artifacts\m7\run-" + [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
)
$testProject = Join-Path $repositoryRoot "tests\Wintodo.Data.Tests\Wintodo.Data.Tests.csproj"

if ($VerifyToastCompletion -and $VerifyToastSnooze) {
    throw "Choose only one toast verification mode."
}

if ($VerifyToastCompletion -or $VerifyToastSnooze) {
    if ([string]::IsNullOrWhiteSpace($GateDatabasePath)) {
        throw "-GateDatabasePath is required with toast verification."
    }

    $verificationName = if ($VerifyToastCompletion) {
        "RequestedGateToastCompletionPersisted"
    } else {
        "RequestedGateToastSnoozePersisted"
    }
    $failureMessage = if ($VerifyToastCompletion) {
        "The toast Complete action was not persisted."
    } else {
        "The toast Snooze action was not persisted and rescheduled."
    }
    $env:WINTODO_M7_GATE_DB_PATH = [System.IO.Path]::GetFullPath($GateDatabasePath)
    try {
        dotnet test $testProject `
            -p:Platform=x64 --no-build --filter-method "*M7GateDatabaseTests.$verificationName" `
            --verbosity quiet
        if ($LASTEXITCODE -ne 0) {
            throw $failureMessage
        }
    } finally {
        Remove-Item Env:WINTODO_M7_GATE_DB_PATH -ErrorAction SilentlyContinue
    }

    Write-Host "M7 toast action persistence: PASS"
    return
}

$databasePath = Join-Path $artifactDirectory "m7-gate.db"

New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null
$env:WINTODO_M7_GATE_DB_PATH = $databasePath
try {
    dotnet test $testProject `
        -p:Platform=x64 --no-build --filter-method "*M7GateDatabaseTests.SeedRequestedGateDatabase" `
        --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        throw "The M7 gate database could not be seeded."
    }

    $launch = winapp run $appDirectory `
        --detach `
        --json `
        -- `
        --database-path $databasePath |
        ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $launch.ProcessId) {
        throw "Wintodo M7 gate launch failed."
    }

    $appPid = [int]$launch.ProcessId
    try {
        & (Join-Path $PSScriptRoot "m7-windows-integration.ps1") `
            -AppPid $appPid `
            -ArtifactDirectory $artifactDirectory
        Start-Sleep -Seconds 2
    } finally {
        Stop-Process -Id $appPid -ErrorAction SilentlyContinue
    }

    dotnet test $testProject `
        -p:Platform=x64 --no-build --filter-method "*M7GateDatabaseTests.RequestedGateDatabaseHasScheduleTagAndBadgeCount" `
        --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        throw "The reminder schedule or badge preflight failed."
    }

} finally {
    Remove-Item Env:WINTODO_M7_GATE_DB_PATH -ErrorAction SilentlyContinue
}

Write-Host "M7 automated preflight: PASS"
Write-Host "Artifacts: $artifactDirectory"
Write-Host "Wait for the toast after app exit and choose Complete or Snooze."
Write-Host "To verify Complete, run:"
Write-Host (
    ".\tests\ui\run-m7-gate.ps1 -VerifyToastCompletion -GateDatabasePath `"" +
    $databasePath + "`""
)
Write-Host "To verify Snooze, replace -VerifyToastCompletion with -VerifyToastSnooze."
Write-Host "Reboot persistence also requires a reboot."
