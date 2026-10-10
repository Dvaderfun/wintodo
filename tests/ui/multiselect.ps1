param([Parameter(Mandatory)][int]$AppPid, [Parameter(Mandatory)][string]$ArtifactDirectory)
# Local desktop input requires explicit owner permission for this session.
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1') {
    throw 'Local UI automation is disabled on this main machine. Use an isolated VM/Sandbox, or obtain owner permission for this session before setting WINTODO_ALLOW_LOCAL_UI=1. See docs/guides/testing.md.'
}

$ErrorActionPreference = 'Stop'
$results = [Collections.Generic.List[object]]::new()
$previousWorkflow = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = [guid]::NewGuid().ToString('N')
Add-Type @'
using System.Runtime.InteropServices;
public static class MultiselectKeys {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint x, uint y, uint data, System.UIntPtr extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, System.UIntPtr extra);
}
'@
function Ui([string[]]$Arguments) {
    $output = winapp ui @Arguments -a $AppPid
    if ($LASTEXITCODE -ne 0) { throw "UI command failed: $($Arguments -join ' ')" }
    $output
}
function Nodes($nodes) { foreach ($node in $nodes) { $node; Nodes $node.children } }
function Rows {
    $tree = Ui @('inspect', '--depth', '12', '--json') | ConvertFrom-Json
    @(Nodes @($tree.windows.elements) | Where-Object { $_.type -eq 'ListItem' -and $_.automationId -match '^Todo.Row.[0-9a-f-]+$' })
}
function Row([int]$Number) { Rows | Where-Object name -like "Bulk task $($Number.ToString('00')),*" | Select-Object -First 1 }
function SelectRow([int]$Number, [byte]$Modifier = 0) {
    $row = Row $Number
    if (-not $row) { throw "Task $Number missing." }
    if (-not $Modifier) { Ui @('click', $row.selector) | Out-Null; return }
    # CLI click clears pressed modifiers during foreground activation. Inject
    # the whole modified click together after the app is already foreground.
    [MultiselectKeys]::SetCursorPos([int]($row.x + $row.width / 2), [int]($row.y + $row.height / 2)) | Out-Null
    [MultiselectKeys]::keybd_event($Modifier, 0, 0, [UIntPtr]::Zero)
    try {
        [MultiselectKeys]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
        [MultiselectKeys]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 100
    } finally { [MultiselectKeys]::keybd_event($Modifier, 0, 2, [UIntPtr]::Zero) }
    Start-Sleep -Milliseconds 350
}
function AssertCount([int]$Count) {
    $script = @'
param([int]$Process)
Add-Type -AssemblyName UIAutomationClient
$root = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
    [System.Windows.Automation.TreeScope]::Children,
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $Process))
$list = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'Todo.List.Tasks'))
$pattern = $list.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern)
$pattern.Current.GetSelection().Count
'@
    $path = Join-Path $ArtifactDirectory 'selection-count.ps1'
    Set-Content $path $script
    $actual = [int](powershell.exe -NoProfile -File $path -Process $AppPid)
    if ($actual -ne $Count) { throw "Expected $Count selected tasks; got $actual." }
}
function Key([string]$Keys) {
    $Keys = $Keys.Replace('ctrl+i', 'ctrl+vk=0x49').Replace('ctrl+t', 'ctrl+vk=0x54').Replace('ctrl+a', 'ctrl+vk=0x41')
 Ui @('send-keys', $Keys, '--via', 'send-input') | Out-Null; Start-Sleep -Milliseconds 400 }
function Check([string]$Name, [scriptblock]$Action) {
    try { Write-Host "Checking: $Name"; & $Action; $results.Add(@{name=$Name;status='PASS'}); Write-Host "PASS: $Name" }
    catch { $results.Add(@{name=$Name;status='FAIL';detail="$_"}) }
}
try {
    Ui @('wait-for', 'Todo.List.Tasks', '-t', '10000') | Out-Null
    Check 'Ctrl-select three; detail hides; SelectionPattern supports multiple selection' {
        SelectRow 1
        Ui @('wait-for', 'Todo.Detail.Title', '-t', '5000') | Out-Null
        SelectRow 3 0x11
        SelectRow 5 0x11
        AssertCount 3
        Ui @('wait-for', 'Todo.Detail.Title', '--gone', '-t', '5000') | Out-Null
        $patternScript = @'
param([int]$Process)
Add-Type -AssemblyName UIAutomationClient
$root = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
    [System.Windows.Automation.TreeScope]::Children,
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $Process))
$list = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'Todo.List.Tasks'))
$pattern = $list.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern)
@{canSelectMultiple=$pattern.Current.CanSelectMultiple;count=$pattern.Current.GetSelection().Count} | ConvertTo-Json
'@
        $patternFile = Join-Path $ArtifactDirectory 'read-selection-pattern.ps1'
        Set-Content $patternFile $patternScript
        $pattern = powershell.exe -NoProfile -File $patternFile -Process $AppPid | ConvertFrom-Json
        $pattern | ConvertTo-Json | Set-Content (Join-Path $ArtifactDirectory 'selection-pattern.json')
        if (-not $pattern.canSelectMultiple -or $pattern.count -ne 3) { throw 'Native SelectionPattern is incorrect.' }
        Ui @('screenshot', '-o', (Join-Path $ArtifactDirectory '01-multiselect.png')) | Out-Null
    }
    Check 'Bulk importance, My Day and due date; right-click preserves selection' {
        Key 'ctrl+i'
        AssertCount 3
        foreach ($n in 1,3,5) { if ((Row $n).name -notlike '*important*') { throw "Task $n was not starred." } }
        Key 'ctrl+t'
        Ui @('click', (Row 3).selector, '--right') | Out-Null
        Ui @('wait-for', 'Todo.Row.Selection.Due', '-t', '3000') | Out-Null
        Ui @('invoke', 'Todo.Row.Selection.Due') | Out-Null
        Ui @('invoke', 'Todo.Row.Selection.Due.Today') | Out-Null
        Start-Sleep -Milliseconds 500
        AssertCount 3
        foreach ($n in 1,3,5) { if ((Row $n).name -notlike '*Today*') { throw "Task $n has no due date." } }
    }
    Check 'Space completes three selected tasks and preserves selection' {
        Ui @('focus', (Row 3).selector) | Out-Null
        Key 'space'
        AssertCount 3
        foreach ($n in 1,3,5) { if ((Row $n).name -like '*not completed*') { throw "Task $n was not completed." } }
        Key 'space'
        foreach ($n in 1,3,5) { if ((Row $n).name -notlike '*not completed*') { throw "Task $n was not restored." } }
        Key 'esc'
        AssertCount 0
    }
    Check 'Shift-click range of five; context move applies to all' {
        SelectRow 2
        SelectRow 6 0x10
        AssertCount 5
        Ui @('click', (Row 4).selector, '--right') | Out-Null
        Ui @('invoke', 'Todo.Row.Selection.Move') | Out-Null
        $target = (Ui @('search', 'Multiselect target', '--type', 'MenuItem', '--json') | ConvertFrom-Json).matches | Select-Object -First 1
        Ui @('invoke', $target.selector) | Out-Null
        Start-Sleep -Milliseconds 600
        foreach ($n in 2..6) { if (Row $n) { throw "Task $n stayed in source." } }
        $target = (Ui @('search', 'Multiselect target', '--type', 'ListItem', '--json') | ConvertFrom-Json).matches | Select-Object -First 1
        Ui @('invoke', $target.selector) | Out-Null
        Start-Sleep -Milliseconds 500
        if ((Rows).Count -ne 5) { throw 'Destination does not contain all five tasks.' }
    }
    Check 'Shift-arrow extends selection; delete two and one undo restores both' {
        SelectRow 2
        Ui @('focus', (Row 2).selector) | Out-Null
        Key 'shift+down'
        AssertCount 2
        Key 'delete'
        Ui @('wait-for', 'Todo.List.UndoButton', '-t', '3000') | Out-Null
        Ui @('invoke', 'Todo.List.UndoButton') | Out-Null
        Start-Sleep -Milliseconds 500
        if (-not (Row 2) -or -not (Row 3)) { throw 'Undo did not restore both tasks.' }
    }
    Check 'Ctrl+A selects current group; Escape clears selection' {
        SelectRow 2
        Ui @('focus', (Row 2).selector) | Out-Null
        Key 'ctrl+a'
        AssertCount 5
        Key 'esc'
        AssertCount 0
    }
    Check 'Drag two selected tasks onto a navigation list' {
        SelectRow 2
        SelectRow 4 0x11
        AssertCount 2
        $target = (Ui @('search', 'Multiselect source', '--type', 'ListItem', '--json') | ConvertFrom-Json).matches | Select-Object -First 1
        $source = Row 4
        $from = '{0},{1}' -f [int]($source.x + $source.width / 2), [int]($source.y + $source.height / 2)
        $to = '{0},{1}' -f [int]($target.x + $target.width / 2), [int]($target.y + $target.height / 2)
        @{source=$source;target=$target;from=$from;to=$to} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $ArtifactDirectory 'drag-bounds.json')
        Ui @('drag', $from, $to, '--hold-ms', '1000', '--dwell-ms', '1000') | Out-Null
        Start-Sleep -Milliseconds 600
        if ((Row 2) -or (Row 4)) { throw 'Selected tasks stayed in the source of the drag.' }
        $target = (Ui @('search', 'Multiselect source', '--type', 'ListItem', '--json') | ConvertFrom-Json).matches | Select-Object -First 1
        Ui @('invoke', $target.selector) | Out-Null
        Start-Sleep -Milliseconds 400
        if (-not (Row 2) -or -not (Row 4)) { throw 'Both tasks did not arrive in the drop list.' }
    }
} catch { $results.Add(@{name='Setup';status='FAIL';detail="$_"}) }
finally {
    winapp ui yield | Out-Null
    $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflow
}
@{results=@($results.ToArray())} | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $ArtifactDirectory 'results.json')
$results.ToArray() | ForEach-Object { [pscustomobject]$_ } | Format-Table name,status,detail -Wrap
if (@($results | Where-Object status -eq 'FAIL').Count) { exit 1 }
