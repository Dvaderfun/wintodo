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

New-Item -ItemType Directory -Force -Path $ArtifactDirectory | Out-Null

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

function Add-Step {
    param([string]$Summary)

    winapp ui set-value "Todo.Detail.NewStep" $Summary -a $AppPid
    winapp ui send-keys "enter" `
        --target "Todo.Detail.NewStep" `
        -a $AppPid `
        --via send-input
    Start-Sleep -Milliseconds 350
    winapp ui wait-for "Todo.Detail.Title" -a $AppPid -t 3000
}

function Get-StepRows {
    return @(
        Get-SearchMatches "Todo.Detail.Step" |
            Where-Object {
                $_.type -eq "ListItem" -and
                $_.automationId -match "^Todo\.Detail\.Step\.[0-9a-f-]+$"
            } |
            Sort-Object y
    )
}

$today = [DateTime]::Today
$daysUntilTuesday = (([int][DayOfWeek]::Tuesday - [int]$today.DayOfWeek + 7) % 7)
if ($daysUntilTuesday -eq 0) {
    $daysUntilTuesday = 7
}

$firstDue = $today.AddDays($daysUntilTuesday)
$nextDue = $firstDue.AddDays(14)
$culture = [System.Globalization.CultureInfo]::GetCultureInfo('en-US')
$expectedDueLabel = "Due {0}" -f $nextDue.ToString("ddd, d MMM", $culture)
$tuesdayName = $culture.DateTimeFormat.GetAbbreviatedDayName([DayOfWeek]::Tuesday)
$expectedRepeatLabel = "Repeats every 2 weeks on $tuesdayName"

Test-UI "Detail XAML is deferred until first open" {
    winapp ui wait-for "Todo.Detail.InstanceCount" `
        -a $AppPid `
        --value "0" `
        -t 5000
    winapp ui invoke "Todo.Nav.NewListButton" -a $AppPid
    winapp ui wait-for "Todo.Nav.NewListNameBox" -a $AppPid -t 3000
    Start-Sleep -Milliseconds 300
    winapp ui set-value "Todo.Nav.NewListNameBox" "M4 Gate" -a $AppPid
    winapp ui wait-for "Todo.Nav.NewListNameBox" `
        -a $AppPid `
        --value "M4 Gate" `
        -t 3000
    winapp ui send-keys "enter" `
        --target "Todo.Nav.NewListNameBox" `
        -a $AppPid `
        --via send-input
    winapp ui wait-for "Todo.List.AddTaskBox" `
        -a $AppPid `
        --property IsEnabled `
        --value True `
        -t 5000

    winapp ui click "Todo.List.AddTaskBox" -a $AppPid
    winapp ui wait-for "Todo.Composer.AddButton" -a $AppPid -t 3000
    winapp ui set-value "Todo.List.AddTaskBox" "M4 recurring task" -a $AppPid
    winapp ui wait-for "Todo.Composer.AddButton" `
        -a $AppPid `
        --property IsEnabled `
        --value True `
        -t 3000
    winapp ui invoke "Todo.Composer.AddButton" -a $AppPid
    $row = $null
    for ($attempt = 0; $attempt -lt 10 -and -not $row; $attempt++) {
        Start-Sleep -Milliseconds 300
        $row = Get-TaskRow "M4 recurring task"
    }
    if (-not $row) {
        throw "Created task row was not found."
    }

    winapp ui invoke $row.automationId -a $AppPid
    winapp ui wait-for "Todo.Detail.Title" `
        -a $AppPid `
        --value "M4 recurring task" `
        -t 5000
    winapp ui wait-for "Todo.Detail.InstanceCount" `
        -a $AppPid `
        --value "1" `
        -t 5000
}

Test-UI "Created date uses the app's English language" {
    $expectedCreated = 'Created {0}' -f $today.ToString('ddd, d MMM', $culture)
    winapp ui wait-for $expectedCreated -a $AppPid -t 3000
}

Test-UI "Enter in detail editors preserves the pane and draft text" {
    $draftTitle = "M4 Enter-safe title"
    winapp ui set-value "Todo.Detail.Title" $draftTitle -a $AppPid
    winapp ui send-keys "enter" `
        --target "Todo.Detail.Title" `
        -a $AppPid `
        --via send-input
    winapp ui wait-for "Todo.Detail.Title" `
        -a $AppPid `
        --value $draftTitle `
        --contains `
        -t 3000

    $draftNote = "M4 Enter-safe note"
    winapp ui set-value "Todo.Detail.Note" $draftNote -a $AppPid
    winapp ui send-keys "enter" `
        --target "Todo.Detail.Note" `
        -a $AppPid `
        --via send-input
    winapp ui wait-for "Todo.Detail.Note" `
        -a $AppPid `
        --value $draftNote `
        --contains `
        -t 3000
}

Test-UI "Title note importance My Day and reminder update" {
    winapp ui set-value "Todo.Detail.Title" "M4 recurring task edited" -a $AppPid
    winapp ui set-value "Todo.Detail.Note" "M4 note survives restart" -a $AppPid
    winapp ui invoke "Todo.Detail.Importance" -a $AppPid
    winapp ui invoke "Todo.Detail.MyDay" -a $AppPid
    winapp ui invoke "Todo.Detail.Reminder" -a $AppPid
    Start-Sleep -Milliseconds 250
    winapp ui invoke "Todo.Detail.Reminder.Tomorrow" -a $AppPid
    Start-Sleep -Milliseconds 900

    winapp ui wait-for "Todo.Detail.Title" `
        -a $AppPid `
        --value "M4 recurring task edited" `
        -t 3000
    winapp ui wait-for "Todo.Detail.Note" `
        -a $AppPid `
        --value "M4 note survives restart" `
        -t 3000
    winapp ui wait-for "Todo.Detail.Importance" `
        -a $AppPid `
        --value "On" `
        -t 3000
    winapp ui wait-for "Todo.Detail.ClearMyDay" -a $AppPid -t 3000
    winapp ui wait-for "Todo.Detail.ClearReminder" -a $AppPid -t 3000
}

Test-UI "Steps add edit complete delete and reorder" {
    Add-Step "First step"
    Add-Step "Second step"
    Add-Step "Delete this step"

    $rows = Get-StepRows
    if ($rows.Count -ne 3) {
        throw "Expected three steps, found $($rows.Count)."
    }

    $firstEditor = Get-SearchMatches $rows[0].automationId |
        Where-Object { $_.type -eq "Edit" } |
        Select-Object -First 1
    winapp ui set-value $firstEditor.selector "Edited first step" -a $AppPid

    $secondUid = $rows[1].automationId.Substring("Todo.Detail.Step.".Length)
    winapp ui invoke "Todo.Detail.StepComplete.$secondUid" -a $AppPid

    $thirdUid = $rows[2].automationId.Substring("Todo.Detail.Step.".Length)
    winapp ui invoke "Todo.Detail.StepDelete.$thirdUid" -a $AppPid
    Start-Sleep -Milliseconds 500

    $rows = Get-StepRows
    if ($rows.Count -ne 2) {
        throw "Step deletion did not leave two steps."
    }

    $dragX = $rows[1].x + $rows[1].height - 3
    $source = "{0},{1}" -f $dragX, ($rows[1].y + ($rows[1].height / 2))
    $destination = "{0},{1}" -f $dragX, ($rows[0].y + 8)
    winapp ui drag $source $destination `
        -a $AppPid `
        --hold-ms 800 `
        --dwell-ms 800
    Start-Sleep -Milliseconds 500

    $reordered = Get-StepRows
    $reorderedFirstUid =
        $reordered[0].automationId.Substring("Todo.Detail.Step.".Length)
    if ($reorderedFirstUid -ne $secondUid) {
        throw "Completed second step did not move before the first step."
    }
}

Test-UI "Biweekly Tuesday recurrence advances exactly 14 days" {
    winapp ui invoke "Todo.Detail.Due" -a $AppPid
    Start-Sleep -Milliseconds 250
    winapp ui invoke "Todo.Detail.Due.Pick" -a $AppPid
    winapp ui wait-for "Todo.Detail.DueDatePicker" -a $AppPid -t 3000
    winapp ui invoke "Todo.Detail.DueDatePicker" -a $AppPid
    Start-Sleep -Milliseconds 250

    $tuesdayHeader = Get-SearchMatches "WeekDay3" |
        Select-Object -First 1
    # Day numbers also match task uids; filter in UIA before the result cap.
    $calendarDays = (winapp ui search $firstDue.Day.ToString() `
        -a $AppPid --type DataItem --class-name CalendarViewDayItem --json |
        ConvertFrom-Json).matches
    $calendarTarget = $calendarDays |
        Where-Object {
            $_.type -eq "DataItem" -and
            $_.className -eq "CalendarViewDayItem" -and
            $_.name -eq $firstDue.Day.ToString() -and
            -not $_.isOffscreen
        } |
        Sort-Object {
            $itemCenter = $_.x + ($_.width / 2)
            $headerCenter = $tuesdayHeader.x + ($tuesdayHeader.width / 2)
            [Math]::Abs($itemCenter - $headerCenter)
        } |
        Select-Object -First 1

    if (-not $calendarTarget) {
        throw "Could not locate the upcoming Tuesday in the due date calendar."
    }

    winapp ui invoke $calendarTarget.selector -a $AppPid
    Start-Sleep -Milliseconds 300
    winapp ui invoke "Todo.Detail.ApplyDue" -a $AppPid
    winapp ui wait-for "Todo.Detail.ClearDue" -a $AppPid -t 3000

    winapp ui invoke "Todo.Detail.Repeat" -a $AppPid
    winapp ui wait-for "Todo.Detail.RepeatOption.Custom" -a $AppPid -t 3000
    winapp ui invoke "Todo.Detail.RepeatOption.Custom" -a $AppPid
    winapp ui wait-for "Todo.Detail.CustomInterval" -a $AppPid -t 3000
    winapp ui set-value "Todo.Detail.CustomInterval" "2" -a $AppPid
    winapp ui invoke "Todo.Detail.Weekday.Tuesday" -a $AppPid
    Start-Sleep -Milliseconds 500
    winapp ui send-keys "escape" -a $AppPid --via send-input
    Start-Sleep -Milliseconds 300

    winapp ui wait-for "Todo.Detail.Repeat" `
        -a $AppPid `
        --value $expectedRepeatLabel `
        -t 3000
    winapp ui invoke "Todo.Detail.Complete" -a $AppPid
    winapp ui wait-for "Todo.Detail.Due" `
        -a $AppPid `
        --value $expectedDueLabel `
        -t 5000
    winapp ui wait-for "Todo.Detail.Complete" `
        -a $AppPid `
        --value "Off" `
        -t 3000
}

Test-UI "Attachment picker copies a local file" {
    $attachmentPath = Join-Path $ArtifactDirectory "m4-attachment.txt"
    [System.IO.File]::WriteAllText(
        $attachmentPath,
        "M4 attachment round-trip",
        [System.Text.Encoding]::UTF8)

    $mainWindow = winapp ui list-windows -a $AppPid --json 2>$null |
        ConvertFrom-Json |
        Where-Object { $_.title -eq "Wintodo" } |
        Select-Object -First 1
    winapp ui invoke "Todo.Detail.AddFile" -a $AppPid
    $picker = $null
    for ($attempt = 0; $attempt -lt 10; $attempt++) {
        Start-Sleep -Milliseconds 500
        $picker = winapp ui list-windows --json 2>$null |
            ConvertFrom-Json |
            Where-Object {
                $_.ownerHwnd -eq $mainWindow.hwnd -and
                $_.hwnd -ne $mainWindow.hwnd
            } |
            Select-Object -First 1
        if ($picker) {
            break
        }
    }
    if (-not $picker) {
        throw "File picker window was not found."
    }

    $fileNameEdit = (
        winapp ui search "1148" -w $picker.hwnd --json |
            ConvertFrom-Json
    ).matches |
        Where-Object { $_.type -eq "Edit" } |
        Select-Object -First 1
    $openButton = (
        winapp ui search "1" -w $picker.hwnd --json |
            ConvertFrom-Json
    ).matches |
        Where-Object {
            $_.type -eq "SplitButton" -and
            $_.automationId -eq "1"
        } |
        Select-Object -First 1
    if (-not $fileNameEdit -or -not $openButton) {
        throw "File picker controls were not found."
    }

    winapp ui set-value $fileNameEdit.selector $attachmentPath -w $picker.hwnd
    winapp ui click $openButton.selector -w $picker.hwnd
    $attachment = $null
    for ($attempt = 0; $attempt -lt 10 -and -not $attachment; $attempt++) {
        Start-Sleep -Milliseconds 300
        $attachment = Get-SearchMatches "m4-attachment.txt" |
            Where-Object { $_.type -eq "Button" } |
            Select-Object -First 1
    }
    if (-not $attachment) {
        throw "Copied attachment was not shown in the detail pane."
    }
}

winapp ui screenshot -a $AppPid -o (
    Join-Path $ArtifactDirectory "01-detail-complete.png"
) 2>$null

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

Test-UI "Escape closes the detail pane" {
    winapp ui send-keys "escape" -a $AppPid --via send-input
    winapp ui wait-for "Todo.Detail.Title" -a $AppPid --gone -t 3000
}

$resultPath = Join-Path $ArtifactDirectory "phase-one-results.json"
$results | ConvertTo-Json | Out-File $resultPath
Write-Host "Phase one passed: $pass | Failed: $fail"
$results |
    Where-Object { $_.status -eq "FAIL" } |
    ForEach-Object {
        Write-Host "FAIL: $($_.name) - $($_.detail)" -ForegroundColor Red
    }

if ($fail -gt 0) {
    exit 1
}
