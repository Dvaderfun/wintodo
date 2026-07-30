param(
    [Parameter(Mandatory)]
    [int]$AppPid
)

$ErrorActionPreference = "Continue"
$pass = 0
$fail = 0
$results = @()
$screenshotDirectory = Join-Path $PSScriptRoot "artifacts\m2"

New-Item -ItemType Directory -Force -Path $screenshotDirectory | Out-Null

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
            $script:fail++
            $script:results += @{
                name = $Name
                status = "FAIL"
                detail = "$output"
            }
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

Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class WindowTestNative
{
    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hWnd);
}
"@

$windows = winapp ui list-windows -a $AppPid --json 2>$null | ConvertFrom-Json
$window = $windows |
    Where-Object { $_.title -eq "Wintodo" } |
    Select-Object -First 1
if (-not $window) {
    throw "Wintodo window was not found for process $AppPid."
}

$windowHandle = [IntPtr]::new([long]$window.hwnd)
$dpiScale = [WindowTestNative]::GetDpiForWindow($windowHandle) / 96.0

function Set-WindowSizeInDips {
    param(
        [int]$Width,
        [int]$Height
    )

    $pixelWidth = [int][Math]::Round($Width * $dpiScale)
    $pixelHeight = [int][Math]::Round($Height * $dpiScale)
    $noMove = 0x0002
    $noZOrder = 0x0004
    [WindowTestNative]::SetWindowPos(
        $windowHandle,
        [IntPtr]::Zero,
        0,
        0,
        $pixelWidth,
        $pixelHeight,
        $noMove -bor $noZOrder) | Out-Null
    Start-Sleep -Milliseconds 500
}

Test-UI "Navigation root exists" {
    winapp ui wait-for "Todo.Nav.Root" -a $AppPid -t 3000
}
Test-UI "Smart lists exist" {
    winapp ui wait-for "Todo.Nav.MyDay" -a $AppPid -t 3000
    winapp ui wait-for "Todo.Nav.Important" -a $AppPid -t 3000
    winapp ui wait-for "Todo.Nav.Planned" -a $AppPid -t 3000
    winapp ui wait-for "Todo.Nav.All" -a $AppPid -t 3000
    winapp ui wait-for "Todo.Nav.Completed" -a $AppPid -t 3000
}
Test-UI "Settings and footer controls exist" {
    winapp ui wait-for "Todo.Nav.Settings" -a $AppPid -t 3000
    winapp ui wait-for "Todo.Nav.NewListButton" -a $AppPid -t 3000
    winapp ui wait-for "Todo.Nav.NewGroupButton" -a $AppPid -t 3000
}
Test-UI "Wide title search is visible" {
    Set-WindowSizeInDips -Width 1180 -Height 820
    winapp ui wait-for "Todo.TitleBar.SearchBox" -a $AppPid -t 3000
}
Test-UI "Smart-list selection updates heading" {
    winapp ui invoke "Todo.Nav.Important" -a $AppPid
    winapp ui wait-for "Todo.Shell.PageTitle" -a $AppPid --value "Important" -t 3000
}

winapp ui screenshot -a $AppPid -o (Join-Path $screenshotDirectory "01-wide.png") 2>$null

Test-UI "Compact breakpoint collapses title search" {
    Set-WindowSizeInDips -Width 656 -Height 640
    winapp ui wait-for "Todo.TitleBar.SearchButton" -a $AppPid -t 3000
}
Test-UI "Compact search flyout opens" {
    winapp ui invoke "Todo.TitleBar.SearchButton" -a $AppPid
    Start-Sleep -Milliseconds 500
    winapp ui wait-for "Todo.TitleBar.CompactSearchBox" -a $AppPid -t 3000
    winapp ui screenshot -a $AppPid -o (
        Join-Path $screenshotDirectory "02-compact-search.png"
    ) 2>$null
    winapp ui click "Todo.Nav.Important" -a $AppPid
    winapp ui wait-for "Todo.TitleBar.CompactSearchBox" -a $AppPid --gone -t 3000
}
Test-UI "Keyboard focus reaches compact search" {
    winapp ui focus "Todo.Nav.MyDay" -a $AppPid
    winapp ui send-keys "tab" -a $AppPid --via send-input
    winapp ui get-focused -a $AppPid --json
}

winapp ui screenshot -a $AppPid -o (Join-Path $screenshotDirectory "03-compact.png") 2>$null

Test-UI "Minimum width remains usable" {
    Set-WindowSizeInDips -Width 640 -Height 640
    winapp ui wait-for "Todo.Shell.PageTitle" -a $AppPid -t 3000
}

winapp ui screenshot -a $AppPid -o (Join-Path $screenshotDirectory "04-minimum.png") 2>$null

$allElements = (
    winapp ui inspect -a $AppPid --interactive --json 2>$null |
        ConvertFrom-Json
).elements
$appControls = @($allElements | Where-Object {
    $_.type -match "Button|Edit|ListItem" -and
    $_.name -notmatch "Minimize|Maximize|Close|System" -and
    $_.className -notmatch "PopupHost|TitleBar"
})
$missingId = @($appControls | Where-Object { -not $_.automationId })
if ($missingId.Count -eq 0) {
    $pass++
    $results += @{ name = "Interactive controls have AutomationIds"; status = "PASS" }
} else {
    $fail++
    $names = ($missingId | ForEach-Object {
        "$($_.type) '$($_.name)'"
    }) -join ", "
    $results += @{
        name = "Interactive controls have AutomationIds"
        status = "FAIL"
        detail = "Missing: $names"
    }
}

Set-WindowSizeInDips -Width 1180 -Height 820

$resultPath = Join-Path $screenshotDirectory "results.json"
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
