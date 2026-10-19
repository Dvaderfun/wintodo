if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1') {
    throw 'Host UI automation is disabled. Use scripts/Invoke-VmGate.ps1.'
}
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$appDirectory = Join-Path $repositoryRoot 'src\Wintodo.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64'
$artifactDirectory = Join-Path $PSScriptRoot ('artifacts\header-alignment\run-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null
$results = [Collections.Generic.List[object]]::new()
$measurements = [Collections.Generic.List[object]]::new()
$previousWorkflow = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = [guid]::NewGuid().ToString('N')
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class HeaderAlignmentNative {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
}
'@
function Ui([string[]]$Arguments) {
    $output = winapp ui @Arguments -a $appPid
    if ($LASTEXITCODE -ne 0) { throw "UI command failed: $($Arguments -join ' ') $output" }
    $output
}
function Element([string]$Id) {
    $element = (Ui @('search', $Id, '--json') | ConvertFrom-Json).matches |
        Where-Object automationId -eq $Id | Select-Object -First 1
    if (-not $element -or $element.isOffscreen) { throw "Visible element missing: $Id" }
    $element
}
function Ink-Bounds($bitmap, $element, $windowRect) {
    $left = [int]($element.x - $windowRect.Left)
    $top = [int]($element.y - $windowRect.Top)
    $right = [int]($left + $element.width)
    $bottom = [int]($top + $element.height)
    # The fixed-icon and title boxes have a plain background and no focus chrome.
    $background = $bitmap.GetPixel($left, $top)
    $inkTop = $bottom
    $inkBottom = -1
    for ($y = $top; $y -lt $bottom; $y++) {
        for ($x = $left; $x -lt $right; $x++) {
            $pixel = $bitmap.GetPixel($x, $y)
            $difference = [Math]::Max([Math]::Abs([int]$pixel.R - $background.R),
                [Math]::Max([Math]::Abs([int]$pixel.G - $background.G), [Math]::Abs([int]$pixel.B - $background.B)))
            if ($difference -gt 60) {
                $inkTop = [Math]::Min($inkTop, $y)
                $inkBottom = [Math]::Max($inkBottom, $y)
            }
        }
    }
    if ($inkBottom -lt 0) { throw 'No visible header ink found.' }
    @{ top = $inkTop; bottom = $inkBottom; center = ($inkTop + $inkBottom) / 2.0 }
}
$appPid = $null
try {
    $launch = winapp run $appDirectory --detach --json -- --database-path (Join-Path $artifactDirectory 'header.db') | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $launch.ProcessId) { throw 'Header gate launch failed.' }
    $appPid = [int]$launch.ProcessId
    Ui @('wait-for', 'Todo.List.SortButton', '-t', '15000') | Out-Null
    foreach ($theme in @(@('Light', 1), @('Dark', 2))) {
        Ui @('invoke', 'Todo.Nav.Settings') | Out-Null
        Ui @('wait-for', 'Todo.Settings.Theme', '-t', '5000') | Out-Null
        Ui @('focus', 'Todo.Settings.Theme') | Out-Null
        $keys = @('home') + @(1..$theme[1] | ForEach-Object { 'down' }) + @('enter')
        Ui @('send-keys', ($keys -join ' '), '--via', 'send-input') | Out-Null
        Ui @('wait-for', 'Todo.Settings.Theme', '--value', $theme[0]) | Out-Null
        foreach ($category in @(@('DefaultList', 'Tasks'), @('Important', 'Important'),
            @('Planned', 'Planned'), @('All', 'All'), @('Completed', 'Completed'))) {
            $name = "$($theme[0])-$($category[1])"
            try {
                Ui @('invoke', "Todo.Nav.$($category[0])") | Out-Null
                Ui @('wait-for', 'Todo.Shell.PageTitle', '--value', $category[1], '-t', '5000') | Out-Null
                Ui @('focus', 'Todo.List.MenuButton') | Out-Null
                Start-Sleep -Milliseconds 300
                $title = Element 'Todo.Shell.PageTitle'
                # The XAML fixes this decorative slot at 44 px with 8 px spacing.
                # FontIcon has no UIA peer; derive its crop from the adjacent title.
                $icon = @{ x=$title.x - 52; y=$title.y + $title.height / 2.0 - 22; width=44; height=44 }
                $screenshot = Join-Path $artifactDirectory "$name.png"
                $mainWindow = (Get-Process -Id $appPid).MainWindowHandle
                # A plain CLI capture is a labeled multi-window composite.
                # Capture the actual screen rectangle to match UIA coordinates.
                Ui @('screenshot', '-w', "$mainWindow", '--capture-screen', '-o', $screenshot) | Out-Null
                $windowRect = [HeaderAlignmentNative+Rect]::new()
                if (-not [HeaderAlignmentNative]::GetWindowRect((Get-Process -Id $appPid).MainWindowHandle, [ref]$windowRect)) { throw 'Window bounds unavailable.' }
                $bitmap = [Drawing.Bitmap]::new($screenshot)
                try {
                    $iconInk = Ink-Bounds $bitmap $icon $windowRect
                    $titleInk = Ink-Bounds $bitmap $title $windowRect
                } finally { $bitmap.Dispose() }
                $inkDelta = $iconInk.center - $titleInk.center
                $measurements.Add(@{ name=$name; icon=$icon; title=$title; iconInk=$iconInk;
                    titleInk=$titleInk; inkCenterDelta=$inkDelta })
                if ([Math]::Abs($inkDelta) -gt 1) { throw "Visible ink centers differ: $inkDelta px" }
                $results.Add(@{ name=$name; status='PASS'; inkCenterDelta=$inkDelta })
            } catch { $results.Add(@{ name=$name; status='FAIL'; detail="$_" }) }
        }
        foreach ($listName in @('adwawd', 'Project gy')) {
            $name = "$($theme[0])-UserList-$listName"
            try {
                Ui @('invoke', 'Todo.Nav.NewListButton') | Out-Null
                Ui @('wait-for', 'Todo.Nav.NewListNameBox', '-t', '5000') | Out-Null
                Ui @('set-value', 'Todo.Nav.NewListNameBox', $listName) | Out-Null
                Ui @('send-keys', 'enter', '--via', 'send-input') | Out-Null
                Ui @('wait-for', 'Todo.List.TitleButton', '--value', $listName, '-t', '5000') | Out-Null
                Ui @('focus', 'Todo.List.MenuButton') | Out-Null
                Start-Sleep -Milliseconds 300
                $icon = Element 'Todo.List.IconPickerButton'
                $title = Element 'Todo.List.TitleButton'
                $mainWindow = (Get-Process -Id $appPid).MainWindowHandle
                $screenshot = Join-Path $artifactDirectory "$name.png"
                Ui @('screenshot', '-w', "$mainWindow", '--capture-screen', '-o', $screenshot) | Out-Null
                $windowRect = [HeaderAlignmentNative+Rect]::new()
                if (-not [HeaderAlignmentNative]::GetWindowRect($mainWindow, [ref]$windowRect)) { throw 'Window bounds unavailable.' }
                $bitmap = [Drawing.Bitmap]::new($screenshot)
                try {
                    $iconInk = Ink-Bounds $bitmap $icon $windowRect
                    $titleInk = Ink-Bounds $bitmap $title $windowRect
                } finally { $bitmap.Dispose() }
                $inkDelta = $iconInk.center - $titleInk.center
                $measurements.Add(@{name=$name;icon=$icon;title=$title;iconInk=$iconInk;titleInk=$titleInk;inkCenterDelta=$inkDelta})
                # Owner-requested optical alignment places only the dot 2 px
                # below the title's geometric ink center. Emoji stay unchanged.
                if ([Math]::Abs($inkDelta - 2) -gt 1) { throw "Placeholder optical offset differs: $inkDelta px" }
                $results.Add(@{name=$name;status='PASS';inkCenterDelta=$inkDelta})
            } catch { $results.Add(@{name=$name;status='FAIL';detail="$_"}) }
        }
    }
} finally {
    if ($appPid) { Stop-Process -Id $appPid -ErrorAction SilentlyContinue }
    $measurements | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $artifactDirectory 'measurements.json')
    $results | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $artifactDirectory 'results.json')
    if ($appPid) { winapp ui yield | Out-Null }
    $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflow
}
$results | ForEach-Object { [pscustomobject]$_ } | Format-Table name,status,inkCenterDelta,detail
Write-Host "Artifacts: $artifactDirectory"
if (@($results | Where-Object status -eq 'FAIL').Count -gt 0 -or $results.Count -ne 14) { exit 1 }
