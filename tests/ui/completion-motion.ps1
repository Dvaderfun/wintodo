param([Parameter(Mandatory)][int]$AppPid, [Parameter(Mandatory)][string]$ArtifactDirectory,
    [Parameter(Mandatory)][string]$BaselineTimings, [switch]$TestReducedMotion)
# Local desktop input requires explicit owner permission for this session.
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1') {
    throw 'Local UI automation is disabled on this main machine. Use an isolated VM/Sandbox, or obtain owner permission for this session before setting WINTODO_ALLOW_LOCAL_UI=1. See docs/guides/testing.md.'
}

$ErrorActionPreference = 'Stop'
$results = [Collections.Generic.List[object]]::new()
$screenshots = [Collections.Generic.List[string]]::new()
$timings = [Collections.Generic.List[object]]::new()
$previousWorkflow = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = [guid]::NewGuid().ToString('N')
$recordJob = $null
$originalAnimations = $null
function Ui([string[]]$Arguments) {
    $output = winapp ui @Arguments -a $AppPid
    if ($LASTEXITCODE -ne 0) { throw "UI command failed: $($Arguments -join ' ')" }
    $output
}
function Check([string]$Name, [scriptblock]$Action) {
    try { & $Action | Out-Null; $results.Add(@{name=$Name;status='PASS'}) }
    catch { $results.Add(@{name=$Name;status='FAIL';detail="$_"}) }
}
function Capture([string]$Name) {
    $path = Join-Path $ArtifactDirectory $Name
    Ui @('screenshot', '-o', $path) | Out-Null
    if (-not (Test-Path $path) -or (Get-Item $path).Length -eq 0) { throw 'Screenshot missing.' }
    $screenshots.Add($path)
}
function Nodes($nodes) { foreach ($node in $nodes) { $node; Nodes $node.children } }
function TaskRow([string]$Uid) {
    $tree = Ui @('inspect', '--depth', '12', '--json') | ConvertFrom-Json
    Nodes @($tree.windows.elements) | Where-Object { $_.type -eq 'ListItem' -and $_.automationId -eq "Todo.Row.$Uid" } | Select-Object -First 1
}
try {
    Ui @('wait-for', 'Todo.Row.CompleteToggle.perf-task-00001', '-t', '10000') | Out-Null
    Capture '01-active.png'
    # Retain the old CLI round trip as a diagnostic. CLI startup and a header
    # appearing before layout are insufficient proof of completion performance.
    Check 'CLI completion diagnostic reaches Completed' {
        $timer = [Diagnostics.Stopwatch]::StartNew()
        Ui @('click', 'Todo.Row.CompleteToggle.perf-task-00001') | Out-Null
        Ui @('wait-for', 'Todo.List.CompletedHeader', '-t', '5000') | Out-Null
        $timer.Stop()
        $timings.Add(@{action='Completion to Completed header';elapsedMs=$timer.Elapsed.TotalMilliseconds;baselineMs=830.2392})
    }
    Ui @('invoke', 'Todo.List.CompletedHeader', '--action', 'toggle-on') | Out-Null
    Ui @('wait-for', 'Todo.Row.CompleteToggle.perf-task-00001', '--value', 'On', '-t', '5000') | Out-Null
    Capture '02-completed.png'
    Check 'Completion placement does not regress matched UX-07 baseline' {
        $baseline = Get-Content -LiteralPath $BaselineTimings -Raw | ConvertFrom-Json
        $currentPath = Join-Path $ArtifactDirectory 'placement-timings.json'
        & powershell.exe -NoProfile -File (Join-Path $PSScriptRoot 'measure-completion.ps1') -AppPid $AppPid -Output $currentPath -SampleCount 15
        if ($LASTEXITCODE -ne 0) { throw 'Completion measurement failed.' }
        $current = Get-Content -LiteralPath $currentPath -Raw | ConvertFrom-Json
        if ($baseline.sampleCount -ne 15 -or $baseline.method -ne $current.method) { throw 'Baseline must use the same method and 15 samples.' }
        if ($current.medianMs -gt $baseline.medianMs -or $current.meanMs -gt $baseline.meanMs) {
            throw "Completion regression: median $($current.medianMs)/$($baseline.medianMs), mean $($current.meanMs)/$($baseline.meanMs) ms."
        }
    }
    # A single recording workflow also owns all interactions, so capture cannot
    # block the gestures it is meant to record. Jobs do not open console windows.
    $video = Join-Path $ArtifactDirectory 'completion.mp4'
    $readyPath = Join-Path $ArtifactDirectory 'recording-ready.txt'
    $recordJob = Start-Job -ArgumentList $AppPid,$video,$env:WINAPP_UI_WORKFLOW_ID,$readyPath -ScriptBlock {
        param($targetPid,$path,$workflow,$ready)
        $env:WINAPP_UI_WORKFLOW_ID = $workflow
        winapp ui record -a $targetPid --duration-sec 12 --fps 30 --frames -o $path --json 2>&1 | ForEach-Object {
            $line = $_.ToString()
            if ($line -like '*recording-started*') { Set-Content -LiteralPath $ready -Value 'started' }
            $line
        }
        if ($LASTEXITCODE -ne 0) { throw 'Motion recording failed.' }
    }
    $deadline = [DateTime]::UtcNow.AddSeconds(8)
    while (-not (Test-Path $readyPath) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
    if (-not (Test-Path $readyPath)) { throw 'Recording did not start before interactions.' }
    Check 'Completion and uncompletion recorded with Completed placement' {
        Ui @('click', 'Todo.Row.CompleteToggle.perf-task-00021') | Out-Null
        Start-Sleep -Milliseconds 600
        Ui @('wait-for', 'Todo.Row.CompleteToggle.perf-task-00021', '--value', 'On', '-t', '5000') | Out-Null
        $row = TaskRow 'perf-task-00021'
        $header = (Ui @('search', 'Todo.List.CompletedHeader', '--json') | ConvertFrom-Json).matches | Select-Object -First 1
        if (-not $row -or $row.y -le $header.y) { throw 'Completed row did not move below its header.' }
        Capture '03-completion-placement.png'
        Ui @('click', 'Todo.Row.CompleteToggle.perf-task-00021') | Out-Null
        Ui @('wait-for', 'Todo.Row.CompleteToggle.perf-task-00021', '--value', 'Off', '-t', '5000') | Out-Null
    }
    Check 'Importance bounce recorded' {
        Ui @('invoke', 'Todo.Row.ImportanceToggle.perf-task-00021') | Out-Null
        Ui @('wait-for', 'Todo.Row.ImportanceToggle.perf-task-00021', '--value', 'On', '-t', '5000') | Out-Null
        Capture '04-important.png'
    }
    Check 'Undo snackbar enters and auto-dismisses' {
        Ui @('invoke', 'Todo.Row.perf-task-00021') | Out-Null
        Ui @('send-keys', 'delete', '--target', 'Todo.Row.perf-task-00021', '--via', 'send-input') | Out-Null
        Ui @('wait-for', 'Todo.Dialog.DeleteTask', '-t', '3000') | Out-Null
        Ui @('invoke', 'Todo.Dialog.DeleteTask.Confirm') | Out-Null
        Ui @('wait-for', 'Todo.List.UndoButton', '-t', '3000') | Out-Null
        Capture '05-undo.png'
        Ui @('wait-for', 'Todo.List.UndoButton', '--gone', '-t', '6500') | Out-Null
    }
    Wait-Job $recordJob -Timeout 20 | Out-Null
    Receive-Job $recordJob -ErrorAction Stop | Set-Content (Join-Path $ArtifactDirectory 'recording.json')
    if ($recordJob.State -ne 'Completed' -or -not (Test-Path $video) -or (Get-Item $video).Length -eq 0) { throw 'Recording was not delivered.' }
    Remove-Job $recordJob
    $recordJob = $null
    if ($TestReducedMotion) {
        # Owner-authorized only: SPI_SETCLIENTAREAANIMATION, restored in finally.
        Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class WintodoMotionSettings {
    [DllImport("user32.dll", SetLastError=true)]
    public static extern bool SystemParametersInfo(uint action, uint param, ref int value, uint flags);
    [DllImport("user32.dll", EntryPoint="SystemParametersInfoW", SetLastError=true)]
    public static extern bool SetAnimation(uint action, uint param, IntPtr value, uint flags);
}
'@
        $value = 0
        if (-not [WintodoMotionSettings]::SystemParametersInfo(0x1042,0,[ref]$value,0)) { throw 'Cannot read animation setting.' }
        $originalAnimations = $value
        if (-not [WintodoMotionSettings]::SetAnimation(0x1043,0,[IntPtr]::Zero,2)) { throw 'Cannot disable animation effects.' }
        $value = 1
        if (-not [WintodoMotionSettings]::SystemParametersInfo(0x1042,0,[ref]$value,0) -or $value -ne 0) { throw 'Animation effects did not turn off.' }
        Start-Sleep -Milliseconds 500
        Check 'Reduced motion completion moves instantly while app stays open' {
            # UIA in one process avoids CLI startup hiding the 250 ms delay.
            & powershell.exe -NoProfile -File (Join-Path $PSScriptRoot 'measure-completion.ps1') -AppPid $AppPid -Output (Join-Path $ArtifactDirectory 'reduced-motion-timing.json') -SampleCount 3 -ReducedMotion
            if ($LASTEXITCODE -ne 0) { throw 'Reduced motion measurement failed.' }
            Ui @('click', 'Todo.Row.CompleteToggle.perf-task-00041') | Out-Null
            Ui @('wait-for', 'Todo.Row.CompleteToggle.perf-task-00041', '--value', 'On', '-t', '2000') | Out-Null
            Capture '06-reduced-motion.png'
        }
    } else { $results.Add(@{name='Reduced motion';status='SKIPPED';detail='Requires owner authorization for temporary Windows setting change.'}) }
} catch { $results.Add(@{name='Setup / capture';status='FAIL';detail="$_"}) }
finally {
    if ($null -ne $originalAnimations) {
        if (-not [WintodoMotionSettings]::SetAnimation(0x1043,0,[IntPtr]::new($originalAnimations),2)) {
            $results.Add(@{name='Restore animation setting';status='FAIL'})
        } else {
            $restored = -1
            $readBack = [WintodoMotionSettings]::SystemParametersInfo(0x1042,0,[ref]$restored,0)
            @{original=$originalAnimations;restored=$restored} | ConvertTo-Json | Set-Content (Join-Path $ArtifactDirectory 'animation-setting.json')
            if (-not $readBack -or $restored -ne $originalAnimations) { $results.Add(@{name='Restore animation setting';status='FAIL'}) }
            else { $results.Add(@{name='Restore animation setting';status='PASS'}) }
        }
    }
    if ($recordJob) { Stop-Job $recordJob; Remove-Job $recordJob }
    winapp ui yield | Out-Null
    $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflow
}
$timings.ToArray() | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $ArtifactDirectory 'timings.json')
@{results=@($results.ToArray());screenshots=@($screenshots.ToArray());video='completion.mp4'} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $ArtifactDirectory 'results.json')
$results.ToArray() | ForEach-Object { [pscustomobject]$_ } | Format-Table name,status,detail -Wrap
if (@($results | Where-Object status -eq 'FAIL').Count) { exit 1 }
