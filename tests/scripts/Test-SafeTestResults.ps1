$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$exporter = Join-Path $repositoryRoot 'scripts/Export-SafeTestResults.ps1'
$root = Join-Path $repositoryRoot ('artifacts/dep08/script-tests/' + [Guid]::NewGuid().ToString('N'))
$inputDirectory = Join-Path $root 'input'
$outputDirectory = Join-Path $root 'safe'
New-Item -ItemType Directory -Force -Path $inputDirectory | Out-Null
$markers = 'SYNTHETIC-TOKEN SYNTHETIC-PASSWORD person@example.invalid SYNTHETIC-TASK SYNTHETIC-ACCOUNT C:\Users\SYNTHETIC-OWNER\private.db'
$suites = @('Core', 'Data', 'Import', 'Sync')
foreach ($suite in $suites) {
    @"
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010" name="$markers">
<Results><UnitTestResult testName="$markers"><Output><ErrorInfo><Message>$markers</Message><StackTrace>$markers</StackTrace></ErrorInfo></Output></UnitTestResult></Results>
<ResultSummary><Counters total="3" executed="2" passed="1" failed="1" notExecuted="1"/></ResultSummary>
</TestRun>
"@ | Set-Content -LiteralPath (Join-Path $inputDirectory "Wintodo.$suite.Tests_net10.0_x64.trx") -Encoding utf8
}
& $exporter -InputDirectory $inputDirectory -OutputDirectory $outputDirectory
$json = Get-Content -LiteralPath (Join-Path $outputDirectory 'summary.json') -Raw
foreach ($marker in $markers.Split(' ')) {
    if ($json.Contains($marker, [StringComparison]::Ordinal)) { throw 'Private marker leaked.' }
}
$report = $json | ConvertFrom-Json
if (-not $report.Complete -or $report.Suites.Count -ne 4) { throw 'Suite coverage was lost.' }
foreach ($suite in $report.Suites) {
    if ($suite.Total -ne 3 -or $suite.Passed -ne 1 -or $suite.Failed -ne 1 -or
        $suite.Skipped -ne 1 -or $suite.Unresolved -ne 0) { throw 'Failure/skip counts were lost.' }
}

$target = Join-Path $inputDirectory 'Wintodo.Sync.Tests_net10.0_x64.trx'
foreach ($invalid in @(
    "<TestRun><$markers",
    '<!DOCTYPE TestRun [<!ENTITY unsafe SYSTEM "file:///SYNTHETIC-OWNER/private.db">]><TestRun/>',
    '<TestRun><ResultSummary><Counters total="-1" executed="0" passed="0" failed="0" notExecuted="0"/></ResultSummary></TestRun>'
)) {
    $invalid | Set-Content -LiteralPath $target -Encoding utf8
    $rejected = $false
    try { & $exporter -InputDirectory $inputDirectory -OutputDirectory $outputDirectory }
    catch {
        $rejected = $true
        if ($_.Exception.Message -ne 'WINTODO_TEST_REPORT_EXPORT_FAILED') { throw 'Unsafe failure diagnostic.' }
    }
    if (-not $rejected -or (Test-Path -LiteralPath (Join-Path $outputDirectory 'summary.json'))) {
        throw 'Invalid report was accepted or retained stale successful output.'
    }
}
Copy-Item -LiteralPath (Join-Path $inputDirectory 'Wintodo.Core.Tests_net10.0_x64.trx') -Destination $target
$duplicate = Join-Path $inputDirectory 'Wintodo.Sync.Tests_duplicate.trx'
Copy-Item -LiteralPath $target -Destination $duplicate
$rejected = $false
try { & $exporter -InputDirectory $inputDirectory -OutputDirectory $outputDirectory }
catch {
    $rejected = $true
    if ($_.Exception.Message -ne 'WINTODO_TEST_REPORT_EXPORT_FAILED') { throw 'Unsafe duplicate diagnostic.' }
}
if (-not $rejected -or (Test-Path -LiteralPath (Join-Path $outputDirectory 'summary.json'))) {
    throw 'Duplicate suite report was accepted.'
}
Remove-Item -LiteralPath $duplicate, $target
$rejected = $false
try { & $exporter -InputDirectory $inputDirectory -OutputDirectory $outputDirectory }
catch {
    $rejected = $true
    if ($_.Exception.Message -ne 'WINTODO_TEST_REPORT_EXPORT_FAILED') { throw 'Unsafe incomplete diagnostic.' }
}
$incomplete = Get-Content -LiteralPath (Join-Path $outputDirectory 'summary.json') -Raw | ConvertFrom-Json
if (-not $rejected -or $incomplete.Complete -or $incomplete.Suites.Count -ne 3) {
    throw 'Incomplete report was accepted or misrepresented.'
}
Write-Output 'Safe test-result export: marker/counter proof and five invalid-input cases passed.'
