param([switch]$AppOnly)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$privateRoot = Join-Path $repositoryRoot ('artifacts/ci-security/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $privateRoot | Out-Null
$project = if ($AppOnly) { 'src/Wintodo.App/Wintodo.csproj' } else { 'Wintodo.sln' }
$projectSet = if ($AppOnly) { 'app' } else { 'solution' }
$report = Join-Path $privateRoot "dependencies-$projectSet.json"
try {
    $summary = Join-Path $repositoryRoot 'SafeTestResults/dependencies.json'
    if (Test-Path -LiteralPath $summary) { Remove-Item -LiteralPath $summary }
    dotnet package list --project (Join-Path $repositoryRoot $project) --vulnerable --include-transitive `
        --format json --output-version 1 --no-restore 1> $report 2> (Join-Path $privateRoot 'dependencies.log')
    $auditExit = $LASTEXITCODE
    python (Join-Path $PSScriptRoot 'Test-CiSecurity.py') dependencies $report --exit-code $auditExit `
        --project-set $projectSet --output (Join-Path $repositoryRoot 'SafeTestResults/dependencies.json')
    if ($LASTEXITCODE -ne 0) { throw 'Dependency audit did not pass.' }
} catch {
    Write-Host 'CI dependency audit failed; review private report.'
    exit 1
}
