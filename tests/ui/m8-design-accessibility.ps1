param(
    [Parameter(Mandatory)]
    [int]$AppPid,
    [Parameter(Mandatory)]
    [string]$ArtifactDirectory
)
# Local desktop input requires explicit owner permission for this session.
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1') {
    throw 'Local UI automation is disabled on this main machine. Use an isolated VM/Sandbox, or obtain owner permission for this session before setting WINTODO_ALLOW_LOCAL_UI=1. See docs/guides/testing.md.'
}


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
    if ($LASTEXITCODE -ne 0) { throw "Screenshot failed: $Name" }
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
    if ($authored.Count -eq 0) { throw "No authored controls were audited." }
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

    [DllImport("user32.dll")]
    public static extern uint GetSysColor(int index);

    [DllImport("user32.dll")]
    public static extern bool SetSysColors(int count, int[] indices, uint[] colors);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    public static extern int SHLoadIndirectString(
        string source, System.Text.StringBuilder output, uint size, IntPtr reserved);
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
$highContrastRegistryPath = 'HKCU:\Control Panel\Accessibility\HighContrast'
$originalSchemePreference = Get-ItemPropertyValue -LiteralPath $highContrastRegistryPath `
    -Name 'High Contrast Scheme'
$colorIndices = [int[]](0..30)
$originalColors = [uint32[]]@($colorIndices | ForEach-Object {
    [WintodoM8ThemeNative]::GetSysColor($_)
})
# Use the shipped theme files so this covers the four Windows palettes even
# when scheme display names are localised or the current scheme was customised.
$contrastThemes = @(
    @{ Name = "Aquatic"; File = "hc1.theme"; SchemeId = 2107 },
    @{ Name = "Desert"; File = "hcwhite.theme"; SchemeId = 2104 },
    @{ Name = "Dusk"; File = "hc2.theme"; SchemeId = 2108 },
    @{ Name = "Night sky"; File = "hcblack.theme"; SchemeId = 2103 }
)
$checkedColors = @{ Window = 5; WindowText = 8; Hilight = 13; HilightText = 14 }
try {
    foreach ($theme in $contrastThemes) {
        Test-UI "High Contrast $($theme.Name): selected/unselected list and search" {
            $schemeName = [Text.StringBuilder]::new(256)
            if ([WintodoM8ThemeNative]::SHLoadIndirectString(
                "@themeui.dll,-$($theme.SchemeId)", $schemeName, 256, [IntPtr]::Zero) -ne 0) {
                throw "Could not resolve the localised contrast scheme name."
            }
            Set-HighContrast `
                -Flags ($originalHighContrast.Flags -band (-bnot $highContrastOn)) `
                -Scheme $originalHighContrast.Scheme
            Set-HighContrast `
                -Flags ($originalHighContrast.Flags -bor $highContrastOn) `
                -Scheme $schemeName.ToString()
            $themePath = Join-Path $env:SystemRoot "Resources\Ease of Access Themes\$($theme.File)"
            $themeText = Get-Content -LiteralPath $themePath -Raw
            # SPI can enable High Contrast while retaining a customised palette.
            # Apply the shipped theme's actual system colours, then verify them.
            $systemColorNames = @('Scrollbar','Background','ActiveTitle','InactiveTitle','Menu',
                'Window','WindowFrame','MenuText','WindowText','TitleText','ActiveBorder','InactiveBorder',
                'AppWorkspace','Hilight','HilightText','ButtonFace','ButtonShadow','GrayText','ButtonText',
                'InactiveTitleText','ButtonHilight','ButtonDkShadow','ButtonLight','InfoText','InfoWindow',
                '', 'HotTrackingColor','GradientActiveTitle','GradientInactiveTitle','MenuHilight','MenuBar')
            $paletteIndices = [Collections.Generic.List[int]]::new()
            $paletteValues = [Collections.Generic.List[uint32]]::new()
            for ($index = 0; $index -lt $systemColorNames.Count; $index++) {
                if (-not $systemColorNames[$index]) { continue }
                $color = [regex]::Match($themeText, "(?m)^$($systemColorNames[$index])=(\d+) (\d+) (\d+)\r?$")
                if (-not $color.Success) { continue }
                $paletteIndices.Add($index)
                $paletteValues.Add([uint32]([int]$color.Groups[1].Value -bor
                    ([int]$color.Groups[2].Value -shl 8) -bor ([int]$color.Groups[3].Value -shl 16)))
            }
            if (-not [WintodoM8ThemeNative]::SetSysColors($paletteIndices.Count,
                    $paletteIndices.ToArray(), $paletteValues.ToArray())) { throw 'Could not apply shipped contrast colours.' }
            Start-Sleep -Milliseconds 300
            foreach ($colorName in $checkedColors.Keys) {
                $match = [regex]::Match($themeText, "(?m)^$colorName=(\d+) (\d+) (\d+)\r?$")
                if (-not $match.Success) { throw "Theme colour missing: $colorName" }
                $expectedColor = [uint32]([int]$match.Groups[1].Value -bor
                    ([int]$match.Groups[2].Value -shl 8) -bor
                    ([int]$match.Groups[3].Value -shl 16))
                if ([WintodoM8ThemeNative]::GetSysColor($checkedColors[$colorName]) -ne $expectedColor) {
                    throw "$($theme.Name) system colour $colorName was not applied."
                }
            }
            if (-not ((Get-HighContrast).Flags -band $highContrastOn)) {
                throw "High Contrast is not enabled."
            }
            winapp ui invoke "Todo.Nav.MyDay" -w $mainHwnd | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "Could not select My Day." }
            Assert-AuthoredControlsHaveIds
            $prefix = "05-high-contrast-" + $theme.Name.Replace(" ", "-").ToLowerInvariant()
            Take-Screenshot "$prefix-unselected.png"
            Invoke-UserList "M8 Gate"
            winapp ui wait-for "Todo.TitleBar.SearchBox" -w $mainHwnd -t 5000 | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "Search is missing." }
            Assert-AuthoredControlsHaveIds
            Take-Screenshot "$prefix-selected.png"
            # UX-14: include the non-default chip and keyboard reset in every palette.
            winapp ui invoke 'Todo.List.SortButton' -w $mainHwnd | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Could not open Sort in High Contrast.' }
            winapp ui invoke 'Todo.List.Sort.Reverse' -a $AppPid | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Could not reverse sorting in High Contrast.' }
            winapp ui wait-for 'Todo.List.SortLabel' -w $mainHwnd --value 'Sorted by Manual, descending' -t 5000 | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Non-default sort chip is missing in High Contrast.' }
            Assert-AuthoredControlsHaveIds
            winapp ui focus 'Todo.List.Sort.Reset' -w $mainHwnd | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Sort reset cannot receive keyboard focus.' }
            Take-Screenshot "$prefix-sort-reset-focused.png"
            winapp ui send-keys 'space' -w $mainHwnd --via send-input | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Keyboard sort reset failed.' }
            winapp ui wait-for 'Todo.List.Sort.Reset' -w $mainHwnd --gone -t 3000 | Out-Null
            if ($LASTEXITCODE -ne 0) { throw 'Sort chip did not disappear after reset.' }
            foreach ($controlId in @("Todo.List.IconPickerButton", "Todo.List.TitleButton")) {
                winapp ui focus $controlId -w $mainHwnd | Out-Null
                if ($LASTEXITCODE -ne 0) { throw "Could not focus $controlId." }
                Take-Screenshot "$prefix-$($controlId.Split('.')[-1])-focused.png"
            }
        }
    }
} finally {
    Set-HighContrast `
        -Flags $originalHighContrast.Flags `
        -Scheme $originalHighContrast.Scheme
    if ([string]::IsNullOrEmpty($originalHighContrast.Scheme)) {
        # SPI_SETHIGHCONTRAST retains the last scheme even for an explicit empty
        # string. Restore a fresh profile's empty preference after disabling HC.
        Set-ItemProperty -LiteralPath 'HKCU:\Control Panel\Accessibility\HighContrast' `
            -Name 'High Contrast Scheme' -Value $originalSchemePreference -ErrorAction Stop
    }
    if (-not [WintodoM8ThemeNative]::SetSysColors(31, $colorIndices, $originalColors)) {
        throw "Could not restore original system colours."
    }
}

$restoredHighContrast = Get-HighContrast
$restoredSchemePreference = Get-ItemPropertyValue -LiteralPath $highContrastRegistryPath `
    -Name 'High Contrast Scheme'
@{ original = $originalHighContrast; restored = $restoredHighContrast
   originalPreference = $originalSchemePreference; restoredPreference = $restoredSchemePreference } |
    ConvertTo-Json | Set-Content (Join-Path $ArtifactDirectory 'contrast-restoration.json')
$restoreDifferences = @(
    if ($restoredHighContrast.Flags -ne $originalHighContrast.Flags) {
        "flags 0x{0:X} -> 0x{1:X}" -f $originalHighContrast.Flags, $restoredHighContrast.Flags
    }
    # Windows cannot clear the cached SPI scheme name. With HC originally off
    # and no scheme selected, verify the restored preference instead. Flags and
    # every system colour still require exact equality; named schemes do too.
    $emptyInactiveScheme = [string]::IsNullOrEmpty($originalHighContrast.Scheme) -and
        -not ($originalHighContrast.Flags -band $highContrastOn)
    if (-not $emptyInactiveScheme -and $restoredHighContrast.Scheme -ne $originalHighContrast.Scheme) {
        "scheme '$($originalHighContrast.Scheme)' -> '$($restoredHighContrast.Scheme)'"
    }
    if ($restoredSchemePreference -ne $originalSchemePreference) {
        "scheme preference '$originalSchemePreference' -> '$restoredSchemePreference'"
    }
    $colorIndices | Where-Object {
        [WintodoM8ThemeNative]::GetSysColor($_) -ne $originalColors[$_]
    } | ForEach-Object { "colour $_" }
)
if ($restoreDifferences.Count -ne 0) {
    $fail++
    $results += @{
        name = "Original High Contrast state is restored"
        status = "FAIL"
        detail = "Not restored: $($restoreDifferences -join ', ')."
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
