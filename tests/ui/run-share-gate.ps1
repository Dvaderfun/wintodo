param([switch]$Aot, [string]$ArtifactDirectory, [switch]$OnlyCold)
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1') { throw 'Share automation requires WintodoTest.' }
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $ArtifactDirectory) { $ArtifactDirectory = Join-Path $PSScriptRoot ('artifacts\share\run-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')) }
$ArtifactDirectory = [IO.Path]::GetFullPath($ArtifactDirectory)
New-Item -ItemType Directory -Force $ArtifactDirectory | Out-Null
$appDirectory = if ($Aot) { Join-Path $root 'publish\aot\win-x64' } else { Join-Path $root 'src\Wintodo.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64' }
$source = Join-Path $PSScriptRoot 'artifacts\share-source\share-source.exe'
if (-not (Test-Path $source)) { throw 'Build and include the real share-source fixture first.' }
$results = [Collections.Generic.List[object]]::new()
$expected = [Collections.Generic.List[object]]::new()
$mainPid = $null
$sourceProcess = $null
$previousWorkflow = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = [guid]::NewGuid().ToString('N')
function Ui([string[]]$Arguments) {
    $output = winapp ui @Arguments
    if ($LASTEXITCODE -ne 0) { throw "UI command failed: $($Arguments -join ' ')" }
    $output
}
function Check([string]$Name, [scriptblock]$Action) {
    try { & $Action | Out-Null; $results.Add(@{name=$Name;status='PASS'}); Write-Host "PASS: $Name" }
    catch { $results.Add(@{name=$Name;status='FAIL';detail="$_"}); Write-Host "FAIL: $Name - $_" }
}
function VerifyDatabase {
    $expectedPath = Join-Path $ArtifactDirectory 'expected.json'
    ConvertTo-Json -InputObject @($expected.ToArray()) -Depth 5 | Set-Content $expectedPath
    $env:WINTODO_SHARE_DB_PATH = $db
    $env:WINTODO_SHARE_EXPECTED = $expectedPath
    try {
        dotnet test --project (Join-Path $root 'tests\Wintodo.Data.Tests') -p:Platform=x64 --no-build --filter-class '*ShareGateDatabaseTests'
        if ($LASTEXITCODE -ne 0) { throw 'Exact persisted share content failed.' }
    } finally {
        Remove-Item Env:\WINTODO_SHARE_DB_PATH,Env:\WINTODO_SHARE_EXPECTED -ErrorAction SilentlyContinue
    }
}
function Share([string]$Mode, [string]$Text, [string]$Link, [string]$Title, [string]$Name, [switch]$Fails, [switch]$Unsupported) {
    $fixture = Join-Path $ArtifactDirectory $Name
    New-Item -ItemType Directory -Force $fixture | Out-Null
    [IO.File]::WriteAllText((Join-Path $fixture 'text.txt'), $Text)
    [IO.File]::WriteAllText((Join-Path $fixture 'link.txt'), $Link)
    [IO.File]::WriteAllText((Join-Path $fixture 'title.txt'), $Title)
    $script:sourceProcess = Start-Process -FilePath $source -ArgumentList $Mode,('"' + $fixture + '"') -WindowStyle Hidden -PassThru
    try {
        Ui @('wait-for','Share fixture','-a',"$($sourceProcess.Id)",'-t','10000') | Out-Null
        if ($sourceProcess.HasExited) { throw "Share source exited before showing UI (exit $($sourceProcess.ExitCode))." }
        Ui @('invoke','Share fixture','-a',"$($sourceProcess.Id)") | Out-Null
        $timer = [Diagnostics.Stopwatch]::StartNew()
        do {
            $windows = @(Ui @('list-windows','--json') | ConvertFrom-Json)
            $ready = $windows | Where-Object { $_.width -gt 100 -and $_.height -gt 100 -and ($_.className -eq 'ApplicationFrameWindow' -or $_.title -eq 'Share') }
            if ($ready) { break }
            Start-Sleep -Milliseconds 300
        } while ($timer.Elapsed.TotalSeconds -lt 15)
        $windows | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $fixture 'windows.json')
        $shareCandidates = @($windows | Where-Object {
            $_.width -gt 100 -and $_.height -gt 100 -and
            (($_.className -eq 'ApplicationFrameWindow' -and $_.processName -eq 'explorer') -or
             $_.title -eq 'Share' -or $_.processName -match '^Share')
        })
        $shareWindow = $null
        foreach ($candidate in $shareCandidates) {
            $probe = Ui @('inspect','--json','-w',"$($candidate.hwnd)")
            if (($probe -join "`n") -match 'ShareTargets|SharePicker') { $shareWindow = $candidate; break }
        }
        if (-not $shareWindow) { throw 'Windows Share broker window not found.' }
        $hwnd = [string]$shareWindow.hwnd
        if (-not $Unsupported) {
            winapp ui wait-for 'Wintodo' -w $hwnd -t 15000 | Out-Null
            if ($LASTEXITCODE -ne 0) {
                # The first OS picker can stall on its contact/app discovery spinner.
                # Reopen only before any target activation; this cannot repeat a save.
                Ui @('send-keys','esc','--via','send-input','-w',$hwnd) | Out-Null
                Ui @('invoke','Share fixture','-a',"$($sourceProcess.Id)") | Out-Null
                Start-Sleep -Seconds 3
                $windows = @(Ui @('list-windows','--json') | ConvertFrom-Json)
                foreach ($candidate in @($windows | Where-Object { $_.width -gt 100 -and $_.className -eq 'ApplicationFrameWindow' })) {
                    $probe = Ui @('inspect','--json','-w',"$($candidate.hwnd)")
                    if (($probe -join "`n") -match 'ShareTargets|SharePicker') { $hwnd = [string]$candidate.hwnd; break }
                }
                Ui @('wait-for','Wintodo','-w',$hwnd,'-t','30000') | Out-Null
            }
        }
        $tree = Ui @('inspect','--json','-w',$hwnd)
        $tree | Set-Content (Join-Path $fixture 'broker-tree.json')
        if ($Unsupported) {
            $search = winapp ui search 'Wintodo' --json -w $hwnd
            $searchCode = $LASTEXITCODE
            $parsed = $search | ConvertFrom-Json
            if ($searchCode -ne 0 -and $parsed.matches.Count -ne 0) { throw 'Unsupported target search failed unexpectedly.' }
            $targets = $parsed.matches
        } else {
            $targets = (Ui @('search','Wintodo','--json','-w',$hwnd) | ConvertFrom-Json).matches
        }
        Ui @('screenshot','--capture-screen','-o',(Join-Path $fixture 'broker.png'),'-w',$hwnd) | Out-Null
        if ($Unsupported) {
            if ($targets) { throw 'Unsupported data unexpectedly offered Wintodo.' }
            Ui @('send-keys','esc','--via','send-input','-w',$hwnd) | Out-Null
            return
        }
        $target = $targets | Where-Object { $_.type -match 'Button|ListItem' } | Select-Object -First 1
        if (-not $target) { throw 'Wintodo was not discovered by the real Windows Share UI.' }
        $selector = if ($target.automationId) { $target.automationId } else { $target.selector }
        Ui @('invoke',$selector,'-w',$hwnd) | Out-Null
        if ($Fails) {
            Start-Sleep -Seconds 3
            $sharePid = (Get-Process Wintodo | Where-Object Id -ne $mainPid | Select-Object -First 1).Id
            if (-not $sharePid) { throw 'Share error process not found.' }
            Ui @('wait-for','Todo.Share.Error','-a',"$sharePid",'-t','10000') | Out-Null
            Ui @('screenshot','-a',"$sharePid",'-o',(Join-Path $fixture 'error.png')) | Out-Null
            Ui @('click','Todo.Share.Close','-a',"$sharePid") | Out-Null
        } else {
            $timer = [Diagnostics.Stopwatch]::StartNew()
            while (-not (Test-Path (Join-Path $fixture 'completed.txt'))) {
                if ($timer.Elapsed.TotalSeconds -gt 25) {
                    Ui @('list-windows','--json') | Set-Content (Join-Path $fixture 'timeout-windows.json')
                    Get-CimInstance Win32_Process -Filter "name = 'Wintodo.exe'" | Select-Object ProcessId,CommandLine | ConvertTo-Json | Set-Content (Join-Path $fixture 'timeout-processes.json')
                    Get-WinEvent -FilterHashtable @{LogName='Application';StartTime=(Get-Date).AddMinutes(-5)} -ErrorAction SilentlyContinue | Where-Object ProviderName -match 'Error|Runtime' | Select-Object ProviderName,Message | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $fixture 'timeout-events.json')
                    foreach ($process in @(Get-Process Wintodo -ErrorAction SilentlyContinue | Where-Object Id -ne $mainPid)) {
                        Ui @('inspect','--json','-a',"$($process.Id)") | Set-Content (Join-Path $fixture 'timeout-target-tree.json')
                    }
                    throw 'Source did not receive broker ShareCompleted.'
                }
                Start-Sleep -Milliseconds 100
            }
        }
        Start-Sleep -Seconds 2
        if (Get-Process Wintodo -ErrorAction SilentlyContinue | Where-Object Id -ne $mainPid) { throw 'Share-only process remained resident.' }
    } finally {
        if ($sourceProcess -and -not $sourceProcess.HasExited) { Stop-Process -Id $sourceProcess.Id -ErrorAction SilentlyContinue }
        $script:sourceProcess = $null
    }
}
try {
    $registration = winapp run $appDirectory --no-launch --json | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw 'Share target registration failed.' }
    $package = Get-AppxPackage | Where-Object { $_.InstallLocation -and (Test-Path (Join-Path $_.InstallLocation 'Wintodo.exe')) } | Select-Object -First 1
    if (-not $package) { throw 'Guest Wintodo package not found.' }
    $db = Join-Path $env:LOCALAPPDATA "Packages\$($package.PackageFamilyName)\LocalState\wintodo.db"
    [xml]$manifest = Get-Content (Join-Path $package.InstallLocation 'AppxManifest.xml')
    Check 'Generated manifest advertises Text and WebLink' {
        $formats = @($manifest.SelectNodes("//*[local-name()='ShareTarget']/*[local-name()='DataFormat']") | ForEach-Object InnerText)
        if ($formats.Count -ne 2 -or 'Text' -notin $formats -or 'WebLink' -notin $formats) { throw 'Share formats incorrect.' }
    }
    Check 'Cold multiline Unicode Text share: exact task and process exit' {
        Share 'text' "Shared café ☕`r`nNote українська`r`n`r`nKeep lines" '' 'Source metadata' '01-text'
        $expected.Add(@{title='Shared café ☕';note="Note українська`n`nKeep lines";count=1})
        VerifyDatabase
    }
    if ($OnlyCold) {
        if (@($results | Where-Object status -eq 'FAIL').Count) { exit 1 }
        exit 0
    }
    Check 'WebLink source title and URL are preserved' {
        Share 'link' '' 'https://example.test/page?q=1#frag' 'Shared page' '02-link'
        $expected.Add(@{title='Shared page';note='https://example.test/page?q=1#frag';count=1})
        VerifyDatabase
    }
    Check 'Text URL is retained in the note' {
        Share 'text' 'https://example.test/text' '' 'Source' '03-text-url'
        $expected.Add(@{title='https://example.test/text';note='https://example.test/text';count=1})
        VerifyDatabase
    }
    Check 'Open main window observes repeated shares and retains its editor' {
        $launch = winapp run $appDirectory --detach --json | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0 -or -not $launch.ProcessId) { throw 'Main app launch failed.' }
        $script:mainPid = [int]$launch.ProcessId
        Ui @('wait-for','Todo.List.MenuButton','-a',"$mainPid",'-t','15000') | Out-Null
        Ui @('invoke','Todo.Nav.DefaultList','-a',"$mainPid") | Out-Null
        Ui @('wait-for','Shared café ☕','-a',"$mainPid",'-t','5000') | Out-Null
        $row = (Ui @('search','Shared café ☕','--json','-a',"$mainPid") | ConvertFrom-Json).matches | Where-Object type -eq 'ListItem' | Select-Object -First 1
        Ui @('invoke',$row.automationId,'-a',"$mainPid") | Out-Null
        Ui @('set-value','Todo.Detail.Title','Edited while sharing','-a',"$mainPid") | Out-Null
        $expected[0].title = 'Edited while sharing'
        foreach ($n in 1..2) { Share 'combined' "Accompanying text`nhttps://example.test/both" 'https://example.test/both' 'Shared twice' "04-repeat-$n" }
        $expected.Add(@{title='Shared twice';note="Accompanying text`nhttps://example.test/both";count=2})
        # Bring the actual main HWND forward before clicking; its editor may be
        # behind the source's window after the share-only process exits.
        Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ShareGateWindow {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int command);
}
'@
        $mainWindow = @(Ui @('list-windows','--json','-a',"$mainPid") | ConvertFrom-Json) | Where-Object title -eq 'Wintodo' | Select-Object -First 1
        [ShareGateWindow]::ShowWindow([IntPtr]([long]$mainWindow.hwnd), 9) | Out-Null
        [ShareGateWindow]::SetForegroundWindow([IntPtr]([long]$mainWindow.hwnd)) | Out-Null
        Ui @('click','Todo.Detail.Title','-a',"$mainPid") | Out-Null
        Ui @('list-windows','--json') | Set-Content (Join-Path $ArtifactDirectory 'after-return-windows.json')
        Ui @('screenshot','-a',"$mainPid",'-o',(Join-Path $ArtifactDirectory 'main-after-return.png')) | Out-Null
        Ui @('wait-for','Todo.Detail.Title','--value','Edited while sharing','-a',"$mainPid",'-t','5000') | Out-Null
        $sharedRows = @((Ui @('search','Shared twice','--json','-a',"$mainPid") | ConvertFrom-Json).matches | Where-Object type -eq 'ListItem')
        if ($sharedRows.Count -ne 2) { throw "Expected two shared task rows, found $($sharedRows.Count)." }
        Ui @('screenshot','-a',"$mainPid",'-o',(Join-Path $ArtifactDirectory 'main-after-share.png')) | Out-Null
        VerifyDatabase
        Stop-Process -Id $mainPid
        $script:mainPid = $null
    }
    if ($mainPid) { Stop-Process -Id $mainPid -ErrorAction SilentlyContinue; $script:mainPid = $null }
    Check 'Empty Text reports localized error and saves nothing' { Share 'text' ' ' '' 'Empty' '05-empty' -Fails; VerifyDatabase }
    Check 'Failed Text provider reports localized error and saves nothing' { Share 'bad-provider' '' '' 'Bad provider' '06-provider' -Fails; VerifyDatabase }
    Check 'Unavailable database reports an error without a committed task' {
        $lock = [IO.File]::Open($db, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        try { Share 'text' 'Must not be saved' '' 'Save failure' '06-database' -Fails }
        finally { $lock.Dispose() }
        VerifyDatabase
    }
    Check 'Unsupported formats do not offer Wintodo' { Share 'unsupported' '' '' 'Unsupported' '07-unsupported' -Unsupported; VerifyDatabase }
    Check 'Restart retains exact share content' {
        if ($expected.Count -ne 4) { throw 'Earlier successful share fixtures are missing; restart proof cannot pass.' }
        $launch = winapp run $appDirectory --detach --json | ConvertFrom-Json
        if ($LASTEXITCODE -ne 0 -or -not $launch.ProcessId) { throw 'Restart launch failed.' }
        $script:mainPid = [int]$launch.ProcessId
        Ui @('wait-for','Todo.List.MenuButton','-a',"$mainPid",'-t','15000') | Out-Null
        VerifyDatabase
    }
} finally {
    if ($mainPid) { Stop-Process -Id $mainPid -ErrorAction SilentlyContinue }
    if ($sourceProcess) { Stop-Process -Id $sourceProcess.Id -ErrorAction SilentlyContinue }
    Get-Process Wintodo -ErrorAction SilentlyContinue | Stop-Process -ErrorAction SilentlyContinue
    winapp ui yield | Out-Null
    $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflow
    ConvertTo-Json -InputObject @($results.ToArray()) -Depth 6 | Set-Content (Join-Path $ArtifactDirectory 'results.json')
}
if (@($results | Where-Object status -eq 'FAIL').Count) { exit 1 }
Write-Host "Real share gate PASS: $ArtifactDirectory (AOT=$Aot)"
