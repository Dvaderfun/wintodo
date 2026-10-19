if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1') { throw 'Run in WintodoTest through Invoke-VmGate.' }
$ErrorActionPreference = 'Stop'
$artifact = Join-Path $PSScriptRoot 'artifacts/import-perf'
New-Item -ItemType Directory -Force -Path $artifact | Out-Null
$env:WINTODO_IMPORT_PERF = '1'
$env:WINTODO_IMPORT_PERF_OUTPUT = Join-Path $artifact 'measurements.md'
dotnet test --project tests/Wintodo.Import.Tests -p:Platform=x64 --filter-class '*ImportPerformanceTests'
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Add-Content -LiteralPath docs/PERF.md -Value "`n### IMP-16 / IMP-20 — WintodoTest fixture measurement (2026-10-20)`n`nDebug x64; 1,000 tasks / 20 lists; FixtureGraphHandler with 50 ms request latency.`nTest-only bounded candidate comparison; production reader unchanged.`n`n| Reader | Time to Choose (read) | Preview | Total read + preview + save |`n|---|---|---|---|"
Get-Content -LiteralPath $env:WINTODO_IMPORT_PERF_OUTPUT | Add-Content -LiteralPath docs/PERF.md
Get-Content -LiteralPath $env:WINTODO_IMPORT_PERF_OUTPUT
exit 0
