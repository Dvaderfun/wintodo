param(
    [Parameter(Mandatory)]
    [int]$AppPid,
    [Parameter(Mandatory)]
    [string]$ArtifactDirectory,
    [Parameter(Mandatory)]
    [string]$ClockPath
)
# Local desktop input requires explicit owner permission for this session.
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1') {
    throw 'Local UI automation is disabled on this main machine. Use an isolated VM/Sandbox, or obtain owner permission for this session before setting WINTODO_ALLOW_LOCAL_UI=1. See docs/guides/testing.md.'
}


$ErrorActionPreference = "Continue"
$pass = 0
$fail = 0
$results = @()
$MainHwnd = $null
for ($attempt = 0; $attempt -lt 10 -and -not $MainHwnd; $attempt++) {
    Start-Sleep -Milliseconds 300
    $MainHwnd = (winapp ui list-windows -a $AppPid --json 2>$null |
        ConvertFrom-Json |
        Where-Object { $_.title -eq "Wintodo" } |
        Select-Object -First 1).hwnd
}
if (-not $MainHwnd) {
    throw "The Wintodo main window was not found."
}

function Test-UI {
    param([string]$Name, [scriptblock]$Script)

    try {
        $output = & $Script 2>&1
        if ($LASTEXITCODE -eq 0) {
            $script:pass++
            $script:results += @{ name = $Name; status = "PASS" }
        } else {
            throw "$output"
        }
    } catch {
        $script:fail++
        $script:results += @{
            name = $Name
            status = "FAIL"
            detail = "$_"
        }
    }
}

function Get-SearchMatches {
    param([string]$Selector)

    return @(
        (winapp ui search $Selector -w $MainHwnd --json 2>$null |
            ConvertFrom-Json).matches
    )
}

function Get-TaskRows {
    param([string]$Name)

    return @(Get-SearchMatches $Name | Where-Object {
        $_.type -eq "ListItem" -and
        $_.automationId -match "^Todo\.Row\.[0-9a-f-]+$"
    })
}

function Invoke-List {
    param([string]$Name)

    $item = Get-SearchMatches $Name |
        Where-Object {
            $_.type -eq "ListItem" -and
            $_.automationId -match "^Todo\.Nav\.UserList\."
        } |
        Select-Object -First 1
    if (-not $item) {
        Start-Sleep -Milliseconds 300
        $item = Get-SearchMatches $Name |
            Where-Object {
                $_.type -eq "ListItem" -and
                $_.automationId -match "^Todo\.Nav\.UserList\."
            } |
            Select-Object -First 1
    }

    if (-not $item) {
        throw "Navigation item '$Name' was not found."
    }

    winapp ui invoke $item.selector -a $AppPid
}

Test-UI "My Day refreshes after the injected clock crosses midnight" {
    winapp ui wait-for "Todo.Nav.MyDay" -a $AppPid -t 5000
    if ((Get-TaskRows "Midnight focus").Count -ne 1) {
        throw "The seeded My Day task was not visible before midnight."
    }

    [System.IO.File]::WriteAllText(
        $ClockPath,
        "2026-08-01T09:00:00Z",
        [System.Text.Encoding]::UTF8)

    if (-not ("WintodoM5NativeWindow" -as [type])) {
        Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class WintodoM5NativeWindow
{
    [DllImport("user32.dll")]
    public static extern bool ShowWindowAsync(IntPtr hWnd, int command);
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
}
"@
    }

    $window = winapp ui list-windows -a $AppPid --json 2>$null |
        ConvertFrom-Json |
        Where-Object { $_.title -eq "Wintodo" } |
        Select-Object -First 1
    $handleValue = if ("$($window.hwnd)" -match "^0x") {
        [Convert]::ToInt64("$($window.hwnd)".Substring(2), 16)
    } else {
        [Convert]::ToInt64($window.hwnd)
    }
    $handle = [IntPtr]::new($handleValue)
    [WintodoM5NativeWindow]::ShowWindowAsync($handle, 6) | Out-Null
    Start-Sleep -Milliseconds 500
    [WintodoM5NativeWindow]::ShowWindowAsync($handle, 9) | Out-Null
    [WintodoM5NativeWindow]::SetForegroundWindow($handle) | Out-Null
    Start-Sleep -Milliseconds 1000

    if ((Get-TaskRows "Midnight focus").Count -ne 0) {
        throw "My Day still contains yesterday's task after activation."
    }

    $global:LASTEXITCODE = 0
}

Test-UI "Search is debounced, grouped by list, and Escape clears it" {
    winapp ui focus "Todo.TitleBar.SearchBox" -w $MainHwnd
    winapp ui send-keys "needle" `
        --target "Todo.TitleBar.SearchBox" `
        -w $MainHwnd `
        --via send-input
    winapp ui wait-for "Todo.Shell.PageTitle" `
        -w $MainHwnd `
        --value "Search" `
        -t 5000
    Start-Sleep -Milliseconds 300

    if ((Get-TaskRows "Needle").Count -ne 2) {
        throw "Search did not return both seeded matches."
    }

    $groups = @(Get-SearchMatches "Todo.List.SearchGroup" | Where-Object {
        $_.automationId -match "^Todo\.List\.SearchGroup\."
    })
    if ($groups.Count -ne 2 -or
        "Alpha" -notin $groups.name -or
        "Beta" -notin $groups.name) {
        throw "Expected Alpha and Beta search groups."
    }

    winapp ui screenshot -a $AppPid -o (
        Join-Path $ArtifactDirectory "01-search-groups.png"
    ) 2>$null
    winapp ui send-keys "escape" `
        --target "Todo.TitleBar.SearchBox" `
        -w $MainHwnd `
        --via send-input
    $searchRows = $null
    for ($attempt = 0; $attempt -lt 10; $attempt++) {
        Start-Sleep -Milliseconds 300
        $searchRows = Get-TaskRows "Needle"
        if ($searchRows.Count -eq 0) {
            break
        }
    }
    if ($searchRows.Count -ne 0) {
        throw "Escape did not clear the search results."
    }

    $global:LASTEXITCODE = 0
}

Test-UI "Planned places yesterday in a critical Overdue group" {
    winapp ui invoke "Todo.Nav.Planned" -a $AppPid
    winapp ui wait-for "Todo.List.PlannedGroup.Overdue" `
        -a $AppPid `
        --value "Overdue" `
        -t 5000
    if ((Get-TaskRows "Overdue alpha").Count -ne 1) {
        throw "The overdue task was not shown in Planned."
    }

    winapp ui screenshot -a $AppPid -o (
        Join-Path $ArtifactDirectory "02-planned-overdue.png"
    ) 2>$null
}

Test-UI "Completed count is visible and Alpha sort is changed" {
    Invoke-List "Alpha"
    $completedHeader = $null
    for ($attempt = 0; $attempt -lt 10 -and -not $completedHeader; $attempt++) {
        Start-Sleep -Milliseconds 300
        $completedHeader = Get-SearchMatches "Todo.List.CompletedHeader" |
            Where-Object { $_.name -eq "Completed, 1 tasks" } |
            Select-Object -First 1
    }
    if (-not $completedHeader) {
        throw "The completed header did not expose its persisted count."
    }

    winapp ui invoke "Todo.List.SortButton" -w $MainHwnd
    Start-Sleep -Milliseconds 250
    winapp ui invoke "Todo.List.Sort.Alphabetical" -a $AppPid
    winapp ui invoke "Todo.List.SortButton" -w $MainHwnd
    Start-Sleep -Milliseconds 250
    winapp ui invoke "Todo.List.Sort.Reverse" -a $AppPid
    winapp ui wait-for "Todo.List.SortLabel" `
        -w $MainHwnd `
        --value "Alphabetical, descending" `
        --contains `
        -t 5000
}

Test-UI "Beta keeps an independent sort choice" {
    Invoke-List "Beta"
    winapp ui invoke "Todo.List.SortButton" -w $MainHwnd
    Start-Sleep -Milliseconds 250
    winapp ui invoke "Todo.List.Sort.DueDate" -a $AppPid
    winapp ui wait-for "Todo.List.SortLabel" `
        -w $MainHwnd `
        --value "Due date, ascending" `
        --contains `
        -t 5000
}

# UX-11: keep the task in its real list while adding smart-list membership.
function Invoke-DropUi([string[]]$Arguments) {
    $output = winapp ui @Arguments -a $AppPid
    if ($LASTEXITCODE -ne 0) { throw "Drop UI command failed: $($Arguments -join ' ')" }
    $output
}
function Drop-Task([string]$Name, [string]$TargetId, [switch]$PreserveSelection) {
    $row = Get-TaskRows $Name | Select-Object -First 1
    if (-not $row) { throw "Drop source '$Name' missing." }
    if (-not $PreserveSelection) { Invoke-DropUi @('click', $row.selector) | Out-Null }
    Start-Sleep -Milliseconds 400
    $row = Get-TaskRows $Name | Select-Object -First 1
    $target = Get-SearchMatches $TargetId | Where-Object automationId -eq $TargetId | Select-Object -First 1
    if (-not $target) { throw "Drop target '$TargetId' missing." }
    $from = '{0},{1}' -f [int]($row.x + $row.width / 2), [int]($row.y + $row.height / 2)
    $to = '{0},{1}' -f [int]($target.x + $target.width / 2), [int]($target.y + $target.height / 2)
    Invoke-DropUi @('drag', $from, $to, '--hold-ms', '1000', '--dwell-ms', '1000') | Out-Null
    Start-Sleep -Milliseconds 500
}
Test-UI "Dropping onto My Day adds a task and repeating the drop keeps it set" {
    Invoke-List "Beta"
    Start-Sleep -Milliseconds 400
    Drop-Task 'Needle beta' 'Todo.Nav.MyDay'
    Drop-Task 'Needle beta' 'Todo.Nav.MyDay'
    if ((Get-TaskRows 'Needle beta').Count -ne 1) { throw 'Task left its original list.' }
    Invoke-DropUi @('invoke', 'Todo.Nav.MyDay') | Out-Null
    Start-Sleep -Milliseconds 400
    if ((Get-TaskRows 'Needle beta').Count -ne 1) { throw 'Dropped task is missing from My Day.' }
    Invoke-DropUi @('screenshot', '-o', (Join-Path $ArtifactDirectory '04-drop-my-day.png')) | Out-Null
    $global:LASTEXITCODE = 0
}
Test-UI "Dropping onto Important stars a task and repeating the drop keeps it set" {
    Invoke-List "Beta"
    Start-Sleep -Milliseconds 400
    Drop-Task 'Beta later' 'Todo.Nav.Important'
    Drop-Task 'Beta later' 'Todo.Nav.Important'
    $row = Get-TaskRows 'Beta later' | Select-Object -First 1
    if (-not $row -or $row.name -notmatch ', important,') { throw 'Task left its list or was not starred.' }
    Invoke-DropUi @('invoke', 'Todo.Nav.Important') | Out-Null
    Start-Sleep -Milliseconds 400
    if ((Get-TaskRows 'Beta later').Count -ne 1) { throw 'Dropped task is missing from Important.' }
    Invoke-DropUi @('screenshot', '-o', (Join-Path $ArtifactDirectory '05-drop-important.png')) | Out-Null
    $global:LASTEXITCODE = 0
}

Test-UI "A multi-task drop adds every selected task to both smart lists" {
    Invoke-List 'Beta'
    Start-Sleep -Milliseconds 400
    $first = Get-TaskRows 'Needle beta' | Select-Object -First 1
    Invoke-DropUi @('click', $first.selector) | Out-Null
    Start-Sleep -Milliseconds 400
    $second = Get-TaskRows 'Beta later' | Select-Object -First 1
    Add-Type @'
using System.Runtime.InteropServices;
public static class WintodoSmartDropInput {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint x, uint y, uint data, System.UIntPtr extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, System.UIntPtr extra);
}
'@
    # WinApp CLI click releases held modifiers; inject the modified click together.
    [WintodoSmartDropInput]::SetCursorPos([int]($second.x + $second.width / 2), [int]($second.y + $second.height / 2)) | Out-Null
    [WintodoSmartDropInput]::keybd_event(0x11, 0, 0, [UIntPtr]::Zero)
    try {
        [WintodoSmartDropInput]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero)
        [WintodoSmartDropInput]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 100
    } finally { [WintodoSmartDropInput]::keybd_event(0x11, 0, 2, [UIntPtr]::Zero) }
    Start-Sleep -Milliseconds 400
    foreach ($name in 'Needle beta', 'Beta later') {
        $row = Get-TaskRows $name | Select-Object -First 1
        $properties = Invoke-DropUi @('get-property', $row.selector, '--json') | ConvertFrom-Json
        if ($properties.properties.IsSelected -ne 'True') { throw "Task '$name' was not selected for drag." }
    }
    Drop-Task 'Beta later' 'Todo.Nav.MyDay' -PreserveSelection
    # Re-select both after the pointer press, which may select the drag source.
    Invoke-DropUi @('focus', 'Todo.List.Tasks') | Out-Null
    Invoke-DropUi @('send-keys', 'ctrl+vk=0x41', '--via', 'send-input') | Out-Null
    Drop-Task 'Beta later' 'Todo.Nav.Important' -PreserveSelection
    foreach ($nav in 'Todo.Nav.MyDay', 'Todo.Nav.Important') {
        Invoke-DropUi @('invoke', $nav) | Out-Null
        Start-Sleep -Milliseconds 400
        foreach ($name in 'Needle beta', 'Beta later') {
            if ((Get-TaskRows $name).Count -ne 1) { throw "'$name' is missing from '$nav' after bulk drop." }
        }
    }
    Invoke-List 'Beta'
    Start-Sleep -Milliseconds 400
    if ((Get-TaskRows 'Needle beta').Count -ne 1 -or (Get-TaskRows 'Beta later').Count -ne 1) {
        throw 'Bulk drop changed the original list.'
    }
    $global:LASTEXITCODE = 0
}

Test-UI "All visible app controls have AutomationIds" {
    $inspection = winapp ui inspect -a $AppPid --interactive --json 2>$null |
        ConvertFrom-Json

    function Get-DescendantElements {
        param([object[]]$Nodes)

        foreach ($node in $Nodes) {
            $node
            if ($node.children) {
                Get-DescendantElements -Nodes @($node.children)
            }
        }
    }

    $allElements = @(
        foreach ($window in $inspection.windows) {
            Get-DescendantElements -Nodes @($window.elements)
        }
    )
    $interactiveControls = @($allElements | Where-Object {
        $_.type -match "Button|CheckBox|Edit|ListItem|ComboBox|Spinner" -and
        $_.name -notmatch "Minimize|Maximize|Close|System" -and
        $_.className -notmatch "PopupHost|TitleBar|ReunionWindowing" -and
        -not ($_.type -eq "Button" -and $_.className -eq "Button" -and -not $_.name)
    })
    $missingId = @($interactiveControls | Where-Object {
        [string]::IsNullOrWhiteSpace($_.automationId)
    })
    if ($missingId.Count -ne 0) {
        $description = ($missingId |
            ForEach-Object { "$($_.type) '$($_.name)' [$($_.className)]" }) -join ", "
        throw "$($missingId.Count) visible controls lack AutomationIds: $description"
    }
}

$results | ConvertTo-Json | Out-File (
    Join-Path $ArtifactDirectory "phase-one-results.json"
)
Write-Host "M5 phase one passed: $pass | Failed: $fail"
$results |
    Where-Object { $_.status -eq "FAIL" } |
    ForEach-Object {
        Write-Host "FAIL: $($_.name) - $($_.detail)" -ForegroundColor Red
    }

if ($fail -gt 0) {
    exit 1
}
