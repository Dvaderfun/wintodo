param([Parameter(Mandatory)][int]$AppPid, [Parameter(Mandatory)][string]$TaskUid,
    [switch]$Keyboard, [switch]$Recurring, [switch]$Detail)
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1') { throw 'UI automation requires the test VM.' }
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
$app = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
    [System.Windows.Automation.TreeScope]::Children,
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $AppPid))
function Find([string]$Id) {
    $app.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Id))
}
function Wait([scriptblock]$Condition, [string]$Message, [int]$Timeout = 4000) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do { if (& $Condition) { return }; Start-Sleep -Milliseconds 20 } while ($timer.ElapsedMilliseconds -lt $Timeout)
    throw $Message
}
function ToggleState([string]$Id) {
    $element = Find $Id
    if (-not $element) { return $null }
    ([System.Windows.Automation.TogglePattern]$element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)).Current.ToggleState.ToString()
}
$toggleId = if ($Detail) { 'Todo.Detail.Complete' } else { "Todo.Row.CompleteToggle.$TaskUid" }
Wait { -not (Find 'Todo.List.UndoButton') } 'Previous snackbar did not expire.' 6500
$pattern = [System.Windows.Automation.TogglePattern](Find $toggleId).GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
if ($pattern.Current.ToggleState -ne 'Off') { throw 'Fixture task must be incomplete.' }
$pattern.Toggle()
if ($Detail) {
    Wait { (ToggleState $toggleId) -eq 'On' } 'Detail completion failed.'
    Start-Sleep -Milliseconds 700
    if (Find 'Todo.List.UndoButton') { throw 'Detail completion showed a snackbar.' }
    ([System.Windows.Automation.TogglePattern](Find $toggleId).GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)).Toggle()
    Wait { (ToggleState $toggleId) -eq 'Off' } 'Detail reset failed.'
    return
}
Wait { Find 'Todo.List.UndoButton' } 'Row completion did not show Undo.'
if (-not $Recurring) { Wait { (ToggleState $toggleId) -eq 'On' } 'Row did not complete.' }
if ($Keyboard) {
    Wait { -not (Find 'Todo.List.UndoButton') } 'Snackbar exceeded its five-second duration.' 6500
    # Input and focus stay within this guest process. Ctrl+Z tests retained history.
    (Find 'Todo.List.MenuButton').SetFocus()
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.SendKeys]::SendWait('^z')
} else {
    ([System.Windows.Automation.InvokePattern](Find 'Todo.List.UndoButton').GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
}
Wait { (ToggleState $toggleId) -eq 'Off' } 'Undo did not restore incomplete state.'
if ($Recurring) {
    Wait {
        $row = Find "Todo.Row.$TaskUid"
        $row -and $row.Current.Name -like '*Today*'
    } 'Recurring undo did not restore the original due date.'
}
