param(
    [string]$SeedDatabase = (Join-Path $PSScriptRoot 'artifacts\schema-guard\seed-v7.db')
)
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1') {
    throw 'Run this gate through scripts/Invoke-VmGate.ps1 in WintodoTest; host UI automation requires explicit session permission.'
}
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$appDirectory = Join-Path $repositoryRoot 'src\Wintodo.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64'
$artifactDirectory = Join-Path $PSScriptRoot ('artifacts\schema-guard\run-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null
if (-not (Test-Path -LiteralPath $SeedDatabase -PathType Leaf)) {
    throw 'Copy the synthetic newer-schema seed into the guest with Invoke-VmGate.ps1 -Include tests/ui/artifacts/schema-guard/seed-v7.db.'
}
$databasePath = Join-Path $artifactDirectory 'newer-schema.db'
Copy-Item -LiteralPath $SeedDatabase -Destination $databasePath
$originalHash = (Get-FileHash -LiteralPath $databasePath -Algorithm SHA256).Hash
$results = [Collections.Generic.List[object]]::new()
$appPid = 0

function Invoke-UiChecked {
    param([string[]]$Arguments)
    $output = & winapp ui @Arguments -a $script:appPid
    if ($LASTEXITCODE -ne 0) { throw "UI command failed: $($Arguments[0])" }
    $output
}

function Test-UI {
    param([string]$Name, [scriptblock]$Action)
    try {
        & $Action | Out-Null
        $results.Add(@{ name = $Name; status = 'PASS' })
    } catch {
        $results.Add(@{ name = $Name; status = 'FAIL'; detail = "$_" })
    }
}

function Start-SchemaApp {
    $launch = & winapp run $appDirectory --detach --json -- --database-path $databasePath |
        ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $launch.ProcessId) { throw 'Schema gate launch failed.' }
    $script:appPid = [int]$launch.ProcessId
    Invoke-UiChecked @('wait-for', 'Todo.Startup.NewerSchemaClose', '-t', '60000') | Out-Null
}

function Assert-AppExited {
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while ([DateTime]::UtcNow -lt $deadline -and (Get-Process -Id $script:appPid -ErrorAction SilentlyContinue)) {
        Start-Sleep -Milliseconds 100
    }
    if (Get-Process -Id $script:appPid -ErrorAction SilentlyContinue) { throw 'Closing the warning did not exit.' }
}

function Assert-DatabaseUnchanged {
    if ((Get-FileHash -LiteralPath $databasePath -Algorithm SHA256).Hash -ne $originalHash) {
        throw 'Rejected database bytes changed.'
    }
    if (@(Get-ChildItem -LiteralPath $artifactDirectory -File | Where-Object {
        $_.Name -like 'newer-schema.db-*' -or $_.Name -like 'newer-schema.db.bak-*'
    }).Count) { throw 'Rejected database created journal or backup files.' }
}

try {
    Test-UI 'Newer-schema warning appears' { Start-SchemaApp }
    if ($appPid -gt 0 -and (Get-Process -Id $appPid -ErrorAction SilentlyContinue)) {
        Test-UI 'Localized title and recovery text' {
            Invoke-UiChecked @('wait-for', 'A newer version of Wintodo is needed', '--type', 'Text', '-t', '5000')
            Invoke-UiChecked @('wait-for', 'This database was updated by a newer version of Wintodo. Update Wintodo to open it. Your tasks have not been changed.', '--type', 'Text', '-t', '5000')
            Invoke-UiChecked @('wait-for', 'Todo.Startup.NewerSchemaClose', '--value', 'Close', '-t', '5000')
        }
        Test-UI 'Warning screenshot captured' {
            $path = Join-Path $artifactDirectory 'newer-schema.png'
            Invoke-UiChecked @('screenshot', '--output', $path)
            if (-not (Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).Length -eq 0) {
                throw 'Warning screenshot missing.'
            }
        }
        Test-UI 'Close command exits without writing' {
            Invoke-UiChecked @('invoke', 'Todo.Startup.NewerSchemaClose')
            Assert-AppExited
            Assert-DatabaseUnchanged
        }
    }
    Test-UI 'Reopening still rejects; Escape closes safely' {
        if (Get-Process -Id $appPid -ErrorAction SilentlyContinue) { throw 'Previous launch still running.' }
        Start-SchemaApp
        Invoke-UiChecked @('send-keys', 'esc', '--via', 'send-input')
        Assert-AppExited
        Assert-DatabaseUnchanged
    }
} finally {
    if ($appPid -gt 0) { Stop-Process -Id $appPid -ErrorAction SilentlyContinue }
    $failed = @($results | Where-Object status -eq 'FAIL').Count
    @{ passed = $results.Count - $failed; failed = $failed; results = @($results.ToArray()) } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $artifactDirectory 'results.json') -Encoding utf8
}
Write-Host "Schema guard: $($results.Count - $failed) passed, $failed failed."
if ($failed) { exit 1 }
