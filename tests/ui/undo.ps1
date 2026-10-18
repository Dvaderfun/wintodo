param([Parameter(Mandatory)][int]$AppPid, [Parameter(Mandatory)][string]$ArtifactDirectory)
# Local desktop input requires explicit owner permission for this session.
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1') {
    throw 'Local UI automation is disabled on this main machine. Use an isolated VM/Sandbox, or obtain owner permission for this session before setting WINTODO_ALLOW_LOCAL_UI=1. See docs/guides/testing.md.'
}

$ErrorActionPreference = 'Stop'
$results = [Collections.Generic.List[object]]::new()
$previousWorkflow = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = [guid]::NewGuid().ToString('N')
function Ui([string[]]$Arguments) {
    $output = winapp ui @Arguments -a $AppPid
    if ($LASTEXITCODE -ne 0) { throw "UI command failed: $($Arguments -join ' ')" }
    $output
}
function Check([string]$Name, [scriptblock]$Action) {
    try { & $Action | Out-Null; $results.Add(@{name=$Name;status='PASS'}); Write-Host "PASS: $Name" }
    catch { $results.Add(@{name=$Name;status='FAIL';detail="$_"}); Write-Host "FAIL: $Name — $_" }
}
function Match([string]$Text, [string]$Type) {
    @((Ui @('search', $Text, '--json') | ConvertFrom-Json).matches | Where-Object type -eq $Type) | Select-Object -First 1
}
function Row([string]$Title) { Match $Title 'ListItem' }
function OpenTask([string]$Title) {
    $row = Row $Title
    if (-not $row) { throw "Task missing: $Title" }
    Ui @('invoke', $row.automationId) | Out-Null
    Ui @('wait-for', 'Todo.Detail.Title', '--value', $Title, '-t', '5000') | Out-Null
}
function Undo([switch]$Keyboard) {
    if ($Keyboard) {
        Ui @('focus', 'Todo.List.MenuButton') | Out-Null
        Ui @('send-keys', 'ctrl+vk=0x5A', '--via', 'send-input') | Out-Null
    } else {
        # Invoke itself checks that the visible button exists. A separate CLI
        # wait can consume the five-second lifetime before a second CLI starts.
        Ui @('invoke', 'Todo.List.UndoButton') | Out-Null
    }
    Start-Sleep -Milliseconds 700
}
function SelectTwo {
    $first = (Row 'Bulk task 05').automationId
    $second = (Row 'Bulk task 06').automationId
    $path = Join-Path $ArtifactDirectory 'select-two.ps1'
    @'
param([int]$AppPid, [string]$First, [string]$Second)
Add-Type -AssemblyName UIAutomationClient
$app = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
    [System.Windows.Automation.TreeScope]::Children,
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $AppPid))
foreach ($id in @($First, $Second)) {
    $row = $app.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id))
    $pattern = [System.Windows.Automation.SelectionItemPattern]$row.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if ($id -eq $First) { $pattern.Select() } else { $pattern.AddToSelection() }
}
'@ | Set-Content $path
    powershell.exe -NoProfile -File $path -AppPid $AppPid -First $first -Second $second
    if ($LASTEXITCODE -ne 0) { throw 'Multi-selection setup failed.' }
    Ui @('focus', (Row 'Bulk task 05').selector) | Out-Null
}
try {
    Ui @('wait-for', 'Todo.List.Tasks', '-t', '10000') | Out-Null
    Check 'Deleted tasks undo after navigation and snackbar expiry; older history remains' {
        OpenTask 'Bulk task 01'
        $deletedRow = (Row 'Bulk task 01').automationId
        Ui @('invoke', 'Todo.Detail.Delete') | Out-Null
        Ui @('wait-for', $deletedRow, '--gone', '-t', '5000') | Out-Null
        OpenTask 'Bulk task 02'
        Ui @('invoke', 'Todo.Detail.Delete') | Out-Null
        $target = Match 'Multiselect target' 'ListItem'
        Ui @('invoke', $target.automationId) | Out-Null
        Ui @('wait-for', 'Todo.List.UndoButton', '--gone', '-t', '6500') | Out-Null
        Undo -Keyboard
        Ui @('invoke', (Match 'Multiselect source' 'ListItem').automationId) | Out-Null
        Ui @('wait-for', 'Bulk task 02', '-t', '5000') | Out-Null
        Ui @('wait-for', 'Bulk task 01', '--gone', '-t', '3000') | Out-Null
        # This case verifies retained history after expiry; slow CLI calls can
        # consume the newly shown snackbar too. Use Ctrl+Z for the older entry.
        Undo -Keyboard
        Ui @('wait-for', 'Bulk task 01', '-t', '5000') | Out-Null
    }
    Check 'Due clear restores date and keeps a newer title' {
        OpenTask 'Bulk task 03'
        Ui @('invoke', 'Todo.Detail.Due') | Out-Null
        Ui @('invoke', 'Todo.Detail.Due.Tomorrow') | Out-Null
        Ui @('wait-for', 'Todo.Detail.ClearDue', '-t', '3000') | Out-Null
        Ui @('invoke', 'Todo.Detail.ClearDue') | Out-Null
        Ui @('wait-for', 'Todo.Detail.Due', '--value', 'Add due date', '-t', '3000') | Out-Null
        Ui @('set-value', 'Todo.Detail.Title', 'Newer undo title') | Out-Null
        Undo
        Ui @('wait-for', 'Todo.Detail.Title', '--value', 'Newer undo title', '-t', '3000') | Out-Null
        Ui @('wait-for', 'Todo.Detail.ClearDue', '-t', '3000') | Out-Null
    }
    Check 'Reminder and repeat clears undo by keyboard and snackbar' {
        Ui @('invoke', 'Todo.Detail.Reminder') | Out-Null
        Ui @('invoke', 'Todo.Detail.Reminder.Tomorrow') | Out-Null
        Ui @('wait-for', 'Todo.Detail.ClearReminder', '-t', '3000') | Out-Null
        Ui @('invoke', 'Todo.Detail.ClearReminder') | Out-Null
        Undo -Keyboard
        Ui @('wait-for', 'Todo.Detail.ClearReminder', '-t', '3000') | Out-Null
        Ui @('invoke', 'Todo.Detail.Repeat') | Out-Null
        Ui @('wait-for', 'Todo.Detail.RepeatOptions', '-t', '3000') | Out-Null
        Ui @('invoke', 'Daily') | Out-Null
        Ui @('send-keys', 'esc', '--via', 'send-input') | Out-Null
        Ui @('wait-for', 'Todo.Detail.ClearRepeat', '-t', '3000') | Out-Null
        Ui @('invoke', 'Todo.Detail.ClearRepeat') | Out-Null
        Undo
        Ui @('wait-for', 'Todo.Detail.ClearRepeat', '-t', '3000') | Out-Null
    }
    Check 'Deleted step returns with the detail pane open' {
        Ui @('set-value', 'Todo.Detail.NewStep', 'Undo step') | Out-Null
        Ui @('send-keys', 'enter', '--target', 'Todo.Detail.NewStep', '--via', 'send-input') | Out-Null
        Ui @('wait-for', 'Undo step', '-t', '3000') | Out-Null
        $button = Match 'Delete step Undo step' 'Button'
        if (-not $button) { $button = (Ui @('search', 'Todo.Detail.StepDelete.', '--json') | ConvertFrom-Json).matches | Select-Object -First 1 }
        Ui @('invoke', $button.automationId) | Out-Null
        Undo
        Ui @('wait-for', 'Undo step', '-t', '3000') | Out-Null
    }
    Check 'Move to another list is undone from that list' {
        OpenTask 'Bulk task 04'
        $row = Row 'Bulk task 04'
        Ui @('click', $row.automationId, '--right') | Out-Null
        Ui @('wait-for', 'Todo.Row.Selection.Move', '-t', '3000') | Out-Null
        Ui @('invoke', 'Todo.Row.Selection.Move') | Out-Null
        Ui @('invoke', (Match 'Multiselect target' 'MenuItem').automationId) | Out-Null
        Ui @('invoke', (Match 'Multiselect target' 'ListItem').automationId) | Out-Null
        Ui @('wait-for', 'Bulk task 04', '-t', '3000') | Out-Null
        Undo -Keyboard
        Ui @('wait-for', 'Bulk task 04', '--gone', '-t', '3000') | Out-Null
        Ui @('invoke', (Match 'Multiselect source' 'ListItem').automationId) | Out-Null
        Ui @('wait-for', 'Bulk task 04', '-t', '3000') | Out-Null
    }
    Check 'Bulk importance, My Day, due and completion each undo as one action' {
        SelectTwo
        Ui @('send-keys', 'ctrl+vk=0x49', '--via', 'send-input') | Out-Null
        Start-Sleep -Milliseconds 400
        foreach ($title in 'Bulk task 05','Bulk task 06') {
            $uid = (Row $title).automationId.Substring('Todo.Row.'.Length)
            Ui @('wait-for', "Todo.Row.ImportanceToggle.$uid", '--value', 'On', '-t', '3000') | Out-Null
        }
        Undo
        foreach ($title in 'Bulk task 05','Bulk task 06') {
            $uid = (Row $title).automationId.Substring('Todo.Row.'.Length)
            Ui @('wait-for', "Todo.Row.ImportanceToggle.$uid", '--value', 'Off', '-t', '3000') | Out-Null
        }
        SelectTwo
        Ui @('send-keys', 'ctrl+vk=0x54', '--via', 'send-input') | Out-Null
        Start-Sleep -Milliseconds 500
        foreach ($title in 'Bulk task 05','Bulk task 06') {
            OpenTask $title
            Ui @('wait-for', 'Todo.Detail.ClearMyDay', '-t', '3000') | Out-Null
        }
        Undo -Keyboard
        foreach ($title in 'Bulk task 05','Bulk task 06') {
            OpenTask $title
            Ui @('wait-for', 'Todo.Detail.MyDay', '--value', 'Add to My Day', '-t', '3000') | Out-Null
        }
        SelectTwo
        Ui @('click', (Row 'Bulk task 05').automationId, '--right') | Out-Null
        Ui @('invoke', 'Todo.Row.Selection.Due') | Out-Null
        Ui @('invoke', 'Todo.Row.Selection.Due.Today') | Out-Null
        Start-Sleep -Milliseconds 500
        foreach ($title in 'Bulk task 05','Bulk task 06') { if ((Row $title).name -notlike '*Today*') { throw 'Due date missing.' } }
        Undo
        foreach ($title in 'Bulk task 05','Bulk task 06') { if ((Row $title).name -like '*Today*') { throw 'Due date retained after undo.' } }
        SelectTwo
        Ui @('send-keys', 'space', '--via', 'send-input') | Out-Null
        Start-Sleep -Milliseconds 700
        foreach ($title in 'Bulk task 05','Bulk task 06') {
            $uid = (Row $title).automationId.Substring('Todo.Row.'.Length)
            Ui @('wait-for', "Todo.Row.CompleteToggle.$uid", '--value', 'On', '-t', '3000') | Out-Null
        }
        Undo -Keyboard
        foreach ($title in 'Bulk task 05','Bulk task 06') {
            $uid = (Row $title).automationId.Substring('Todo.Row.'.Length)
            Ui @('wait-for', "Todo.Row.CompleteToggle.$uid", '--value', 'Off', '-t', '3000') | Out-Null
        }
    }
    Check 'List deletion undo restores navigation and tasks together' {
        OpenTask 'Bulk task 04'
        $navId = (Match 'Multiselect source' 'ListItem').automationId
        Ui @('invoke', 'Todo.List.MenuButton') | Out-Null
        Ui @('invoke', 'Todo.List.Menu.Delete') | Out-Null
        Ui @('wait-for', 'Todo.Dialog.DeleteList', '-t', '3000') | Out-Null
        Ui @('invoke', 'Todo.Dialog.DeleteList.Confirm') | Out-Null
        Ui @('wait-for', $navId, '--gone', '-t', '3000') | Out-Null
        Undo -Keyboard
        Ui @('wait-for', $navId, '-t', '3000') | Out-Null
        Ui @('invoke', (Match 'Multiselect source' 'ListItem').automationId) | Out-Null
        Ui @('wait-for', 'Bulk task 04', '-t', '3000') | Out-Null
        Ui @('wait-for', 'Undo step', '--gone', '-t', '3000') | Out-Null
    }
    Ui @('screenshot', '-o', (Join-Path $ArtifactDirectory 'undo.png')) | Out-Null
} finally {
    winapp ui yield | Out-Null
    $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflow
    $results.ToArray() | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $ArtifactDirectory 'results.json')
}
if (@($results | Where-Object status -eq 'FAIL').Count) { exit 1 }
