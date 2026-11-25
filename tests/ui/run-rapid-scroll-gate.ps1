# Sample the actual title pixels while real wheel input crosses several screens.
[CmdletBinding(DefaultParameterSetName='Debug')]
param(
    [Parameter(ParameterSetName='Debug')][string]$AppOutput = 'src/Wintodo.App/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64',
    [Parameter(Mandatory,ParameterSetName='Dev')][string]$InputDirectory,
    [switch]$CheckRename,
    [ValidateSet('1','8')][int]$WheelNotchesPerPulse = 1
)
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
$report = [ordered]@{status='FAIL'; appOutput=$AppOutput; wheelNotchesPerPulse=$WheelNotchesPerPulse;
    requestedAbsoluteWheelDelta=140*120*$WheelNotchesPerPulse}

function Ui([string[]]$Arguments) {
    $output = & winapp ui @Arguments --on local -a $appPid
    if ($LASTEXITCODE -ne 0) { throw "winapp ui $Arguments failed: $output" }
    $output
}
function Nodes($Items) { foreach ($item in $Items) { $item; Nodes $item.children } }
function VisibleTitles([string]$label) {
    $raw = Ui @('inspect','--hide-offscreen','--depth','16','--json')
    $raw | Set-Content -LiteralPath (Join-Path $artifacts "$label-tree.json")
    $tree = $raw | ConvertFrom-Json
    $window = @($tree.windows | Where-Object { @(Nodes $_.elements | Where-Object automationId -eq 'Todo.List.Tasks').Count -eq 1 })
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
function Wait-VisibleTitles([string]$label) {
    $deadline=[DateTime]::UtcNow.AddSeconds(20)
    do {
        try { return (VisibleTitles $label) }
        catch { $lastError = $_.Exception.Message; Start-Sleep -Milliseconds 250 }
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "$label task list did not show the numbered fixture: $lastError"
}
function TitleInk([string]$Path, $Before) {
    $bmp = [Drawing.Bitmap]::new($Path)
    try {
        $sx = $bmp.Width / $Before.list.width
        if ([Math]::Abs($sx - $bmp.Height / $Before.list.height) -gt 0.02) { throw 'Recording is not the calibrated list crop.' }
        # Within the numbered title, several light/dark transitions on adjacent
        # scanlines distinguish glyphs from a border, checkbox or flat placeholder.
        $left = [int](($Before.titles[0].x - $Before.list.x + 3*$Before.scale)*$sx)
        $right = [int]($left + 100*$Before.scale*$sx)
        if ($left -lt 0 -or $right -ge $bmp.Width) { throw 'Title column is outside the recording.' }
        $step = [Math]::Max(1,[int][Math]::Round(2*$Before.scale*$sx))
        $centres = [Collections.Generic.List[double]]::new()
        $profile = [Text.StringBuilder]::new()
        $ink = 0; $first = -1; $last = -100
        for ($y=0; $y -lt $bmp.Height; $y+=$step) {
            $edges=0; $previous=0
            for ($x=$left; $x -le $right; $x+=$step) {
                $p = $bmp.GetPixel($x,$y)
                $l = [int]$p.R + $p.G + $p.B
                if ($x -gt $left -and [Math]::Abs($l-$previous) -gt 132) { $edges++ }
                $previous=$l
            }
            if ($edges -lt 4) { continue }
            if ($y-$last -gt 2*$step) {
                if ($first -ge 0 -and $last -gt $first) { $centres.Add(($first+$last)/2) }
                $first=$y
            }
            $last=$y; $ink++; $null=$profile.Append("${y}/${edges};")
        }
        if ($first -ge 0 -and $last -gt $first) { $centres.Add(($first+$last)/2) }
        @{ink=$ink; centres=$centres.ToArray(); profile=$profile.ToString()}
    } finally { $bmp.Dispose() }
}
function MissingTitles([double[]]$Centres, [double]$Pitch, [double]$Height, [double]$Margin) {
    if (!$Centres.Count) { return [int][Math]::Floor(($Height-2*$Margin)/$Pitch) }
    # Partially clipped glyphs can vanish at either edge. Count an edge gap
    # only when a complete title would fit there at the phase of the next row.
    $missing = [Math]::Max(0,[Math]::Floor(($Centres[0]-$Margin)/$Pitch)) +
               [Math]::Max(0,[Math]::Floor(($Height-$Centres[-1]-$Margin)/$Pitch))
    for ($i=1; $i -lt $Centres.Count; $i++) {
        $missing += [Math]::Max(0,[Math]::Round(($Centres[$i]-$Centres[$i-1])/$Pitch)-1)
    }
    [int]$missing
}
function Start-SignedFixture {
    $family = 'Wintodo.Hybrid.Dev_1z32rh13vfry6'
    $directory = Get-Item -LiteralPath $InputDirectory
    $build = Get-Content -LiteralPath (Join-Path $directory.FullName 'build.json') -Raw | ConvertFrom-Json
    $version = [version]$build.version
    $msix = Join-Path $directory.FullName "Wintodo-DEV-$version-x64.msix"
    $sig = Get-AuthenticodeSignature -LiteralPath $msix
    if ($build.identity -cne 'Wintodo.Hybrid.Dev' -or $build.publisher -cne 'CN=AppPublisher' -or
        $sig.Status -ne 'Valid' -or $sig.SignerCertificate.Thumbprint -ine $build.signerThumbprint -or
        (Get-FileHash -LiteralPath $msix).Hash -ine $build.sha256) { throw 'Unverified DEV installer.' }
    $stopDeadline = [DateTime]::UtcNow.AddSeconds(12)
    while ((Get-Process -Name Wintodo -ErrorAction SilentlyContinue) -and [DateTime]::UtcNow -lt $stopDeadline) { Start-Sleep -Milliseconds 250 }
    if (Get-Process -Name Wintodo -ErrorAction SilentlyContinue) { throw 'Another Wintodo instance is running.' }
    $old = Get-AppxPackage -Name $build.identity
    if ($old -and ($old.PackageFamilyName -cne $family -or $old.IsDevelopmentMode -or [version]$old.Version -gt $version)) {
        throw 'Unexpected existing DEV registration on the test guest.'
    }
    if (!$old -or [version]$old.Version -lt $version) { Add-AppxPackage -Path $msix }
    $reg = Get-AppxPackage -Name $build.identity
    if ($reg.PackageFamilyName -cne $family -or $reg.Publisher -cne $build.publisher -or $reg.IsDevelopmentMode -or
        [string]$reg.Version -cne $build.version -or $reg.Status -ne 'Ok') { throw 'Signed DEV registration mismatch.' }
    $exe = Join-Path $reg.InstallLocation 'Wintodo.exe'
    # Verify the actual installed native binary and compiled XAML against the signed container.
    $zip = [IO.Compression.ZipFile]::OpenRead($msix)
    try {
        foreach ($entry in 'Wintodo.exe','resources.pri') {
            $inPackage = $zip.GetEntry($entry).Open()
            try {
                if ((Get-FileHash -InputStream $inPackage).Hash -ine (Get-FileHash -LiteralPath (Join-Path $reg.InstallLocation $entry)).Hash) {
                    throw "Installed $entry does not match the signed DEV installer."
                }
            } finally { $inPackage.Dispose() }
        }
    } finally { $zip.Dispose() }
    $report.appOutput = $InputDirectory; $report.version = $build.version
    $report.source = $build.source; $report.msixSha256 = $build.sha256
    $script:db = Join-Path $env:LOCALAPPDATA "Packages\$family\LocalState\wintodo.db"
    (New-Object -ComObject Shell.Application).ShellExecute("shell:AppsFolder\$family!App")
    $deadline = [DateTime]::UtcNow.AddSeconds(90)
    do {
        $app = Get-Process -Name Wintodo -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -ieq $exe -and $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
        if ($app) { break }; Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    if (!$app) { throw 'No window for the signed DEV package.' }
    $script:appPid = $app.Id
    if (!$old) {
        Ui @('wait-for','Todo.Welcome.StartFresh','-t','30000') | Out-Null
        Ui @('invoke','Todo.Welcome.StartFresh') | Out-Null
    }
}
function FixtureSql([string]$Sql) {
    Invoke-SuggestionsFixtureSql $db $Sql -InstalledDevFixture:([bool]$InputDirectory)
}
function Test-Rename([string]$title) {
    $uid = 'd0000000-0000-4000-8000-{0:D12}' -f [int]$title.Substring(0,4)
    $row = "Todo.Row.$uid"; $editor = "Todo.Row.TitleEdit.$uid"
    $readTitle = "SELECT summary FROM tasks WHERE uid='$uid'"
    Ui @('focus',$row) | Out-Null
    Ui @('send-keys','enter','--via','send-input') | Out-Null
    Ui @('wait-for','Todo.Detail.Title','--value',$title,'-t','5000') | Out-Null
    foreach ($mode in 'Escape','Enter','Blur') {
        Ui @('focus',$row) | Out-Null
        Ui @('send-keys','f2','--via','send-input') | Out-Null
        Ui @('wait-for',$editor,'-p','HasKeyboardFocus','--value','True','-t','5000') | Out-Null
        $expected = if ($mode -eq 'Enter') { 'Confirmed scroll fixture rename' } else { $title }
        $draft = if ($mode -eq 'Escape') { 'Discard this draft' } else { $expected }
        Ui @('set-value',$editor,$draft) | Out-Null
        if ($mode -eq 'Blur') { Ui @('focus','Todo.List.AddTaskBox') | Out-Null }
        else { Ui @('send-keys',$mode.ToLowerInvariant(),'--via','send-input') | Out-Null }
        Ui @('wait-for',$editor,'--gone','-t','5000') | Out-Null
        if ((FixtureSql $readTitle) -cne $expected) { throw "F2 $mode persisted an unexpected title." }
    }
    $report.rename = 'PASS: F2 focused editor; Escape discarded; Enter and blur saved by UID.'
}
try {
    if ($InputDirectory) { Start-SignedFixture }
    else {
        $launch = & winapp run $AppOutput --detach --json -- --database-path $db | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0 -or !$launch.ProcessId) { throw "Packaged launch failed: $($launch.Error)" }
        $appPid = $launch.ProcessId
        $report.sha256 = [ordered]@{}
        foreach ($file in 'Wintodo.dll','resources.pri','Controls/TaskListView.xbf','Controls/TaskRow.xbf') {
            $actual = Join-Path (Split-Path -Parent (Get-Process -Id $appPid).Path) $file
            $report.sha256[$file] = (Get-FileHash -LiteralPath $actual).Hash
            if ($report.sha256[$file] -ne (Get-FileHash -LiteralPath (Join-Path $AppOutput $file)).Hash) {
                throw "Launched $file differs from the requested payload."
            }
        }
    }
    Ui @('wait-for','Todo.Nav.DefaultList','-t','120000') | Out-Null
    $report.executable = (Get-Process -Id $appPid).Path
    Ui @('invoke','Todo.Nav.MyDay') | Out-Null
    $previousCount = FixtureSql 'SELECT count(*) FROM tasks'
    if ($previousCount -ne '0' -and $previousCount -ne '1000') { throw 'Expected empty or matching scroll fixture database.' }
    if ($previousCount -eq '0') {
        FixtureSql @'
WITH RECURSIVE seq(n) AS (VALUES(1) UNION ALL SELECT n+1 FROM seq WHERE n<1000)
INSERT INTO tasks(uid,list_id,summary,created_utc,modified_utc,sort_order)
SELECT printf('d0000000-0000-4000-8000-%012d',n), (SELECT id FROM lists WHERE is_default=1 AND is_deleted=0),
       printf('%04d Scroll sample',n),'2026-11-25T00:00:00.0000000Z','2026-11-25T00:00:00.0000000Z',n*1024 FROM seq;
'@
    }
    $count = FixtureSql "SELECT count(*) FROM tasks WHERE uid=printf('d0000000-0000-4000-8000-%012d',sort_order/1024) AND summary=printf('%04d Scroll sample',sort_order/1024) AND list_id=(SELECT id FROM lists WHERE is_default=1 AND is_deleted=0) AND is_deleted=0"
    if ($count -ne '1000') { throw 'Fixture population failed.' }
    Ui @('invoke','Todo.Nav.DefaultList') | Out-Null
    $report.fixtureRows = $count
    $before = Wait-VisibleTitles 'before'
    Add-Type -Path (Join-Path $PSScriptRoot 'RapidScrollWheel.cs')
    $ready = Join-Path $artifacts 'ready'
    $video = Join-Path $artifacts 'scroll.mp4'
    $recording = Start-Job -ArgumentList $appPid,$env:WINAPP_UI_WORKFLOW_ID,$video,$ready -ScriptBlock {
        param($targetPid,$workflowId,$path,$ready)
        $env:WINAPP_UI_WORKFLOW_ID = $workflowId
        & winapp ui record Todo.List.Tasks --on local -a $targetPid --capture-screen --frames --duration-sec 8 --fps 30 --max-edge 640 -o $path --json 2>&1 |
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
    $report.wheelStartUtc = $start.ToString('O')
    $wheelX = [int]($before.list.x + $before.list.width / 2)
    $wheelY = [int]($before.list.y + $before.list.height / 2)
    $report.wheelPulses = [RapidScrollWheel]::Run($appPid,$wheelX,$wheelY,$WheelNotchesPerPulse)
    $end = [DateTimeOffset]::UtcNow
    $report.wheelEndUtc = $end.ToString('O')
    $report.sentAbsoluteWheelDelta = $report.wheelPulses * 120 * $WheelNotchesPerPulse
    if ($report.sentAbsoluteWheelDelta -ne $report.requestedAbsoluteWheelDelta) { throw 'The full wheel stimulus was not delivered.' }
    if (!(Wait-Job $recording -Timeout 15)) { throw 'Recording did not finish.' }
    Receive-Job $recording -ErrorAction Stop | Set-Content (Join-Path $artifacts 'recording.txt')
    $after = Wait-VisibleTitles 'after'
    $report.firstBefore = $before.titles[0].name
    $report.firstAfter = $after.titles[0].name
    $renameError = $null
    if ($CheckRename) {
        try { Test-Rename $report.firstAfter }
        catch { $renameError = $_.Exception.Message; $report.rename = "FAIL: $renameError" }
    }
    $report.rowDelta = [int]$report.firstAfter.Substring(0,4) - [int]$report.firstBefore.Substring(0,4)
    if ([Math]::Abs($report.rowDelta) -lt 2*$before.titles.Count) {
        throw 'Wheel input did not move at least two screens of tasks.'
    }
    $bundle = Join-Path $artifacts 'scroll.frames'
    $manifest = Get-Content -Raw (Join-Path $bundle 'manifest.json') | ConvertFrom-Json
    if ($manifest.status -ne 'complete' -or $manifest.frames.truncated) { throw 'Incomplete frame bundle.' }
    $samples = @(Get-Content (Join-Path $bundle $manifest.frames.index) | ForEach-Object { $_ | ConvertFrom-Json })
    $base = [DateTimeOffset]$manifest.startedUtc
    $interval = @(($start-$base).TotalMilliseconds, ($end-$base).TotalMilliseconds)
    $report.scroll = 'INCONCLUSIVE'
    if ($samples.Count -ne $manifest.timing.sampleCount -or $samples.Count -lt 3 -or $samples[0].elapsedMs -ge $interval[0]) {
        throw 'The recording lacks a complete index and visible pre-input reference.'
    }
    $sy = $manifest.frames.height / $before.list.height
    $titleHeight = ($before.titles | Measure-Object height -Maximum).Maximum
    $fullTitles = @($before.titles | Where-Object height -ge ($titleHeight-1))
    if ($fullTitles.Count -lt 3) { throw 'Not enough complete UIA titles to calibrate row spacing.' }
    $pitches = @(for ($i=1; $i -lt $fullTitles.Count; $i++) { ($fullTitles[$i].y-$fullTitles[$i-1].y)*$sy })
    $pitch = ($pitches | Sort-Object)[[int][Math]::Floor($pitches.Count/2)]
    if ($pitch -le $titleHeight*$sy -or @($pitches | Where-Object { [Math]::Abs($_-$pitch) -gt 2 }).Count) {
        throw 'The fixture titles do not have a uniform measurable row pitch.'
    }
    $report.rowPitchPx = $pitch
    $cache = @{}
    $frames = @(foreach ($sample in $samples) {
        if (!$cache.ContainsKey($sample.file)) {
            $stats = TitleInk (Join-Path $bundle $sample.file) $before
            $stats.missing = MissingTitles $stats.centres $pitch $manifest.frames.height ($titleHeight*$sy/2+2)
            $cache[$sample.file] = $stats
        }
        $stats = $cache[$sample.file]
        [pscustomobject]@{ms=$sample.elapsedMs; file=$sample.file; ink=$stats.ink; bands=$stats.centres.Count;
            missing=$stats.missing; profile=$stats.profile}
    })
    $reference = @($frames | Where-Object ms -lt $interval[0])[-1]
    $referenceCentres = $cache[$reference.file].centres
    foreach ($title in $fullTitles) {
        $top = ($title.y-$before.list.y)*$sy
        if (@($referenceCentres | Where-Object { $_ -ge $top -and $_ -le $top+$title.height*$sy }).Count -ne 1) {
            throw 'The initial image did not match the UIA title rectangles.'
        }
    }
    if ($reference.missing) { throw 'The pre-input reference contains a title gap.' }
    $lastChange = $interval[0]
    for ($i=1; $i -lt $frames.Count; $i++) {
        if ($frames[$i].ms -lt $frames[$i-1].ms) { throw 'Recording timestamps went backwards.' }
        if ($frames[$i].ms -ge $interval[0] -and $frames[$i].profile -cne $frames[$i-1].profile) { $lastChange=$frames[$i].ms }
    }
    # Inertia continues beyond the final input. Require quiet pixels for 250 ms
    # after the last observed title change or last input, whichever came later.
    $settled = [Math]::Max($interval[1],$lastChange)+250
    $report.lastTitleChangeMs = $lastChange
    $inMotion = @($frames | Where-Object { $_.ms -ge $interval[0] -and $_.ms -le $settled })
    $report.motionSamples = $inMotion.Count
    $report.distinctTitlePatterns = @($inMotion.profile | Sort-Object -Unique).Count
    $report.referenceInkLines = $reference.ink
    $report.minimumInkLines = ($inMotion | Measure-Object ink -Minimum).Minimum
    $report.minimumVisibleTitleBands = ($inMotion | Measure-Object bands -Minimum).Minimum
    $report.blankSamples = @($inMotion | Where-Object bands -eq 0 | Select-Object ms,file,ink)
    $report.missingTitleSamples = @($inMotion | Where-Object missing -gt 0 | Select-Object ms,file,bands,missing)
    $report.maxGapMs = 0
    for ($i=1; $i -lt $frames.Count; $i++) {
        if ($frames[$i].ms -ge $interval[0] -and $frames[$i-1].ms -le $settled) {
            $report.maxGapMs = [Math]::Max($report.maxGapMs, $frames[$i].ms-$frames[$i-1].ms)
        }
    }
    if ($report.missingTitleSamples.Count) {
        $report.scroll = 'FAIL'
        throw 'One or more task titles disappeared in the recorded wheel scroll.'
    }
    if ($frames[-1].ms -lt $settled -or $report.distinctTitlePatterns -lt 3 -or $report.maxGapMs -gt 100) {
        throw 'Insufficient motion, sampling coverage, or settling time; inspect the frame bundle.'
    }
    $report.scroll = 'PASS'
    if ($renameError) { throw "Scroll passed, but the inline edit check failed: $renameError" }
    $report.status = 'PASS'
} catch {
    $report.error = "$_"
    if ($appPid) {
        try { Ui @('inspect','--depth','18','--json') | Set-Content -LiteralPath (Join-Path $artifacts 'error-tree.json') } catch {}
        try { Ui @('screenshot','-o',(Join-Path $artifacts 'error.png')) | Out-Null } catch {}
    }
    throw
}
finally {
    $report | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $artifacts 'results.json')
    if ($recording) { Stop-Job $recording; Remove-Job $recording }
    if ($appPid) {
        $app = Get-Process -Id $appPid -ErrorAction SilentlyContinue
        if ($app -and !$app.HasExited) {
            if (!$app.CloseMainWindow() -or !$app.WaitForExit(5000)) { $app.Kill(); $null = $app.WaitForExit(5000) }
        }
        & winapp ui yield --on local | Out-Null
    }
    $env:WINAPP_UI_WORKFLOW_ID = $workflow
    Write-Host "Rapid-scroll gate: $($report.status) - $artifacts"
}
