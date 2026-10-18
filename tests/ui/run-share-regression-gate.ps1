param([switch]$Aot)
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1') { throw 'UI regression automation requires WintodoTest.' }
$ErrorActionPreference = 'Stop'
$results = [Collections.Generic.List[object]]::new()
# dotnet test fixtures need the guest data test build performed by Invoke-VmGate.
foreach ($gate in 'm0','m7','m13') {
    try {
        $global:LASTEXITCODE = 0
        if ($gate -eq 'm13' -and $Aot) { & (Join-Path $PSScriptRoot 'run-m13-gate.ps1') -Aot }
        else { & (Join-Path $PSScriptRoot "run-$gate-gate.ps1") }
        if ($LASTEXITCODE -ne 0) { throw "$gate exited with $LASTEXITCODE" }
        $results.Add(@{gate=$gate;status='PASS'})
    } catch { $results.Add(@{gate=$gate;status='FAIL';detail="$_"}); Write-Host "FAIL: $gate - $_" }
}
$directory = Join-Path $PSScriptRoot ('artifacts\share-regression\run-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force $directory | Out-Null
ConvertTo-Json -InputObject @($results.ToArray()) -Depth 5 | Set-Content (Join-Path $directory 'results.json')
if (@($results | Where-Object status -eq 'FAIL').Count) { exit 1 }
