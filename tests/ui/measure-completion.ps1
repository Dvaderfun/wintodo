param(
    [Parameter(Mandatory)][int]$AppPid,
    [Parameter(Mandatory)][string]$Output,
    [int]$SampleCount = 15,
    [string]$TaskUid = 'perf-task-00041',
    [switch]$ReducedMotion
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
$root = [System.Windows.Automation.AutomationElement]::RootElement
$app = $root.FindFirst([System.Windows.Automation.TreeScope]::Children,
    [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $AppPid))
if (-not $app) { throw 'App window not found.' }
function Find($parent, [string]$Id) {
    $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Id))
}
$list = Find $app 'Todo.List.Tasks'
$header = Find $list 'Todo.List.CompletedHeader'
if (-not $list -or -not $header) { throw 'Measure requires an expanded Completed section with a completed anchor task.' }
# Caller expands the section; keep it expanded for every sample.
if (([System.Windows.Automation.TogglePattern]$header.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)).Current.ToggleState -ne 'On') {
    ([System.Windows.Automation.TogglePattern]$header.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)).Toggle()
}
$samples = [Collections.Generic.List[object]]::new()
# Prewarm the UIA search/property paths outside the measured command.
$rowId = "Todo.Row.$TaskUid"
$toggleId = "Todo.Row.CompleteToggle.$TaskUid"
$null = (Find $list $rowId).Current.BoundingRectangle
foreach ($iteration in 0..$SampleCount) {
    $toggle = Find $list $toggleId
    if (-not $toggle) { throw 'Target task is not realized.' }
    $pattern = [System.Windows.Automation.TogglePattern]$toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    if ($pattern.Current.ToggleState -ne 'Off') { throw 'Target must be incomplete before each sample.' }
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $pattern.Toggle()
    $inputMs = $timer.Elapsed.TotalMilliseconds
    $stateMs = $null
    $placed = $false
    do {
        $row = Find $list $rowId
        $header = Find $list 'Todo.List.CompletedHeader'
        if ($row -and $row.Current.Name -notlike '*not completed*' -and $null -eq $stateMs) {
            $stateMs = $timer.Elapsed.TotalMilliseconds
        }
        if ($row -and $header -and $row.Current.BoundingRectangle.Top -gt $header.Current.BoundingRectangle.Top) {
            $placed = $true
            break
        }
        Start-Sleep -Milliseconds 5
    } while ($timer.ElapsedMilliseconds -lt 2000)
    $timer.Stop()
    if (-not $placed) { throw 'Task did not move below Completed.' }
    if ($iteration -gt 0) { $samples.Add(@{inputMs=$inputMs;stateMs=$stateMs;placementMs=$timer.Elapsed.TotalMilliseconds}) }
    if ($ReducedMotion -and $timer.ElapsedMilliseconds -ge 250) {
        $samples.ToArray() | ConvertTo-Json -Depth 6 | Set-Content $Output
        throw 'Reduced motion retained a completion delay.'
    }
    Start-Sleep -Milliseconds 750
    $toggle = Find $list $toggleId
    ([System.Windows.Automation.TogglePattern]$toggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)).Toggle()
    $reset = [Diagnostics.Stopwatch]::StartNew()
    do {
        $row = Find $list $rowId
        $header = Find $list 'Todo.List.CompletedHeader'
        if ($row -and $row.Current.Name -like '*not completed*' -and $row.Current.BoundingRectangle.Top -lt $header.Current.BoundingRectangle.Top) { break }
        Start-Sleep -Milliseconds 5
    } while ($reset.ElapsedMilliseconds -lt 2000)
    if ($reset.ElapsedMilliseconds -ge 2000) { throw 'Uncompletion did not restore the target row.' }
    Start-Sleep -Milliseconds 750
}
$ordered = @($samples.ToArray().placementMs | Sort-Object)
@{
    method='Prewarmed in-process UIA toggle to realized row below Completed; one warm-up excluded'
    sampleCount=$SampleCount
    medianMs=$ordered[[int][Math]::Floor($ordered.Count / 2)]
    meanMs=($ordered | Measure-Object -Average).Average
    samples=@($samples.ToArray())
} | ConvertTo-Json -Depth 8 | Set-Content $Output
