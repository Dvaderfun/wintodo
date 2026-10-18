param(
    [Parameter(Mandatory)][int]$AppPid,
    [Parameter(Mandatory)][string]$ArtifactDirectory,
    [Parameter(Mandatory)][ValidateSet('initial', 'persisted', 'reset')][string]$Phase
)
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1') {
    throw 'Host UI automation is disabled. Use scripts/Invoke-VmGate.ps1.'
}
$ErrorActionPreference = 'Stop'
$results = [Collections.Generic.List[object]]::new()
$previousWorkflow = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = [guid]::NewGuid().ToString('N')
function Ui([string[]]$Arguments) {
    $output = winapp ui @Arguments -a $AppPid
    if ($LASTEXITCODE -ne 0) { throw "UI command failed: $($Arguments -join ' ') $output" }
    $output
}
function Check([string]$Name, [scriptblock]$Action) {
    try { & $Action; $results.Add(@{name=$Name;status='PASS'}) }
    catch {
        $results.Add(@{name=$Name;status='FAIL';detail="$_"})
        try {
            Ui @('inspect', '--json', '--depth', '16') | Set-Content (Join-Path $ArtifactDirectory "$Phase-failure-$($results.Count).json")
            Ui @('screenshot', '-o', (Join-Path $ArtifactDirectory "$Phase-failure-$($results.Count).png")) | Out-Null
        } catch { Write-Warning "Could not capture failure state: $_" }
    }
}
function Matches([string]$Selector) {
    @((Ui @('search', $Selector, '--json') | ConvertFrom-Json).matches)
}
function Navigate([string]$Selector, [string]$Title) {
    Ui @('invoke', $Selector) | Out-Null
    Ui @('wait-for', 'Todo.Shell.PageTitle', '--value', $Title, '-t', '5000') | Out-Null
    Start-Sleep -Milliseconds 300
}
function UserList([string]$Name) {
    $item = Matches $Name | Where-Object { $_.automationId -match '^Todo\.Nav\.UserList\.' -and $_.type -eq 'ListItem' } | Select-Object -First 1
    if (-not $item) { throw "List '$Name' missing." }
    Ui @('invoke', $item.selector) | Out-Null
    Ui @('wait-for', 'Todo.List.TitleButton', '--value', $Name, '-t', '5000') | Out-Null
    Start-Sleep -Milliseconds 300
}
function Set-SortChoice([string]$Choice) {
    Ui @('invoke', 'Todo.List.SortButton') | Out-Null
    Ui @('wait-for', "Todo.List.Sort.$Choice", '-t', '3000') | Out-Null
    Ui @('invoke', "Todo.List.Sort.$Choice") | Out-Null
}
function Default {
    Ui @('wait-for', 'Todo.List.SortButton', '-t', '3000') | Out-Null
    $button = Matches 'Todo.List.SortButton' | Where-Object automationId -eq 'Todo.List.SortButton' | Select-Object -First 1
    if ($button.name -ne 'Sort') { throw "Unexpected Sort button name: $($button.name)" }
    Start-Sleep -Milliseconds 200
    foreach ($id in 'Todo.List.SortLabel', 'Todo.List.Sort.Reset') {
        Ui @('wait-for', $id, '--gone', '-t', '3000') | Out-Null
    }
}
function Indicator([string]$Label) {
    Ui @('wait-for', 'Todo.List.SortLabel', '--value', "Sorted by $Label", '-t', '5000') | Out-Null
    Ui @('wait-for', 'Todo.List.Sort.Reset', '-t', '3000') | Out-Null
}
function Reset {
    Ui @('invoke', 'Todo.List.Sort.Reset') | Out-Null
    Default
}
function AlphaOrder([string[]]$Expected) {
    $rows = Matches 'Todo.Row.' | Where-Object { $_.type -eq 'ListItem' -and $_.automationId -match '^Todo\.Row\.[0-9a-f-]+$' } | Sort-Object y
    $titles = @($rows | ForEach-Object { $_.name.Split(',')[0] })
    if (($titles -join '|') -ne ($Expected -join '|')) { throw "Unexpected Alpha order: $($titles -join '|')" }
}
function Width([int]$Value) {
    $window = (Get-Process -Id $AppPid).MainWindowHandle
    $scale = [SortGateNative]::GetDpiForWindow($window) / 96.0
    if (-not [SortGateNative]::SetWindowPos($window, [IntPtr]::Zero, 0, 0,
        [int]($Value * $scale), [int](820 * $scale), 0x0006)) { throw 'Window resize failed.' }
    Start-Sleep -Milliseconds 500
}
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class SortGateNative {
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
}
'@
try {
    Ui @('wait-for', 'Todo.Nav.MyDay', '-t', '10000') | Out-Null
    if ($Phase -eq 'initial') {
        $smartLists = @(
            @('MyDay', 'My Day', 'Manual'), @('Important', 'Important', 'Importance'),
            @('Planned', 'Planned', 'Due date'), @('All', 'All', 'Creation date'),
            @('Completed', 'Completed', 'Completion date')
        )
        foreach ($entry in $smartLists) {
            Check "$($entry[1]): default, reverse and reset restore actual default" {
                Navigate "Todo.Nav.$($entry[0])" $entry[1]
                Default
                Set-SortChoice 'Reverse'
                $direction = if ($entry[0] -eq 'Completed') { 'ascending' } else { 'descending' }
                Indicator "$($entry[2]), $direction"
                Reset
                Set-SortChoice 'Alphabetical'
                if ($entry[0] -eq 'Completed') { Default } else { Indicator 'Alphabetical, ascending'; Reset }
                Set-SortChoice 'Reverse'
                Indicator "$($entry[2]), $direction"
                Reset
            }
        }
        Check 'Tasks default, direction-only change and reset' {
            Navigate 'Todo.Nav.DefaultList' 'Tasks'; Default
            Set-SortChoice 'Reverse'; Indicator 'Manual, descending'; Reset
        }
        Check 'User list: mode, direction, reset and task order' {
            UserList 'Alpha'; Default
            AlphaOrder @('Midnight focus', 'Needle alpha', 'Overdue alpha')
            Set-SortChoice 'Alphabetical'; Indicator 'Alphabetical, ascending'
            Set-SortChoice 'Reverse'; Indicator 'Alphabetical, descending'
            AlphaOrder @('Overdue alpha', 'Needle alpha', 'Midnight focus')
            Ui @('screenshot', '-o', (Join-Path $ArtifactDirectory '01-non-default.png')) | Out-Null
            Ui @('focus', 'Todo.List.Sort.Reset') | Out-Null
            Ui @('send-keys', 'space', '--via', 'send-input') | Out-Null
            Default
            AlphaOrder @('Midnight focus', 'Needle alpha', 'Overdue alpha')
            Set-SortChoice 'Alphabetical'; Set-SortChoice 'Reverse'; Indicator 'Alphabetical, descending'
        }
        Check 'Per-list indicator survives navigation; search hides sorting' {
            UserList 'Beta'; Default
            Set-SortChoice 'DueDate'; Indicator 'Due date, ascending'
            UserList 'Alpha'; Indicator 'Alphabetical, descending'
            Ui @('focus', 'Todo.List.SortButton') | Out-Null
            Ui @('send-keys', 'ctrl+vk=0x46', '--via', 'send-input') | Out-Null
            Ui @('send-keys', 'needle', '--via', 'send-input') | Out-Null
            Ui @('wait-for', 'Todo.Shell.PageTitle', '--value', 'Search', '-t', '5000') | Out-Null
            Ui @('wait-for', 'Todo.List.Sort.Reset', '--gone', '-t', '3000') | Out-Null
            Ui @('send-keys', 'escape', '--via', 'send-input') | Out-Null
            UserList 'Alpha'; Indicator 'Alphabetical, descending'
        }
        Check 'Sort chip and keyboard reset work in Light and Dark' {
            foreach ($theme in @(@('Light', 1), @('Dark', 2))) {
                Ui @('invoke', 'Todo.Nav.Settings') | Out-Null
                Ui @('wait-for', 'Todo.Settings.Theme', '-t', '5000') | Out-Null
                Ui @('focus', 'Todo.Settings.Theme') | Out-Null
                Ui @('send-keys', 'home', '--via', 'send-input') | Out-Null
                for ($index = 0; $index -lt $theme[1]; $index++) {
                    Ui @('send-keys', 'down', '--via', 'send-input') | Out-Null
                }
                Ui @('send-keys', 'enter', '--via', 'send-input') | Out-Null
                Ui @('wait-for', 'Todo.Settings.Theme', '--value', $theme[0], '-t', '3000') | Out-Null
                UserList 'Alpha'; Indicator 'Alphabetical, descending'
                Ui @('focus', 'Todo.List.Sort.Reset') | Out-Null
                Ui @('screenshot', '-o', (Join-Path $ArtifactDirectory ("03-$($theme[0])-reset-focused.png"))) | Out-Null
                Ui @('send-keys', 'space', '--via', 'send-input') | Out-Null
                Default
                Set-SortChoice 'Alphabetical'; Set-SortChoice 'Reverse'; Indicator 'Alphabetical, descending'
            }
        }
        Check 'Chip and reset fit with detail open and at minimum window width' {
            try {
                $row = Matches 'Needle alpha' | Where-Object { $_.automationId -match '^Todo\.Row\.[0-9a-f-]+$' -and $_.type -eq 'ListItem' } | Select-Object -First 1
                Ui @('invoke', $row.selector) | Out-Null
                Ui @('wait-for', 'Todo.Detail.Title', '-t', '5000') | Out-Null
                foreach ($width in 1180, 640) {
                    Width $width
                    # Detail overlays the list at the minimum breakpoint. Close it
                    # before checking the list's visible header controls there.
                    if ($width -eq 640) {
                        Ui @('invoke', 'Todo.Detail.Close') | Out-Null
                        Ui @('wait-for', 'Todo.Detail.Title', '--gone', '-t', '3000') | Out-Null
                    }
                    Indicator 'Alphabetical, descending'
                    $label = Matches 'Todo.List.SortLabel' | Where-Object automationId -eq 'Todo.List.SortLabel' | Select-Object -First 1
                    $reset = Matches 'Todo.List.Sort.Reset' | Where-Object automationId -eq 'Todo.List.Sort.Reset' | Select-Object -First 1
                    if ($reset.width -lt 32 -or $reset.height -lt 32 -or $label.width -le 0 -or
                        $label.x + $label.width -gt $reset.x) { throw 'Chip label overlaps reset or reset is clipped.' }
                    $properties = (Ui @('get-property', $reset.selector, '--json') | ConvertFrom-Json).properties
                    if ($properties.IsOffscreen -ne 'False') {
                        throw 'Reset is outside the visible list header.'
                    }
                    Ui @('screenshot', '-o', (Join-Path $ArtifactDirectory "04-width-$width.png")) | Out-Null
                }
            } finally { Width 1180 }
        }
    } elseif ($Phase -eq 'persisted') {
        Check 'Non-default sort and ordering survive app restart' {
            UserList 'Alpha'; Indicator 'Alphabetical, descending'
            AlphaOrder @('Overdue alpha', 'Needle alpha', 'Midnight focus')
            UserList 'Beta'; Indicator 'Due date, ascending'
        }
        Check 'Persist reset independently for each user list' {
            UserList 'Alpha'; Reset
            AlphaOrder @('Midnight focus', 'Needle alpha', 'Overdue alpha')
            UserList 'Beta'; Indicator 'Due date, ascending'; Reset
            UserList 'Alpha'; Default
            Ui @('screenshot', '-o', (Join-Path $ArtifactDirectory '02-default.png')) | Out-Null
        }
    } else {
        Check 'Reset persists after second app restart' {
            UserList 'Alpha'; Default
            AlphaOrder @('Midnight focus', 'Needle alpha', 'Overdue alpha')
            UserList 'Beta'; Default
        }
    }
} finally {
    $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflow
    $results | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $ArtifactDirectory "$Phase-results.json")
}
$failed = @($results | Where-Object status -eq 'FAIL')
Write-Host "Sort indicator $Phase`: $($results.Count - $failed.Count)/$($results.Count) PASS, $($failed.Count) FAIL, 0 SKIP"
if ($failed.Count) { $failed | ConvertTo-Json -Depth 6 | Write-Host; exit 1 }
exit 0
