# Exercise the Settings popup and sample the composited header while navigation is in flight.
[CmdletBinding(DefaultParameterSetName='Debug')]
param(
    [Parameter(ParameterSetName='Debug')][string]$AppOutput = 'src/Wintodo.App/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64',
    [Parameter(Mandatory,ParameterSetName='Dev')][string]$DevPackageDirectory
)
$ErrorActionPreference = 'Stop'
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1' -or $PSScriptRoot -ine 'C:\wintodo\tests\ui' -or
    (Get-ItemPropertyValue 'HKLM:\SOFTWARE\Microsoft\Virtual Machine\Guest\Parameters' VirtualMachineName) -cne 'WintodoTest') {
    throw 'Run this gate in WintodoTest through scripts/Invoke-VmGate.ps1.'
}
$root = Split-Path (Split-Path $PSScriptRoot)
$artifacts = Join-Path $PSScriptRoot ('artifacts/category-colors/' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $artifacts
$database = Join-Path $artifacts 'colors.db'
$originalWorkflow = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = [guid]::NewGuid().ToString('N')
$appPid = 0
$results = [ordered]@{ status='FAIL'; themes=@(); palette=@() }
. (Join-Path $PSScriptRoot 'SuggestionsFixtureSql.ps1')
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class CategoryColorsNative {
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
'@

function Ui([string[]]$Arguments) {
    $output = & winapp ui @Arguments -a $appPid
    if ($LASTEXITCODE -ne 0) { throw "winapp ui $Arguments failed: $output" }
    $output
}
function Start-SignedCategoryFixture {
    if ([IO.Path]::IsPathRooted($DevPackageDirectory)) { throw 'Pass a repo-relative DEV package directory.' }
    $dir = (Resolve-Path -LiteralPath (Join-Path $root $DevPackageDirectory)).ProviderPath
    if (!$dir.StartsWith("$root\", [StringComparison]::OrdinalIgnoreCase)) { throw 'DEV package directory is outside the guest checkout.' }
    $build = Get-Content -Raw -LiteralPath (Join-Path $dir 'build.json') | ConvertFrom-Json
    $version = [version]$build.version
    $msix = Join-Path $dir "Wintodo-DEV-$version-x64.msix"
    $sig = Get-AuthenticodeSignature -LiteralPath $msix
    $hash = (Get-FileHash -LiteralPath $msix).Hash
    if ($build.identity -cne 'Wintodo.Hybrid.Dev' -or $build.publisher -cne 'CN=AppPublisher' -or
        $sig.Status -ne 'Valid' -or $sig.SignerCertificate.Thumbprint -ine $build.signerThumbprint -or $hash -ine $build.sha256) {
        throw 'Unverified DEV installer.'
    }
    $family = 'Wintodo.Hybrid.Dev_1z32rh13vfry6'
    $script:database = Join-Path $env:LOCALAPPDATA "Packages\$family\LocalState\wintodo.db"
    if ((Test-Path -LiteralPath $database) -or (Get-Process -Name Wintodo -ErrorAction SilentlyContinue)) {
        throw 'This gate requires the clean checkpoint, with no DEV database or running app.'
    }
    Add-AppxPackage -Path $msix
    $registration = Get-AppxPackage -Name $build.identity
    if ($registration.PackageFamilyName -cne $family -or $registration.Publisher -cne $build.publisher -or
        $registration.IsDevelopmentMode -or [string]$registration.Version -cne $build.version -or $registration.Status -ne 'Ok') {
        throw 'Installed DEV registration mismatch.'
    }
    $zip = [IO.Compression.ZipFile]::OpenRead($msix)
    try {
        foreach ($file in 'Wintodo.exe','resources.pri') {
            $stream = $zip.GetEntry($file).Open()
            try {
                if ((Get-FileHash -InputStream $stream).Hash -ne (Get-FileHash -LiteralPath (Join-Path $registration.InstallLocation $file)).Hash) {
                    throw "Installed $file differs from the signed package."
                }
            } finally { $stream.Dispose() }
        }
    } finally { $zip.Dispose() }
    $results.installer = @{version=[string]$registration.Version; sha256=$hash; source=$build.source}
    $exe = Join-Path $registration.InstallLocation 'Wintodo.exe'
    (New-Object -ComObject Shell.Application).ShellExecute("shell:AppsFolder\$family!App")
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do {
        $process = Get-Process -Name Wintodo -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -ieq $exe -and $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
        if ($process) { break }
        Start-Sleep -Milliseconds 250
    } while ($clock.Elapsed.TotalSeconds -lt 90)
    if (!$process) { throw 'Signed DEV window did not appear.' }
    $script:appPid = $process.Id
    Ui @('wait-for','Todo.Welcome.StartFresh','-t','30000') | Out-Null
    Ui @('invoke','Todo.Welcome.StartFresh') | Out-Null
}
function Select-Category([string]$Name) {
    Ui @('invoke',"Todo.Nav.$Name") | Out-Null
    Ui @('wait-for','Todo.Shell.PageTitle','--value',$Name,'-t','10000') | Out-Null
}
function Accent([string]$Theme,[string]$Hue) {
    $brush = $colors.SelectSingleNode("//*[@x:Key='$Theme']/*[@x:Key='ListAccent${Hue}Brush']", $ns)
    [Drawing.ColorTranslator]::FromHtml($brush.GetAttribute('Color'))
}
function Color-Distance($A,$B) {
    [Math]::Abs([int]$A.R-$B.R) + [Math]::Abs([int]$A.G-$B.G) + [Math]::Abs([int]$A.B-$B.B)
}
function Assert-SidebarPalette([string]$Theme) {
    foreach ($entry in $categories.GetEnumerator()) {
        $id = $entry.Key
        $expected = Accent $Theme $entry.Value
        $path = Join-Path $artifacts "$Theme-$id.png"
        Ui @('screenshot',"Todo.Nav.$id",'-o',$path) | Out-Null
        $bmp = [Drawing.Bitmap]::new($path)
        try {
            # The icon occupies the leading slot. Skip the selection indicator at the left.
            $matches = 0
            for ($x=[int](10*$scale); $x -lt [Math]::Min($bmp.Width, [int](46*$scale)); $x++) {
                for ($y=1; $y -lt $bmp.Height-1; $y++) {
                    if ((Color-Distance $bmp.GetPixel($x,$y) $expected) -le 48) { $matches++ }
                }
            }
            if ($matches -lt 4) { throw "$Theme $id sidebar glyph lacks its $($entry.Value) foreground ($matches pixels)." }
            $results.palette += @{theme=$Theme;category=$id;matchingPixels=$matches}
        } finally { $bmp.Dispose() }
    }
}
function Header-Crop {
    $bitmap = [Drawing.Bitmap]::new($area.Width, $area.Height)
    $g = [Drawing.Graphics]::FromImage($bitmap)
    try { $g.CopyFromScreen($area.Location,[Drawing.Point]::Empty,$area.Size) } finally { $g.Dispose() }
    $bitmap
}
function Assert-HeaderTransition {
    Select-Category 'Completed'
    $title = (Ui @('search','Todo.Shell.PageTitle','--json') | ConvertFrom-Json).matches |
        Where-Object { $_.automationId -eq 'Todo.Shell.PageTitle' -and $_.name -eq 'Completed' } | Select-Object -First 1
    if (-not $title -or $title.isOffscreen) { throw 'Completed title is missing.' }
    # The decorative icon has no UIA peer. Anchor its 44-DIP slot and the title to UIA's physical pixels.
    $area = [Drawing.Rectangle]::new([int]($title.x-52*$scale),[int]($title.y+$title.height/2-22*$scale),
        [int](52*$scale+$title.width),[int](44*$scale))
    $green = Accent 'Light' 'Green'; $purple = Accent 'Light' 'Purple'
    $destination = Header-Crop
    try {
        Select-Category 'All'
        $before = Header-Crop
        try {
            # Calibrate on both settled glyphs and on title ink beyond the width of "All".
            $iconPoints = @(); $titlePoints = @(); $oldInk=0
            for ($y=0; $y -lt $area.Height; $y++) {
                for ($x=0; $x -lt $area.Width; $x++) {
                    $a=$before.GetPixel($x,$y); $b=$destination.GetPixel($x,$y)
                    if ($x -lt 44*$scale) {
                        if ((Color-Distance $a $purple) -le 30) { $oldInk++ }
                        if ($iconPoints.Count -lt 8 -and (Color-Distance $b $green) -le 30) { $iconPoints += [Drawing.Point]::new($x,$y) }
                    } elseif ($x -gt 100*$scale -and $titlePoints.Count -lt 8 -and (Color-Distance $a $b) -gt 450) {
                        $titlePoints += [Drawing.Point]::new($x,$y)
                    }
                }
            }
            if ($oldInk -lt 8 -or $titlePoints.Count -ne 8 -or $iconPoints.Count -ne 8) { throw 'Could not distinguish the two settled headers.' }
            $before.Save((Join-Path $artifacts 'header-All.png'))
        } finally { $before.Dispose() }

        $cli = Start-Process -FilePath (Get-Command winapp).Source -ArgumentList @('ui','invoke','Todo.Nav.Completed','-a',"$appPid") -PassThru -WindowStyle Hidden
        $clock = [Diagnostics.Stopwatch]::StartNew()
        $frameCount = 0; $newFrames = 0; $last = 0; $transitionGap = 0
        try {
            do {
                $frame = Header-Crop
                try {
                    $now = $clock.ElapsedMilliseconds; $frameCount++
                    $hasTitle = @($titlePoints | Where-Object {
                        (Color-Distance $frame.GetPixel($_.X,$_.Y) $destination.GetPixel($_.X,$_.Y)) -le 30 }).Count -ge 6
                    if ($hasTitle) {
                        if (!$newFrames) { $transitionGap = $now-$last; $frame.Save((Join-Path $artifacts 'first-Completed.png')) }
                        $newFrames++
                        $greenPixels = @($iconPoints | Where-Object { (Color-Distance $frame.GetPixel($_.X,$_.Y) $green) -le 48 }).Count
                        if ($greenPixels -lt 6) { throw "Completed title painted with the wrong icon color at ${now}ms ($greenPixels / 8 green samples)." }
                    }
                    $last=$now
                } finally { $frame.Dispose() }
                Start-Sleep -Milliseconds 8
            } while ((!$cli.HasExited -or $newFrames -lt 5) -and $clock.ElapsedMilliseconds -lt 10000)
            if (!$cli.HasExited -or $cli.ExitCode -ne 0 -or $newFrames -lt 5 -or $transitionGap -gt 100) {
                throw "Incomplete All to Completed capture ($frameCount samples; $newFrames Completed; transition gap ${transitionGap}ms)."
            }
            $results.header = @{samples=$frameCount;completedFrames=$newFrames;transitionGapMs=$transitionGap}
        } finally { if (!$cli.HasExited) { $cli.Kill() }; $cli.Dispose() }
    } finally { $destination.Dispose() }
}

try {
    if ($DevPackageDirectory) { Start-SignedCategoryFixture }
    else {
        $trace = Join-Path $artifacts 'app.trace'
        $app = Join-Path $root $AppOutput
        $launch = winapp run $app --detach --json -- --database-path $database --trace-file $trace | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0 -or !$launch.ProcessId) { throw 'Category colors gate launch failed.' }
        $appPid = [int]$launch.ProcessId
    }
    Ui @('wait-for','Todo.Nav.Settings','-t','15000') | Out-Null
    $scale = [CategoryColorsNative]::GetDpiForWindow((Get-Process -Id $appPid).MainWindowHandle)/96.0
    [xml]$colors = Get-Content (Join-Path $root 'src/Wintodo.App/Themes/Colors.xaml') -Raw
    $ns = [Xml.XmlNamespaceManager]::new($colors.NameTable)
    $ns.AddNamespace('x', 'http://schemas.microsoft.com/winfx/2006/xaml')
    $categories = [ordered]@{MyDay='Yellow'; Important='Pink'; Planned='Blue'; All='Purple'; Completed='Green'; DefaultList='Blue'}
    # A real user item exercises the dynamic navigation collection on each theme change.
    $listName = 'Category color fixture'
    Ui @('invoke','Todo.Nav.NewListButton') | Out-Null
    Ui @('wait-for','Todo.Nav.NewListNameBox','-t','5000') | Out-Null
    Ui @('set-value','Todo.Nav.NewListNameBox',$listName) | Out-Null
    Ui @('send-keys','enter','--target','Todo.Nav.NewListNameBox','--via','send-input') | Out-Null
    Ui @('wait-for','Todo.List.TitleButton','--value',$listName,'-t','10000') | Out-Null
    $list = @((Ui @('search',$listName,'--json') | ConvertFrom-Json).matches | Where-Object {
        $_.automationId -like 'Todo.Nav.UserList.*' -and !$_.isOffscreen })
    if ($list.Count -ne 1) { throw 'Expected a single user list in the navigation pane.' }
    Ui @('invoke','Todo.List.MenuButton') | Out-Null
    Ui @('invoke','Todo.List.Menu.ChangeTheme') | Out-Null
    Ui @('invoke','Todo.List.Theme.Orange') | Out-Null
    $categories[$list[0].automationId.Substring('Todo.Nav.'.Length)] = 'Orange'
    Ui @('invoke','Todo.Nav.Settings') | Out-Null
    foreach ($theme in ('Dark','Light')*4) {
        Ui @('click','Todo.Settings.Theme') | Out-Null
        Ui @('click',"Todo.Settings.Theme.$theme") | Out-Null
        Ui @('wait-for','Todo.Settings.Theme','--value',$theme,'-t','5000') | Out-Null
        $results.themes += $theme
        if ($results.themes.Count -ge 7) { Assert-SidebarPalette $theme }
    }
    Ui @('invoke',$list[0].automationId) | Out-Null
    Ui @('wait-for','Todo.List.TitleButton','--value',$listName,'-t','5000') | Out-Null
    # A populated Completed view exposes a misplaced post-query color update even on fast machines.
    Invoke-SuggestionsFixtureSql -Database $database -InstalledDevFixture:([bool]$DevPackageDirectory) -Sql @'
WITH RECURSIVE seq(n) AS (VALUES(1) UNION ALL SELECT n+1 FROM seq WHERE n<1000)
INSERT INTO tasks(uid,list_id,summary,status,percent_complete,completed_utc,created_utc,modified_utc,sort_order)
SELECT printf('a0000000-0000-4000-8000-%012d',n),(SELECT id FROM lists WHERE is_default=1 AND is_deleted=0),
       printf('Completed fixture %04d',n),2,100,'2026-11-26T00:00:00.0000000Z','2026-11-26T00:00:00.0000000Z','2026-11-26T00:00:00.0000000Z',n*1024 FROM seq;
'@ | Out-Null
    $priorDpi = [CategoryColorsNative]::SetThreadDpiAwarenessContext([IntPtr](-4))
    try { Assert-HeaderTransition } finally { [void][CategoryColorsNative]::SetThreadDpiAwarenessContext($priorDpi) }
    Ui @('wait-for','Todo.Shell.PageTitle','--value','Completed','-t','5000') | Out-Null
    $results.status = 'PASS'
} catch {
    $results.error = "$_"
    throw
} finally {
    if ($appPid) { Stop-Process -Id $appPid -ErrorAction SilentlyContinue }
    winapp ui yield | Out-Null
    $env:WINAPP_UI_WORKFLOW_ID = $originalWorkflow
    $results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $artifacts 'results.json')
    Write-Host "Category colors: $($results.status) ($artifacts)"
}
