param([switch]$Aot)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$artifactDirectory = Join-Path $PSScriptRoot ('artifacts/composer/run-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
$appDirectory = if ($Aot) {
    Join-Path $repositoryRoot 'publish/aot/win-x64'
} else {
    Join-Path $repositoryRoot 'src/Wintodo.App/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64'
}
New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null
$previousWorkflowId = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = [guid]::NewGuid().ToString('N')
$results = [Collections.Generic.List[object]]::new()
$screenshots = [Collections.Generic.List[string]]::new()
$appPid = 0
$mainHwnd = ''

function Invoke-Ui {
    param([string[]]$Arguments)
    $target = if ($mainHwnd -and $Arguments[0] -in @('focus', 'send-keys', 'set-value', 'screenshot')) {
        @('-w', $mainHwnd)
    } else { @('-a', "$appPid") }
    $output = @(& winapp ui @Arguments @target)
    if ($LASTEXITCODE -ne 0) { throw "winapp ui $($Arguments -join ' ') failed: $output" }
    $output
}

function Test-UI {
    param([string]$Name, [scriptblock]$Script)
    try {
        & $Script | Out-Null
        $results.Add(@{ name = $Name; status = 'PASS' })
    } catch {
        $results.Add(@{ name = $Name; status = 'FAIL'; detail = "$_" })
    }
}

function Get-Element {
    param([string]$Id)
    $matches = @((Invoke-Ui @('search', $Id, '--json') | ConvertFrom-Json).matches |
        Where-Object { $_.automationId -eq $Id -and -not $_.isOffscreen })
    if ($matches.Count -ne 1) { throw "Expected one visible $Id, found $($matches.Count)" }
    $matches[0]
}

function Save-Screenshot {
    param([string]$Name)
    $path = Join-Path $artifactDirectory $Name
    Invoke-Ui @('screenshot', '--output', $path) | Out-Null
    if (-not (Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).Length -eq 0) {
        throw "Screenshot not delivered: $path"
    }
    $screenshots.Add($path)
}

function Assert-CompactLayout {
    $input = Get-Element 'Todo.List.AddTaskBox'
    $options = Get-Element 'Todo.Composer.OptionsButton'
    $add = Get-Element 'Todo.Composer.AddButton'
    if ($input.width -lt 160) { throw "Task input is only $($input.width) pixels wide." }
    if ($input.x + $input.width -gt $options.x + 1 -or $options.x + $options.width -gt $add.x + 1) {
        throw 'Composer controls overlap.'
    }
    if ([Math]::Abs(($input.y + $input.height / 2) - ($add.y + $add.height / 2)) -gt 4) {
        throw 'Composer wrapped to another row.'
    }
}

function Set-WindowWidth {
    param([int]$Width)
    $window = (Get-Process -Id $appPid).MainWindowHandle
    $scale = [ComposerLayoutNative]::GetDpiForWindow($window) / 96.0
    if (-not [ComposerLayoutNative]::SetWindowPos($window, [IntPtr]::Zero, 0, 0,
        [int]($Width * $scale), [int](820 * $scale), 0x0006)) { throw 'Window resize failed.' }
    Start-Sleep -Milliseconds 500
}

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ComposerLayoutNative {
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
}
'@

try {
    $launch = & winapp run $appDirectory --detach --json -- --database-path (Join-Path $artifactDirectory 'composer.db') |
        ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $launch.ProcessId) { throw 'Composer gate launch failed.' }
    $appPid = [int]$launch.ProcessId
    for ($attempt = 0; $attempt -lt 20 -and -not $mainHwnd; $attempt++) {
        Start-Sleep -Milliseconds 250
        $mainHwnd = [string]((Invoke-Ui @('list-windows', '--json') | ConvertFrom-Json |
            Where-Object title -eq 'Wintodo' | Select-Object -First 1).hwnd)
    }
    if (-not $mainHwnd) { throw 'Main window not found.' }
    Invoke-Ui @('wait-for', 'Todo.List.AddTaskBox', '-t', '5000') | Out-Null
    Set-WindowWidth 1180
    Invoke-Ui @('invoke', 'Todo.Nav.All') | Out-Null
    Invoke-Ui @('focus', 'Todo.List.AddTaskBox') | Out-Null
    Invoke-Ui @('set-value', 'Todo.List.AddTaskBox', 'Detail layout sample') | Out-Null
    Invoke-Ui @('send-keys', 'enter', '--target', 'Todo.List.AddTaskBox', '--via', 'send-input') | Out-Null
    Start-Sleep -Milliseconds 300
    $row = (Invoke-Ui @('search', 'Detail layout sample', '--json') | ConvertFrom-Json).matches |
        Where-Object { $_.type -eq 'ListItem' -and $_.automationId -match '^Todo.Row.' } | Select-Object -First 1
    if (-not $row) { throw 'Created sample task not found.' }
    Invoke-Ui @('focus', $row.selector) | Out-Null
    Invoke-Ui @('send-keys', 'enter', '--via', 'send-input') | Out-Null
    Invoke-Ui @('wait-for', 'Todo.Detail.Title', '-t', '5000') | Out-Null
    Invoke-Ui @('focus', 'Todo.List.AddTaskBox') | Out-Null

    Test-UI 'Detail open keeps a usable single-row composer' {
        Assert-CompactLayout
        Save-Screenshot '01-detail-open.png'
    }
    Test-UI 'Compact options expose target, due, reminder and repeat' {
        Invoke-Ui @('invoke', 'Todo.Composer.OptionsButton') | Out-Null
        foreach ($id in @('List', 'Due', 'Reminder', 'Repeat')) {
            Invoke-Ui @('wait-for', "Todo.Composer.Options.$id", '-t', '3000') | Out-Null
        }
        Invoke-Ui @('invoke', 'Todo.Composer.Options.List') | Out-Null
        Invoke-Ui @('wait-for', 'Tasks', '--type', 'MenuItem', '-t', '3000') | Out-Null
        Invoke-Ui @('send-keys', 'escape', '--via', 'send-input') | Out-Null
        Invoke-Ui @('invoke', 'Todo.Composer.Options.Due') | Out-Null
        Invoke-Ui @('wait-for', 'Todo.Composer.Options.DueTomorrow', '-t', '3000') | Out-Null
        Save-Screenshot '02-options.png'
        Invoke-Ui @('invoke', 'Todo.Composer.Options.DueTomorrow') | Out-Null
        Invoke-Ui @('invoke', 'Todo.Composer.OptionsButton') | Out-Null
        Invoke-Ui @('invoke', 'Todo.Composer.Options.Repeat') | Out-Null
        Invoke-Ui @('invoke', 'Todo.Composer.Options.RepeatWeekly') | Out-Null
        Invoke-Ui @('set-value', 'Todo.List.AddTaskBox', 'Compact scheduled task') | Out-Null
        Invoke-Ui @('invoke', 'Todo.Composer.AddButton') | Out-Null
        Invoke-Ui @('wait-for', 'Compact scheduled task', '--type', 'ListItem', '-t', '5000') | Out-Null
        Invoke-Ui @('wait-for', 'Todo.List.AddTaskBox', '--value', '', '-t', '3000') | Out-Null
        Assert-CompactLayout
    }
    Test-UI 'Narrow list and wider window update without losing the draft' {
        # Escape in the composer cancels its draft before the shell sees it.
        # Close detail explicitly so the wide list can reach the inline breakpoint.
        Invoke-Ui @('invoke', 'Todo.Detail.Close') | Out-Null
        Invoke-Ui @('wait-for', 'Todo.Detail.Title', '--gone', '-t', '3000') | Out-Null
        Set-WindowWidth 900
        Invoke-Ui @('invoke', 'Todo.Nav.All') | Out-Null
        Invoke-Ui @('focus', 'Todo.List.AddTaskBox') | Out-Null
        Invoke-Ui @('set-value', 'Todo.List.AddTaskBox', 'Keep this draft while resizing') | Out-Null
        Assert-CompactLayout
        Save-Screenshot '03-narrow.png'
        Set-WindowWidth 1600
        Invoke-Ui @('wait-for', 'Todo.Composer.DueDateButton', '-t', '3000') | Out-Null
        Invoke-Ui @('wait-for', 'Todo.List.AddTaskBox', '--value', 'Keep this draft while resizing', '-t', '3000') | Out-Null
        if ((Get-Element 'Todo.List.AddTaskBox').width -lt 160) { throw 'Wide input is still cramped.' }
        Save-Screenshot '04-wide.png'
        Set-WindowWidth 1180
        Invoke-Ui @('focus', 'Todo.List.AddTaskBox') | Out-Null
        Invoke-Ui @('send-keys', 'enter', '--via', 'send-input') | Out-Null
        Invoke-Ui @('wait-for', 'Keep this draft while resizing', '--type', 'ListItem', '-t', '5000') | Out-Null
    }
} catch {
    $results.Add(@{ name = 'Gate setup'; status = 'FAIL'; detail = "$_" })
} finally {
    if ($appPid) { Stop-Process -Id $appPid -ErrorAction SilentlyContinue }
    & winapp ui yield | Out-Null
    $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflowId
}
$failed = @($results | Where-Object status -eq 'FAIL').Count
@{ passed = $results.Count - $failed; failed = $failed; results = $results.ToArray(); screenshots = $screenshots.ToArray() } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $artifactDirectory 'results.json')
$results | Format-Table -AutoSize
Write-Host "Composer gate artifacts: $artifactDirectory"
if ($failed) { throw "Composer layout gate failed: $failed checks." }
