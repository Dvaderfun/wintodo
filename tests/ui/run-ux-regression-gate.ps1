param([string]$ArtifactDirectory)
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1') { throw 'UI regression automation requires WintodoTest.' }
$ErrorActionPreference = 'Stop'
if (-not $ArtifactDirectory) { $ArtifactDirectory = Join-Path $PSScriptRoot ('artifacts\ux-regression\run-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')) }
New-Item -ItemType Directory -Force $ArtifactDirectory | Out-Null
$results = [Collections.Generic.List[object]]::new()
# These runners seed through dotnet test; Invoke-VmGate builds the guest data tests once.
foreach ($gate in 'm4','m13','m8','m7','m0') {
    try {
        $global:LASTEXITCODE = 0
        & (Join-Path $PSScriptRoot "run-$gate-gate.ps1")
        if ($LASTEXITCODE -ne 0) { throw "$gate exited with $LASTEXITCODE" }
        $results.Add(@{gate=$gate;status='PASS'})
    } catch { $results.Add(@{gate=$gate;status='FAIL';detail="$_"}); Write-Host "FAIL: $gate - $_" }
}
ConvertTo-Json -InputObject @($results.ToArray()) -Depth 5 | Set-Content (Join-Path $ArtifactDirectory 'results.json')
if (@($results | Where-Object status -eq 'FAIL').Count) { exit 1 }
