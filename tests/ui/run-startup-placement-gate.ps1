# Run with Invoke-VmGate.ps1 -NoWarmup -Include $candidate -GateArguments '-InputDirectory',$candidate
# $candidate is a repo-relative directory with Build-DevPreview.ps1's signed x64 MSIX and build.json.
param([Parameter(Mandatory)][string]$InputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1' -or $root -ine 'C:\wintodo' -or
    (Get-ItemPropertyValue 'HKLM:\SOFTWARE\Microsoft\Virtual Machine\Guest\Parameters' VirtualMachineName) -cne 'WintodoTest') {
    throw 'This gate requires the clean WintodoTest VM.'
}
# UI Automation's desktop assemblies are available in Windows PowerShell on the guest.
if ($PSVersionTable.PSEdition -ne 'Desktop') {
    & powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $PSCommandPath -InputDirectory $InputDirectory
    exit $LASTEXITCODE
}
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
Add-Type -Path (Join-Path $PSScriptRoot 'StartupWindowProbe.cs')
$output = (New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot ('artifacts/startup-placement/'+[guid]::NewGuid().ToString('N')))).FullName
$build = Get-Content -LiteralPath (Join-Path (Join-Path $root $InputDirectory) 'build.json') -Raw | ConvertFrom-Json
$msix = Join-Path (Join-Path $root $InputDirectory) "Wintodo-DEV-$($build.version)-x64.msix"
$report = [ordered]@{Status='FAIL'; Version=$build.version; Source=$build.source; Sha256=$build.sha256; OS=[Environment]::OSVersion.Version.ToString(); Launches=@()}
$app = $null
$shell = New-Object -ComObject Shell.Application
$env:WINAPP_UI_WORKFLOW_ID = [guid]::NewGuid().ToString('N')
function Ui([string[]]$Arguments) {
    $result = @(& winapp ui @Arguments -a $app.Id)
    if ($LASTEXITCODE -ne 0) { throw "winapp ui failed: $($Arguments -join ' '): $result" }
    $result
}
function Close-Fixture {
    $app.Refresh()
    if (-not $app.CloseMainWindow() -or -not $app.WaitForExit(15000)) { throw 'Normal window close did not exit.' }
    $app.Dispose()
    $script:app = $null
}
function Wait-WindowState([bool]$Maximized, [switch]$CheckNormalBounds) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $prior=$null
    do {
        $s = [StartupWindowProbe]::Read($app.MainWindowHandle)
        if ($s -and $prior -and $s.SameRect($prior) -and $prior.Maximized -eq $Maximized -and
            $s.Visible -and $s.Maximized -eq $Maximized -and (-not $CheckNormalBounds -or
            ($s.X -eq 120 -and $s.Y -eq 96 -and $s.Width -eq 1432 -and $s.Height -eq 912))) { return $s }
        $prior=$s
        Start-Sleep -Milliseconds 100
    } while ($clock.Elapsed.TotalSeconds -lt 5)
    throw "Expected window maximized=$Maximized (compare normal bounds: $CheckNormalBounds); observed $($s | ConvertTo-Json -Compress)."
}

function Measure-Launch([string]$Name, $Expected) {
    if (Get-Process -Name Wintodo -ErrorAction SilentlyContinue) { throw 'Fresh process required before launch.' }
    $uia = [System.Windows.Automation.AutomationElement]
    $condition = [System.Windows.Automation.PropertyCondition]::new($uia::AutomationIdProperty,$rowId)
    $probe = [StartupWindowProbe]::new($exe)
    $rowMs=$null; $queries=0; $element=$null; $returned=$null; $failure=$null
    $probe.Begin()
    try {
        $shell.ShellExecute($launchUri)
        $returned=$probe.Ms
        while ($probe.Ms -lt 30000 -and $null -eq $rowMs) {
            if ($probe.Error) { throw $probe.Error }
            if ($probe.Hwnd) {
                try {
                    if (-not $element) { $element=$uia::FromHandle([IntPtr]$probe.Hwnd) }
                    $queries++
                    $row=$element.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$condition)
                    if ($row) {
                        $info=$row.Current
                        if ($info.ProcessId -eq $probe.ProcessId -and $info.Name -ceq $label -and
                            $info.ControlType -eq [System.Windows.Automation.ControlType]::ListItem -and -not $info.IsOffscreen -and
                            $info.BoundingRectangle.Width -gt 0 -and $info.BoundingRectangle.Height -gt 0) { $rowMs=$probe.Ms; break }
                    }
                } catch [System.Windows.Automation.ElementNotAvailableException] { $element=$null }
            }
            [Threading.Thread]::Sleep(10)
        }
        if ($null -eq $rowMs) { throw 'The exact fixture task did not become an onscreen ListItem within 30 s.' }
        Start-Sleep -Seconds 2 # Include late resizes; same observation interval as the baseline.
    } catch { $failure=$_.Exception.Message } finally { $probe.End() }
    $changes=@($probe.Changes); $visible=@($changes | Where-Object Visible); $max=@($visible | Where-Object Maximized)
    if (-not $failure) { $failure=$probe.Error }
    if (-not $failure -and (-not $changes.Count -or $changes[0].Visible -or -not $visible.Count)) {
        $failure='Observer missed the hidden-to-visible boundary.'
    }
    foreach ($s in $visible) {
        if (-not $failure -and ($s.Maximized -ne $Expected.Maximized -or -not $s.SameRect($Expected))) {
            $failure="Unexpected visible bounds/state at $([Math]::Round($s.Ms)) ms; see WindowChanges."
        }
    }
    $record = [ordered]@{SampleName=$Name; Status=$(if($failure){'FAIL'}else{'PASS'}); Error=$failure; PackageVersion=$build.version
        ProcessId=$probe.ProcessId; Hwnd=$probe.Hwnd; FirstVisibleMs=$(if($visible.Count){$visible[0].Ms})
        FirstMaximizedMs=$(if($max.Count){$max[0].Ms}); TaskOnscreenMs=$rowMs; TaskLabel=$label; RowAutomationId=$rowId
        ProcessSeenMs=$probe.ProcessSeenMs; ShellExecuteReturnedMs=$returned; ObservationEndedMs=$probe.Ms; AfterRowSeconds=2
        ExpectedWindow=$Expected; WindowChanges=$changes; WindowPollSleepMs=5; WindowPolls=$probe.Polls; MaxWindowPollGapMs=$probe.MaxWindowPollGapMs
        UiaQueries=$queries; UiaPollSleepMs=10}
    $record | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output "$Name.json") -Encoding UTF8
    $report.Launches+= "$Name.json"
    if ($failure) { throw "$Name failed: $failure" }
    $script:app = Get-Process -Id $probe.ProcessId
}

try {
    $signature = Get-AuthenticodeSignature -LiteralPath $msix
    if ($build.identity -cne 'Wintodo.Hybrid.Dev' -or $build.publisher -cne 'CN=AppPublisher' -or $build.architecture -ne 'x64' -or
        $signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ine $build.signerThumbprint -or
        (Get-FileHash -LiteralPath $msix).Hash -ine $build.sha256) { throw 'Unverified candidate installer.' }
    if ((Get-AppxPackage -Name $build.identity) -or (Get-Process -Name Wintodo -ErrorAction SilentlyContinue)) { throw 'Expected a clean guest.' }
    Add-AppxPackage -Path $msix
    $reg = Get-AppxPackage -Name $build.identity
    if ($reg.PackageFamilyName -cne 'Wintodo.Hybrid.Dev_1z32rh13vfry6' -or $reg.IsDevelopmentMode -or $reg.Status -ne 'Ok' -or
        $reg.Publisher -cne $build.publisher -or [string]$reg.Version -cne $build.version) { throw 'Installed registration mismatch.' }
    $exe = Join-Path $reg.InstallLocation Wintodo.exe
    $launchUri = 'shell:AppsFolder\'+$reg.PackageFamilyName+'!App'
    $shell.ShellExecute($launchUri)
    $clock = [Diagnostics.Stopwatch]::StartNew()
    do {
        $app = Get-Process -Name Wintodo -ErrorAction SilentlyContinue | Where-Object { $_.Path -ieq $exe -and $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
        if ($app) { break }
        Start-Sleep -Milliseconds 100
    } while ($clock.Elapsed.TotalSeconds -lt 90)
    if (-not $app) { throw 'No main window from the installed application.' }
    Ui @('wait-for','Todo.Welcome.StartFresh','-t','30000') | Out-Null
    Ui @('invoke','Todo.Welcome.StartFresh') | Out-Null
    Ui @('wait-for','Todo.List.AddTaskBox','-t','15000') | Out-Null
    $title = 'Startup observation fixture'
    Ui @('focus','Todo.List.AddTaskBox') | Out-Null
    Ui @('set-value','Todo.List.AddTaskBox',$title) | Out-Null
    Ui @('invoke','Todo.Composer.AddButton') | Out-Null
    Ui @('wait-for',$title,'--type','ListItem','-t','15000') | Out-Null
    $rows = @((Ui @('search','Todo.Row.','--type','ListItem','--json') | ConvertFrom-Json).matches |
        Where-Object automationId -Match '^Todo\.Row\.[0-9a-f-]{36}$')
    if ($rows.Count -ne 1 -or $rows[0].name -notlike "$title*") { throw 'Expected exactly the synthetic fixture task.' }
    $label=$rows[0].name; $rowId=$rows[0].automationId
    [StartupWindowProbe]::SetNormalBounds($app.MainWindowHandle)
    $normal = Wait-WindowState $false -CheckNormalBounds
    Close-Fixture

    Measure-Launch 'normal-1' $normal
    [StartupWindowProbe]::RequestMaximized($app.MainWindowHandle,$true)
    $maximized = Wait-WindowState $true
    Close-Fixture
    foreach ($sample in 1..3) {
        Measure-Launch "max-$sample" $maximized
        if ($sample -lt 3) { Close-Fixture }
    }
    [StartupWindowProbe]::RequestMaximized($app.MainWindowHandle,$false)
    $report.RestoreDown = Wait-WindowState $false -CheckNormalBounds
    Close-Fixture
    $report.Status='PASS'
} catch { $report.Error=$_.Exception.Message; throw } finally {
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'summary.json') -Encoding UTF8
    Write-Host "Startup placement: $($report.Status), $output"
    & winapp ui yield | Out-Null
}
