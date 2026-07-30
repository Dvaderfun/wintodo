param(
    [Parameter(Mandatory)]
    [int]$AppPid,
    [Parameter(Mandatory)]
    [string]$ArtifactDirectory,
    [Parameter(Mandatory)]
    [string]$ClockPath
)

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
        winapp ui invoke "Todo.Nav.UserLists" -a $AppPid
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
