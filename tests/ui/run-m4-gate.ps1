
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
    "artifacts\m4\run-" + [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
)
$databasePath = Join-Path $artifactDirectory "m4-gate.db"

New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null

function Start-GateApp {
    $launch = winapp run $appDirectory `
        --detach `
        --json `
        -- `
        --database-path $databasePath |
        ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $launch.ProcessId) {
        throw "Wintodo gate launch failed."
    }

    $gatePid = [int]$launch.ProcessId
    winapp ui wait-for "Todo.Nav.DefaultList" -a $gatePid -t 15000 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "The task shell did not become ready." }
    return $gatePid
}

$firstPid = Start-GateApp
try {
    & (Join-Path $PSScriptRoot "m4-detail.ps1") `
        -AppPid $firstPid `
        -ArtifactDirectory $artifactDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "M4 phase-one UI gate failed."
    }

    $row = (winapp ui search "M4 recurring task edited" -a $firstPid --json |
        ConvertFrom-Json).matches | Where-Object {
            $_.type -eq "ListItem" -and $_.automationId -match "^Todo\.Row\.[0-9a-f-]+$"
        } | Select-Object -First 1
    if ($LASTEXITCODE -ne 0 -or -not $row) { throw "Could not find the task for the close check." }
    winapp ui invoke $row.automationId -a $firstPid | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not reopen the detail pane." }
    winapp ui wait-for "Todo.Detail.Note" -a $firstPid -t 5000 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "The detail note did not appear." }
    $process = Get-Process -Id $firstPid
    winapp ui send-keys 'ctrl+a text=M4\snote\ssaved\son\sclose alt+f4' --target "Todo.Detail.Note" `
        -a $firstPid --via send-input --allow-system-keys | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not $process.WaitForExit(10000)) {
        throw "The window did not exit after editing the note and closing."
    }
} finally {
    Stop-Process -Id $firstPid -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
}

$restartPid = Start-GateApp
try {
    & (Join-Path $PSScriptRoot "m4-restart.ps1") `
        -AppPid $restartPid `
        -ArtifactDirectory $artifactDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "M4 restart UI gate failed."
    }
} finally {
    Stop-Process -Id $restartPid -ErrorAction SilentlyContinue
}

Write-Host "M4 UI gate: PASS"
Write-Host "Artifacts: $artifactDirectory"
