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
    [string]$ClockFile,
    [Parameter(Mandatory)]
    [string]$ArtifactDirectory,
    [switch]$VerifyTimers
)
# Local desktop input requires explicit owner permission for this session.
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1') {
    throw 'Local UI automation is disabled on this main machine. Use an isolated VM/Sandbox, or obtain owner permission for this session before setting WINTODO_ALLOW_LOCAL_UI=1. See docs/guides/testing.md.'
}


$ErrorActionPreference = "Continue"
$PSNativeCommandUseErrorActionPreference = $true
$pass = 0
$fail = 0
$results = @()
$mainHwnd = $null
$initialAppPid = $AppPid
$firstTrace = Join-Path $ArtifactDirectory 'first.trace'
$restartTrace = Join-Path $ArtifactDirectory 'restart.trace'
$accountName = 'long-account-for-responsive-settings-' + ('w' * 38) + '@example.test'

# The Debug gate observes the live dispatcher timers; AOT has no file listener.
function Assert-TimerTrace([string]$TraceFile, [string]$Marker, [int]$Count = 1) {
    if (-not $VerifyTimers) { return }
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        if (Test-Path -LiteralPath $TraceFile) {
            $matchesFound = @(Select-String -LiteralPath $TraceFile -SimpleMatch "WINTODO_SHELL_TIMERS=$Marker").Count
            if ($matchesFound -eq $Count) { return }
            if ($matchesFound -gt $Count) { break }
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Expected $Count '$Marker' timer trace(s), found $matchesFound."
}

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

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class M13GateWindow {
    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hwnd, int command);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    public static void Resize(IntPtr hwnd, int widthPx, int heightPx) {
        var previous = SetThreadDpiAwarenessContext(new IntPtr(-4)); // UIA reports physical screen pixels.
        if (previous == IntPtr.Zero) throw new InvalidOperationException("Cannot select per-monitor DPI awareness.");
        try {
            if (!SetWindowPos(hwnd, IntPtr.Zero, 0, 0, widthPx, heightPx, 0x16)) // No move, z-order change or activation.
                throw new InvalidOperationException("Could not resize the test window.");
        } finally { SetThreadDpiAwarenessContext(previous); }
    }
}
'@
$handle = [IntPtr]::new([long]$mainHwnd)

function Get-LayoutElement([string]$Id) {
    $selector = if ($Id) { @($Id) } else { @() }
    $tree = winapp ui inspect @selector -w $mainHwnd --depth 1 --json | ConvertFrom-Json
    $items = @($tree.windows | Where-Object hwnd -eq $handle.ToInt64() | ForEach-Object { $_.elements })
    if ($items.Count -ne 1 -or ($Id -and $items[0].automationId -cne $Id)) { throw "No unique layout element for '$Id'." }
    return $items[0]
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

function Set-RemoteSuggestion([string]$Uid, [switch]$Completed) {
    $status = if ($Completed) { 'COMPLETED' } else { 'NEEDS-ACTION' }
    $sequence = if ($Completed) { 2 } else { 1 }
    $calendar = @('BEGIN:VCALENDAR', 'VERSION:2.0', 'PRODID:-//Wintodo//M13 Fixture//EN',
        'BEGIN:VTODO', "UID:$Uid", 'DTSTAMP:20261120T120000Z', "SUMMARY:$Uid", "SEQUENCE:$sequence",
        "STATUS:$status", 'END:VTODO', 'END:VCALENDAR', '') -join "`r`n"
    Invoke-RestMethod -Method Put -Uri "http://127.0.0.1:$ServerPort/cal/tasks/$Uid.ics" `
        -ContentType 'text/calendar; charset=utf-8' -Body $calendar | Out-Null
}

Test-UI "Fresh database hides the navigation sync status" {
    winapp ui wait-for "Todo.Nav.Settings" -w $mainHwnd -t 5000
    winapp ui wait-for "Todo.Nav.SyncStatus" -w $mainHwnd --gone -t 5000
    winapp ui wait-for "Todo.Nav.SyncStatus.Text" -w $mainHwnd --gone -t 5000
    Assert-TimerTrace $firstTrace 'startup;SYNC=absent;MIDNIGHT=running'
    Assert-TimerTrace $firstTrace 'sync;' 0
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
    winapp ui set-value "Todo.Settings.Sync.Username" $accountName -w $mainHwnd
    winapp ui wait-for "Todo.Settings.Sync.Connect" -w $mainHwnd -p IsEnabled --value "True" -t 3000
}
Test-UI "Connect and discover VTODO list" {
    winapp ui invoke "Todo.Settings.Sync.Connect" -w $mainHwnd
    winapp ui wait-for "Todo.Settings.Sync.Account" -w $mainHwnd --value $accountName -t 10000
    winapp ui wait-for "Todo.Settings.Sync.Status" -w $mainHwnd --value "Up to date" -t 10000
    winapp ui wait-for "Todo.Nav.SyncStatus" -w $mainHwnd -p IsOffscreen --value "False" -t 5000
    winapp ui wait-for "Todo.Nav.SyncStatus.Text" -w $mainHwnd --value "Up to date" -t 5000
    $toggle = (winapp ui search "Todo.Settings.Sync.List." -w $mainHwnd --json 2>$null |
        ConvertFrom-Json).matches |
        Where-Object { $_.automationId -match "^Todo\.Settings\.Sync\.List\." } |
        Select-Object -First 1
    if (-not $toggle) { throw "The per-list sync toggle was not found." }
    winapp ui wait-for $toggle.selector -w $mainHwnd --value "On" -t 3000
    Assert-TimerTrace $firstTrace 'account;SYNC=running;MIDNIGHT=running'
    Assert-TimerTrace $firstTrace 'sync;' 0 # Connect performs its own initial sync.
}
Take-Screenshot "02-sync-connected.png"

function Assert-SyncLayout([int]$WindowWidth) {
    $original = Get-LayoutElement
    $scale = [M13GateWindow]::GetDpiForWindow($handle) / 96.0
    if ($scale -le 0 -or $original.width -le 0 -or $original.height -le 0) { throw 'Window bounds or DPI unavailable.' }
    $targetWidth = [int][Math]::Round($WindowWidth * $scale)
    try {
        [M13GateWindow]::Resize($handle, $targetWidth, $original.height)
        winapp ui scroll 'Todo.Settings.ScrollViewer' --to top -w $mainHwnd | Out-Null
        $actual = Get-LayoutElement
        if ([Math]::Abs($actual.width - $targetWidth) -gt 1) { throw "Expected $WindowWidth DIPs, got $($actual.width / $scale)." }
        $viewport = Get-LayoutElement 'Todo.Settings.ScrollViewer'
        # Bring the entire action group into the viewport without invoking either command.
        for ($step = 0; $step -lt 40; $step++) {
            $disconnect = Get-LayoutElement 'Todo.Settings.Sync.Disconnect'
            if (!$disconnect.isOffscreen -and $disconnect.height -gt 0 -and
                $disconnect.y + $disconnect.height -le $viewport.y + $viewport.height) { break }
            winapp ui scroll 'Todo.Settings.ScrollViewer' --wheel -2 -w $mainHwnd | Out-Null
        }
        $now = Get-LayoutElement 'Todo.Settings.Sync.Now'
        Take-Screenshot "02b-sync-account-${WindowWidth}dip.png"
        foreach ($button in @($now, $disconnect)) {
            if ($button.isOffscreen -or $button.width -lt (152 * $scale - 1) -or $button.height -lt (32 * $scale - 1) -or
                $button.x -lt [Math]::Max($actual.x, $viewport.x) -or $button.y -lt $viewport.y -or
                $button.x + $button.width -gt [Math]::Min(($actual.x + $actual.width), ($viewport.x + $viewport.width)) -or
                $button.y + $button.height -gt $viewport.y + $viewport.height) {
                throw "$($button.automationId) is clipped at $WindowWidth DIPs."
            }
        }
        if ($WindowWidth -eq 640) {
            if ([Math]::Abs($now.x - $disconnect.x) -gt 1 -or $now.y + $now.height -gt $disconnect.y) {
                throw 'Expected Sync now above Disconnect at 640 DIPs.'
            }
        } elseif ([Math]::Abs($now.y - $disconnect.y) -gt 1 -or $now.x + $now.width -gt $disconnect.x) {
            throw 'Expected Sync now beside Disconnect at wide width.'
        }
    } finally {
        [M13GateWindow]::Resize($handle, $original.width, $original.height)
    }
}
Test-UI "Long account keeps both Sync actions stacked and visible at 640 DIPs" { Assert-SyncLayout 640 }
Test-UI "Long account keeps both Sync actions side by side and visible at 1160 DIPs" { Assert-SyncLayout 1160 }

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
    Set-RemoteSuggestion 'suggestion-original'
    winapp ui invoke "Todo.Settings.Sync.Now" -w $mainHwnd
    winapp ui wait-for "Todo.Settings.Sync.Status" -w $mainHwnd --value "Up to date" -t 10000
}
Test-UI 'Open Suggestions reflects a remote add and completion after background sync' {
    winapp ui invoke 'Todo.Nav.MyDay' -w $mainHwnd
    winapp ui wait-for 'Todo.Suggestions.Open' -w $mainHwnd -p IsOffscreen --value 'False' -t 5000
    winapp ui invoke 'Todo.Suggestions.Open' -w $mainHwnd
    winapp ui wait-for 'Todo.Suggestions.Add.suggestion-original' -w $mainHwnd -t 10000
    winapp ui wait-for 'Todo.Suggestions.Add.suggestion-new' -w $mainHwnd --gone -t 3000

    Set-RemoteSuggestion 'suggestion-original' -Completed
    Set-RemoteSuggestion 'suggestion-new'
    # Activation syncs after one minute; advance only the app's clock within the same day.
    # The gate makes no SQLite writes and leaves the same My Day pane open throughout.
    [M13GateWindow]::ShowWindowAsync($handle, 6) | Out-Null
    Start-Sleep -Milliseconds 500
    Assert-TimerTrace $firstTrace 'minimize;SYNC=running;MIDNIGHT=off'
    $time = [DateTime]::new(2026, 11, 20, 12, 2, 0).ToUniversalTime()
    [IO.File]::WriteAllText("$ClockFile.next", $time.ToString('O'))
    [IO.File]::Move("$ClockFile.next", $ClockFile, $true)
    [M13GateWindow]::ShowWindowAsync($handle, 9) | Out-Null
    [M13GateWindow]::SetForegroundWindow($handle) | Out-Null

    winapp ui wait-for 'Todo.Suggestions.Add.suggestion-new' -w $mainHwnd -t 15000
    winapp ui wait-for 'Todo.Suggestions.Add.suggestion-original' -w $mainHwnd --gone -t 5000
    winapp ui wait-for 'Todo.Suggestions.Close' -w $mainHwnd -p IsOffscreen --value 'False' -t 3000
    Assert-TimerTrace $firstTrace 'restore;SYNC=running;MIDNIGHT=running'
    Take-Screenshot '02a-open-suggestions-after-sync.png'
}
Test-UI "Restart restores account and resumes sync" {
    Stop-Process -Id $script:AppPid -ErrorAction Stop
    Wait-Process -Id $script:AppPid -Timeout 10 -ErrorAction SilentlyContinue
    $traceArgs = @()
    if ($VerifyTimers) { $traceArgs = @('--trace-file', $restartTrace) }
    $launch = winapp run $AppDirectory `
        --detach `
        --json `
        -- `
        --database-path $DatabasePath --clock-file $ClockFile @traceArgs |
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
    winapp ui wait-for "Todo.Settings.Sync.Account" -w $script:mainHwnd --value $accountName -t 5000
    winapp ui wait-for "Todo.Settings.Sync.Status" -w $script:mainHwnd --value "Up to date" -t 5000
    Assert-TimerTrace $restartTrace 'startup;SYNC=running;MIDNIGHT=running'
    Assert-TimerTrace $restartTrace 'sync;' 1
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
    Assert-TimerTrace $restartTrace 'account;SYNC=absent;MIDNIGHT=running'
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
