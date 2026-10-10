param([string]$ArtifactDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $ArtifactDirectory) {
    $ArtifactDirectory = Join-Path $PSScriptRoot ('artifacts\multiselect\run-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
}
$ArtifactDirectory = [IO.Path]::GetFullPath($ArtifactDirectory)
New-Item -ItemType Directory -Force -Path $ArtifactDirectory | Out-Null
$db = Join-Path $ArtifactDirectory 'multiselect.db'
$env:WINTODO_MULTISELECT_DB_PATH = $db
try {
    dotnet test --project (Join-Path $root 'tests\Wintodo.Data.Tests') -p:Platform=x64 --filter-class '*MultiselectGateDatabaseTests'
    if ($LASTEXITCODE -ne 0) { throw 'Multiselect database setup failed.' }
} finally { Remove-Item Env:\WINTODO_MULTISELECT_DB_PATH -ErrorAction SilentlyContinue }
$app = Join-Path $root 'src\Wintodo.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64'
$launch = winapp run $app --detach --json -- --database-path $db | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $launch.ProcessId) { throw 'Multiselect gate launch failed.' }
try {
    & (Join-Path $PSScriptRoot 'multiselect.ps1') -AppPid $launch.ProcessId -ArtifactDirectory $ArtifactDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Multiselect gate failed.' }
} finally { Stop-Process -Id $launch.ProcessId -ErrorAction SilentlyContinue }
Write-Host "Multiselect gate: PASS — $ArtifactDirectory"
