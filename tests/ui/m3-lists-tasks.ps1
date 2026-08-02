param(
    [Parameter(Mandatory)]
    [int]$AppPid
)

$ErrorActionPreference = "Continue"
$pass = 0
$fail = 0
$results = @()
$artifactDirectory = Join-Path $PSScriptRoot "artifacts\m3"

New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null

function Test-UI {
    param(
        [string]$Name,
        [scriptblock]$Script
    )

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
        (winapp ui search $Selector -a $AppPid --json 2>$null |
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

Test-UI "Create list inline" {
    winapp ui invoke "Todo.Nav.NewListButton" -a $AppPid
    winapp ui wait-for "Todo.Nav.NewListNameBox" -a $AppPid -t 3000
    winapp ui set-value "Todo.Nav.NewListNameBox" "M3 Gate" -a $AppPid
    winapp ui send-keys "enter" `
        --target "Todo.Nav.NewListNameBox" `
        -a $AppPid `
        --via send-input
    winapp ui wait-for "Todo.List.TitleButton" `
        -a $AppPid `
        --value "M3 Gate" `
        -t 5000
}

Test-UI "Add five tasks" {
    winapp ui focus "Todo.List.AddTaskBox" -a $AppPid
    winapp ui wait-for "Todo.Composer.AddButton" -a $AppPid -t 3000
    1..5 | ForEach-Object {
        winapp ui set-value "Todo.List.AddTaskBox" "Gate task $_" -a $AppPid
        winapp ui invoke "Todo.Composer.AddButton" -a $AppPid
    }

    foreach ($index in 1..5) {
        if (-not (Get-TaskRow "Gate task $index")) {
            throw "Gate task $index was not found."
        }
    }
}

winapp ui screenshot -a $AppPid -o (
    Join-Path $artifactDirectory "04-five-tasks.png"
) 2>$null

Test-UI "Complete two tasks" {
    foreach ($index in 1..2) {
        $row = Get-TaskRow "Gate task $index"
        if (-not $row) {
            throw "Gate task $index row was not found."
        }

        $uid = $row.automationId.Substring("Todo.Row.".Length)
        winapp ui click "Todo.Row.CompleteToggle.$uid" -a $AppPid
        Start-Sleep -Milliseconds 500
    }

    winapp ui wait-for "Todo.List.CompletedHeader" -a $AppPid -t 5000
}

Test-UI "Completed section expands" {
    # Exercise the pointer route: rebuilding the group from this click used to
    # destroy the live header and fail-fast inside Microsoft.UI.Xaml.
    winapp ui click "Todo.List.CompletedHeader" -a $AppPid
    Start-Sleep -Milliseconds 400
    if (-not (Get-TaskRow "Gate task 1")) {
        throw "Completed task was not revealed."
    }
}

Test-UI "Reorder remaining tasks" {
    $task5 = Get-TaskRow "Gate task 5"
    $task3 = Get-TaskRow "Gate task 3"
    if (-not $task5 -or -not $task3) {
        throw "The reorder source or destination row was not found."
    }

    $source = "{0},{1}" -f ($task5.x + 8), ($task5.y + ($task5.height / 2))
    $destination = "{0},{1}" -f ($task3.x + 8), ($task3.y + 12)
    winapp ui drag $source $destination `
        -a $AppPid `
        --hold-ms 1000 `
        --dwell-ms 1000
    Start-Sleep -Milliseconds 500

    $task5 = Get-TaskRow "Gate task 5"
    $task3 = Get-TaskRow "Gate task 3"
    if ($task5.y -ge $task3.y) {
        throw "Gate task 5 was not moved before Gate task 3."
    }
}

Test-UI "Rename list with F2" {
    winapp ui send-keys "f2" -a $AppPid --via send-input
    winapp ui wait-for "Todo.List.RenameBox" -a $AppPid -t 3000
    winapp ui set-value "Todo.List.RenameBox" "M3 Gate renamed" -a $AppPid
    winapp ui send-keys "enter" `
        --target "Todo.List.RenameBox" `
        -a $AppPid `
        --via send-input
    winapp ui wait-for "Todo.List.TitleButton" `
        -a $AppPid `
        --value "M3 Gate renamed" `
        -t 3000
    winapp ui send-keys "f2" -a $AppPid --via send-input
    winapp ui set-value "Todo.List.RenameBox" "M3 Gate" -a $AppPid
    winapp ui send-keys "enter" `
        --target "Todo.List.RenameBox" `
        -a $AppPid `
        --via send-input
}

Test-UI "Change list theme" {
    winapp ui invoke "Todo.List.MenuButton" -a $AppPid
    Start-Sleep -Milliseconds 300
    winapp ui invoke "Todo.List.Menu.ChangeTheme" -a $AppPid
    Start-Sleep -Milliseconds 300
    winapp ui invoke "Todo.List.Theme.Red" -a $AppPid
}

Test-UI "Delete task offers undo" {
    winapp ui focus "Todo.List.AddTaskBox" -a $AppPid
    winapp ui set-value "Todo.List.AddTaskBox" "Undo me" -a $AppPid
    winapp ui invoke "Todo.Composer.AddButton" -a $AppPid
    $row = Get-TaskRow "Undo me"
    if (-not $row) {
        throw "Undo task was not created."
    }

    winapp ui invoke $row.automationId -a $AppPid
    winapp ui send-keys "delete" -a $AppPid --via send-input
    winapp ui wait-for "Todo.List.UndoButton" -a $AppPid -t 3000
    winapp ui invoke "Todo.List.UndoButton" -a $AppPid
    Start-Sleep -Milliseconds 400
    if (-not (Get-TaskRow "Undo me")) {
        throw "Undo did not restore the task."
    }

    $restored = Get-TaskRow "Undo me"
    winapp ui invoke $restored.automationId -a $AppPid
    winapp ui send-keys "delete" -a $AppPid --via send-input
}

Test-UI "Create list group" {
    winapp ui invoke "Todo.Nav.NewGroupButton" -a $AppPid
    winapp ui wait-for "Todo.Nav.NewGroupNameBox" -a $AppPid -t 3000
    winapp ui set-value "Todo.Nav.NewGroupNameBox" "Gate group" -a $AppPid
    winapp ui send-keys "enter" `
        --target "Todo.Nav.NewGroupNameBox" `
        -a $AppPid `
        --via send-input
    Start-Sleep -Milliseconds 500
    Start-Sleep -Milliseconds 500
    $groups = Get-SearchMatches "Todo.Nav.Group."
    if ($groups.Count -eq 0) {
        throw "Created group was not found."
    }
}

winapp ui screenshot -a $AppPid -o (
    Join-Path $artifactDirectory "05-core-loop.png"
) 2>$null

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
    $_.type -match "Button|CheckBox|Edit|ListItem|MenuItem" -and
    $_.name -notmatch "Minimize|Maximize|Close|System" -and
    $_.className -notmatch "PopupHost|TitleBar" -and
    -not ($_.type -eq "MenuItem" -and [string]::IsNullOrWhiteSpace($_.className))
})
$missingId = @($interactiveControls | Where-Object {
    [string]::IsNullOrWhiteSpace($_.automationId)
})
if ($missingId.Count -eq 0) {
    $pass++
    $results += @{
        name = "Interactive controls have AutomationIds"
        status = "PASS"
    }
} else {
    $fail++
    $results += @{
        name = "Interactive controls have AutomationIds"
        status = "FAIL"
        detail = "$($missingId.Count) controls lack AutomationIds"
    }
}

$resultPath = Join-Path $artifactDirectory "results.json"
$results | ConvertTo-Json | Out-File $resultPath
Write-Host "Passed: $pass | Failed: $fail"
$results |
    Where-Object { $_.status -eq "FAIL" } |
    ForEach-Object {
        Write-Host "FAIL: $($_.name) - $($_.detail)" -ForegroundColor Red
    }

if ($fail -gt 0) {
    exit 1
}
