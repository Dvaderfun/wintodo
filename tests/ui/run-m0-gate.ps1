
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
    "artifacts\m0\run-" + [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
)
$databasePath = Join-Path $artifactDirectory "m0-gate.db"

New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null

$launch = winapp run $appDirectory `
    --detach `
    --json `
    -- `
    --database-path $databasePath |
    ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $launch.ProcessId) {
    throw "Wintodo M0 smoke launch failed."
}

$appPid = [int]$launch.ProcessId
try {
    # --detach returns at process start. A first, cold launch (fresh test VM)
    # takes several seconds to show the window, and m0-smoke checks at once.
    winapp ui wait-for "Todo.List.AddTaskBox" -a $appPid -t 60000 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Wintodo main window did not appear within 60 s."
    }
    & (Join-Path $PSScriptRoot "m0-smoke.ps1") -AppPid $appPid
    if ($LASTEXITCODE -ne 0) {
        throw "M0 smoke test failed."
    }
} finally {
    Stop-Process -Id $appPid -ErrorAction SilentlyContinue
}

Write-Host "M0 smoke gate: PASS"
Write-Host "Artifacts: $artifactDirectory"
