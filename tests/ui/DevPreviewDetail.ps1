# Called by run-dev-preview-gate.ps1 after creating the named disposable tasks.
param(
    [Parameter(Mandatory)][int]$AppPid,
    [Parameter(Mandatory)][string[]]$Titles,
    [Parameter(Mandatory)][string]$ArtifactDirectory,
    [double]$RestoredWidth = 0
)
$ErrorActionPreference = 'Stop'
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1' -or
    (Get-ItemPropertyValue 'HKLM:\SOFTWARE\Microsoft\Virtual Machine\Guest\Parameters' VirtualMachineName) -cne 'WintodoTest') {
    throw 'Run the signed DEV gate inside WintodoTest.'
}
$package = Get-AppxPackage -Name Wintodo.Hybrid.Dev
$app = Get-Process -Id $AppPid
if ($package.IsDevelopmentMode -or $app.Path -ine (Join-Path $package.InstallLocation 'Wintodo.exe')) { throw 'Expected installed DEV.' }
if (-not ('DevPaneWindow' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class DevPaneWindow {
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int command);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
}
'@
}
[void][DevPaneWindow]::ShowWindow($app.MainWindowHandle, 3)
$scale = [DevPaneWindow]::GetDpiForWindow($app.MainWindowHandle) / 96.0
function Invoke-PaneUi([string[]]$Arguments) {
    $reply = @(& winapp ui @Arguments -a $AppPid)
    if ($LASTEXITCODE -ne 0) { throw "UI failed: $Arguments. $reply" }
    $reply
}
function Bounds([string]$id) {
    $result = (Invoke-PaneUi @('search',$id,'--json') | ConvertFrom-Json).matches | Where-Object automationId -ceq $id
    if (@($result).Count -ne 1 -or $result.width -le 0 -or $result.height -le 0) { throw "No unique visible bounds: $id" }
    $result
}
function Select-Task([string]$title) {
    $rows = (Invoke-PaneUi @('search',$title,'--type','ListItem','--json') | ConvertFrom-Json).matches |
        Where-Object { $_.automationId -match '^Todo\.Row\.[^.]+$' -and $_.name.StartsWith($title,[StringComparison]::Ordinal) }
    if (@($rows).Count -ne 1) { throw "Expected one task row for $title" }
    Invoke-PaneUi @('invoke',$rows.automationId) | Out-Null
    Invoke-PaneUi @('wait-for','Todo.Detail.Title','--value',$title,'-t','10000') | Out-Null
}
function Read-StepDraft {
    (Invoke-PaneUi @('get-value','Todo.Detail.NewStep','--json') | ConvertFrom-Json).text
}
function Measured-Width {
    $grip = Bounds 'Todo.Detail.Sizer'
    $closeButton = Bounds 'Todo.Detail.Close'
    # Same two visible edges before and after restart; excludes fixed outer right padding.
    ($closeButton.x + $closeButton.width - $grip.x) / $scale
}
Invoke-PaneUi @('invoke','Todo.Nav.All') | Out-Null
Invoke-PaneUi @('wait-for',$Titles[0],'--type','ListItem','-t','10000') | Out-Null
Select-Task $Titles[0]
if ($RestoredWidth -gt 0) {
    $width = Measured-Width
    if ([Math]::Abs($RestoredWidth-$width) -gt 3) { throw "Restored width $width; expected $RestoredWidth DIPs between controls." }
    return
}
$marker = 'Draft belongs to the first task'
Invoke-PaneUi @('set-value','Todo.Detail.NewStep',$marker) | Out-Null
Select-Task $Titles[1]
if ((Read-StepDraft) -ne '') { throw 'Unsubmitted step followed the selection.' }
Invoke-PaneUi @('set-value','Todo.Detail.NewStep','Second task draft') | Out-Null
Select-Task $Titles[0]
if ((Read-StepDraft) -cne $marker) { throw 'Lost the first task draft.' }
Invoke-PaneUi @('send-keys','enter','--target','Todo.Detail.NewStep','--via','send-input') | Out-Null
Invoke-PaneUi @('wait-for',$marker,'--type','ListItem','-t','5000') | Out-Null
Select-Task $Titles[1]
if ((Read-StepDraft) -cne 'Second task draft') { throw 'Submitting a step changed another task draft.' }
Invoke-PaneUi @('wait-for',$marker,'--type','ListItem','--gone','-t','5000') | Out-Null

$compact = Bounds 'Todo.Detail.Note'
$footer = Bounds 'Todo.Detail.Delete'
Invoke-PaneUi @('focus','Todo.Detail.Note') | Out-Null
Invoke-PaneUi @('set-value','Todo.Detail.Note',"Review the details.`nWrite the next action.") | Out-Null
$expanded = Bounds 'Todo.Detail.Note'
$footerAfter = Bounds 'Todo.Detail.Delete'
if (($expanded.height-$compact.height)/$scale -lt 32 -or [Math]::Abs($footer.y-$footerAfter.y)/$scale -gt 2 -or
    $expanded.y+$expanded.height -gt $footerAfter.y) { throw 'Focused multiline note did not use spare space above the pinned footer.' }
Invoke-PaneUi @('screenshot','-o',(Join-Path $ArtifactDirectory '06-expanded-note.png')) | Out-Null
foreach ($pair in @(@('ctrl+shift+d','Todo.Detail.DueDatePicker'),@('ctrl+shift+r','Todo.Detail.ReminderDatePicker'))) {
    Invoke-PaneUi @('send-keys',$pair[0],'--target','Todo.Detail.Title','--via','send-input') | Out-Null
    Invoke-PaneUi @('wait-for',$pair[1],'-t','5000') | Out-Null
    Invoke-PaneUi @('send-keys','escape','--via','send-input') | Out-Null
    Invoke-PaneUi @('wait-for',$pair[1],'--gone','-t','5000') | Out-Null
    Invoke-PaneUi @('wait-for','Todo.Detail.Title','--value',$Titles[1],'-t','5000') | Out-Null
}
Invoke-PaneUi @('focus','Todo.Detail.Sizer') | Out-Null
$wide = Measured-Width
for ($press = 0; $press -lt 60 -and $wide -le 748; $press++) {
    # The native sizer reads its arranged width on each key. An entire burst
    # in one SendInput call runs before the layout from the first key is ready.
    Invoke-PaneUi @('send-keys','left','--target','Todo.Detail.Sizer','--via','send-input') | Out-Null
    if ($press % 4 -eq 3) { $wide = Measured-Width }
}
if ($wide -le 720) { throw "Keyboard resize did not exceed the old 720-DIP restore limit; measured $wide." }
[ordered]@{wideDip=$wide;noteCompactDip=$compact.height/$scale;noteExpandedDip=$expanded.height/$scale} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $ArtifactDirectory 'detail-layout.json')
