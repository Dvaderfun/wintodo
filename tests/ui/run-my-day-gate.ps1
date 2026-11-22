# Local desktop input requires explicit owner permission for this session.
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1') {
    throw 'Run this UI gate in WintodoTest through scripts/Invoke-VmGate.ps1.'
}
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$app = Join-Path $root 'src/Wintodo.App/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64'
$artifacts = Join-Path $PSScriptRoot "artifacts/my-day/$([guid]::NewGuid())"
New-Item -ItemType Directory -Path $artifacts | Out-Null
$database = Join-Path $artifacts 'my-day.db'
$clock = Join-Path $artifacts 'clock.txt'
function Set-TestClock([DateTime]$LocalTime) {
    $utc = [TimeZoneInfo]::ConvertTimeToUtc($LocalTime)
    $staged = "$clock.next"
    [IO.File]::WriteAllText($staged, $utc.ToString('O'))
    [IO.File]::Move($staged, $clock, $true)
}
Set-TestClock ([DateTime]::new(2026, 11, 20, 18, 0, 0))
$launch = winapp run $app --detach --json -- --database-path $database --clock-file $clock | ConvertFrom-Json
if ($LASTEXITCODE -ne 0 -or -not $launch.ProcessId) { throw 'My Day gate launch failed.' }
$appPid = [int]$launch.ProcessId

function Ui([string[]]$Command) {
    $output = winapp ui @Command -a $appPid
    if ($LASTEXITCODE -ne 0) { throw "UI command failed ($Command): $output" }
    $output
}

function Assert-MyDay([bool]$Included) {
    $label = if ($Included) { 'Added to My Day' } else { 'Add to My Day' }
    $navigationName = if ($Included) { 'My Day, 1 tasks' } else { 'My Day' }
    Ui @('wait-for', 'Todo.Detail.MyDay', '--property', 'Name', '--value', $label, '-t', '5000') | Out-Null
    Ui @('wait-for', 'Todo.Nav.MyDay', '--property', 'Name', '--value', $navigationName, '-t', '5000') | Out-Null
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        $tree = (Ui @('inspect', '--depth', '30', '--hide-offscreen', '--json')) -join "`n"
        $hasMetadata = $tree -match ' (?:·|\\u00b7) My Day'
        if ($hasMetadata -eq $Included) { return }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    $tree | Set-Content (Join-Path $artifacts 'failure-tree.txt')
    throw "Expected My Day row metadata visibility to be $Included."
}

try {
    Ui @('wait-for', 'Todo.Nav.DefaultList', '-t', '10000') | Out-Null
    Ui @('invoke', 'Todo.Nav.DefaultList') | Out-Null
    Ui @('click', 'Todo.List.AddTaskBox') | Out-Null
    Ui @('set-value', 'Todo.List.AddTaskBox', 'My Day gate task') | Out-Null
    Ui @('wait-for', 'Todo.Composer.AddButton', '--property', 'IsEnabled', '--value', 'True', '-t', '5000') | Out-Null
    Ui @('invoke', 'Todo.Composer.AddButton') | Out-Null
    Ui @('wait-for', 'My Day gate task', '-t', '5000') | Out-Null
    $row = (Ui @('search', 'My Day gate task', '--json') | ConvertFrom-Json).matches |
        Where-Object { $_.automationId -match '^Todo\.Row\.[0-9a-f-]+$' } | Select-Object -First 1
    if (-not $row) { throw 'The newly added task row was not found.' }
    Ui @('invoke', $row.automationId) | Out-Null
    Assert-MyDay $false
    Ui @('invoke', 'Todo.Detail.Due') | Out-Null
    Ui @('invoke', 'Todo.Detail.Due.Today') | Out-Null
    Assert-MyDay $true
    Ui @('invoke', 'Todo.Detail.MyDay') | Out-Null
    Assert-MyDay $false
    Ui @('focus', 'Todo.Detail.MyDay') | Out-Null
    Ui @('send-keys', 'ctrl+vk=0x5A', '--via', 'send-input') | Out-Null
    Assert-MyDay $true
    Ui @('invoke', 'Todo.Detail.MyDay') | Out-Null
    Assert-MyDay $false
    Ui @('invoke', 'Todo.Detail.MyDay') | Out-Null
    Assert-MyDay $true

    # Re-activate the same window to exercise the date-change path with detail still open.
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class MyDayGateWindow
{
    [DllImport("user32.dll")]
    public static extern bool ShowWindowAsync(IntPtr hwnd, int command);
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hwnd);
}
'@
    Set-TestClock ([DateTime]::new(2026, 11, 21, 0, 1, 0))
    $window = (Ui @('list-windows', '--json') | ConvertFrom-Json) |
        Where-Object title -like 'Wintodo*' | Select-Object -First 1
    if (-not $window) { throw 'The Wintodo window was not found for rollover.' }
    $handle = if ("$($window.hwnd)" -match '^0x') {
        [IntPtr]::new([Convert]::ToInt64("$($window.hwnd)".Substring(2), 16))
    } else { [IntPtr]::new([long]$window.hwnd) }
    [MyDayGateWindow]::ShowWindowAsync($handle, 6) | Out-Null
    Start-Sleep -Milliseconds 500
    [MyDayGateWindow]::ShowWindowAsync($handle, 9) | Out-Null
    [MyDayGateWindow]::SetForegroundWindow($handle) | Out-Null
    Assert-MyDay $false

    # Changing a due date from the row must refresh the same open detail.
    Ui @('click', $row.automationId, '--right') | Out-Null
    Ui @('wait-for', 'Todo.Row.Selection.Due', '-t', '5000') | Out-Null
    Ui @('invoke', 'Todo.Row.Selection.Due') | Out-Null
    Ui @('invoke', 'Todo.Row.Selection.Due.Today') | Out-Null
    Assert-MyDay $true
    Ui @('wait-for', 'Todo.Detail.Due', '--property', 'Name', '--value', 'Due Today', '-t', '5000') | Out-Null
    Ui @('screenshot', '-o', (Join-Path $artifacts 'due-date-updated.png')) | Out-Null
    Write-Host 'My Day UI gate: PASS (automatic inclusion, dismissal, undo, manual pin, rollover, due date change from row)'
} finally {
    Stop-Process -Id $appPid -ErrorAction SilentlyContinue
}
