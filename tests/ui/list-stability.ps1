param([Parameter(Mandatory)][int]$AppPid, [Parameter(Mandatory)][string]$ArtifactDirectory)
# Local desktop input requires explicit owner permission for this session.
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1') {
    throw 'Local UI automation is disabled on this main machine. Use an isolated VM/Sandbox, or obtain owner permission for this session before setting WINTODO_ALLOW_LOCAL_UI=1. See docs/guides/testing.md.'
}

$ErrorActionPreference = 'Stop'
$results = [Collections.Generic.List[object]]::new()
$screenshots = [Collections.Generic.List[string]]::new()
$env:WINAPP_UI_WORKFLOW_ID = [guid]::NewGuid().ToString('N')
function Ui([string[]]$Arguments) {
    $output = winapp ui @Arguments -a $AppPid
    if ($LASTEXITCODE -ne 0) { throw "UI command failed: $($Arguments -join ' ')" }
    $output
}
function Row([string]$Title) {
    $tree = Ui @('inspect', '--depth', '12', '--json') | ConvertFrom-Json
    Nodes @($tree.windows.elements) |
        Where-Object { $_.name -like "$Title,*" } |
        Where-Object { $_.type -eq 'ListItem' -and $_.automationId -match '^Todo.Row.[0-9a-f-]+$' } |
        Select-Object -First 1
}
function Check([string]$Name, [scriptblock]$Action) {
    try { & $Action; $results.Add(@{name=$Name;status='PASS'}) }
    catch { $results.Add(@{name=$Name;status='FAIL';detail="$_"}) }
}
function Capture([string]$Name, $WindowHandle) {
    $path = Join-Path $ArtifactDirectory $Name
    if ($WindowHandle) {
        $null = winapp ui screenshot -w $WindowHandle --capture-screen -o $path
        if ($LASTEXITCODE) { throw 'Window screenshot failed.' }
    } else { Ui @('screenshot', '-o', $path) | Out-Null }
    if (-not (Test-Path $path)) { throw 'Screenshot missing.' }
    $screenshots.Add($path)
}
function Properties($item) { (Ui @('get-property', $item.selector, '--json') | ConvertFrom-Json).properties }
function Nodes($nodes) { foreach ($node in $nodes) { $node; Nodes $node.children } }
try {
    Ui @('wait-for', 'Todo.List.Tasks', '-t', '120000') | Out-Null
    Ui @('wait-for', 'Stability task 001', '-t', '10000') | Out-Null
    Ui @('focus', 'Todo.List.MenuButton') | Out-Null
    Ui @('scroll', 'Todo.List.Tasks', '--wheel', '-54') | Out-Null
    Start-Sleep -Milliseconds 600
    $tree = Ui @('inspect', '--depth', '12', '--json') | ConvertFrom-Json
    $tree | ConvertTo-Json -Depth 30 | Set-Content (Join-Path $ArtifactDirectory 'initial-tree.json')
    $list = (Ui @('search', 'Todo.List.Tasks', '--json') | ConvertFrom-Json).matches | Select-Object -First 1
    $matches = Nodes @($tree.windows.elements) |
        Where-Object { $_.type -eq 'ListItem' -and $_.automationId -match '^Todo.Row.[0-9a-f-]+$' -and -not $_.isOffscreen -and $_.y -gt $list.y + 40 -and $_.y + $_.height -lt $list.y + $list.height - 80 }
    if (-not $matches) { throw 'No rows visible in middle of fixture.' }
    $selected = @($matches)[[int](@($matches).Count / 2)]
    $number = [int]([regex]::Match($selected.name, 'Stability task (\d+)').Groups[1].Value)
    if ($number -lt 80 -or $number -gt 120) { throw "Fixture did not scroll to middle: task $number." }
    Check 'Scrolling active tasks leaves no blank header above the cards' {
        $window = $tree.windows | Where-Object { $_.elements[0].className -eq 'WinUIDesktopWin32WindowClass' } | Select-Object -First 1
        if (-not $window) { throw 'Main window missing from the inspection.' }
        $row = @($matches)[1]
        Capture '00-scrolled-list.png' $window.hwnd
        # UIA can report full row bounds even when a sticky header clips the card.
        # Compare the rendered top strip to the fill of the unselected task card.
        Add-Type -AssemblyName System.Drawing
        $bitmap = [Drawing.Bitmap]::new((Join-Path $ArtifactDirectory '00-scrolled-list.png'))
        try {
            $origin = $window.elements[0]
            $x = [int]($list.x - $origin.x + $list.width * 0.75)
            $top = [int]($list.y - $origin.y)
            $card = $bitmap.GetPixel($x, [int]($row.y - $origin.y + $row.height / 2))
            $hasCard = $false
            for ($i = 0; $i -lt [Math]::Ceiling(12 * $window.scale); $i++) {
                $color = $bitmap.GetPixel($x, $top + $i)
                if ([Math]::Max([Math]::Abs($color.R - $card.R),
                    [Math]::Max([Math]::Abs($color.G - $card.G), [Math]::Abs($color.B - $card.B))) -le 3) {
                    $hasCard = $true
                    break
                }
            }
            if (-not $hasCard) { throw 'The top of the scrolled list is blank above the task cards.' }
        } finally { $bitmap.Dispose() }
    }
    $selected = (Ui @('search', $selected.automationId, '--type', 'ListItem', '--json') | ConvertFrom-Json).matches | Select-Object -First 1
    Ui @('invoke', $selected.selector) | Out-Null
    Ui @('wait-for', 'Todo.Detail.Title', '-t', '5000') | Out-Null
    Ui @('focus', $selected.selector) | Out-Null
    $selectedUid = $selected.automationId
    $selectedTitle = $selected.name.Split(',')[0]
    $anchor = @($matches)[0]
    $anchorTitle = $anchor.name.Split(',')[0]
    $before = Row $anchorTitle
    Capture '01-before.png'
    Check 'Ctrl+I keeps selected row and focus' {
        Ui @('send-keys', 'ctrl+vk=0x49', '--via', 'send-input') | Out-Null
        Start-Sleep -Milliseconds 500
        $current = (Ui @('search', $selectedUid, '--type', 'ListItem', '--json') | ConvertFrom-Json).matches | Select-Object -First 1
        $props = Properties $current
        if ($props.IsSelected -ne 'True' -or $props.HasKeyboardFocus -ne 'True') { throw "Selection/focus lost: $($props | ConvertTo-Json -Compress)" }
        Capture '02-star.png'
    }
    Check 'Alt+Down moves selected row and keeps focus' {
        $current = (Ui @('search', $selectedUid, '--type', 'ListItem', '--json') | ConvertFrom-Json).matches | Select-Object -First 1
        $oldY = $current.y
        Ui @('send-keys', 'alt+down', '--via', 'send-input') | Out-Null
        Start-Sleep -Milliseconds 600
        $current = (Ui @('search', $selectedUid, '--type', 'ListItem', '--json') | ConvertFrom-Json).matches | Select-Object -First 1
        $props = Properties $current
        Capture '03-reorder.png'
        if ($current.y -le $oldY -or $props.IsSelected -ne 'True' -or $props.HasKeyboardFocus -ne 'True') { throw "Reorder/selection/focus failed: oldY=$oldY newY=$($current.y), $($props | ConvertTo-Json -Compress)" }
    }
    Check 'Space completion keeps scroll within one row and selection/focus' {
        $before = Row $anchorTitle
        $listBefore = (Ui @('search', 'Todo.List.Tasks', '--json') | ConvertFrom-Json).matches | Select-Object -First 1
        Ui @('send-keys', 'space', '--via', 'send-input') | Out-Null
        Start-Sleep -Milliseconds 900
        Capture '04-completed.png'
        $after = Row $anchorTitle
        $listAfter = (Ui @('search', 'Todo.List.Tasks', '--json') | ConvertFrom-Json).matches | Select-Object -First 1
        $delta = ($after.y - $listAfter.y) - ($before.y - $listBefore.y)
        @{before=$before;after=$after;listBefore=$listBefore;listAfter=$listAfter;delta=$delta} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $ArtifactDirectory 'scroll-anchor.json')
        if (-not $after -or [Math]::Abs($delta) -gt $before.height + 4) { throw "Scroll anchor moved more than one row: relative delta=$delta,height=$($before.height)." }
        $listProperties = (Ui @('get-property', 'Todo.List.Tasks', '--json') | ConvertFrom-Json).properties
        Ui @('get-property', 'Todo.List.Tasks', '--json') | Set-Content (Join-Path $ArtifactDirectory 'completion-list-properties.json')
        $focusScript = @'
Add-Type -AssemblyName UIAutomationClient
$focused = [System.Windows.Automation.AutomationElement]::FocusedElement
@{ id = $focused.Current.AutomationId; name = $focused.Current.Name; process = $focused.Current.ProcessId } | ConvertTo-Json
'@
        $focus = & powershell.exe -NoProfile -NonInteractive -Command $focusScript | ConvertFrom-Json
        $focus | ConvertTo-Json | Set-Content (Join-Path $ArtifactDirectory 'completion-focus.json')
        if ($focus.process -ne $AppPid -or $focus.id -ne $selectedUid) { throw 'Keyboard focus left the selected completed row.' }
        Start-Sleep -Milliseconds 1200
        $settled = Row $anchorTitle
        $settledList = (Ui @('search', 'Todo.List.Tasks', '--json') | ConvertFrom-Json).matches | Select-Object -First 1
        $settledDelta = ($settled.y - $settledList.y) - ($before.y - $listBefore.y)
        @{row=$settled;list=$settledList;delta=$settledDelta} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $ArtifactDirectory 'settled-scroll-anchor.json')
        Capture '05-settled.png'
        if (-not $settled -or [Math]::Abs($settledDelta) -gt $before.height + 4) { throw "Settled scroll anchor moved more than one row: delta=$settledDelta,height=$($before.height)." }
        # The completed container can be virtualized. Prove logical selection by
        # toggling it again through the focused list, then inspecting that task.
        Ui @('send-keys', 'space', '--via', 'send-input') | Out-Null
        Start-Sleep -Milliseconds 800
        $restored = Row $selectedTitle
        if (-not $restored -or $restored.name -notlike '*not completed*') { throw 'Space did not restore the same selected task.' }
        Start-Sleep -Milliseconds 1200
        $uncompletedAnchor = Row $anchorTitle
        $uncompletedList = (Ui @('search', 'Todo.List.Tasks', '--json') | ConvertFrom-Json).matches | Select-Object -First 1
        $uncompletedDelta = ($uncompletedAnchor.y - $uncompletedList.y) - ($before.y - $listBefore.y)
        @{row=$uncompletedAnchor;list=$uncompletedList;delta=$uncompletedDelta} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $ArtifactDirectory 'uncompleted-scroll-anchor.json')
        Capture '06-uncompleted.png'
        if (-not $uncompletedAnchor -or [Math]::Abs($uncompletedDelta) -gt $before.height + 4) { throw "Uncompleted scroll anchor moved more than one row: delta=$uncompletedDelta,height=$($before.height)." }
    }
} catch { $results.Add(@{name='Setup';status='FAIL';detail="$_"}) }
finally { winapp ui yield | Out-Null; Remove-Item Env:\WINAPP_UI_WORKFLOW_ID -ErrorAction SilentlyContinue }
@{results=@($results.ToArray());screenshots=@($screenshots.ToArray())} | ConvertTo-Json -Depth 12 |
    Set-Content (Join-Path $ArtifactDirectory 'results.json')
$results.ToArray() | ForEach-Object { [pscustomobject]$_ } | Format-Table name,status,detail -Wrap
if (@($results | Where-Object status -eq 'FAIL').Count) { exit 1 }
