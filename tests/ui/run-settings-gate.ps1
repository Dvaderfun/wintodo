param([ValidateSet('local')][string]$Target = 'local')
# Local desktop input requires explicit owner permission for this session.
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1') {
    throw 'Local UI automation is disabled on this main machine. Use an isolated VM/Sandbox, or obtain owner permission for this session before setting WINTODO_ALLOW_LOCAL_UI=1. See docs/guides/testing.md.'
}

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$appDirectory = Join-Path $repositoryRoot 'src/Wintodo.App/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64'
$artifactDirectory = Join-Path $PSScriptRoot ('artifacts/settings/run-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null
$databasePath = Join-Path $artifactDirectory 'settings.db'
$clockPath = Join-Path $artifactDirectory 'clock.txt'
# Friday: Sunday belongs to This week for Monday start, Later for Sunday start.
'2026-10-09T12:00:00+03:00' | Set-Content -LiteralPath $clockPath
$previousWorkflowId = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = [guid]::NewGuid().ToString('N')
$appPid = 0
$mainHwnd = ''
$results = [Collections.Generic.List[object]]::new()
$screenshots = [Collections.Generic.List[string]]::new()

function Invoke-WinAppChecked {
    param([string[]]$Arguments)
    $output = @(& winapp @Arguments)
    if ($LASTEXITCODE -ne 0) { throw "winapp $($Arguments -join ' ') failed: $output" }
    $output
}
function Invoke-Ui {
    param([string[]]$Arguments, [string]$Window)
    $selector = if ($Window) { @('-w', $Window) } else { @('-a', "$appPid") }
    Invoke-WinAppChecked (@('ui') + $Arguments + @('--on', $Target) + $selector)
}
function Start-App {
    $launch = Invoke-WinAppChecked @('run', $appDirectory, '--detach', '--json', '--',
        '--database-path', $databasePath, '--clock-file', $clockPath) | ConvertFrom-Json
    if (-not $launch.ProcessId -or ($launch.ProcessScope -and $launch.ProcessScope -ne $Target)) {
        throw 'Settings gate launch did not return a local app PID.'
    }
    $script:appPid = [int]$launch.ProcessId
    $script:mainHwnd = ''
    Invoke-Ui @('wait-for', 'Todo.Shell.PageTitle', '-t', '10000') | Out-Null
    $script:mainHwnd = [string]((Invoke-Ui @('list-windows', '--json') | ConvertFrom-Json |
        Where-Object title -eq 'Wintodo' | Select-Object -First 1).hwnd)
    if (-not $mainHwnd) { throw 'No app window; cannot pass UI validation.' }
}
function Test-UI {
    param([string]$Name, [scriptblock]$Script)
    try {
        & $Script | Out-Null
        $results.Add(@{ name = $Name; status = 'PASS' })
    } catch {
        $results.Add(@{ name = $Name; status = 'FAIL'; detail = "$_" })
        try {
            Invoke-Ui @('inspect', '--interactive', '--json', '--depth', '16') |
                Set-Content -LiteralPath (Join-Path $artifactDirectory ("failure-" + $results.Count + ".json"))
        } catch { Write-Warning "Could not capture failure state: $_" }
    }
}
function Save-Screenshot {
    param([string]$Name)
    $path = Join-Path $artifactDirectory $Name
    Invoke-WinAppChecked @('ui', 'screenshot', '-w', $mainHwnd, '--on', $Target, '--output', $path) | Out-Null
    if (-not (Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).Length -eq 0) {
        throw "Screenshot was not delivered: $path"
    }
    $screenshots.Add($path)
}
function Get-Nodes {
    param([object[]]$Nodes)
    foreach ($node in $Nodes) { $node; if ($node.children) { Get-Nodes @($node.children) } }
}
function Get-Tree {
    $inspection = Invoke-Ui @('inspect', '--json', '--depth', '16') | ConvertFrom-Json
    $inspection | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath (Join-Path $artifactDirectory 'latest-tree.json')
    @(Get-Nodes @($inspection.windows | ForEach-Object { $_.elements }))
}
function Get-Row {
    param([string]$Name)
    $row = Get-Tree | Where-Object { $_.type -eq 'ListItem' -and
        $_.automationId -match '^Todo\.Row\.' -and $_.name.StartsWith($Name) } | Select-Object -First 1
    if (-not $row) { throw "Missing task row: $Name" }
    $row
}
function Add-Task {
    param([string]$Name)
    Invoke-Ui @('set-value', 'Todo.List.AddTaskBox', $Name) | Out-Null
    Invoke-Ui @('invoke', 'Todo.Composer.AddButton') | Out-Null
    Invoke-Ui @('wait-for', $Name, '-t', '5000') | Out-Null
}
function Open-Settings {
    Invoke-Ui @('invoke', 'Todo.Nav.Settings') | Out-Null
    Invoke-Ui @('wait-for', 'Todo.Settings.Tasks.ConfirmBeforeDelete', '-t', '5000') | Out-Null
    Invoke-Ui @('scroll', 'Todo.Settings.ScrollViewer', '--to', 'top') | Out-Null
    Invoke-Ui @('scroll', 'Todo.Settings.ScrollViewer', '--direction', 'down') | Out-Null
    Start-Sleep -Milliseconds 200
}
function Select-WeekStart {
    param([string]$Name)
    $index = @('Use system setting','Sunday','Monday','Tuesday','Wednesday','Thursday','Friday','Saturday').IndexOf($Name)
    Invoke-Ui @('focus', 'Todo.Settings.Tasks.WeekStart') | Out-Null
    Invoke-Ui @('send-keys', 'home', '--via', 'send-input') | Out-Null
    for ($step = 0; $step -lt $index; $step++) {
        Invoke-Ui @('send-keys', 'down', '--via', 'send-input') | Out-Null
    }
    Invoke-Ui @('send-keys', 'enter', '--via', 'send-input') | Out-Null
    Invoke-Ui @('wait-for', 'Todo.Settings.Tasks.WeekStart', '--value', $Name, '-t', '3000') | Out-Null
}
function Set-Toggle {
    param([string]$Name, [bool]$Enabled)
    $action = if ($Enabled) { 'toggle-on' } else { 'toggle-off' }
    Invoke-Ui @('invoke', "Todo.Settings.Tasks.$Name", '--action', $action) | Out-Null
    Invoke-Ui @('wait-for', "Todo.Settings.Tasks.$Name", '--value', $(if ($Enabled) { 'On' } else { 'Off' }), '-t', '3000') | Out-Null
}
function Assert-RowOrder {
    param([string[]]$Expected)
    $actual = @(Get-Tree | Where-Object { $_.type -eq 'ListItem' -and $_.automationId -match '^Todo\.Row\.' } |
        ForEach-Object { ($_.name -split ',')[0] })
    if (($actual -join '|') -ne ($Expected -join '|')) { throw "Expected $Expected; actual $actual" }
}
try {
    Start-App
    Test-UI 'Task preference defaults' {
        Open-Settings
        foreach ($entry in @(@('ConfirmBeforeDelete','On'), @('AddNewToTop','Off'), @('MoveStarredToTop','Off'))) {
            Invoke-Ui @('wait-for', "Todo.Settings.Tasks.$($entry[0])", '--value', $entry[1], '-t', '3000')
        }
        Invoke-Ui @('wait-for', 'Todo.Settings.Tasks.WeekStart', '--value', 'Use system setting', '-t', '3000')
        Save-Screenshot '01-defaults.png'
    }
    Test-UI 'Set every Tasks preference' {
        Set-Toggle 'ConfirmBeforeDelete' $false
        Set-Toggle 'AddNewToTop' $true
        Set-Toggle 'MoveStarredToTop' $true
        Select-WeekStart 'Monday'
        Invoke-Ui @('invoke', 'FlyoutButton')
        Invoke-Ui @('wait-for', 'HourLoopingSelector', '-t', '3000')
        $pickerWindow = (Invoke-Ui @('list-windows', '--json') | ConvertFrom-Json |
            Where-Object title -eq 'PopupHost' | Select-Object -First 1).hwnd
        if (-not $pickerWindow) { throw 'Reminder time flyout window was not found.' }
        $hourSelector = Get-Tree | Where-Object automationId -eq 'HourLoopingSelector' | Select-Object -First 1
        $tenOClock = $hourSelector.children | Where-Object { $_.type -eq 'ListItem' -and $_.name -eq '10' } |
            Select-Object -First 1
        if (-not $tenOClock) { throw 'Hour 10 was not found in the reminder selector.' }
        Invoke-Ui @('invoke', $tenOClock.selector, '--action', 'select') -Window "$pickerWindow"
        Invoke-Ui @('invoke', 'AcceptButton') -Window "$pickerWindow"
        Invoke-Ui @('wait-for', 'TimePickerFlyoutPresenter', '--gone', '-t', '3000')
        Save-Screenshot '02-preferences.png'
    }
    Test-UI 'Add new and star from row/detail move tasks to top' {
        Invoke-Ui @('invoke', 'Todo.Nav.DefaultList')
        Add-Task 'First task'
        Add-Task 'Second task'
        Add-Task 'Third task'
        Assert-RowOrder @('Third task','Second task','First task')
        $uid = (Get-Row 'First task').automationId.Substring('Todo.Row.'.Length)
        Invoke-Ui @('invoke', "Todo.Row.ImportanceToggle.$uid")
        Assert-RowOrder @('First task','Third task','Second task')
        $row = Get-Row 'Second task'
        Invoke-Ui @('invoke', $row.selector)
        Invoke-Ui @('wait-for', 'Todo.Detail.Title', '-t', '3000')
        Invoke-Ui @('invoke', 'Todo.Detail.Importance')
        Assert-RowOrder @('Second task','First task','Third task')
        Invoke-Ui @('send-keys', 'escape', '--via', 'send-input')
    }
    Test-UI 'F2 renames the selected row without reordering it' {
        $row = Get-Row 'First task'
        $uid = $row.automationId.Substring('Todo.Row.'.Length)
        Invoke-Ui @('invoke', $row.selector)
        Invoke-Ui @('send-keys', 'f2', '--target', $row.selector, '--via', 'send-input')
        $editor = "Todo.Row.TitleEdit.$uid"
        Invoke-Ui @('wait-for', $editor, '-t', '3000')
        Invoke-Ui @('send-keys', 'ctrl+a text=Renamed\sfirst\stask enter', '--target', $editor, '--via', 'send-input')
        Invoke-Ui @('wait-for', $editor, '--gone', '-t', '3000')
        Assert-RowOrder @('Second task','Renamed first task','Third task')
    }
    Test-UI 'Delete with confirmation off is immediate and undo restores' {
        $row = Get-Row 'Third task'
        Invoke-Ui @('invoke', $row.selector)
        Invoke-Ui @('focus', (Get-Row 'Third task').selector)
        Invoke-Ui @('send-keys', 'delete', '--target', (Get-Row 'Third task').automationId, '--via', 'send-input')
        Invoke-Ui @('wait-for', 'Todo.List.UndoButton', '-t', '3000')
        Invoke-Ui @('wait-for', 'Todo.Dialog.DeleteTask', '--gone', '-t', '1000')
        Invoke-Ui @('invoke', 'Todo.List.UndoButton')
        Save-Screenshot '03-delete-restored.png'
        Invoke-Ui @('wait-for', 'Third task', '-t', '3000')
    }
    Test-UI 'Restart preserves all five preferences' {
        Open-Settings
        $script:expectedTime = (Invoke-Ui @('get-property', 'FlyoutButton', '--json') | ConvertFrom-Json).properties.Name
        if (($expectedTime -replace '[^0-9]', '') -ne '1000') { throw "Reminder time did not change to 10:00: $expectedTime" }
        Start-Sleep -Milliseconds 600
        Stop-Process -Id $appPid
        $script:appPid = 0
        Start-App
        Open-Settings
        foreach ($entry in @(@('ConfirmBeforeDelete','Off'), @('AddNewToTop','On'), @('MoveStarredToTop','On'))) {
            Invoke-Ui @('wait-for', "Todo.Settings.Tasks.$($entry[0])", '--value', $entry[1], '-t', '3000')
        }
        Invoke-Ui @('wait-for', 'Todo.Settings.Tasks.WeekStart', '--value', 'Monday', '-t', '3000')
        $restoredTime = (Invoke-Ui @('get-property', 'FlyoutButton', '--json') | ConvertFrom-Json).properties.Name
        if ($restoredTime -ne $expectedTime) { throw "Reminder time was not restored: $restoredTime vs $expectedTime" }
        Save-Screenshot '04-restored.png'
    }
    Test-UI 'Reminder picker uses configured time' {
        Invoke-Ui @('invoke', 'Todo.Nav.DefaultList')
        $row = Get-Row 'Third task'
        Invoke-Ui @('invoke', $row.selector)
        Invoke-Ui @('wait-for', 'Todo.Detail.Title', '-t', '3000')
        Invoke-Ui @('send-keys', 'ctrl+shift+vk=0x52', '--via', 'send-input')
        Invoke-Ui @('wait-for', 'Todo.Detail.ReminderTimePicker', '-t', '3000')
        $detailTime = (Invoke-Ui @('get-property', 'FlyoutButton', '--json') | ConvertFrom-Json).properties.Name
        $expectedDigits = $expectedTime -replace '[^0-9]', ''
        if (-not $expectedDigits -or ($detailTime -replace '[^0-9]', '') -ne $expectedDigits) {
            throw "Reminder picker did not use default: $detailTime vs $expectedTime"
        }
        Save-Screenshot '05-reminder-default.png'
        Invoke-Ui @('send-keys', 'escape', '--via', 'send-input')
        Invoke-Ui @('send-keys', 'escape', '--via', 'send-input')
    }
    Test-UI 'Week start changes Planned grouping and calendar headers' {
        Invoke-Ui @('invoke', 'Todo.Nav.DefaultList')
        Invoke-Ui @('invoke', (Get-Row 'Third task').selector)
        Invoke-Ui @('invoke', 'Todo.Detail.Due')
        Invoke-Ui @('wait-for', 'Todo.Detail.Due.Tomorrow', '-t', '3000')
        Invoke-Ui @('invoke', 'Todo.Detail.Due.Tomorrow')
        Invoke-Ui @('wait-for', 'Todo.Detail.ClearDue', '-t', '3000')
        Invoke-Ui @('send-keys', 'escape', '--via', 'send-input')
        # Saturday's due date is now two days away, beyond the Today/Tomorrow buckets.
        '2026-10-08T12:00:00+03:00' | Set-Content -LiteralPath $clockPath
        Invoke-Ui @('invoke', 'Todo.Nav.Planned')
        Invoke-Ui @('wait-for', 'Todo.List.PlannedGroup.ThisWeek', '-t', '3000')
        Save-Screenshot '06-planned-monday.png'
        Open-Settings
        Select-WeekStart 'Saturday'
        Invoke-Ui @('wait-for', 'Todo.Settings.Tasks.WeekStart', '--value', 'Saturday', '-t', '3000')
        Invoke-Ui @('invoke', 'Todo.Nav.Planned')
        Invoke-Ui @('wait-for', 'Todo.List.PlannedGroup.Later', '-t', '3000')
        Invoke-Ui @('wait-for', 'Todo.List.PlannedGroup.ThisWeek', '--gone', '-t', '1000')
        Save-Screenshot '07-planned-saturday.png'
        Invoke-Ui @('invoke', (Get-Row 'Third task').selector)
        Invoke-Ui @('send-keys', 'ctrl+shift+vk=0x44', '--via', 'send-input')
        Invoke-Ui @('invoke', 'Todo.Detail.DueDatePicker')
        $headers = @(Get-Tree | Where-Object { $_.automationId -match '^WeekDay[1-7]$' } | Sort-Object x)
        if ($headers.Count -ne 7 -or $headers[0].name -notmatch '^Sat') {
            throw "Calendar does not start on Saturday: $(($headers.name) -join ', ')"
        }
        Save-Screenshot '08-calendar-saturday.png'
        Invoke-Ui @('send-keys', 'escape', '--via', 'send-input')
        Invoke-Ui @('send-keys', 'escape', '--via', 'send-input')
        Invoke-Ui @('send-keys', 'escape', '--via', 'send-input')
    }
    Test-UI 'Confirmation on preserves dialog and cancel keeps task' {
        Open-Settings
        Set-Toggle 'ConfirmBeforeDelete' $true
        Invoke-Ui @('invoke', 'Todo.Nav.DefaultList')
        Invoke-Ui @('invoke', (Get-Row 'Third task').selector)
        Invoke-Ui @('invoke', 'Todo.Detail.Delete')
        Invoke-Ui @('wait-for', 'Todo.Dialog.DeleteTask', '-t', '3000')
        Save-Screenshot '09-confirmation.png'
        Invoke-Ui @('invoke', 'Todo.Dialog.DeleteTask.Cancel')
        Get-Row 'Third task'
        Invoke-Ui @('send-keys', 'escape', '--via', 'send-input')
    }
    Test-UI 'Settings accessibility audit has zero errors' {
        Open-Settings
        $inspection = Invoke-Ui @('inspect', '--interactive', '--json', '--depth', '16') | ConvertFrom-Json
        $all = @(Get-Nodes @($inspection.windows | ForEach-Object { $_.elements }))
        if ($all.Count -eq 0) { throw 'No elements to audit.' }
        $controls = @($all | Where-Object {
            $_.type -match 'Button|CheckBox|Edit|ListItem|MenuItem|ComboBox|Spinner' -and
            $_.name -notmatch 'Minimize|Maximize|Close|System|Scroll' -and
            $_.className -notmatch 'PopupHost|TitleBar|ReunionWindowing|ScrollBar' -and
            -not ($_.type -eq 'Button' -and -not $_.name) -and
            -not ($_.type -eq 'MenuItem' -and [string]::IsNullOrWhiteSpace($_.className))
        })
        if ($controls.Count -eq 0) { throw 'No controls audited.' }
        $errors = @($controls | Where-Object { -not $_.automationId })
        if ($errors.Count) { throw "Missing AutomationIds: $(($errors | ForEach-Object { $_.name }) -join ', ')" }
        foreach ($name in @('ConfirmBeforeDelete','DefaultReminderTime','WeekStart','AddNewToTop','MoveStarredToTop')) {
            $properties = Invoke-Ui @('get-property', "Todo.Settings.Tasks.$name", '--json') | ConvertFrom-Json
            if (-not $properties.properties.Name) { throw "Tasks control lacks an accessible name: $name" }
        }
        @{ controls = $controls.Count; errors = 0 } | ConvertTo-Json |
            Set-Content -LiteralPath (Join-Path $artifactDirectory 'accessibility.json')
        Save-Screenshot '10-settings-audit.png'
    }
} catch {
    $results.Add(@{ name = 'Gate setup'; status = 'FAIL'; detail = "$_" })
} finally {
    if ($appPid) { Stop-Process -Id $appPid -ErrorAction SilentlyContinue }
    Invoke-WinAppChecked @('ui','yield','--on',$Target) | Out-Null
    $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflowId
}
$failed = @($results | Where-Object status -eq 'FAIL').Count
@{ passed = $results.Count - $failed; failed = $failed; results = $results.ToArray(); screenshots = $screenshots.ToArray() } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $artifactDirectory 'results.json')
$results | Format-Table -Wrap -AutoSize
Write-Host "Settings gate artifacts: $artifactDirectory"
if ($failed) { throw 'Settings gate failed.' }


