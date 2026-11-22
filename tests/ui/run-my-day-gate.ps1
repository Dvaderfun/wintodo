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
    if ($LASTEXITCODE -ne 0) {
        winapp ui inspect --depth 30 --json -a $appPid | Set-Content (Join-Path $artifacts 'failure-tree.json')
        throw "UI command failed ($Command): $output"
    }
    $output
}

function Add-GateTask([string]$Title) {
    Ui @('click', 'Todo.List.AddTaskBox') | Out-Null
    Ui @('set-value', 'Todo.List.AddTaskBox', $Title) | Out-Null
    Ui @('wait-for', 'Todo.Composer.AddButton', '--property', 'IsEnabled', '--value', 'True', '-t', '5000') | Out-Null
    Ui @('invoke', 'Todo.Composer.AddButton') | Out-Null
    Ui @('wait-for', $Title, '-t', '5000') | Out-Null
    $match = (Ui @('search', $Title, '--json') | ConvertFrom-Json).matches |
        Where-Object { $_.automationId -match '^Todo\.Row\.[0-9a-f-]+$' } | Select-Object -First 1
    if (-not $match) { throw "Cannot find the new task row: $Title" }
    $match.automationId
}

function Select-GateRow([string]$Id) {
    $items = @((Ui @('search', $Id, '--json') | ConvertFrom-Json).matches |
        Where-Object { $_.automationId -eq $Id -and $_.type -eq 'ListItem' })
    if ($items.Count -ne 1) { throw "Expected one list item for $Id." }
    Ui @('invoke', $items[0].selector, '--action', 'select') | Out-Null
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
    $row = Add-GateTask 'My Day gate task'
    Ui @('invoke', $row) | Out-Null
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
    Ui @('click', $row, '--right') | Out-Null
    Ui @('wait-for', 'Todo.Row.Selection.Due', '-t', '5000') | Out-Null
    Ui @('invoke', 'Todo.Row.Selection.Due') | Out-Null
    Ui @('invoke', 'Todo.Row.Selection.Due.Today') | Out-Null
    Assert-MyDay $true
    Ui @('wait-for', 'Todo.Detail.Due', '--property', 'Name', '--value', 'Due Today', '-t', '5000') | Out-Null
    Ui @('screenshot', '-o', (Join-Path $artifacts 'due-date-updated.png')) | Out-Null
    Ui @('invoke', 'Todo.Nav.Planned') | Out-Null
    Ui @('wait-for', 'Todo.List.PlannedGroup.Today', '-t', '5000') | Out-Null
    Ui @('wait-for', $row, '--property', 'Name', '--contains', '--value', 'Today', '-t', '5000') | Out-Null
    Ui @('invoke', $row) | Out-Null
    # The same row click must remove an automatic occurrence and retain a pinned one.
    Ui @('invoke', 'Todo.Detail.Repeat') | Out-Null
    Ui @('wait-for', 'Todo.Detail.RepeatOption.Daily', '-t', '3000') | Out-Null
    Ui @('invoke', 'Todo.Detail.RepeatOption.Daily') | Out-Null
    Ui @('send-keys', 'escape', '--via', 'send-input') | Out-Null
    Ui @('wait-for', 'Todo.Detail.Repeat', '--property', 'Name', '--value', 'Repeats daily', '-t', '5000') | Out-Null
    Ui @('invoke', 'Todo.Nav.MyDay') | Out-Null
    Ui @('wait-for', $row, '-t', '5000') | Out-Null
    Ui @('invoke', $row) | Out-Null
    $complete = $row.Replace('Todo.Row.', 'Todo.Row.CompleteToggle.')
    Ui @('invoke', $complete, '--action', 'toggle-on') | Out-Null
    Ui @('wait-for', $row, '--gone', '-t', '5000') | Out-Null
    Ui @('wait-for', 'Todo.Nav.MyDay', '--property', 'Name', '--value', 'My Day', '-t', '5000') | Out-Null
    Ui @('focus', 'Todo.List.MenuButton') | Out-Null
    Ui @('send-keys', 'ctrl+vk=0x5A', '--via', 'send-input') | Out-Null
    Ui @('wait-for', $row, '-t', '5000') | Out-Null
    Ui @('invoke', 'Todo.Nav.DefaultList') | Out-Null
    Ui @('wait-for', $row, '-t', '5000') | Out-Null
    Ui @('invoke', $row) | Out-Null
    Ui @('wait-for', 'Todo.Detail.Due', '--property', 'Name', '--value', 'Due Today', '-t', '5000') | Out-Null
    Ui @('invoke', 'Todo.Detail.MyDay') | Out-Null
    Assert-MyDay $false
    Ui @('invoke', 'Todo.Detail.MyDay') | Out-Null
    Assert-MyDay $true
    Ui @('invoke', 'Todo.Nav.MyDay') | Out-Null
    Ui @('wait-for', $row, '-t', '5000') | Out-Null
    Ui @('invoke', $row) | Out-Null
    Ui @('invoke', $complete, '--action', 'toggle-on') | Out-Null
    Ui @('wait-for', $row, '--property', 'Name', '--contains', '--value', 'Tomorrow', '-t', '5000') | Out-Null
    Ui @('wait-for', 'Todo.Detail.Due', '--property', 'Name', '--value', 'Due Tomorrow', '-t', '5000') | Out-Null
    Ui @('wait-for', 'Todo.Nav.MyDay', '--property', 'Name', '--value', 'My Day, 1 tasks', '-t', '5000') | Out-Null

    # A pending BEGIN can hold the shared-cache mutex for a native 5 s busy wait.
    # Readers must finish during a retry gap, before we release the fixture writer.
    $lockedReadTimeout = '15000'
    Ui @('invoke', 'Todo.Nav.DefaultList') | Out-Null
    $otherTitle = 'Second gate task'
    $otherRow = Add-GateTask $otherTitle
    Select-GateRow $otherRow
    Ui @('wait-for', 'Todo.Detail.Title', '--value', $otherTitle, '--contains', '-t', '5000') | Out-Null
    Select-GateRow $row
    Ui @('wait-for', 'Todo.Detail.Title', '--value', 'My Day gate task', '--contains', '-t', '5000') | Out-Null
    Assert-MyDay $true
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class MyDayGateWriteLock
{
    [DllImport("winsqlite3", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int sqlite3_open16(string path, out IntPtr db);
    [DllImport("winsqlite3", ExactSpelling = true)]
    private static extern int sqlite3_exec(IntPtr db, string sql, IntPtr callback, IntPtr state, IntPtr error);
    [DllImport("winsqlite3", ExactSpelling = true)]
    public static extern int sqlite3_close(IntPtr db);
    public static IntPtr Acquire(string path)
    {
        IntPtr db;
        int rc = sqlite3_open16(path, out db);
        if (rc == 0) rc = sqlite3_exec(db, "PRAGMA busy_timeout=3000; BEGIN IMMEDIATE", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (rc == 0) return db;
        sqlite3_close(db);
        throw new InvalidOperationException("Cannot hold fixture writer: SQLite " + rc);
    }
}
'@
    $writer = [MyDayGateWriteLock]::Acquire($database)
    try {
        Ui @('invoke', 'Todo.Detail.MyDay') | Out-Null
        Select-GateRow $otherRow
        Ui @('wait-for', 'Todo.Detail.Title', '--value', $otherTitle, '--contains', '-t', $lockedReadTimeout) | Out-Null
        Ui @('wait-for', 'Todo.Nav.MyDay', '--property', 'Name', '--value', 'My Day, 1 tasks', '-t', '3000') | Out-Null
    } finally {
        if ([MyDayGateWriteLock]::sqlite3_close($writer) -ne 0) { throw 'Cannot release fixture writer.' }
    }
    Assert-MyDay $false
    Ui @('wait-for', 'Todo.Detail.Title', '--value', $otherTitle, '--contains', '-t', '5000') | Out-Null

    # Navigate to an empty My Day before the saved pin becomes visible to readers.
    Select-GateRow $row
    Ui @('wait-for', 'Todo.Detail.Title', '--value', 'My Day gate task', '--contains', '-t', '5000') | Out-Null
    $writer = [MyDayGateWriteLock]::Acquire($database)
    try {
        Ui @('invoke', 'Todo.Detail.MyDay') | Out-Null
        Ui @('invoke', 'Todo.Nav.MyDay') | Out-Null
        Ui @('wait-for', 'Focus on your day', '-t', $lockedReadTimeout) | Out-Null
        Ui @('wait-for', $row, '--gone', '-t', '3000') | Out-Null
    } finally {
        if ([MyDayGateWriteLock]::sqlite3_close($writer) -ne 0) { throw 'Cannot release fixture writer.' }
    }
    Ui @('wait-for', $row, '-t', '5000') | Out-Null
    Ui @('wait-for', 'Todo.Nav.MyDay', '--property', 'Name', '--value', 'My Day, 1 tasks', '-t', '5000') | Out-Null
    Ui @('invoke', $row) | Out-Null
    Ui @('wait-for', 'Todo.Detail.Due', '--property', 'Name', '--value', 'Due Tomorrow', '-t', '5000') | Out-Null
    Ui @('invoke', 'Todo.Nav.DefaultList') | Out-Null
    Ui @('wait-for', $otherRow, '-t', '5000') | Out-Null
    Ui @('invoke', $otherRow) | Out-Null
    $otherComplete = $otherRow.Replace('Todo.Row.', 'Todo.Row.CompleteToggle.')
    Ui @('invoke', $otherComplete, '--action', 'toggle-on') | Out-Null
    Ui @('wait-for', 'Todo.Detail.Complete', '--value', 'On', '-t', '5000') | Out-Null
    Ui @('invoke', $otherComplete, '--action', 'toggle-off') | Out-Null
    Ui @('wait-for', 'Todo.Detail.Complete', '--value', 'Off', '-t', '5000') | Out-Null
    Write-Host 'My Day UI gate: PASS (inclusion, dismissal, undo, pin, rollover, row due date, recurring completion, switch during save)'
} finally {
    Stop-Process -Id $appPid -ErrorAction SilentlyContinue
}
