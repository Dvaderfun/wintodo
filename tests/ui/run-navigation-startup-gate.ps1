
# Local desktop input requires explicit owner permission for this session.
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1') {
    throw 'Local UI automation is disabled on this main machine. Use an isolated VM/Sandbox, or obtain owner permission for this session before setting WINTODO_ALLOW_LOCAL_UI=1. See docs/guides/testing.md.'
}
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$appDirectory = Join-Path $repositoryRoot 'src/Wintodo.App/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64'
$artifactDirectory = Join-Path $PSScriptRoot ('artifacts/navigation/run-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null
$databasePath = Join-Path $artifactDirectory 'navigation.db'
$previousWorkflowId = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = [guid]::NewGuid().ToString('N')
$appPid = 0
$mainHwnd = ''
$results = [Collections.Generic.List[object]]::new()
$screenshots = [Collections.Generic.List[string]]::new()
$fixedIds = @('MyDay', 'Important', 'Planned', 'All', 'Completed', 'DefaultList')

function Invoke-Ui {
    param([string[]]$Arguments)
    $output = @(& winapp ui @Arguments -w $mainHwnd)
    if ($LASTEXITCODE -ne 0) { throw "winapp ui $($Arguments -join ' ') failed: $output" }
    $output
}

function Start-App {
    $launch = winapp run $appDirectory --detach --json -- --database-path $databasePath | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $launch.ProcessId) { throw 'Navigation gate launch failed.' }
    $script:appPid = [int]$launch.ProcessId
    $script:mainHwnd = ''
    for ($attempt = 0; $attempt -lt 20 -and -not $mainHwnd; $attempt++) {
        Start-Sleep -Milliseconds 250
        $script:mainHwnd = [string]((winapp ui list-windows -a $appPid --json | ConvertFrom-Json |
            Where-Object title -eq 'Wintodo' | Select-Object -First 1).hwnd)
    }
    if (-not $mainHwnd) { throw 'Main window not found.' }
    Invoke-Ui @('wait-for', 'Todo.Shell.PageTitle', '-t', '5000') | Out-Null
}

function Assert-SingleSelection {
    param([string]$Expected, [string]$Name)
    $properties = foreach ($id in $fixedIds) {
        $element = Invoke-Ui @('get-property', "Todo.Nav.$id", '--json') | ConvertFrom-Json
        @{ id = $id; selected = $element.properties.IsSelected; focused = $element.properties.HasKeyboardFocus }
    }
    $properties | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifactDirectory "$Name-selection.json")
    $selected = @($properties | Where-Object selected -eq 'True')
    if ($selected.Count -ne 1 -or $selected[0].id -ne $Expected) {
        throw "Expected only $Expected selected; found: $(($selected.id) -join ', ')"
    }
    $path = Join-Path $artifactDirectory "$Name.png"
    Invoke-Ui @('screenshot', '--output', $path) | Out-Null
    if (-not (Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).Length -eq 0) {
        throw "Screenshot was not delivered: $path"
    }
    $screenshots.Add($path)
    $results.Add(@{ name = $Name; status = 'PASS' })
}

try {
    Start-App
    Invoke-Ui @('wait-for', 'Todo.Shell.PageTitle', '--value', 'My Day', '-t', '5000') | Out-Null
    Invoke-Ui @('hover', 'Todo.TitleBar.SearchBox') | Out-Null
    Assert-SingleSelection 'MyDay' '01-first-launch'
    Invoke-Ui @('invoke', 'Todo.Nav.Completed') | Out-Null
    Invoke-Ui @('wait-for', 'Todo.Shell.PageTitle', '--value', 'Completed', '-t', '5000') | Out-Null
    Invoke-Ui @('hover', 'Todo.TitleBar.SearchBox') | Out-Null
    Assert-SingleSelection 'Completed' '02-changed-selection'
    # Allow the async selection handler to finish persisting the key.
    Start-Sleep -Milliseconds 500
    Stop-Process -Id $appPid
    $appPid = 0
    Start-App
    Invoke-Ui @('wait-for', 'Todo.Shell.PageTitle', '--value', 'Completed', '-t', '5000') | Out-Null
    Invoke-Ui @('hover', 'Todo.TitleBar.SearchBox') | Out-Null
    Assert-SingleSelection 'Completed' '03-restored-selection'
    Invoke-Ui @('hover', 'Todo.Nav.MyDay') | Out-Null
    Invoke-Ui @('hover', 'Todo.TitleBar.SearchBox') | Out-Null
    Assert-SingleSelection 'Completed' '04-hover-left'
} catch {
    $results.Add(@{ name = 'Navigation startup gate'; status = 'FAIL'; detail = "$_" })
} finally {
    if ($appPid) { Stop-Process -Id $appPid -ErrorAction SilentlyContinue }
    winapp ui yield | Out-Null
    $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflowId
}
$failed = @($results | Where-Object status -eq 'FAIL').Count
@{ passed = $results.Count - $failed; failed = $failed; results = $results.ToArray(); screenshots = $screenshots.ToArray() } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $artifactDirectory 'results.json')
$results | Format-Table -AutoSize
Write-Host "Navigation startup artifacts: $artifactDirectory"
if ($failed) { throw 'Navigation startup gate failed.' }
