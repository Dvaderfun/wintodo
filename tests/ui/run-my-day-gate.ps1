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
$trace = Join-Path $artifacts 'my-day.trace'
function Set-TestClock([DateTime]$LocalTime) {
    $utc = [TimeZoneInfo]::ConvertTimeToUtc($LocalTime)
    $staged = "$clock.next"
    [IO.File]::WriteAllText($staged, $utc.ToString('O'))
    [IO.File]::Move($staged, $clock, $true)
}
Set-TestClock ([DateTime]::new(2026, 11, 20, 18, 0, 0))
$launch = winapp run $app --detach --json -- --database-path $database --clock-file $clock --trace-file $trace | ConvertFrom-Json
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

function Wait-TimerState([string]$Marker) {
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        if ((Test-Path -LiteralPath $trace) -and
            (Select-String -LiteralPath $trace -SimpleMatch "WINTODO_SHELL_TIMERS=$Marker" -Quiet)) { return }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Missing timer transition: $Marker"
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

    # Let a day pass while minimized, then restore without activation. A correct
    # detail, count and row must not depend on another window taking or losing focus.
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class MyDayGateWindow
{
    [DllImport("user32.dll")]
    public static extern bool ShowWindowAsync(IntPtr hwnd, int command);
    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();
}
'@
    $window = (Ui @('list-windows', '--json') | ConvertFrom-Json) |
        Where-Object title -like 'Wintodo*' | Select-Object -First 1
    if (-not $window) { throw 'The Wintodo window was not found for rollover.' }
    $handle = if ("$($window.hwnd)" -match '^0x') {
        [IntPtr]::new([Convert]::ToInt64("$($window.hwnd)".Substring(2), 16))
    } else { [IntPtr]::new([long]$window.hwnd) }
    [MyDayGateWindow]::ShowWindowAsync($handle, 6) | Out-Null
    Wait-TimerState 'minimize;SYNC=absent;MIDNIGHT=off'
    Set-TestClock ([DateTime]::new(2026, 11, 21, 0, 1, 0))
    [MyDayGateWindow]::ShowWindowAsync($handle, 4) | Out-Null # SW_SHOWNOACTIVATE
    Wait-TimerState 'restore;SYNC=absent;MIDNIGHT=running'
    if ([MyDayGateWindow]::GetForegroundWindow() -eq $handle) { throw 'Rollover restore unexpectedly activated the window.' }
    Assert-MyDay $false
    if ([MyDayGateWindow]::GetForegroundWindow() -eq $handle) { throw 'The window gained focus before rollover was verified.' }

    # Changing a due date from the row must refresh the same open detail.
    # winapp focus activates the target window and verifies foreground before physical input.
    Ui @('focus') | Out-Null
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
    # Selecting the task can rebind its row. Wait for that selection, then click
    # once: a TogglePattern call can retain the recycled checkbox peer.
    Ui @('wait-for', 'Todo.Detail.Title', '--value', $otherTitle, '-t', '5000') | Out-Null
    Ui @('wait-for', 'Todo.Detail.Complete', '--value', 'Off', '-t', '5000') | Out-Null
    Ui @('click', $otherComplete) | Out-Null
    Ui @('wait-for', 'Todo.Detail.Complete', '--value', 'On', '-t', '5000') | Out-Null
    # A completed row may move under a collapsed group. The same task stays in detail.
    Ui @('click', 'Todo.Detail.Complete') | Out-Null
    Ui @('wait-for', 'Todo.Detail.Complete', '--value', 'Off', '-t', '5000') | Out-Null
    Ui @('wait-for', $otherRow, '--property', 'Name', '--value', 'not completed', '--contains', '-t', '5000') | Out-Null

    Ui @('wait-for', 'Todo.Suggestions.Open', '--gone', '-t', '5000') | Out-Null
    $overdueRow = Add-GateTask 'Overdue suggestion gate task'
    Ui @('invoke', $overdueRow) | Out-Null
    Ui @('invoke', 'Todo.Detail.Due') | Out-Null
    Ui @('invoke', 'Todo.Detail.Due.Today') | Out-Null
    Ui @('wait-for', 'Todo.Detail.Due', '--property', 'Name', '--value', 'Due Today', '-t', '5000') | Out-Null
    Set-TestClock ([DateTime]::new(2026, 11, 22, 12, 0, 0))
    $futureRow = Add-GateTask 'Upcoming suggestion gate task'
    Ui @('invoke', $futureRow) | Out-Null
    Ui @('invoke', 'Todo.Detail.Due') | Out-Null
    Ui @('invoke', 'Todo.Detail.Due.Tomorrow') | Out-Null
    Ui @('wait-for', 'Todo.Detail.Due', '--property', 'Name', '--value', 'Due Tomorrow', '-t', '5000') | Out-Null
    $recentRow = Add-GateTask 'Recently added suggestion gate task'
    Ui @('invoke', 'Todo.Nav.MyDay') | Out-Null
    Ui @('invoke', 'Todo.Suggestions.Open') | Out-Null
    Ui @('wait-for', 'Todo.Suggestions.Close', '-t', '5000') | Out-Null
    $addOverdue = $overdueRow.Replace('Todo.Row.', 'Todo.Suggestions.Add.')
    $completeFuture = $futureRow.Replace('Todo.Row.', 'Todo.Suggestions.Complete.')
    $addRecent = $recentRow.Replace('Todo.Row.', 'Todo.Suggestions.Add.')
    Ui @('wait-for', $addOverdue, '-t', '5000') | Out-Null
    Ui @('wait-for', $completeFuture, '-t', '5000') | Out-Null
    Ui @('wait-for', $addRecent, '-t', '5000') | Out-Null
    $suggestionTree = (Ui @('inspect', '--depth', '30', '--json')) -join "`n"
    $overdueIndex = $suggestionTree.IndexOf($addOverdue, [StringComparison]::Ordinal)
    $futureIndex = $suggestionTree.IndexOf($completeFuture, [StringComparison]::Ordinal)
    $recentIndex = $suggestionTree.IndexOf($addRecent, [StringComparison]::Ordinal)
    if ($overdueIndex -lt 0 -or $futureIndex -le $overdueIndex -or $recentIndex -le $futureIndex) {
        throw 'Expected earlier, future, and recently added suggestions in that order.'
    }
    foreach ($heading in @('Earlier', 'Later', 'Recently added')) {
        if (-not $suggestionTree.Contains('"' + $heading + '"')) {
            throw "Expected suggestion heading '$heading'."
        }
    }
    Ui @('screenshot', '-o', (Join-Path $artifacts 'suggestions.png')) | Out-Null
    Ui @('invoke', $addOverdue) | Out-Null
    Ui @('wait-for', $addOverdue, '--gone', '-t', '5000') | Out-Null
    Ui @('invoke', $completeFuture) | Out-Null
    Ui @('wait-for', $completeFuture, '--gone', '-t', '5000') | Out-Null
    Ui @('send-keys', 'escape', '--via', 'send-input') | Out-Null
    Ui @('wait-for', 'Todo.Suggestions.Close', '--gone', '-t', '5000') | Out-Null
    Ui @('wait-for', $overdueRow, '-t', '5000') | Out-Null
    Ui @('invoke', 'Todo.Nav.DefaultList') | Out-Null
    Ui @('wait-for', $overdueRow, '-t', '5000') | Out-Null

    . (Join-Path $PSScriptRoot 'SuggestionsFixtureSql.ps1')
    $repeatRow = Add-GateTask 'Daily suggestion fixture'
    $seed = "UPDATE tasks SET due_utc='2026-11-23', due_is_date_only=1, rrule='FREQ=DAILY', sequence=sequence+1 WHERE summary='Daily suggestion fixture'; SELECT changes();"
    if ((Invoke-SuggestionsFixtureSql $database $seed) -ne '1') { throw 'Could not seed the daily occurrence.' }
    Ui @('invoke', 'Todo.Nav.MyDay') | Out-Null
    Ui @('invoke', 'Todo.Suggestions.Open') | Out-Null
    $completeRepeat = $repeatRow.Replace('Todo.Row.', 'Todo.Suggestions.Complete.')
    Ui @('wait-for', $completeRepeat, '-t', '5000') | Out-Null
    # Another writer reschedules the task while this exact row is displayed. Both occurrences are eligible.
    $advance = "UPDATE tasks SET due_utc='2026-11-24', sequence=sequence+1 WHERE summary='Daily suggestion fixture' AND due_utc='2026-11-23'; SELECT changes();"
    if ((Invoke-SuggestionsFixtureSql $database $advance) -ne '1') { throw 'Could not advance the test occurrence.' }
    Ui @('invoke', $completeRepeat) | Out-Null
    Ui @('wait-for', 'Todo.Suggestions.Status', '--property', 'Name', '--contains', '--value', 'try again', '-t', '5000') | Out-Null
    $occurrenceState = "SELECT t.due_utc || '|' || (SELECT count(*) FROM completion_history h WHERE h.task_uid=t.uid) FROM tasks t WHERE t.summary='Daily suggestion fixture';"
    if ((Invoke-SuggestionsFixtureSql $database $occurrenceState) -ne '2026-11-24|0') { throw 'The stale action completed the replacement occurrence.' }
    # An intentional click on the refreshed row must still work.
    Ui @('wait-for', $completeRepeat, '--property', 'IsEnabled', '--value', 'True', '-t', '5000') | Out-Null
    Ui @('invoke', $completeRepeat) | Out-Null
    $retryDeadline = [DateTime]::UtcNow.AddSeconds(5)
    while ((Invoke-SuggestionsFixtureSql $database $occurrenceState) -ne '2026-11-25|1') {
        if ([DateTime]::UtcNow -ge $retryDeadline) { throw 'The refreshed occurrence did not advance exactly once.' }
        Start-Sleep -Milliseconds 100
    }
    Ui @('wait-for', $completeRepeat, '--property', 'IsEnabled', '--value', 'True', '-t', '5000') | Out-Null

    # Let the next write commit, then fail its detail read without breaking the list query.
    $failRead = @'
CREATE TRIGGER fail_suggestion_read AFTER UPDATE OF due_utc ON tasks
WHEN NEW.summary='Daily suggestion fixture' AND NEW.due_utc='2026-11-26'
BEGIN
    INSERT INTO attachments(task_id,file_name,relative_path,size_bytes,added_utc)
    VALUES(NEW.id,'read-fixture.txt','suggestions-read-fixture.txt',0,'invalid-fixture-date');
END;
'@
    Invoke-SuggestionsFixtureSql $database $failRead | Out-Null
    try {
        Ui @('invoke', $completeRepeat) | Out-Null
        Ui @('wait-for', 'Todo.Suggestions.Status', '--property', 'Name', '--contains', '--value', 'Your change was saved', '-t', '5000') | Out-Null
        if ((Invoke-SuggestionsFixtureSql $database $occurrenceState) -ne '2026-11-26|2') { throw 'The saved action did not advance exactly one occurrence.' }
    } finally {
        Invoke-SuggestionsFixtureSql $database "DROP TRIGGER fail_suggestion_read; DELETE FROM attachments WHERE relative_path='suggestions-read-fixture.txt';" | Out-Null
    }
    Ui @('send-keys', 'ctrl+vk=0x5A', '--via', 'send-input') | Out-Null
    $undoDeadline = [DateTime]::UtcNow.AddSeconds(5)
    while ((Invoke-SuggestionsFixtureSql $database $occurrenceState) -ne '2026-11-25|1') {
        if ([DateTime]::UtcNow -ge $undoDeadline) { throw 'Undo did not restore the prior occurrence and its completion history after the failed refresh.' }
        Start-Sleep -Milliseconds 100
    }
    Ui @('invoke', 'Todo.Suggestions.Close') | Out-Null

    # Reject only this synthetic text write. A failed flush must leave the editor, draft and error visible.
    $switchTarget = Add-GateTask 'Switch target during rejected draft'
    Ui @('invoke', $overdueRow) | Out-Null
    Ui @('wait-for', 'Todo.Detail.Note', '-t', '5000') | Out-Null
    $reject = "CREATE TRIGGER reject_suggestion_draft BEFORE UPDATE OF description ON tasks WHEN NEW.description='Suggestions recovery fixture' BEGIN SELECT RAISE(ABORT, 'fixture write failure'); END;"
    Invoke-SuggestionsFixtureSql $database $reject | Out-Null
    try {
        Ui @('set-value', 'Todo.Detail.Note', 'Suggestions recovery fixture') | Out-Null
        Select-GateRow $switchTarget
        Ui @('wait-for', 'Todo.Detail.Error', '-t', '5000') | Out-Null
        Ui @('wait-for', 'Todo.Detail.Title', '--value', 'Overdue suggestion gate task', '-t', '5000') | Out-Null
        Ui @('wait-for', 'Todo.Detail.Note', '--value', 'Suggestions recovery fixture', '-t', '5000') | Out-Null
        Ui @('invoke', 'Todo.Suggestions.Open') | Out-Null
        Ui @('wait-for', 'Todo.Detail.Error', '-t', '5000') | Out-Null
        Ui @('wait-for', 'Todo.Detail.Note', '--value', 'Suggestions recovery fixture', '-t', '5000') | Out-Null
        Ui @('wait-for', 'Todo.Suggestions.Close', '--gone', '-t', '3000') | Out-Null
        if ((Invoke-SuggestionsFixtureSql $database "SELECT count(*) FROM tasks WHERE description='Suggestions recovery fixture';") -ne '0') { throw 'Expected a failed write.' }
    } finally {
        Invoke-SuggestionsFixtureSql $database 'DROP TRIGGER reject_suggestion_draft;' | Out-Null
    }
    Ui @('invoke', 'Todo.Suggestions.Open') | Out-Null
    Ui @('wait-for', 'Todo.Suggestions.Close', '-t', '5000') | Out-Null
    if ((Invoke-SuggestionsFixtureSql $database "SELECT count(*) FROM tasks WHERE summary='Overdue suggestion gate task' AND description='Suggestions recovery fixture';") -ne '1') { throw 'Retry did not persist the draft to its original task.' }
    # A detail-only read error must not reveal the last task's note or accept an edit for it.
    Ui @('invoke', 'Todo.Suggestions.Close') | Out-Null
    $invalidDetail = "INSERT INTO attachments(task_id,file_name,relative_path,size_bytes,added_utc) SELECT id,'retry.txt','detail-retry-fixture.txt',0,'invalid-fixture-date' FROM tasks WHERE summary='Switch target during rejected draft'; SELECT changes();"
    if ((Invoke-SuggestionsFixtureSql $database $invalidDetail) -ne '1') { throw 'Expected one retry fixture.' }
    try {
        Select-GateRow $switchTarget
        Ui @('wait-for', 'Todo.Detail.PreviewTitle', '--value', 'Switch target during rejected draft', '-t', '5000') | Out-Null
        Ui @('wait-for', 'Todo.Detail.RetryOpen', '-t', '5000') | Out-Null
        Ui @('wait-for', 'Todo.Detail.Note', '--gone', '-t', '5000') | Out-Null
    } finally {
        Invoke-SuggestionsFixtureSql $database "DELETE FROM attachments WHERE relative_path='detail-retry-fixture.txt';" | Out-Null
    }
    Ui @('invoke', 'Todo.Detail.RetryOpen') | Out-Null
    Ui @('wait-for', 'Todo.Detail.Title', '--value', 'Switch target during rejected draft', '-t', '5000') | Out-Null
    Write-Host 'My Day UI gate: PASS (membership, undo, rollover, recurrence, pending writes, Suggestions, stale occurrence, saved action with failed refresh, failed-save recovery, detail retry)'
} finally {
    Stop-Process -Id $appPid -ErrorAction SilentlyContinue
}
