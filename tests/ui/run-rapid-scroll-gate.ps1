# Sample the actual title pixels while real wheel input crosses several screens.
param([string]$AppOutput = 'src/Wintodo.App/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64')
$ErrorActionPreference = 'Stop'
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1' -or $PSScriptRoot -ine 'C:\wintodo\tests\ui' -or
    (Get-ItemPropertyValue 'HKLM:\SOFTWARE\Microsoft\Virtual Machine\Guest\Parameters' VirtualMachineName) -cne 'WintodoTest') {
    throw 'Run this gate in WintodoTest through scripts/Invoke-VmGate.ps1.'
}
. (Join-Path $PSScriptRoot 'SuggestionsFixtureSql.ps1')
Add-Type -AssemblyName System.Drawing
$artifacts = Join-Path $PSScriptRoot ('artifacts/rapid-scroll/' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $artifacts
$db = Join-Path $artifacts 'scroll.db'
$workflow = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = [guid]::NewGuid().ToString('N')
$appPid = 0
$recording = $null
$report = [ordered]@{status='FAIL'; appOutput=$AppOutput}

function Ui([string[]]$Arguments) {
    $output = & winapp ui @Arguments --on local -a $appPid
    if ($LASTEXITCODE -ne 0) { throw "winapp ui $Arguments failed: $output" }
    $output
}
function Nodes($Items) { foreach ($item in $Items) { $item; Nodes $item.children } }
function VisibleTitles {
    $tree = Ui @('inspect','--hide-offscreen','--depth','16','--json') | ConvertFrom-Json
    $window = @($tree.windows | Where-Object title -eq 'Wintodo')
    if ($window.Count -ne 1) { throw 'Expected one Wintodo window.' }
    $list = @(Nodes $window[0].elements | Where-Object automationId -eq 'Todo.List.Tasks')
    if ($list.Count -ne 1) { throw 'Expected one visible task list.' }
    $list = $list[0]
    $titles = @(Nodes $list.children | Where-Object {
        $_.type -eq 'Text' -and $_.name -match '^\d{4} Scroll sample$' -and
        $_.width -gt 0 -and $_.y -ge $list.y -and $_.y+$_.height -le $list.y+$list.height
    } | Sort-Object y)
    if ($titles.Count -lt 3) { throw 'Too few visible fixture titles to calibrate the recording.' }
    @{list=$list; titles=$titles; scale=$window[0].scale}
}
function TitleInk([string]$Path, $Before) {
    $bmp = [Drawing.Bitmap]::new($Path)
    try {
        $sx = $bmp.Width / $Before.list.width
        if ([Math]::Abs($sx - $bmp.Height / $Before.list.height) -gt 0.02) { throw 'Recording is not the calibrated list crop.' }
        # Use the interior of the title column. A card outline, checkbox, scrollbar
        # or placeholder fill cannot supply glyph contrast along these scanlines.
        $left = [int](($Before.titles[0].x - $Before.list.x + 3*$Before.scale)*$sx)
        $right = [int]($left + 80*$Before.scale*$sx)
        if ($left -lt 0 -or $right -ge $bmp.Width) { throw 'Title column is outside the recording.' }
        $profile = [Text.StringBuilder]::new()
        $ink = 0
        for ($y=[int]($bmp.Height*0.08); $y -lt $bmp.Height*0.92; $y+=3) {
            $min=765; $max=0
            for ($x=$left; $x -le $right; $x+=3) {
                $p = $bmp.GetPixel($x,$y)
                $l = [int]$p.R + $p.G + $p.B
                $min = [Math]::Min($min,$l); $max = [Math]::Max($max,$l)
                if ($max-$min -gt 180) { $ink++; $null=$profile.Append("${y}/${x};"); break }
            }
        }
        @{ink=$ink; profile=$profile.ToString()}
    } finally { $bmp.Dispose() }
}
try {
    $launch = & winapp run $AppOutput --detach --json -- --database-path $db | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or !$launch.ProcessId) { throw 'Packaged launch failed.' }
    $appPid = $launch.ProcessId
    Ui @('wait-for','Todo.Nav.DefaultList','-t','120000') | Out-Null
    $report.executable = (Get-Process -Id $appPid).Path
    $report.sha256 = [ordered]@{}
    foreach ($file in 'Wintodo.dll','Controls/TaskListView.xbf','Controls/TaskRow.xbf') {
        $actual = Join-Path (Split-Path -Parent $report.executable) $file
        $report.sha256[$file] = (Get-FileHash -LiteralPath $actual).Hash
        if ($report.sha256[$file] -ne (Get-FileHash -LiteralPath (Join-Path $AppOutput $file)).Hash) {
            throw "Launched $file differs from the requested payload."
        }
    }
    Ui @('invoke','Todo.Nav.MyDay') | Out-Null
    $count = Invoke-SuggestionsFixtureSql $db @'
WITH RECURSIVE seq(n) AS (VALUES(1) UNION ALL SELECT n+1 FROM seq WHERE n<1000)
INSERT INTO tasks(uid,list_id,summary,created_utc,modified_utc,sort_order)
SELECT printf('d0000000-0000-4000-8000-%012d',n), (SELECT id FROM lists WHERE is_default=1 AND is_deleted=0),
       printf('%04d Scroll sample',n),'2026-11-25T00:00:00Z','2026-11-25T00:00:00Z',n*1024 FROM seq;
SELECT count(*) FROM tasks WHERE uid LIKE 'd0000000-0000-4000-8000-%';
'@
    if ($count -ne '1000') { throw 'Fixture population failed.' }
    Ui @('invoke','Todo.Nav.DefaultList') | Out-Null
    Ui @('wait-for','Todo.Row.d0000000-0000-4000-8000-000000000001','--type','ListItem','-t','15000') | Out-Null
    $before = VisibleTitles
    $ready = Join-Path $artifacts 'ready'
    $video = Join-Path $artifacts 'scroll.mp4'
    $recording = Start-Job -ArgumentList $appPid,$env:WINAPP_UI_WORKFLOW_ID,$video,$ready -ScriptBlock {
        param($targetPid,$workflowId,$path,$ready)
        $env:WINAPP_UI_WORKFLOW_ID = $workflowId
        & winapp ui record Todo.List.Tasks --on local -a $targetPid --capture-screen --frames --duration-sec 8 --fps 30 -o $path --json 2>&1 |
            ForEach-Object { if ("$_" -like '*recording-started*') { Set-Content -LiteralPath $ready -Value started }; "$_" }
        if ($LASTEXITCODE -ne 0) { throw 'Frame recording failed.' }
    }
    $deadline = [DateTime]::UtcNow.AddSeconds(12)
    while (!(Test-Path -LiteralPath $ready) -and $recording.State -eq 'Running' -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 50
    }
    if (!(Test-Path -LiteralPath $ready)) { throw 'Recording did not start.' }
    Start-Sleep -Milliseconds 200 # Establish the unchanged frame before input.
    $start = [DateTimeOffset]::UtcNow
    foreach ($wheel in '-48','28','-64') { Ui @('scroll','Todo.List.Tasks','--wheel',$wheel) | Out-Null }
    $end = [DateTimeOffset]::UtcNow
    if (!(Wait-Job $recording -Timeout 15)) { throw 'Recording did not finish.' }
    Receive-Job $recording -ErrorAction Stop | Set-Content (Join-Path $artifacts 'recording.txt')
    $after = VisibleTitles
    $report.firstBefore = $before.titles[0].name
    $report.firstAfter = $after.titles[0].name
    if ([Math]::Abs([int]$report.firstAfter.Substring(0,4) - [int]$report.firstBefore.Substring(0,4)) -lt 2*$before.titles.Count) {
        throw 'Wheel input did not move at least two screens of tasks.'
    }
    $bundle = Join-Path $artifacts 'scroll.frames'
    $manifest = Get-Content -Raw (Join-Path $bundle 'manifest.json') | ConvertFrom-Json
    if ($manifest.status -ne 'complete' -or $manifest.frames.truncated) { throw 'Incomplete frame bundle.' }
    $samples = @(Get-Content (Join-Path $bundle $manifest.frames.index) | ForEach-Object { $_ | ConvertFrom-Json })
    $base = [DateTimeOffset]$manifest.startedUtc
    $interval = @(($start-$base).TotalMilliseconds, ($end-$base).TotalMilliseconds)
    if (!$samples.Count -or $samples[0].elapsedMs -ge $interval[0] -or $samples[-1].elapsedMs -lt $interval[1]+250) {
        throw 'The recording did not cover the entire wheel burst and its settling time.'
    }
    $cache = @{}
    $frames = foreach ($sample in $samples) {
        if (!$cache.ContainsKey($sample.file)) { $cache[$sample.file] = TitleInk (Join-Path $bundle $sample.file) $before }
        $stats = $cache[$sample.file]
        [pscustomobject]@{ms=$sample.elapsedMs; file=$sample.file; ink=$stats.ink; profile=$stats.profile}
    }
    $threshold = [Math]::Max(3,[Math]::Ceiling($frames[0].ink*0.2))
    $inMotion = @($frames | Where-Object { $_.ms -ge $interval[0] -and $_.ms -le $interval[1]+250 })
    $report.motionSamples = $inMotion.Count
    $report.distinctTitlePatterns = @($inMotion.profile | Sort-Object -Unique).Count
    $report.referenceInkLines = $frames[0].ink
    $report.minimumInkLines = ($frames | Measure-Object ink -Minimum).Minimum
    $report.blankSamples = @($frames | Where-Object ink -lt $threshold | Select-Object ms,file,ink)
    $report.maxGapMs = 0
    for ($i=1; $i -lt $frames.Count; $i++) {
        if ($frames[$i].ms -ge $interval[0] -and $frames[$i-1].ms -le $interval[1]+250) {
            $report.maxGapMs = [Math]::Max($report.maxGapMs, $frames[$i].ms-$frames[$i-1].ms)
        }
    }
    if ($report.referenceInkLines -lt 9 -or $report.distinctTitlePatterns -lt 3 -or $report.maxGapMs -gt 100) {
        throw 'Insufficient visible title, motion, or sampling coverage; inspect the frame bundle.'
    }
    if ($report.blankSamples.Count) { throw 'Task titles disappeared during the recorded wheel scroll.' }
    $report.status = 'PASS'
} catch { $report.error = "$_"; throw }
finally {
    $report | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $artifacts 'results.json')
    if ($recording) { Stop-Job $recording; Remove-Job $recording }
    if ($appPid) { Stop-Process -Id $appPid -ErrorAction SilentlyContinue; & winapp ui yield --on local | Out-Null }
    $env:WINAPP_UI_WORKFLOW_ID = $workflow
    Write-Host "Rapid-scroll gate: $($report.status) - $artifacts"
}
