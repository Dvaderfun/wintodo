param(
    [string]$SeedDatabase = (Join-Path $PSScriptRoot 'artifacts/schema-guard/seed-v8.db')
)

# This gate exercises the Release JIT build layout. It is not AOT, signed-MSIX
# installation, live authentication or publication evidence.
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1') {
    throw 'Run this gate through scripts/Invoke-VmGate.ps1 in WintodoTest; host UI automation requires explicit session permission.'
}
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$appDirectory = Join-Path $repositoryRoot 'src/Wintodo.App/bin/x64/Release/net10.0-windows10.0.26100.0/win-x64'
$artifactDirectory = Join-Path $PSScriptRoot ('artifacts/release-diagnostics/run-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null
$previousWorkflowId = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = [guid]::NewGuid().ToString('N')
$results = [Collections.Generic.List[object]]::new()
$appPid = 0
$databasePath = Join-Path $artifactDirectory 'startup.db'
$tracePath = Join-Path $artifactDirectory 'SYNTHETIC-RELEASE-TRACE.log'

function Invoke-WinAppChecked {
    param([string[]]$Arguments)
    $output = @(& winapp @Arguments)
    if ($LASTEXITCODE -ne 0) { throw 'WINTODO_RELEASE_GATE_CLI_FAILED' }
    $output
}

function Invoke-UiChecked {
    param([string[]]$Arguments)
    if ($script:appPid -le 0) { throw 'WINTODO_RELEASE_GATE_NO_APP' }
    Invoke-WinAppChecked (@('ui') + $Arguments + @('-a', "$script:appPid"))
}

function Test-UI {
    param([string]$Name, [scriptblock]$Action)
    try {
        & $Action | Out-Null
        $results.Add(@{ name = $Name; status = 'PASS' })
        Write-Host "PASS  $Name"
    } catch {
        $results.Add(@{ name = $Name; status = 'FAIL'; detail = 'WINTODO_RELEASE_DIAGNOSTIC_CHECK_FAILED' })
        Write-Host "FAIL  $Name"
    }
}

function Start-ReleaseApp {
    if ($script:appPid -gt 0 -and (Get-Process -Id $script:appPid -ErrorAction SilentlyContinue)) {
        throw 'WINTODO_RELEASE_GATE_PREVIOUS_APP_RUNNING'
    }
    # Prove the requested file's parent is writable before asking Release to
    # create a Debug-only trace listener. The target must start absent.
    'synthetic-write-probe' | Set-Content -LiteralPath $script:tracePath -Encoding utf8
    Remove-Item -LiteralPath $script:tracePath
    $launch = Invoke-WinAppChecked @('run', $appDirectory, '--detach', '--json', '--',
        '--database-path', $script:databasePath, '--trace-file', $script:tracePath) | ConvertFrom-Json
    if (-not $launch.ProcessId -or [int]$launch.ProcessId -le 0) {
        throw 'WINTODO_RELEASE_GATE_LAUNCH_FAILED'
    }
    $script:appPid = [int]$launch.ProcessId
}

function Assert-NoTraceFile {
    if (Test-Path -LiteralPath $script:tracePath) { throw 'WINTODO_RELEASE_CREATED_DEBUG_TRACE_FILE' }
}

function Assert-AppExited {
    if ($script:appPid -gt 0 -and (Get-Process -Id $script:appPid -ErrorAction SilentlyContinue)) {
        Wait-Process -Id $script:appPid -Timeout 10 -ErrorAction Stop
    }
    if (Get-Process -Id $script:appPid -ErrorAction SilentlyContinue) { throw 'WINTODO_RELEASE_GATE_APP_DID_NOT_EXIT' }
    $script:appPid = 0
}

function Save-Screenshot {
    param([string]$Name)
    $path = Join-Path $artifactDirectory $Name
    Invoke-UiChecked @('screenshot', '--output', $path) | Out-Null
    if (-not (Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).Length -le 0) {
        throw 'WINTODO_RELEASE_GATE_SCREENSHOT_MISSING'
    }
}

try {
    if (-not (Test-Path -LiteralPath (Join-Path $appDirectory 'Wintodo.dll')) -or
        -not (Test-Path -LiteralPath (Join-Path $appDirectory 'Wintodo.exe'))) {
        throw 'Copy the built Release JIT layout into the guest with Invoke-VmGate.ps1 -Include.'
    }
    if (-not (Test-Path -LiteralPath $SeedDatabase -PathType Leaf)) {
        throw 'Include the synthetic tests/ui/artifacts/schema-guard/seed-v8.db fixture.'
    }
    Test-UI 'Release JIT launches a real interactive task window' {
        Start-ReleaseApp
        Invoke-UiChecked @('wait-for', 'Todo.List.AddTaskBox', '-t', '120000')
        Invoke-UiChecked @('wait-for', 'Todo.Nav.Settings', '-t', '10000')
        $windows = @(Invoke-UiChecked @('list-windows', '--json') | ConvertFrom-Json)
        if (-not ($windows | Where-Object { $_.hwnd -and $_.title -eq 'Wintodo' })) {
            throw 'WINTODO_RELEASE_GATE_MAIN_WINDOW_MISSING'
        }
    }
    Test-UI 'Successful Release startup ignores Debug trace-file switch' {
        Invoke-UiChecked @('wait-for', 'Todo.List.AddTaskBox', '-t', '5000')
        if (-not (Test-Path -LiteralPath $databasePath)) { throw 'WINTODO_RELEASE_GATE_SCRATCH_DATABASE_MISSING' }
        Assert-NoTraceFile
    }
    Test-UI 'Release startup screenshot is captured' { Save-Screenshot '01-release-startup.png' }
    # Stop only this synthetic scratch process before the independent rejection
    # launch. No user database or unrelated process is touched.
    if ($appPid -gt 0) {
        Stop-Process -Id $appPid -ErrorAction SilentlyContinue
        Assert-AppExited
    }

    $databasePath = Join-Path $artifactDirectory 'newer-schema.db'
    $tracePath = Join-Path $artifactDirectory 'SYNTHETIC-RELEASE-ERROR-TRACE.log'
    Copy-Item -LiteralPath $SeedDatabase -Destination $databasePath
    $originalHash = (Get-FileHash -LiteralPath $databasePath -Algorithm SHA256).Hash
    Test-UI 'Release newer-schema failure shows localized safe recovery guidance' {
        Start-ReleaseApp
        Invoke-UiChecked @('wait-for', 'Todo.Startup.NewerSchemaClose', '-t', '120000')
        Invoke-UiChecked @('wait-for', 'A newer version of Wintodo is needed', '--type', 'Text', '-t', '5000')
        Invoke-UiChecked @('wait-for', 'This database was updated by a newer version of Wintodo. Update Wintodo to open it. Your tasks have not been changed.', '--type', 'Text', '-t', '5000')
        Assert-NoTraceFile
    }
    Test-UI 'Release safe error screenshot is captured' { Save-Screenshot '02-release-schema-warning.png' }
    Test-UI 'Release error closes without database writes or a Debug trace file' {
        Invoke-UiChecked @('invoke', 'Todo.Startup.NewerSchemaClose')
        Assert-AppExited
        if ((Get-FileHash -LiteralPath $databasePath -Algorithm SHA256).Hash -ne $originalHash) {
            throw 'WINTODO_RELEASE_GATE_REJECTED_DATABASE_CHANGED'
        }
        if (@(Get-ChildItem -LiteralPath $artifactDirectory -File | Where-Object {
            $_.Name -like 'newer-schema.db-*' -or $_.Name -like 'newer-schema.db.bak-*'
        }).Count) { throw 'WINTODO_RELEASE_GATE_REJECTED_DATABASE_SIDECAR' }
        Assert-NoTraceFile
    }
} catch {
    $results.Add(@{ name = 'Release diagnostic gate setup/execution'; status = 'FAIL'; detail = 'WINTODO_RELEASE_DIAGNOSTIC_GATE_FAILED' })
} finally {
    if ($appPid -gt 0) { Stop-Process -Id $appPid -ErrorAction SilentlyContinue }
    try { Invoke-WinAppChecked @('ui', 'yield') | Out-Null } catch {
        $results.Add(@{ name = 'Release UI workflow'; status = 'FAIL'; detail = 'WINTODO_RELEASE_GATE_YIELD_FAILED' })
    }
    $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflowId
    $failed = @($results | Where-Object status -eq 'FAIL').Count
    @{
        build = 'Release JIT'; passed = $results.Count - $failed; failed = $failed
        results = @($results.ToArray())
        limits = @('No native debugger trace capture; external debugger output remains private.',
            'This is not Native AOT or final signed/installable package evidence.')
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $artifactDirectory 'results.json') -Encoding utf8
}
Write-Host "Release diagnostics (JIT): $($results.Count - $failed) passed, $failed failed."
if ($failed) { exit 1 }
exit 0
