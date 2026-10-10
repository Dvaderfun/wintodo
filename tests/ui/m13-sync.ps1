param(
    [Parameter(Mandatory)]
    [int]$AppPid,
    [Parameter(Mandatory)]
    [int]$ServerPort,
    [Parameter(Mandatory)]
    [string]$AppDirectory,
    [Parameter(Mandatory)]
    [string]$DatabasePath,
    [Parameter(Mandatory)]
    [string]$ArtifactDirectory
)

$ErrorActionPreference = "Continue"
$PSNativeCommandUseErrorActionPreference = $true
$pass = 0
$fail = 0
$results = @()
$mainHwnd = $null
$initialAppPid = $AppPid

for ($attempt = 0; $attempt -lt 30 -and -not $mainHwnd; $attempt++) {
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
        $ErrorActionPreference = "Stop"
        $output = & $Script 2>&1
        if ($LASTEXITCODE -eq 0) {
            $script:pass++
            $script:results += @{ name = $Name; status = "PASS" }
        } else {
            throw "$output"
        }
    } catch {
        $script:fail++
        $script:results += @{ name = $Name; status = "FAIL"; detail = "$_" }
    } finally {
        $global:LASTEXITCODE = 0
    }
}

function Take-Screenshot {
    param([string]$Name)

    winapp ui screenshot -a $AppPid -o (Join-Path $ArtifactDirectory $Name) 2>$null |
        Out-Null
}

Test-UI "Fresh database hides the navigation sync status" {
    winapp ui wait-for "Todo.Nav.Settings" -w $mainHwnd -t 5000
    winapp ui wait-for "Todo.Nav.SyncStatus" -w $mainHwnd --gone -t 5000
    winapp ui wait-for "Todo.Nav.SyncStatus.Text" -w $mainHwnd --gone -t 5000
}
Test-UI "Open Settings" {
    winapp ui invoke "Todo.Nav.Settings" -w $mainHwnd
    winapp ui wait-for "Todo.Settings.Sync.Configure" -w $mainHwnd -t 5000
}
Take-Screenshot "01-sync-disconnected.png"

Test-UI "Configure dialog enables submit and clears password on cancel" {
    winapp ui invoke "Todo.Settings.Sync.Configure" -w $mainHwnd
    winapp ui wait-for "Todo.Settings.Sync.Connect" -w $mainHwnd -p IsEnabled --value "False" -t 3000
    winapp ui set-value "Todo.Settings.Sync.Server" "http://127.0.0.1:$ServerPort/" -w $mainHwnd
    winapp ui set-value "Todo.Settings.Sync.Username" "alice" -w $mainHwnd
    winapp ui send-keys "secret" --target "Todo.Settings.Sync.Password" -w $mainHwnd --via send-input
    winapp ui wait-for "Todo.Settings.Sync.Connect" -w $mainHwnd -p IsEnabled --value "True" -t 3000
    Take-Screenshot "01a-connect-dialog.png"
    winapp ui invoke "Todo.Dialog.CalDav.Cancel" -w $mainHwnd
    winapp ui invoke "Todo.Settings.Sync.Configure" -w $mainHwnd
    winapp ui wait-for "Todo.Settings.Sync.Server" -w $mainHwnd --value "http://127.0.0.1:$ServerPort/" -t 3000
    winapp ui wait-for "Todo.Settings.Sync.Connect" -w $mainHwnd -p IsEnabled --value "False" -t 3000
}
Test-UI "Failed connect remains in the dialog and allows retry" {
    winapp ui set-value "Todo.Settings.Sync.Server" "invalid server address" -w $mainHwnd
    winapp ui send-keys "secret" --target "Todo.Settings.Sync.Password" -w $mainHwnd --via send-input
    winapp ui invoke "Todo.Settings.Sync.Connect" -w $mainHwnd
    winapp ui wait-for "Todo.Dialog.CalDav.Error" -w $mainHwnd -t 5000
    winapp ui wait-for "Todo.Settings.Sync.Connect" -w $mainHwnd -p IsEnabled --value "True" -t 3000
    winapp ui wait-for "Todo.Nav.SyncStatus" -w $mainHwnd --gone -t 3000
    Take-Screenshot "01b-connect-error.png"
}
Test-UI "Enter CalDAV account" {
    winapp ui set-value "Todo.Settings.Sync.Server" "http://127.0.0.1:$ServerPort/" -w $mainHwnd
    winapp ui set-value "Todo.Settings.Sync.Username" "alice" -w $mainHwnd
    winapp ui wait-for "Todo.Settings.Sync.Connect" -w $mainHwnd -p IsEnabled --value "True" -t 3000
}
Test-UI "Connect and discover VTODO list" {
    winapp ui invoke "Todo.Settings.Sync.Connect" -w $mainHwnd
    winapp ui wait-for "Todo.Settings.Sync.Account" -w $mainHwnd --value "alice" -t 10000
    winapp ui wait-for "Todo.Settings.Sync.Status" -w $mainHwnd --value "Up to date" -t 10000
    winapp ui wait-for "Todo.Nav.SyncStatus" -w $mainHwnd -p IsOffscreen --value "False" -t 5000
    winapp ui wait-for "Todo.Nav.SyncStatus.Text" -w $mainHwnd --value "Up to date" -t 5000
    $toggle = (winapp ui search "Todo.Settings.Sync.List." -w $mainHwnd --json 2>$null |
        ConvertFrom-Json).matches |
        Where-Object { $_.automationId -match "^Todo\.Settings\.Sync\.List\." } |
        Select-Object -First 1
    if (-not $toggle) { throw "The per-list sync toggle was not found." }
    winapp ui wait-for $toggle.selector -w $mainHwnd --value "On" -t 3000
}
Take-Screenshot "02-sync-connected.png"

Test-UI "Per-list sync toggle changes" {
    $toggle = (winapp ui search "Todo.Settings.Sync.List." -w $mainHwnd --json 2>$null |
        ConvertFrom-Json).matches |
        Where-Object { $_.automationId -match "^Todo\.Settings\.Sync\.List\." } |
        Select-Object -First 1
    winapp ui invoke $toggle.selector -w $mainHwnd
    winapp ui wait-for $toggle.selector -w $mainHwnd --value "Off" -t 5000
    winapp ui wait-for $toggle.selector -w $mainHwnd -p IsEnabled --value "True" -t 5000
    winapp ui invoke $toggle.selector -w $mainHwnd
    winapp ui wait-for $toggle.selector -w $mainHwnd --value "On" -t 10000
    winapp ui wait-for $toggle.selector -w $mainHwnd -p IsEnabled --value "True" -t 10000
}
Test-UI "Manual Sync now succeeds" {
    winapp ui invoke "Todo.Settings.Sync.Now" -w $mainHwnd
    winapp ui wait-for "Todo.Settings.Sync.Status" -w $mainHwnd --value "Up to date" -t 10000
}
Test-UI "Restart restores account and resumes sync" {
    Stop-Process -Id $script:AppPid -ErrorAction Stop
    Wait-Process -Id $script:AppPid -Timeout 10 -ErrorAction SilentlyContinue
    $launch = winapp run $AppDirectory `
        --detach `
        --json `
        -- `
        --database-path $DatabasePath |
        ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $launch.ProcessId) {
        throw "Wintodo restart failed."
    }

    $script:AppPid = [int]$launch.ProcessId
    $script:mainHwnd = $null
    for ($attempt = 0; $attempt -lt 30 -and -not $script:mainHwnd; $attempt++) {
        Start-Sleep -Milliseconds 250
        $script:mainHwnd = (winapp ui list-windows -a $script:AppPid --json 2>$null |
            ConvertFrom-Json |
            Where-Object { $_.title -eq "Wintodo" } |
            Select-Object -First 1).hwnd
    }
    if (-not $script:mainHwnd) {
        throw "The restarted Wintodo window was not found."
    }

    winapp ui wait-for "Todo.Nav.SyncStatus.Text" -w $script:mainHwnd --value "Up to date" -t 15000
    winapp ui invoke "Todo.Nav.Settings" -w $script:mainHwnd
    winapp ui wait-for "Todo.Settings.Sync.Account" -w $script:mainHwnd --value "alice" -t 5000
    winapp ui wait-for "Todo.Settings.Sync.Status" -w $script:mainHwnd --value "Up to date" -t 5000
}
Test-UI "New sync controls expose AutomationIds" {
    $required = @(
        "Todo.Nav.SyncStatus",
        "Todo.Settings.Sync.Account",
        "Todo.Settings.Sync.Status",
        "Todo.Settings.Sync.Now",
        "Todo.Settings.Sync.Disconnect"
    )
    foreach ($automationId in $required) {
        winapp ui wait-for $automationId -w $mainHwnd -t 3000 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "$automationId was not found." }
    }
}
Test-UI "Disconnect removes the account surface and navigation sync status" {
    winapp ui invoke "Todo.Settings.Sync.Disconnect" -w $mainHwnd
    winapp ui wait-for "Todo.Settings.Sync.Configure" -w $mainHwnd -t 5000
    winapp ui wait-for "Todo.Nav.SyncStatus" -w $mainHwnd --gone -t 5000
    winapp ui wait-for "Todo.Nav.SyncStatus.Text" -w $mainHwnd --gone -t 5000
}
Take-Screenshot "03-sync-disconnected-again.png"

$results | ConvertTo-Json | Set-Content -LiteralPath (
    Join-Path $ArtifactDirectory "results.json"
) -Encoding utf8
Write-Host "M13 UI gate: $pass passed, $fail failed"
if ($fail -ne 0) {
    $results | Where-Object { $_.status -eq "FAIL" } | ForEach-Object {
        Write-Host "FAIL: $($_.name) - $($_.detail)"
    }
    if ($AppPid -ne $initialAppPid) {
        Stop-Process -Id $AppPid -ErrorAction SilentlyContinue
    }
    exit 1
}
if ($AppPid -ne $initialAppPid) {
    Stop-Process -Id $AppPid -ErrorAction SilentlyContinue
}
