
# Local desktop input requires explicit owner permission for this session.
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1') {
    throw 'Local UI automation is disabled on this main machine. Use an isolated VM/Sandbox, or obtain owner permission for this session before setting WINTODO_ALLOW_LOCAL_UI=1. See docs/guides/testing.md.'
}
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$appDirectory = Join-Path $repositoryRoot (
    "src\Wintodo.App\bin\x64\Debug\" +
    "net10.0-windows10.0.26100.0\win-x64"
)
$artifactDirectory = Join-Path $PSScriptRoot (
    "artifacts\list-icon\run-" + [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
)
$databasePath = Join-Path $artifactDirectory "list-icon-gate.db"
$rocket = [char]::ConvertFromUtf32(0x1F680)
$targetEmoji = [char]::ConvertFromUtf32(0x1F3AF)
$results = @()
$pass = 0
$fail = 0

New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null

function Start-TestApp {
    $launch = winapp run $appDirectory `
        --detach `
        --json `
        -- `
        --database-path $databasePath |
        ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $launch.ProcessId) {
        throw "Wintodo list-icon gate launch failed."
    }

    return [int]$launch.ProcessId
}

function Get-MainWindow {
    param([int]$AppPid)

    for ($attempt = 0; $attempt -lt 24; $attempt++) {
        Start-Sleep -Milliseconds 250
        $window = winapp ui list-windows -a $AppPid --json 2>$null |
            ConvertFrom-Json |
            Where-Object { $_.title -eq "Wintodo" } |
            Select-Object -First 1
        if ($window) {
            return $window.hwnd
        }
    }

    throw "The Wintodo main window was not found."
}

function Test-UI {
    param([string]$Name, [scriptblock]$Script)

    try {
        & $Script
        if ($LASTEXITCODE -ne 0) {
            throw "winapp exited with code $LASTEXITCODE."
        }

        $script:pass++
        $script:results += @{ name = $Name; status = "PASS" }
    } catch {
        $script:fail++
        $script:results += @{ name = $Name; status = "FAIL"; detail = "$_" }
    } finally {
        $global:LASTEXITCODE = 0
    }
}

function Get-UserList {
    param([int]$AppPid, [string]$Name)

    $matches = (winapp ui search $Name -a $AppPid --json 2>$null |
        ConvertFrom-Json).matches
    return $matches |
        Where-Object {
            $_.type -eq "ListItem" -and
            $_.automationId -match "^Todo\.Nav\.UserList\."
        } |
        Select-Object -First 1
}

function Get-TaskRow {
    param([int]$AppPid, [string]$Title)

    return (winapp ui search $Title -a $AppPid --json 2>$null |
        ConvertFrom-Json).matches |
        Where-Object { $_.type -eq "ListItem" -and $_.automationId -match "^Todo\.Row\." } |
        Select-Object -First 1
}

function Open-ListIconDialog {
    param([int]$AppPid, [string]$MainHwnd)

    winapp ui invoke "Todo.List.MenuButton" -w $MainHwnd | Out-Null
    Start-Sleep -Milliseconds 200
    winapp ui invoke "Todo.List.Menu.ChangeIcon" -a $AppPid | Out-Null
    Start-Sleep -Milliseconds 200
    winapp ui invoke "Todo.List.Icon.Custom" -a $AppPid | Out-Null
    winapp ui wait-for "Todo.Dialog.ListIcon.Input" -a $AppPid -t 3000 | Out-Null
}

$appPid = Start-TestApp
try {
    $mainHwnd = Get-MainWindow -AppPid $appPid

    Test-UI "Default Tasks list shows its incomplete count" {
        winapp ui invoke "Todo.Nav.DefaultList" -w $mainHwnd | Out-Null
        winapp ui wait-for "Todo.List.AddTaskBox" -w $mainHwnd -t 3000 | Out-Null
        winapp ui set-value "Todo.List.AddTaskBox" "Default count task" -a $appPid | Out-Null
        winapp ui invoke "Todo.Composer.AddButton" -a $appPid | Out-Null
        winapp ui wait-for "Todo.Nav.DefaultList.Count" `
            -w $mainHwnd `
            --value "1" `
            -t 5000 | Out-Null
    }

    Test-UI "Create a custom user list" {
        winapp ui invoke "Todo.Nav.NewListButton" -w $mainHwnd | Out-Null
        winapp ui wait-for "Todo.Nav.NewListNameBox" -w $mainHwnd -t 3000 | Out-Null
        winapp ui set-value "Todo.Nav.NewListNameBox" "Emoji Gate" -w $mainHwnd | Out-Null
        winapp ui send-keys "enter" `
            --target "Todo.Nav.NewListNameBox" `
            -w $mainHwnd `
            --via send-input | Out-Null
        winapp ui wait-for "Todo.List.TitleButton" `
            -w $mainHwnd `
            --value "Emoji Gate" `
            -t 5000 | Out-Null
    }

    $list = Get-UserList -AppPid $appPid -Name "Emoji Gate"
    if (-not $list) {
        throw "The created user list was not found."
    }
    $listUid = $list.automationId.Substring("Todo.Nav.UserList.".Length)
    $iconId = "Todo.Nav.Icon.$listUid"
    $countId = "Todo.Nav.Count.$listUid"

    Test-UI "Incomplete task count updates in navigation" {
        winapp ui focus "Todo.List.AddTaskBox" -a $appPid | Out-Null
        winapp ui wait-for "Todo.Composer.AddButton" -a $appPid -t 3000 | Out-Null
        foreach ($title in @("Count task one", "Count task two")) {
            winapp ui set-value "Todo.List.AddTaskBox" $title -a $appPid | Out-Null
            winapp ui invoke "Todo.Composer.AddButton" -a $appPid | Out-Null
        }
        winapp ui wait-for $countId -a $appPid --value "2" -t 5000 | Out-Null
    }

    Test-UI "Choose an emoji beside the title editor" {
        winapp ui invoke "Todo.List.TitleButton" -w $mainHwnd | Out-Null
        winapp ui wait-for "Todo.List.RenameBox" -w $mainHwnd -t 3000 | Out-Null
        winapp ui invoke "Todo.List.IconPickerButton" -w $mainHwnd | Out-Null
        winapp ui wait-for "Todo.List.Icon.Choice.31" -a $appPid -t 3000 | Out-Null
        winapp ui screenshot -a $appPid --capture-screen -o (
            Join-Path $artifactDirectory "01-title-emoji-picker.png"
        ) 2>$null | Out-Null
        winapp ui invoke "Todo.List.Icon.Choice.31" -a $appPid | Out-Null
        winapp ui wait-for $iconId -a $appPid --value $targetEmoji -t 5000 | Out-Null
        winapp ui wait-for "Todo.List.HeaderIcon" `
            -w $mainHwnd `
            --value $targetEmoji `
            -t 3000 | Out-Null
    }

    Test-UI "Count hides at zero and returns for a new task" {
        foreach ($title in @("Count task one", "Count task two")) {
            $row = Get-TaskRow -AppPid $appPid -Title $title
            if (-not $row) {
                throw "Task row '$title' was not found."
            }
            $uid = $row.automationId.Substring("Todo.Row.".Length)
            winapp ui invoke "Todo.Row.CompleteToggle.$uid" --action toggle-on -a $appPid | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "Could not complete '$title'." }
        }
        winapp ui wait-for $countId -a $appPid --gone -t 5000 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'The count did not hide after completing both tasks.' }
        winapp ui focus "Todo.List.AddTaskBox" -a $appPid | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not focus the composer.' }
        winapp ui wait-for "Todo.Composer.AddButton" -a $appPid -t 3000 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'The composer did not expand.' }
        winapp ui set-value "Todo.List.AddTaskBox" "Persistent count task" -a $appPid | Out-Null
        winapp ui invoke "Todo.Composer.AddButton" -a $appPid | Out-Null
        winapp ui wait-for $countId -a $appPid --value "1" -t 5000 | Out-Null
    }

    Test-UI "Set a custom emoji through the list menu" {
        Open-ListIconDialog -AppPid $appPid -MainHwnd $mainHwnd
        winapp ui set-value "Todo.Dialog.ListIcon.Input" $rocket -a $appPid | Out-Null
        winapp ui invoke "Todo.Dialog.ListIcon.Save" -a $appPid | Out-Null
        winapp ui wait-for $iconId -a $appPid --value $rocket -t 5000 | Out-Null
    }

    winapp ui screenshot -a $appPid -o (
        Join-Path $artifactDirectory "02-emoji-list-icon-and-count.png"
    ) 2>$null | Out-Null
} finally {
    Stop-Process -Id $appPid -ErrorAction SilentlyContinue
}

$appPid = Start-TestApp
try {
    $mainHwnd = Get-MainWindow -AppPid $appPid

    Test-UI "Emoji icon persists after restart" {
        $list = Get-UserList -AppPid $appPid -Name "Emoji Gate"
        if (-not $list) {
            throw "The persisted user list was not found."
        }
        winapp ui invoke $list.selector -w $mainHwnd | Out-Null
        winapp ui wait-for $iconId -a $appPid --value $rocket -t 5000 | Out-Null
        winapp ui wait-for $countId -a $appPid --value "1" -t 5000 | Out-Null

        $label = (winapp ui search "Emoji Gate" -a $appPid --json 2>$null |
            ConvertFrom-Json).matches |
            Where-Object {
                $_.type -eq "Text" -and
                $_.name -eq "Emoji Gate" -and
                -not $_.isOffscreen -and
                $_.width -gt 0 -and
                $_.height -gt 0 -and
                $_.x -ge $list.x -and
                $_.x -lt ($list.x + $list.width)
            } |
            Select-Object -First 1
        if (-not $label) {
            throw "The restored navigation pane reserved space but hid its labels."
        }
    }

    Test-UI "Invalid text stays in the editor with an error" {
        Open-ListIconDialog -AppPid $appPid -MainHwnd $mainHwnd
        winapp ui set-value "Todo.Dialog.ListIcon.Input" "AB" -a $appPid | Out-Null
        winapp ui invoke "Todo.Dialog.ListIcon.Save" -a $appPid | Out-Null
        winapp ui wait-for "Todo.Dialog.ListIcon.Error" `
            -a $appPid `
            --value "Enter a single emoji." `
            -t 3000 | Out-Null
        winapp ui screenshot -a $appPid --capture-screen -o (
            Join-Path $artifactDirectory "02-invalid-emoji.png"
        ) 2>$null | Out-Null
    }

    Test-UI "Corrected emoji updates the navigation icon" {
        winapp ui set-value "Todo.Dialog.ListIcon.Input" $targetEmoji -a $appPid | Out-Null
        winapp ui invoke "Todo.Dialog.ListIcon.Save" -a $appPid | Out-Null
        winapp ui wait-for $iconId -a $appPid --value $targetEmoji -t 5000 | Out-Null
        winapp ui screenshot -a $appPid -o (
            Join-Path $artifactDirectory "03-corrected-emoji.png"
        ) 2>$null | Out-Null
    }

    Test-UI "Remove icon restores the colour-dot fallback" {
        winapp ui invoke "Todo.List.MenuButton" -w $mainHwnd | Out-Null
        Start-Sleep -Milliseconds 200
        winapp ui invoke "Todo.List.Menu.ChangeIcon" -a $appPid | Out-Null
        Start-Sleep -Milliseconds 200
        winapp ui invoke "Todo.List.Icon.Remove" -a $appPid | Out-Null
        winapp ui wait-for $iconId -a $appPid --gone -t 5000 | Out-Null
    }
} finally {
    Stop-Process -Id $appPid -ErrorAction SilentlyContinue
}

$results | ConvertTo-Json | Out-File (
    Join-Path $artifactDirectory "results.json"
) -Encoding utf8

Write-Host "Passed: $pass | Failed: $fail"
Write-Host "Artifacts: $artifactDirectory"
if ($fail -ne 0) {
    $results | Where-Object { $_.status -eq "FAIL" } | ForEach-Object {
        Write-Host "FAIL: $($_.name) - $($_.detail)"
    }
    exit 1
}
