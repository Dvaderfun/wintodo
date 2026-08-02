param(
    [Parameter(Mandatory)]
    [int]$AppPid,
    [Parameter(Mandatory)]
    [string]$ArtifactDirectory,
    [Parameter(Mandatory)]
    [string]$FixtureDirectory
)

$ErrorActionPreference = "Continue"
$pass = 0
$fail = 0
$results = @()
$mainHwnd = $null

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
        & $Script
        if ($LASTEXITCODE -ne 0) {
            throw "A winapp command returned exit code $LASTEXITCODE."
        }

        $script:pass++
        $script:results += @{ name = $Name; status = "PASS" }
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

function Wait-ForElement {
    param(
        [string]$Selector,
        [string]$Value,
        [switch]$Contains
    )

    $arguments = @("ui", "wait-for", $Selector, "-w", $mainHwnd, "-t", "10000")
    if ($Value) {
        $arguments += @("--value", $Value)
    }
    if ($Contains) {
        $arguments += "--contains"
    }

    & winapp @arguments | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Element '$Selector' did not reach the expected state."
    }
}

function Invoke-AppElement {
    param([string]$Selector)

    winapp ui invoke $Selector -w $mainHwnd | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Could not invoke '$Selector'."
    }
}

function Get-SearchMatches {
    param(
        [string]$Selector,
        [long]$Window = $mainHwnd
    )

    $json = winapp ui search $Selector -w $Window --json 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $json) {
        $global:LASTEXITCODE = 0
        return @()
    }

    return @(($json | ConvertFrom-Json).matches)
}

function Wait-ForDialogWindow {
    param([string]$Title)

    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        Start-Sleep -Milliseconds 250
        $window = winapp ui list-windows -a $AppPid --json 2>$null |
            ConvertFrom-Json |
            Where-Object { $_.title -eq $Title } |
            Select-Object -First 1
        if ($window) {
            return [long]$window.hwnd
        }
    }

    throw "The '$Title' file dialog was not found."
}

function Set-FileDialogPath {
    param(
        [long]$DialogHwnd,
        [string]$Path
    )

    $edit = Get-SearchMatches "File name:" $DialogHwnd |
        Where-Object { $_.type -eq "Edit" } |
        Select-Object -First 1
    if (-not $edit) {
        throw "The file-name edit control was not found."
    }

    winapp ui focus $edit.selector -w $DialogHwnd | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "The file-name edit control could not be focused."
    }
    winapp ui send-keys "home shift+end" -w $DialogHwnd --via send-input | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "The existing file name could not be selected."
    }
    winapp ui send-keys $Path -w $DialogHwnd --via send-input --verbatim | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "The file path could not be typed."
    }
}

function Select-ImportFile {
    param([string]$Path)

    Invoke-AppElement "Todo.Settings.ImportIcal"
    $pickerHwnd = Wait-ForDialogWindow "Open"
    Set-FileDialogPath $pickerHwnd $Path
    $open = Get-SearchMatches "Open" $pickerHwnd |
        Where-Object {
            $_.type -eq "SplitButton" -and $_.automationId -eq "1"
        } |
        Select-Object -First 1
    if (-not $open) {
        throw "The Open button was not found."
    }

    winapp ui invoke $open.selector -w $pickerHwnd | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "The selected file could not be opened."
    }
}

function Save-WithPicker {
    param([string]$Path)

    if (Test-Path $Path) {
        Remove-Item -LiteralPath $Path -Force
    }

    $pickerHwnd = Wait-ForDialogWindow "Save As"
    Set-FileDialogPath $pickerHwnd $Path
    $save = Get-SearchMatches "Save" $pickerHwnd |
        Where-Object {
            $_.type -eq "Button" -and $_.automationId -eq "1"
        } |
        Select-Object -First 1
    if (-not $save) {
        throw "The Save button was not found."
    }

    winapp ui invoke $save.selector -w $pickerHwnd | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "The selected target could not be saved."
    }

    for ($attempt = 0; $attempt -lt 20 -and -not (Test-Path $Path); $attempt++) {
        Start-Sleep -Milliseconds 250
    }
    if (-not (Test-Path $Path)) {
        throw "The expected export '$Path' was not created."
    }
}

function Get-UserListItem {
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
        throw "The '$Name' navigation item was not found."
    }

    return $item
}

function Invoke-UserList {
    param([string]$Name)

    $item = Get-UserListItem $Name
    winapp ui invoke $item.selector -w $mainHwnd | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "The '$Name' navigation item could not be invoked."
    }
}

$nextcloudFixture = Join-Path $FixtureDirectory "nextcloud.ics"
$malformedFixture = Join-Path $FixtureDirectory "malformed.ics"
$listExportPath = Join-Path $ArtifactDirectory "nextcloud-work.ics"
$allExportPath = Join-Path $ArtifactDirectory "wintodo-backup.zip"

Test-UI "Settings exposes the iCalendar transfer commands" {
    Wait-ForElement "Todo.Nav.Settings"
    Invoke-AppElement "Todo.Nav.Settings"
    Wait-ForElement "Todo.Settings.ImportIcal"
    Wait-ForElement "Todo.Settings.ExportAll"
    winapp ui screenshot -w $mainHwnd -o (
        Join-Path $ArtifactDirectory "01-settings.png"
    ) 2>$null | Out-Null
}

Test-UI "Valid iCalendar import previews and applies atomically" {
    Select-ImportFile $nextcloudFixture
    Wait-ForElement `
        "Todo.Dialog.ImportPreview.Message" `
        "Tasks ready: 2. Lists: 1. Existing tasks to replace: 0."
    winapp ui screenshot -w $mainHwnd -o (
        Join-Path $ArtifactDirectory "02-import-preview.png"
    ) 2>$null | Out-Null
    Invoke-AppElement "Todo.Dialog.ImportPreview.Import"
    Wait-ForElement "Todo.Transfer.Status"
    Get-UserListItem "Nextcloud work" | Out-Null
    Invoke-UserList "Nextcloud work"
    Wait-ForElement "Prepare release"
    winapp ui screenshot -w $mainHwnd -o (
        Join-Path $ArtifactDirectory "03-imported-list.png"
    ) 2>$null | Out-Null
}

Test-UI "Conflict preview reports existing task UIDs" {
    Invoke-AppElement "Todo.Nav.Settings"
    Wait-ForElement "Todo.Settings.ImportIcal"
    Select-ImportFile $nextcloudFixture
    Wait-ForElement `
        "Todo.Dialog.ImportPreview.Message" `
        "Tasks ready: 2. Lists: 1. Existing tasks to replace: 2."
    winapp ui screenshot -w $mainHwnd -o (
        Join-Path $ArtifactDirectory "04-conflict-preview.png"
    ) 2>$null | Out-Null
    Invoke-AppElement "Todo.Dialog.ImportPreview.Cancel"
}

Test-UI "A list exports as an iCalendar file" {
    Invoke-UserList "Nextcloud work"
    Wait-ForElement "Todo.List.MenuButton"
    Invoke-AppElement "Todo.List.MenuButton"
    Wait-ForElement "Todo.List.Menu.Export"
    Invoke-AppElement "Todo.List.Menu.Export"
    Save-WithPicker $listExportPath
    $content = [System.IO.File]::ReadAllText($listExportPath)
    if ($content -notmatch "BEGIN:VTODO" -or
        $content -notmatch "X-OC-HIDESUBTASKS:0") {
        throw "The list export is missing task or preserved extension data."
    }
    Wait-ForElement "Todo.Transfer.Status"
}

Test-UI "All lists export as a ZIP of iCalendar files" {
    Invoke-AppElement "Todo.Nav.Settings"
    Wait-ForElement "Todo.Settings.ExportAll"
    Invoke-AppElement "Todo.Settings.ExportAll"
    Save-WithPicker $allExportPath
    $archive = [System.IO.Compression.ZipFile]::OpenRead($allExportPath)
    try {
        $entry = $archive.Entries |
            Where-Object { $_.FullName -eq "Nextcloud work.ics" } |
            Select-Object -First 1
        if (-not $entry) {
            throw "The all-lists export is missing Nextcloud work.ics."
        }
        $reader = [System.IO.StreamReader]::new($entry.Open())
        try {
            $content = $reader.ReadToEnd()
        } finally {
            $reader.Dispose()
        }
        if ($content -notmatch "UID:nextcloud-parent") {
            throw "The ZIP entry does not contain the imported parent task."
        }
    } finally {
        $archive.Dispose()
    }
    Wait-ForElement "Todo.Transfer.Status"
}

Test-UI "Malformed iCalendar shows a line-specific error without partial import" {
    Select-ImportFile $malformedFixture
    Wait-ForElement `
        "Todo.Dialog.ImportError.Message" `
        "The selected file is not valid iCalendar data at line 5. No tasks were imported."
    winapp ui screenshot -w $mainHwnd -o (
        Join-Path $ArtifactDirectory "05-malformed-error.png"
    ) 2>$null | Out-Null
    Invoke-AppElement "Todo.Dialog.ImportError.Close"
    Get-UserListItem "Nextcloud work" | Out-Null
    if (-not (Get-Process -Id $AppPid -ErrorAction SilentlyContinue)) {
        throw "The app exited after a malformed import."
    }
}

Test-UI "M6 interactive controls expose stable accessible names and IDs" {
    Invoke-AppElement "Todo.Nav.Settings"
    foreach ($selector in @(
        "Todo.Nav.Settings",
        "Todo.Settings.ImportIcal",
        "Todo.Settings.ExportAll"
    )) {
        $element = Get-SearchMatches $selector |
            Where-Object { $_.automationId -eq $selector } |
            Select-Object -First 1
        if (-not $element -or [string]::IsNullOrWhiteSpace($element.name)) {
            throw "The '$selector' control lacks an accessible name or stable ID."
        }
    }

    winapp ui inspect -w $mainHwnd --interactive --json 2>$null |
        Out-File (Join-Path $ArtifactDirectory "ui-tree.json")
}

$resultPath = Join-Path $ArtifactDirectory "test-results.json"
$results | ConvertTo-Json | Out-File $resultPath
Write-Host "M6 UI tests: $pass passed, $fail failed"
$results | Where-Object { $_.status -eq "FAIL" } | ForEach-Object {
    Write-Host "  FAIL: $($_.name) - $($_.detail)" -ForegroundColor Red
}

if ($fail -gt 0) {
    exit 1
}

exit 0
