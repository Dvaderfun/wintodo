# Run through scripts/Invoke-VmGate.ps1 on the owner's machine.
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1') {
    throw 'Host UI automation is disabled. Use scripts/Invoke-VmGate.ps1.'
}
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$artifacts = Join-Path $PSScriptRoot ('artifacts\sort-indicator\run-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
$database = Join-Path $artifacts 'sort.db'
$clock = Join-Path $artifacts 'clock.txt'
[IO.File]::WriteAllText($clock, '2026-07-31T09:00:00Z')
$env:WINTODO_M5_GATE_DB_PATH = $database
try {
    dotnet test --project (Join-Path $root 'tests\Wintodo.Data.Tests') -p:Platform=x64 --filter-class '*M5GateDatabaseTests' --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Sort fixture seeding failed.' }
} finally { Remove-Item Env:\WINTODO_M5_GATE_DB_PATH -ErrorAction SilentlyContinue }
$app = Join-Path $root 'src\Wintodo.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64'
foreach ($phase in 'initial', 'persisted', 'reset') {
    $launch = winapp run $app --detach --json -- --database-path $database --clock-file $clock | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $launch.ProcessId) { throw "Sort gate $phase launch failed." }
    try {
        & (Join-Path $PSScriptRoot 'sort-indicator.ps1') -AppPid $launch.ProcessId -ArtifactDirectory $artifacts -Phase $phase
        if ($LASTEXITCODE -ne 0) { throw "Sort gate $phase failed." }
    } finally {
        Stop-Process -Id $launch.ProcessId -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 500
    }
}
Write-Host "Sort indicator gate: PASS — $artifacts"
