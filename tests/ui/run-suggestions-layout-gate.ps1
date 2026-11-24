# Run with .\scripts\Invoke-VmGate.ps1 -Gate run-suggestions-layout-gate.ps1.
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1' -or $root -ine 'C:\wintodo' -or
    (Get-ItemPropertyValue 'HKLM:\SOFTWARE\Microsoft\Virtual Machine\Guest\Parameters' VirtualMachineName) -cne 'WintodoTest') {
    throw 'Run this gate in WintodoTest through scripts/Invoke-VmGate.ps1.'
}
. (Join-Path $PSScriptRoot 'SuggestionsFixtureSql.ps1')
$artifacts = Join-Path $PSScriptRoot ('artifacts/suggestions-layout/' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $artifacts
$database = Join-Path $artifacts 'layout.db'
$clock = Join-Path $artifacts 'clock.txt'
$app = Join-Path $root 'src/Wintodo.App/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64'
[IO.File]::WriteAllText($clock, [TimeZoneInfo]::ConvertTimeToUtc([datetime]::new(2026,11,24,12,0,0)).ToString('O'))
$report = [ordered]@{status='FAIL'; screenshots=@(); issues=@(); measurements=[ordered]@{}}
$previousWorkflow = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = [guid]::NewGuid().ToString('N')
$appPid = 0

function Ui([string[]]$Command) {
    $output = & winapp ui @Command -a $appPid
    if ($LASTEXITCODE -ne 0) { throw "winapp ui $Command failed: $output" }
    $output
}
function Nodes($items) { foreach ($item in $items) { $item; Nodes $item.children } }
function Snapshot([string]$Name) {
    $json = Ui @('inspect','--depth','30','--hide-offscreen','--json')
    $json | Set-Content -LiteralPath (Join-Path $artifacts "$Name.json")
    $tree = $json | ConvertFrom-Json
    $main = @($tree.windows | Where-Object hwnd -eq $hwnd.ToInt64())
    if ($main.Count -ne 1) { throw "No unique app window in $Name." }
    $main[0]
}
function Element($Window,[string]$Id) {
    $found = @(Nodes $Window.elements | Where-Object { $_.automationId -ceq $Id -and !$_.isOffscreen })
    if ($found.Count -ne 1) { throw "Expected one visible $Id; found $($found.Count)." }
    $found[0]
}
function Capture([string]$Name,[string]$Id) {
    $path = Join-Path $artifacts "$Name.png"
    [string[]]$selector = if ($Id) { $Id } else { '--capture-screen' }
    & winapp ui screenshot @selector -w $hwnd.ToInt64() -o $path | Out-Null
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) {
        throw "Screenshot missing: $Name."
    }
    $report.screenshots += "$Name.png"
    $path
}
function DateInk([string]$Path) {
    # The TextBlock's accessible name can remain complete even when only 'Tuesday,' is drawn.
    # Measure the rendered glyphs within its screenshot, ignoring the stretched background.
    $image = [Drawing.Bitmap]::new($Path)
    try {
        $first = $image.Width; $last = -1; $count = 0
        for ($x=0; $x -lt $image.Width; $x++) {
            $bg = $image.GetPixel($x,0)
            for ($y=1; $y -lt $image.Height; $y++) {
                $pixel = $image.GetPixel($x,$y)
                if ([math]::Max([math]::Abs($bg.R-$pixel.R),[math]::Max([math]::Abs($bg.G-$pixel.G),[math]::Abs($bg.B-$pixel.B))) -ge 48) {
                    $first = [math]::Min($first,$x); $last = [math]::Max($last,$x); $count++
                }
            }
        }
        return [ordered]@{inkWidthPx=($last-$first+1); inkPixels=$count; cropWidthPx=$image.Width; cropHeightPx=$image.Height}
    } finally { $image.Dispose() }
}
function Set-Width([int]$Width) {
    if (![SuggestionsLayoutNative]::SetWindowPos($hwnd,[IntPtr]::Zero,8,8,[int]($Width*$scale),[int](640*$scale),4)) {
        throw 'Failed to resize the guest window.'
    }
    Start-Sleep -Milliseconds 500
    $bounds = [SuggestionsLayoutNative+Rect]::new()
    if (![SuggestionsLayoutNative]::GetWindowRect($hwnd,[ref]$bounds) -or [math]::Abs($bounds.Right-$bounds.Left-$Width*$scale) -gt 1) {
        throw "The actual guest window width did not reach $Width DIPs."
    }
}
function VisibleAdds($List) {
    Nodes $List.children | Where-Object { $_.automationId -like 'Todo.Suggestions.Add.*' -and !$_.isOffscreen -and
        $_.y -ge $List.y -and $_.y + $_.height -le $List.y + $List.height } | Sort-Object y
}
function RailInkGap([string]$Path,$Window,$List,[double]$ButtonRight) {
    # WinUI can hide the scroll peer from the control tree. Locate the rendered
    # thumb/track as a sustained coloured strip in the right edge of the hover screenshot.
    $bmp = [Drawing.Bitmap]::new($Path)
    try {
        $origin = $Window.elements[0]
        if ($bmp.Width -ne $origin.width -or $bmp.Height -ne $origin.height) { throw 'Hover capture and UIA window bounds differ.' }
        $reference = [int]($ButtonRight-$origin.x+1)
        $last = [int]($List.x+$List.width-$origin.x-1)
        $top = [int]($List.y-$origin.y+20*$scale)
        $bottom = [int]($List.y+$List.height-$origin.y-20*$scale)
        $firstInk = -1; $lastInk = -1
        for ($x=$reference+1; $x -lt $last; $x++) {
            $run = 0
            for ($y=$top; $y -lt $bottom; $y++) {
                $bg=$bmp.GetPixel($reference,$y); $p=$bmp.GetPixel($x,$y)
                if ([math]::Max([math]::Abs($bg.R-$p.R),[math]::Max([math]::Abs($bg.G-$p.G),[math]::Abs($bg.B-$p.B))) -ge 16) {
                    if ((++$run) -ge 24*$scale) {
                        if ($firstInk -lt 0) { $firstInk = $x }; $lastInk = $x; break
                    }
                } else { $run = 0 }
            }
        }
        if ($firstInk -ge 0) { return @{gapDip=($firstInk+$origin.x-$ButtonRight)/$scale; widthDip=($lastInk-$firstInk+1)/$scale} }
        return $null
    } finally { $bmp.Dispose() }
}
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class SuggestionsLayoutNative {
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr z, int x, int y, int w, int height, uint flags);
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out Rect r);
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int x, y; public uint data, flags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint type; public MouseInput mouse; }
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point p);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr h, uint flag);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int metric);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] input, int size);
    // The guest's pinned winapp predates coordinate hover/click. Keep physical input inside this HWND.
    public static void Pointer(IntPtr hwnd, int x, int y, bool click) {
        var point = new Point { x=x, y=y };
        if (GetForegroundWindow() != hwnd || GetAncestor(WindowFromPoint(point), 2) != hwnd)
            throw new InvalidOperationException("Pointer target is not inside the gate window.");
        int left=GetSystemMetrics(76), top=GetSystemMetrics(77), width=GetSystemMetrics(78), height=GetSystemMetrics(79);
        if (width<=0 || height<=0) throw new InvalidOperationException("No guest virtual screen.");
        // SetCursorPos alone can move the cursor without switching WinUI from keyboard to pointer visuals.
        var move = new Input { mouse=new MouseInput {
            x=(int)(((long)(x-left)*65536+32768)/width), y=(int)(((long)(y-top)*65536+32768)/height),
            flags=0xC001 /* ABSOLUTE | VIRTUALDESK | MOVE */ } };
        if (SendInput(1,new[]{move},Marshal.SizeOf<Input>()) != 1) throw new InvalidOperationException("Physical hover failed.");
        if (click) {
            var buttons = new[] { new Input { mouse=new MouseInput { flags=2 } }, new Input { mouse=new MouseInput { flags=4 } } };
            if (SendInput(2, buttons, Marshal.SizeOf<Input>()) != 2) throw new InvalidOperationException("Physical click failed.");
        }
    }
}
'@

try {
    $launch = & winapp run $app --detach --json -- --database-path $database --clock-file $clock | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or !$launch.ProcessId) { throw 'Packaged Debug launch failed.' }
    $appPid = [int]$launch.ProcessId
    Ui @('wait-for','Todo.Nav.DefaultList','-t','120000') | Out-Null
    $hwnd = (Get-Process -Id $appPid).MainWindowHandle
    $scale = [SuggestionsLayoutNative]::GetDpiForWindow($hwnd)/96.0
    if ($hwnd -eq [IntPtr]::Zero -or $scale -le 0) { throw 'No main window/DPI for layout measurement.' }
    $report.measurements.scale = $scale
    Set-Width 960
    Ui @('invoke','Todo.Nav.MyDay') | Out-Null
    Ui @('click','Todo.Suggestions.Open') | Out-Null
    Ui @('wait-for','Todo.Suggestions.Status','-p','Name','--value','You have no suggested tasks.','-t','5000') | Out-Null
    Set-Width 640
    $empty = Snapshot '00-empty-narrow'
    $emptyStatus = Element $empty 'Todo.Suggestions.Status'
    $emptyDate = Element $empty 'Todo.Shell.Subtitle'
    $messages = @(Nodes $empty.elements | Where-Object { $_.type -eq 'Text' -and
        $_.name -in @('Focus on your day','Add tasks you want to get done today.') })
    Capture '00-empty-narrow' | Out-Null
    $report.measurements.emptyMessages = @($messages | Select-Object name,x,y,width,height)
    # The caption uses the date's type ramp. Previously only the top 8 px survived clipping.
    if ($messages.Count -ne 2 -or @($messages | Where-Object {
        $_.x -lt $emptyDate.x-1 -or $_.x+$_.width -gt $emptyStatus.x -or $_.height -lt $emptyDate.height-1 }).Count) {
        $report.issues += 'Empty My Day instructions are clipped beside Suggestions at 640 DIPs.'
    }
    Ui @('click','Todo.Suggestions.Close') | Out-Null
    $seedCount = Invoke-SuggestionsFixtureSql -Database $database -Sql @'
WITH RECURSIVE fixture(n) AS (VALUES(0) UNION ALL SELECT n+1 FROM fixture WHERE n<36)
INSERT INTO tasks(uid,list_id,summary,my_day_date,created_utc,modified_utc,sort_order)
SELECT printf('a0000000-0000-4000-8000-%012d',n), (SELECT id FROM lists WHERE is_default=1 AND is_deleted=0),
       CASE WHEN n=0 THEN 'Pinned layout anchor' ELSE printf('Suggestion %02d',n) END,
       CASE WHEN n=0 THEN '2026-11-24' END, '2026-11-24T06:00:00.0000000Z', '2026-11-24T06:00:00.0000000Z', n*1024
FROM fixture;
SELECT count(*) FROM tasks WHERE uid LIKE 'a0000000-0000-4000-8000-%';
'@
    if ($seedCount -ne '37') { throw 'Overflow fixture did not seed 36 suggestions and one pinned task.' }
    Set-Width 960
    Ui @('invoke','Todo.Nav.DefaultList') | Out-Null
    Ui @('wait-for','Todo.Suggestions.Open','--gone','-t','5000') | Out-Null
    $closed = Snapshot '01-tasks'
    $sort = Element $closed 'Todo.List.SortButton'
    $menu = Element $closed 'Todo.List.MenuButton'
    $gap = $menu.x-$sort.x-$sort.width
    $report.measurements.tasksCommandGapDip = $gap/$scale
    Capture '01-tasks' | Out-Null
    if ([math]::Abs($gap-8*$scale) -gt 1) { $report.issues += "Sort to list menu: $gap px. Expected 8 DIPs when the bulb is absent." }

    Ui @('invoke','Todo.Nav.MyDay') | Out-Null
    Ui @('wait-for','Todo.Row.a0000000-0000-4000-8000-000000000000','-t','10000') | Out-Null
    $wideDate = Element (Snapshot '02-my-day') 'Todo.Shell.Subtitle'
    if ($wideDate.name -cne 'Tuesday, November 24, 2026') { throw "Unexpected fixture date: $($wideDate.name)." }
    $wideInk = DateInk (Capture '02-date-wide' 'Todo.Shell.Subtitle')
    $report.measurements.wideDate = $wideInk
    if ($wideInk.inkWidthPx -lt 125*$scale -or $wideInk.inkPixels -lt 150*$scale*$scale) { throw 'Wide reference lacks a full line of rendered date text.' }
    Ui @('click','Todo.Suggestions.Open') | Out-Null
    Ui @('wait-for','Todo.Suggestions.Add.a0000000-0000-4000-8000-000000000001','-t','10000') | Out-Null
    Capture '03-wide-suggestions' | Out-Null
    Set-Width 640
    $narrow = Snapshot '04-narrow'
    if ([math]::Abs($narrow.elements[0].width-640*$scale) -gt 1) { throw 'The inspected window is not 640 DIPs wide.' }
    $date = Element $narrow 'Todo.Shell.Subtitle'
    Capture '04-narrow' | Out-Null
    $narrowInk = DateInk (Capture '04-date-narrow' 'Todo.Shell.Subtitle')
    $report.measurements.narrowDate = $narrowInk
    if ($date.name -cne $wideDate.name -or [math]::Abs($narrowInk.inkWidthPx-$wideInk.inkWidthPx) -gt 2*$scale -or
        [math]::Abs($narrowInk.inkPixels-$wideInk.inkPixels) -gt 0.08*$wideInk.inkPixels) {
        $report.issues += "Date is clipped at 640 DIPs: $($narrowInk.inkWidthPx) ink pixels across; wide reference is $($wideInk.inkWidthPx)."
    }

    $list = Element $narrow 'Todo.Suggestions.List'
    $nav = Element $narrow 'Todo.Nav.Root'
    $bottomGap = ($nav.y+$nav.height)-($list.y+$list.height)
    $report.measurements.viewportBottomGapDip = $bottomGap/$scale
    if ([math]::Abs($bottomGap) -gt 1) { $report.issues += "Unexpected strip below Suggestions: $bottomGap px." }
    $before = @(VisibleAdds $list)
    if ($before.Count -lt 2) { throw 'Expected at least two visible suggestion buttons before scrolling.' }
    Ui @('scroll','Todo.Suggestions.List','--wheel','-18') | Out-Null
    Start-Sleep -Milliseconds 500
    $scrolled = Element (Snapshot '05-scrolled') 'Todo.Suggestions.List'
    $after = @(VisibleAdds $scrolled)
    $newButtons = @($after | Where-Object { $_.automationId -notin $before.automationId })
    if (!$newButtons.Count) { throw 'Physical wheel scrolling exposed no new fully visible row.' }
    $target = $newButtons[[int][math]::Floor($newButtons.Count/2)]
    $right = $scrolled.x+$scrolled.width
    $buttonRight = $target.x+$target.width
    $middle = [int]($target.y+$target.height/2)
    # Hover inside the rail first to reveal the full native scrollbar, not just its resting indicator.
    # Move across the narrow resting indicator onto its thumb, which is at the bottom after this wheel scroll.
    $railY = [int]($scrolled.y+$scrolled.height-32*$scale)
    [SuggestionsLayoutNative]::Pointer($hwnd,[int]($right-5*$scale),$railY,$false)
    Start-Sleep -Milliseconds 700
    $hoverWindow = Snapshot '06-rail-hover'
    $hoverList = Element $hoverWindow 'Todo.Suggestions.List'
    $bar = @(Nodes $hoverList.children | Where-Object { $_.type -eq 'ScrollBar' -and !$_.isOffscreen -and
        $_.height -gt $_.width -and $_.width -gt 0 -and $_.y -le $middle -and $_.y+$_.height -gt $middle } |
        Sort-Object x | Select-Object -First 1)
    $railImage = Capture '06-rail-hover'
    $railInk = RailInkGap $railImage $hoverWindow $hoverList $buttonRight
    $report.measurements.scroll = [ordered]@{firstBefore=$before[0].automationId;firstAfter=$after[0].automationId;
        add=$target.automationId; addRightInsetDip=($right-$buttonRight)/$scale; visibleRail=$railInk; scrollbarGapDip=$null}
    # Some WinUI versions suppress the scrollbar peer. Its expanded hit area may occupy 16 DIPs.
    if ($right-$buttonRight -lt 20*$scale-1) { $report.issues += 'Add hitbox intrudes on the expanded native scrollbar gutter.' }
    if ($null -eq $railInk -or $railInk.widthDip -lt 4) { $report.issues += 'Expanded scrollbar thumb is not visible in the hover screenshot.' }
    elseif ($railInk.gapDip -lt 4-1/$scale) { $report.issues += 'Expanded rail overlaps the Add target.' }
    if ($bar.Count) {
        $railGap = $bar[0].x-$buttonRight
        $report.measurements.scroll.scrollbarGapDip = $railGap/$scale
        if ($railGap -lt 4*$scale-1) { $report.issues += "Add is only $railGap px from the expanded scrollbar." }
    }
    # Exercise the right edge of the Add target, which a center click or UIA Invoke cannot test.
    $edge = [int]($buttonRight-3*$scale)
    [SuggestionsLayoutNative]::Pointer($hwnd,$edge,$middle,$false)
    Start-Sleep -Milliseconds 150
    Capture '07-add-hover' | Out-Null
    [SuggestionsLayoutNative]::Pointer($hwnd,$edge,$middle,$true)
    $row = $target.automationId.Replace('Todo.Suggestions.Add.','Todo.Row.')
    Ui @('wait-for',$target.automationId,'--gone','-t','5000') | Out-Null
    Ui @('wait-for',$row,'-t','5000') | Out-Null
    Capture '08-added' | Out-Null
    if ($report.issues.Count) { throw ($report.issues -join ' ') }
    $report.status = 'PASS'
    Write-Host "Suggestions layout: PASS (8 DIP toolbar gap, full date at 640 DIPs, flush viewport, working scrolled Add)."
} catch {
    $report.error = "$_"
    if ($appPid) {
        & winapp ui inspect -a $appPid --depth 30 --json | Set-Content -LiteralPath (Join-Path $artifacts 'failure-tree.json')
        & winapp ui screenshot -a $appPid -o (Join-Path $artifacts 'failure.png') | Out-Null
    }
    throw
} finally {
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $artifacts 'results.json')
    if ($appPid) { Stop-Process -Id $appPid -ErrorAction SilentlyContinue; & winapp ui yield | Out-Null }
    $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflow
}
