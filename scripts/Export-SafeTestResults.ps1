param(
    [string]$InputDirectory = 'TestResults',
    [string]$OutputDirectory = 'SafeTestResults'
)

$ErrorActionPreference = 'Stop'
$suiteNames = @('Wintodo.Core.Tests', 'Wintodo.Data.Tests', 'Wintodo.Import.Tests', 'Wintodo.Sync.Tests')
$results = [Collections.Generic.List[object]]::new()
$outputFile = Join-Path $OutputDirectory 'summary.json'

try {
    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
    # Never leave an earlier successful report available after export fails.
    Remove-Item -LiteralPath $outputFile -Force -ErrorAction SilentlyContinue
    foreach ($suite in $suiteNames) {
        $files = @(Get-ChildItem -LiteralPath $InputDirectory -Filter '*.trx' -File -Recurse |
            Where-Object { $_.Name -match ('^' + [regex]::Escape($suite) + '(?:_.*)?\.trx$') })
        if ($files.Count -eq 0) { continue }
        if ($files.Count -ne 1) { throw 'Duplicate suite report.' }
        $settings = [Xml.XmlReaderSettings]::new()
        $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
        $settings.XmlResolver = $null
        $settings.MaxCharactersInDocument = 16MB
        $reader = [Xml.XmlReader]::Create($files[0].FullName, $settings)
        try {
            $document = [Xml.XmlDocument]::new()
            $document.XmlResolver = $null
            $document.Load($reader)
        } finally { $reader.Dispose() }
        $counters = $document.SelectNodes('/*[local-name()="TestRun"]/*[local-name()="ResultSummary"]/*[local-name()="Counters"]')
        if ($counters.Count -ne 1) { throw 'Missing counters.' }
        $counts = @{}
        foreach ($field in @('total', 'executed', 'passed', 'failed', 'notExecuted')) {
            $value = $counters[0].GetAttribute($field)
            [long]$number = 0
            if ($value -notmatch '^\d{1,12}$' -or -not [long]::TryParse($value, [ref]$number)) {
                throw 'Invalid counters.'
            }
            $counts[$field] = $number
        }
        if ($counts.executed -gt $counts.total -or
            $counts.passed + $counts.failed -gt $counts.executed -or
            $counts.passed + $counts.failed + $counts.notExecuted -gt $counts.total) {
            throw 'Inconsistent counters.'
        }
        $results.Add([pscustomobject]@{
            Suite = $suite
            Total = $counts.total
            Executed = $counts.executed
            Passed = $counts.passed
            Failed = $counts.failed
            Skipped = $counts.notExecuted
            Unresolved = $counts.total - $counts.passed - $counts.failed - $counts.notExecuted
        })
    }
    # Only fixed suite labels and validated counts cross the artifact boundary.
    # Test names, messages, stack traces, usernames, paths and coverage stay private.
    $summary = [pscustomobject]@{
        Complete = $results.Count -eq $suiteNames.Count
        Suites = $results.ToArray()
    }
    $summary | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $outputFile -Encoding utf8
    if (-not $summary.Complete) { throw 'Incomplete suite reports.' }
} catch {
    # Xml/IO error text can include paths or document content.
    throw 'WINTODO_TEST_REPORT_EXPORT_FAILED'
}
