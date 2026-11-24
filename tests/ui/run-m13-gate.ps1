param([switch]$Aot)
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
if ($Aot) {
    $appDirectory = Join-Path $repositoryRoot "publish\aot\win-x64"
}
$artifactDirectory = Join-Path $PSScriptRoot (
    "artifacts\m13\run-" + [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
)
$databasePath = Join-Path $artifactDirectory "m13-gate.db"
$readyFile = Join-Path $artifactDirectory "server-ready.txt"
$clockFile = Join-Path $artifactDirectory 'clock.txt'
$traceArgs = @()
if (-not $Aot) { $traceArgs = @('--trace-file', (Join-Path $artifactDirectory 'first.trace')) }
New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null
[IO.File]::WriteAllText($clockFile, [DateTime]::new(2026, 11, 20, 12, 0, 0).ToUniversalTime().ToString('O'))

$tcp = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$tcp.Start()
$port = ([Net.IPEndPoint]$tcp.LocalEndpoint).Port
$tcp.Stop()
$server = Start-Process pwsh `
    -ArgumentList @(
        "-NoProfile",
        "-File",
        (Join-Path $PSScriptRoot "m13-caldav-server.ps1"),
        "-Port",
        "$port",
        "-ReadyFile",
        $readyFile
    ) `
    -WindowStyle Hidden `
    -PassThru

try {
    for ($attempt = 0; $attempt -lt 40 -and -not (Test-Path $readyFile); $attempt++) {
        Start-Sleep -Milliseconds 100
    }
    if (-not (Test-Path $readyFile)) {
        throw "The loopback CalDAV server did not start."
    }

    $launch = winapp run $appDirectory `
        --detach `
        --json `
        -- `
        --database-path $databasePath --clock-file $clockFile @traceArgs |
        ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $launch.ProcessId) {
        throw "Wintodo M13 gate launch failed."
    }

    $appPid = [int]$launch.ProcessId
    try {
        & (Join-Path $PSScriptRoot "m13-sync.ps1") `
            -AppPid $appPid `
            -ServerPort $port `
            -AppDirectory $appDirectory `
            -DatabasePath $databasePath `
            -ClockFile $clockFile `
            -VerifyTimers:(-not $Aot) `
            -ArtifactDirectory $artifactDirectory
        if ($LASTEXITCODE -ne 0) {
            throw "M13 UI gate failed."
        }
    } finally {
        Stop-Process -Id $appPid -ErrorAction SilentlyContinue
    }
} finally {
    Stop-Process -Id $server.Id -ErrorAction SilentlyContinue
}

Write-Host "M13 UI gate: PASS"
Write-Host "Artifacts: $artifactDirectory"
