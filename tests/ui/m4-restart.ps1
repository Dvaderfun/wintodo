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


$ErrorActionPreference = "Stop"

function Get-SearchMatches {
    param([string]$Selector)

    return @(
        (winapp ui search $Selector -a $AppPid --json 2>$null |
            ConvertFrom-Json).matches
    )
}

$today = [DateTime]::Today
$daysUntilTuesday = (([int][DayOfWeek]::Tuesday - [int]$today.DayOfWeek + 7) % 7)
if ($daysUntilTuesday -eq 0) {
    $daysUntilTuesday = 7
}

$nextDue = $today.AddDays($daysUntilTuesday + 14)
$culture = [System.Globalization.CultureInfo]::GetCultureInfo('en-US')
$expectedDueLabel = "Due {0}" -f $nextDue.ToString("ddd, d MMM", $culture)
$tuesdayName = $culture.DateTimeFormat.GetAbbreviatedDayName([DayOfWeek]::Tuesday)
$expectedRepeatLabel = "Repeats every 2 weeks on $tuesdayName"

winapp ui wait-for "Todo.Detail.InstanceCount" `
    -a $AppPid `
    --value "0" `
    -t 5000
winapp ui wait-for "Todo.List.TitleButton" `
    -a $AppPid `
    --value "M4 Gate" `
    -t 5000 2>$null

$row = Get-SearchMatches "M4 recurring task edited" |
    Where-Object {
        $_.type -eq "ListItem" -and
        $_.automationId -match "^Todo\.Row\.[0-9a-f-]+$"
    } |
    Select-Object -First 1
if (-not $row) {
    throw "Restarted task row was not found."
}

winapp ui invoke $row.automationId -a $AppPid
winapp ui wait-for "Todo.Detail.InstanceCount" `
    -a $AppPid `
    --value "1" `
    -t 5000
winapp ui wait-for "Todo.Detail.Title" `
    -a $AppPid `
    --value "M4 recurring task edited" `
    -t 5000
winapp ui wait-for "Todo.Detail.Note" `
    -a $AppPid `
    --value "M4 note saved on close" `
    -t 5000
if ($LASTEXITCODE -ne 0) { throw "The note typed immediately before closing was not saved." }
winapp ui wait-for "Todo.Detail.Importance" `
    -a $AppPid `
    --value "On" `
    -t 5000
winapp ui wait-for "Todo.Detail.ClearMyDay" -a $AppPid -t 5000
winapp ui wait-for "Todo.Detail.ClearReminder" -a $AppPid -t 5000
winapp ui wait-for "Todo.Detail.Due" `
    -a $AppPid `
    --value $expectedDueLabel `
    -t 5000
winapp ui wait-for "Todo.Detail.Repeat" `
    -a $AppPid `
    --value $expectedRepeatLabel `
    -t 5000

$steps = @(
    Get-SearchMatches "Todo.Detail.Step" |
        Where-Object {
            $_.type -eq "ListItem" -and
            $_.automationId -match "^Todo\.Detail\.Step\.[0-9a-f-]+$"
        } |
        Sort-Object y
)
if ($steps.Count -ne 2) {
    throw "Expected two persisted steps, found $($steps.Count)."
}
if ($steps[0].name -ne "Second step" -or $steps[1].name -ne "Edited first step") {
    throw "Persisted step order or titles were incorrect."
}

$completedUid = $steps[0].automationId.Substring("Todo.Detail.Step.".Length)
winapp ui wait-for "Todo.Detail.StepComplete.$completedUid" `
    -a $AppPid `
    --value "On" `
    -t 5000

$attachment = Get-SearchMatches "m4-attachment.txt" |
    Where-Object { $_.type -eq "Button" } |
    Select-Object -First 1
if (-not $attachment) {
    throw "Persisted attachment was not found after restart."
}

winapp ui screenshot -a $AppPid -o (
    Join-Path $ArtifactDirectory "02-restart-roundtrip.png"
) 2>$null

$removeButton = Get-SearchMatches "Todo.Detail.AttachmentRemove." |
    Where-Object { $_.type -eq "Button" } |
    Select-Object -First 1
if (-not $removeButton) {
    throw "Attachment remove button was not found."
}
winapp ui invoke $removeButton.selector -a $AppPid
Start-Sleep -Milliseconds 500
if (Get-SearchMatches "m4-attachment.txt") {
    throw "Attachment remained visible after removal."
}

# UIA can remove the attachment while the restarted window is still in the background.
winapp ui focus "Todo.Detail.Close" -a $AppPid
if ($LASTEXITCODE -ne 0) { throw "Could not focus Close in the restarted detail pane." }
winapp ui send-keys "escape" -a $AppPid --via send-input
if ($LASTEXITCODE -ne 0) { throw "Could not send Escape to the restarted detail pane." }
winapp ui wait-for "Todo.Detail.Title" -a $AppPid --gone -t 3000
if ($LASTEXITCODE -ne 0) { throw "Escape did not dismiss the restarted detail pane." }

@{
    name = "M4 restart round-trip"
    status = "PASS"
} | ConvertTo-Json | Out-File (
    Join-Path $ArtifactDirectory "restart-results.json"
)
Write-Host "M4 restart round-trip: PASS"
