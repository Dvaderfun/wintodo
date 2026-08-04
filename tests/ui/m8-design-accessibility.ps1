param(
    [Parameter(Mandatory)]
    [int]$AppPid,
    [Parameter(Mandatory)]
    [string]$ArtifactDirectory
)

$ErrorActionPreference = "Continue"
$pass = 0
$fail = 0
$results = @()
$mainHwnd = $null
$themeRegistryPath =
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"

for ($attempt = 0; $attempt -lt 20 -and -not $mainHwnd; $attempt++) {
    Start-Sleep -Milliseconds 250
    $mainHwnd = (winapp ui list-windows -a $AppPid --json 2>$null |
        ConvertFrom-Json |
        Where-Object { $_.title -eq "Wintodo" } |
        Select-Object -First 1).hwnd
}
if (-not $mainHwnd) {
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
    } finally {
        $global:LASTEXITCODE = 0
    }
}

function Get-SearchMatches {
    param([string]$Selector)

    return @(
        (winapp ui search $Selector -w $mainHwnd --json 2>$null |
            ConvertFrom-Json).matches
    )
}

function Get-TaskRow {
    param([string]$Name)

    return Get-SearchMatches $Name |
        Where-Object {
            $_.type -eq "ListItem" -and
            $_.automationId -match "^Todo\.Row\.[0-9a-f-]+$"
        } |
        Select-Object -First 1
}

function Wait-TaskRow {
    param(
        [string]$Name,
        [string]$AccessibleNamePattern,
        [int]$TimeoutMilliseconds = 5000
    )

    $deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMilliseconds)
    do {
        $row = Get-TaskRow $Name
        if ($row -and (
            [string]::IsNullOrEmpty($AccessibleNamePattern) -or
            $row.name -match $AccessibleNamePattern)) {
            return $row
        }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)

    return $null
}

function Invoke-UserList {
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
        throw "User list '$Name' was not found."
    }

    winapp ui invoke $item.selector -w $mainHwnd | Out-Null
    winapp ui wait-for "Todo.List.TitleButton" `
        -w $mainHwnd `
        --value $Name `
        -t 5000 | Out-Null
}

function Take-Screenshot {
    param([string]$Name, [switch]$CaptureScreen)

    $arguments = @(
        "ui", "screenshot", "-a", "$AppPid", "-o",
        (Join-Path $ArtifactDirectory $Name)
    )
    if ($CaptureScreen) {
        $arguments += "--capture-screen"
    }

    & winapp $arguments 2>$null | Out-Null
}

function Get-DescendantElements {
    param([object[]]$Nodes)

    foreach ($node in $Nodes) {
        $node
        if ($node.children) {
            Get-DescendantElements -Nodes @($node.children)
        }
    }
}

function Assert-AuthoredControlsHaveIds {
    $inspection = winapp ui inspect -a $AppPid --interactive --json 2>$null |
        ConvertFrom-Json
    $allElements = @(
        foreach ($window in $inspection.windows) {
            Get-DescendantElements -Nodes @($window.elements)
        }
    )
    $authored = @($allElements | Where-Object {
        $_.type -match "Button|CheckBox|Edit|ListItem|MenuItem|ComboBox|Spinner" -and
        $_.name -notmatch "Minimize|Maximize|Close|System|Scroll" -and
        $_.className -notmatch "PopupHost|TitleBar|ReunionWindowing|ScrollBar" -and
        -not ($_.type -eq "Button" -and -not $_.name) -and
        -not ($_.type -eq "MenuItem" -and [string]::IsNullOrWhiteSpace($_.className))
    })
    $missingId = @($authored | Where-Object {
        [string]::IsNullOrWhiteSpace($_.automationId)
    })
    if ($missingId.Count -ne 0) {
        $description = ($missingId | ForEach-Object {
            "$($_.type) '$($_.name)' [$($_.className)]"
        }) -join ", "
        throw "$($missingId.Count) visible controls lack AutomationIds: $description"
    }
}

if (-not ("WintodoM8ThemeNative" -as [type])) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class WintodoM8ThemeNative
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct HIGHCONTRAST
    {
        public uint cbSize;
        public uint dwFlags;
        public IntPtr lpszDefaultScheme;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint message,
        UIntPtr wParam,
        string lParam,
        uint flags,
        uint timeout,
        out UIntPtr result);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SystemParametersInfo(
        uint action,
        uint parameter,
        ref HIGHCONTRAST value,
        uint flags);
}
"@
}

$spiGetHighContrast = 0x0042
$spiSetHighContrast = 0x0043
$highContrastOn = 0x0001
$sendChange = 0x0003
$highContrastSize = [Runtime.InteropServices.Marshal]::SizeOf(
    [type]"WintodoM8ThemeNative+HIGHCONTRAST")

function Get-HighContrast {
    $state = New-Object WintodoM8ThemeNative+HIGHCONTRAST
    $state.cbSize = $highContrastSize
    if (-not [WintodoM8ThemeNative]::SystemParametersInfo(
        $spiGetHighContrast,
        $highContrastSize,
        [ref]$state,
        0)) {
        throw "Could not read the High Contrast state."
    }

    $scheme = if ($state.lpszDefaultScheme -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::PtrToStringUni(
            $state.lpszDefaultScheme)
    } else {
        ""
    }
    return @{ Flags = $state.dwFlags; Scheme = $scheme }
}

function Set-HighContrast {
    param([uint32]$Flags, [string]$Scheme)

    $schemePointer = if ($Scheme) {
        [Runtime.InteropServices.Marshal]::StringToHGlobalUni($Scheme)
    } else {
        [IntPtr]::Zero
    }
    try {
        $state = New-Object WintodoM8ThemeNative+HIGHCONTRAST
        $state.cbSize = $highContrastSize
        $state.dwFlags = $Flags
        $state.lpszDefaultScheme = $schemePointer
        if (-not [WintodoM8ThemeNative]::SystemParametersInfo(
            $spiSetHighContrast,
            $highContrastSize,
            [ref]$state,
            $sendChange)) {
            throw "Could not set the High Contrast state."
        }
    } finally {
        if ($schemePointer -ne [IntPtr]::Zero) {
            [Runtime.InteropServices.Marshal]::FreeHGlobal($schemePointer)
        }
    }
    Start-Sleep -Seconds 3
}

winapp ui wait-for "Todo.TitleBar.SearchBox" -w $mainHwnd -t 5000 | Out-Null

Test-UI "Every smart list has its specified empty state" {
    $cases = @(
        @{ Nav = "Todo.Nav.MyDay"; Title = "Focus on your day" },
        @{ Nav = "Todo.Nav.Important"; Title = "Nothing important yet" },
        @{ Nav = "Todo.Nav.Planned"; Title = "Nothing planned" },
        @{ Nav = "Todo.Nav.All"; Title = "No tasks yet" },
        @{ Nav = "Todo.Nav.Completed"; Title = "Nothing completed yet" }
    )
    foreach ($case in $cases) {
        winapp ui invoke $case.Nav -w $mainHwnd | Out-Null
        winapp ui wait-for $case.Title -w $mainHwnd -t 3000 | Out-Null
    }
    Take-Screenshot "01-empty-completed.png"
}

Test-UI "Keyboard creates a list and exposes its empty state" {
    winapp ui focus "Todo.Nav.Completed" -w $mainHwnd | Out-Null
    winapp ui send-keys "ctrl+vk=0x4C" -w $mainHwnd --via send-input | Out-Null
    winapp ui wait-for "Todo.Nav.NewListNameBox" -w $mainHwnd -t 3000 | Out-Null
    winapp ui set-value "Todo.Nav.NewListNameBox" "M8 Gate" -w $mainHwnd | Out-Null
    winapp ui send-keys "enter" `
        --target "Todo.Nav.NewListNameBox" `
        -w $mainHwnd `
        --via send-input | Out-Null
    winapp ui wait-for "Todo.List.TitleButton" `
        -w $mainHwnd `
        --value "M8 Gate" `
        -t 5000 | Out-Null
    winapp ui wait-for "This list is empty" -w $mainHwnd -t 3000 | Out-Null
}

Test-UI "Keyboard creates a task with a complete accessible sentence" {
    winapp ui send-keys "ctrl+vk=0x4E" -w $mainHwnd --via send-input | Out-Null
    winapp ui wait-for "Todo.List.AddTaskBox" -w $mainHwnd -t 3000 | Out-Null
    winapp ui set-value "Todo.List.AddTaskBox" "Narrator task" -w $mainHwnd | Out-Null
    winapp ui send-keys "enter" `
        --target "Todo.List.AddTaskBox" `
        -w $mainHwnd `
        --via send-input | Out-Null
    Start-Sleep -Milliseconds 500
    $row = Get-TaskRow "Narrator task"
    if (-not $row -or
        $row.name -notmatch "Narrator task" -or
        $row.name -notmatch "in M8 Gate" -or
        $row.name -notmatch "not completed") {
        throw "The created task did not expose title, list, and completion state."
    }
    winapp ui focus $row.selector -w $mainHwnd | Out-Null
    winapp ui send-keys "enter" -w $mainHwnd --via send-input | Out-Null
    winapp ui wait-for "Todo.Detail.Title" -w $mainHwnd -t 5000 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Enter did not select and open the created task."
    }
}

Test-UI "Importance, My Day, selection, and inline rename shortcuts work" {
    winapp ui send-keys "ctrl+vk=0x49" -w $mainHwnd --via send-input | Out-Null
    $row = Wait-TaskRow "Narrator task" "important"
    if (-not $row) {
        throw "Ctrl+I did not update the accessible importance state."
    }
    winapp ui send-keys "ctrl+vk=0x54" -w $mainHwnd --via send-input | Out-Null
    Start-Sleep -Milliseconds 500

    winapp ui send-keys "f2" -w $mainHwnd --via send-input | Out-Null
    $uid = $row.automationId.Substring("Todo.Row.".Length)
    $editorId = "Todo.Row.TitleEdit.$uid"
    winapp ui wait-for $editorId -w $mainHwnd -t 3000 | Out-Null
    winapp ui set-value $editorId "Narrator task renamed" -w $mainHwnd | Out-Null
    winapp ui send-keys "enter" `
        --target $editorId `
        -w $mainHwnd `
        --via send-input | Out-Null
    if (-not (Wait-TaskRow "Narrator task renamed" "not completed$")) {
        throw "F2 inline rename did not persist the new title."
    }
}

Test-UI "Keyboard opens detail, due picker, and reminder picker" {
    $row = Get-TaskRow "Narrator task renamed"
    winapp ui invoke $row.selector -w $mainHwnd | Out-Null
    winapp ui wait-for "Todo.Detail.Title" -w $mainHwnd -t 5000 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "The selected task detail pane was not available."
    }

    winapp ui send-keys "ctrl+shift+vk=0x44" -w $mainHwnd --via send-input | Out-Null
    winapp ui wait-for "Todo.Detail.DueDatePicker" -a $AppPid -t 5000 | Out-Null
    winapp ui send-keys "escape" -w $mainHwnd --via send-input | Out-Null

    winapp ui send-keys "ctrl+shift+vk=0x52" -w $mainHwnd --via send-input | Out-Null
    winapp ui wait-for "Todo.Detail.ReminderDatePicker" -a $AppPid -t 5000 | Out-Null
    winapp ui send-keys "escape" -w $mainHwnd --via send-input | Out-Null

    winapp ui invoke "Todo.Detail.Due" -w $mainHwnd | Out-Null
    Start-Sleep -Milliseconds 300
    winapp ui invoke "Todo.Detail.Due.Tomorrow" -a $AppPid | Out-Null
    $updated = Wait-TaskRow "Narrator task renamed" "due Tomorrow"
    if (-not $updated) {
        throw "The task accessible name omitted its due date."
    }
    winapp ui send-keys "escape" -w $mainHwnd --via send-input | Out-Null
    winapp ui wait-for "Todo.Detail.Title" -w $mainHwnd --gone -t 3000 | Out-Null
}

Test-UI "Keyboard completes, deletes, and restores a task" {
    $row = Get-TaskRow "Narrator task renamed"
    winapp ui invoke $row.selector -w $mainHwnd | Out-Null
    winapp ui focus $row.selector -w $mainHwnd | Out-Null
    winapp ui send-keys "space" -w $mainHwnd --via send-input | Out-Null
    winapp ui wait-for "Todo.List.CompletedHeader" -w $mainHwnd -t 5000 | Out-Null
    winapp ui invoke "Todo.List.CompletedHeader" -w $mainHwnd | Out-Null
    $completed = Wait-TaskRow "Narrator task renamed" ", completed$"
    if (-not $completed) {
        throw "Space did not expose the completed state to UI Automation."
    }

    winapp ui invoke $completed.selector -w $mainHwnd | Out-Null
    winapp ui send-keys "delete" -w $mainHwnd --via send-input | Out-Null
    winapp ui wait-for "Todo.Dialog.DeleteTask" -a $AppPid -t 3000 | Out-Null
    winapp ui invoke "Todo.Dialog.DeleteTask.Confirm" -a $AppPid | Out-Null
    winapp ui wait-for "Todo.List.UndoButton" -w $mainHwnd -t 3000 | Out-Null
    winapp ui send-keys "ctrl+vk=0x5A" -w $mainHwnd --via send-input | Out-Null
    $restored = Wait-TaskRow "Narrator task renamed" ", completed$"
    if (-not $restored) {
        throw "Ctrl+Z did not restore the keyboard-deleted task."
    }
    winapp ui invoke $restored.selector -w $mainHwnd | Out-Null
    winapp ui focus $restored.selector -w $mainHwnd | Out-Null
    winapp ui send-keys "space" -w $mainHwnd --via send-input | Out-Null
    $active = Wait-TaskRow "Narrator task renamed" "not completed$"
    if (-not $active) {
        throw "Space did not restore the active state."
    }
}

Test-UI "Smart-list number shortcuts route to the task" {
    $cases = @(
        @{ Keys = "ctrl+vk=0x31"; Title = "My Day" },
        @{ Keys = "ctrl+vk=0x32"; Title = "Important" },
        @{ Keys = "ctrl+vk=0x33"; Title = "Planned" },
        @{ Keys = "ctrl+vk=0x34"; Title = "All" },
        @{ Keys = "ctrl+vk=0x35"; Title = "Completed" }
    )
    foreach ($case in $cases) {
        winapp ui send-keys $case.Keys -w $mainHwnd --via send-input | Out-Null
        winapp ui wait-for "Todo.Shell.PageTitle" `
            -w $mainHwnd `
            --value $case.Title `
            -t 3000 | Out-Null
        if ($case.Title -eq "Completed") {
            winapp ui wait-for "Nothing completed yet" -w $mainHwnd -t 3000 | Out-Null
        } elseif (-not (Wait-TaskRow "Narrator task renamed" "not completed$")) {
            throw "$($case.Keys) did not route to the expected task."
        }
    }
}

Test-UI "Settings shortcut, appearance controls, and responsive cards work" {
    winapp ui send-keys "ctrl+shift+vk=0xBC" `
        -w $mainHwnd `
        --via send-input | Out-Null
    winapp ui wait-for "Todo.Settings.Theme" -w $mainHwnd -t 5000 | Out-Null
    winapp ui invoke "Todo.Settings.ShowBackgrounds" -w $mainHwnd | Out-Null
    winapp ui invoke "Todo.Settings.Density" -w $mainHwnd | Out-Null
    Start-Sleep -Milliseconds 200
    winapp ui invoke "Todo.Settings.Density.Compact" -a $AppPid | Out-Null
    Take-Screenshot "02-settings.png"
    Assert-AuthoredControlsHaveIds
}

Test-UI "Per-list colour and decoded raster background are selectable" {
    Invoke-UserList "M8 Gate"
    winapp ui invoke "Todo.List.MenuButton" -w $mainHwnd | Out-Null
    Start-Sleep -Milliseconds 200
    winapp ui invoke "Todo.List.Menu.ChangeTheme" -a $AppPid | Out-Null
    Start-Sleep -Milliseconds 200
    winapp ui invoke "Todo.List.Theme.Green" -a $AppPid | Out-Null

    winapp ui invoke "Todo.List.MenuButton" -w $mainHwnd | Out-Null
    Start-Sleep -Milliseconds 200
    winapp ui invoke "Todo.List.Menu.ChangeBackground" -a $AppPid | Out-Null
    Start-Sleep -Milliseconds 200
    winapp ui invoke "Todo.List.Background.Coast" -a $AppPid | Out-Null
    Start-Sleep -Milliseconds 500
    Take-Screenshot "03-list-theme-background.png"
}

Test-UI "Shortcuts dialog is complete and reachable from Ctrl+?" {
    winapp ui focus "Todo.List.MenuButton" -w $mainHwnd | Out-Null
    winapp ui send-keys "ctrl+shift+vk=0xBF" `
        -w $mainHwnd `
        --via send-input | Out-Null
    winapp ui wait-for "Todo.Dialog.Shortcuts" -a $AppPid -t 5000 | Out-Null
    foreach ($label in @("Ctrl+N", "Ctrl+T", "Ctrl+Shift+D")) {
        winapp ui wait-for $label -a $AppPid -t 3000 | Out-Null
    }
    Take-Screenshot "04-shortcuts-dialog.png" -CaptureScreen
    winapp ui scroll "Todo.Dialog.Shortcuts.Scroll" -a $AppPid --to bottom | Out-Null
    foreach ($label in @("Alt+Up", "Delete")) {
        winapp ui wait-for $label -a $AppPid -t 3000 | Out-Null
    }
    Take-Screenshot "04-shortcuts-dialog-bottom.png" -CaptureScreen
    Assert-AuthoredControlsHaveIds
    winapp ui invoke "Todo.Dialog.Shortcuts.Close" -a $AppPid | Out-Null
}

Test-UI "Shortcuts dialog is reachable from the list menu" {
    winapp ui invoke "Todo.List.MenuButton" -w $mainHwnd | Out-Null
    Start-Sleep -Milliseconds 200
    winapp ui invoke "Todo.List.Menu.Shortcuts" -a $AppPid | Out-Null
    winapp ui wait-for "Todo.Dialog.Shortcuts" -a $AppPid -t 5000 | Out-Null
    winapp ui invoke "Todo.Dialog.Shortcuts.Close" -a $AppPid | Out-Null
}

Test-UI "Visible authored controls pass the AutomationId audit" {
    Assert-AuthoredControlsHaveIds
    $row = Get-TaskRow "Narrator task renamed"
    $uid = $row.automationId.Substring("Todo.Row.".Length)
    foreach ($controlId in @(
        "Todo.Row.CompleteToggle.$uid",
        "Todo.Row.ImportanceToggle.$uid"
    )) {
        $control = Get-SearchMatches $controlId | Select-Object -First 1
        if (-not $control -or $control.width -lt 32 -or $control.height -lt 32) {
            throw "$controlId is smaller than the 32x32 minimum hit target."
        }
    }
}

$originalHighContrast = Get-HighContrast
$highContrastScheme = if ($originalHighContrast.Scheme) {
    $originalHighContrast.Scheme
} else {
    "High Contrast Black"
}
try {
    Test-UI "High Contrast keeps authored controls visible and accessible" {
        Set-HighContrast `
            -Flags ($originalHighContrast.Flags -bor $highContrastOn) `
            -Scheme $highContrastScheme
        winapp ui invoke "Todo.Nav.MyDay" -w $mainHwnd | Out-Null
        Invoke-UserList "M8 Gate"
        winapp ui wait-for "Todo.List.MenuButton" -w $mainHwnd -t 5000 | Out-Null
        winapp ui focus "Todo.List.MenuButton" -w $mainHwnd | Out-Null
        Assert-AuthoredControlsHaveIds
        Take-Screenshot "05-high-contrast.png"
    }
} finally {
    Set-HighContrast `
        -Flags $originalHighContrast.Flags `
        -Scheme $originalHighContrast.Scheme
}

$restoredHighContrast = Get-HighContrast
if ($restoredHighContrast.Flags -ne $originalHighContrast.Flags) {
    $fail++
    $results += @{
        name = "Original High Contrast state is restored"
        status = "FAIL"
        detail = "The original flags were not restored."
    }
} else {
    $pass++
    $results += @{
        name = "Original High Contrast state is restored"
        status = "PASS"
    }
}

$results | ConvertTo-Json | Out-File (
    Join-Path $ArtifactDirectory "results.json"
)
Write-Host "M8 gate passed: $pass | Failed: $fail"
$results |
    Where-Object { $_.status -eq "FAIL" } |
    ForEach-Object {
        Write-Host "FAIL: $($_.name) - $($_.detail)" -ForegroundColor Red
    }

if ($fail -gt 0) {
    exit 1
}
