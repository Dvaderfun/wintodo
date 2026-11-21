# Run with Invoke-VmGate.ps1 -NoWarmup -Include artifacts/hybrid-input.
# The input folder holds the signed installer and its build.json from Build-DevPreview.ps1.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$guest = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Virtual Machine\Guest\Parameters'
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1' -or $root -ine 'C:\wintodo' -or $guest.VirtualMachineName -cne 'WintodoTest') {
    throw 'This test is guest-only. Use the WintodoTest VM.'
}
$output = Join-Path $PSScriptRoot ('artifacts/dev-preview/'+[guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $output
$inputDir = Join-Path $root 'artifacts/hybrid-input'
$build = Get-Content -LiteralPath (Join-Path $inputDir 'build.json') -Raw | ConvertFrom-Json
$pkg = Join-Path $inputDir "Wintodo-DEV-$($build.version)-x64.msix"
$report = [ordered]@{status='FAIL'; source=$build.source; sha256=$build.sha256; checks=@(); screenshots=@()}
$appPid = 0
$previous = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = [guid]::NewGuid().ToString('N')
function Ui([string[]]$Arguments) {
    $result = @(& winapp ui @Arguments -a $appPid)
    if ($LASTEXITCODE -ne 0) { throw "UI failed ($LASTEXITCODE): $($Arguments -join ' ') $result" }
    $result
}
function Rows {
    @((Ui @('search','Todo.Row.','--type','ListItem','--json') | ConvertFrom-Json).matches |
        Where-Object automationId -Match '^Todo\.Row\.[0-9a-f-]+$' | Sort-Object y)
}
function Capture([string]$name) {
    $path = Join-Path $output ($name+'.png')
    & winapp ui screenshot -w $hwnd -o $path | Out-Null
    if ($LASTEXITCODE -ne 0 -or (Get-Item -LiteralPath $path).Length -lt 1000) { throw "Screenshot failed: $name" }
    $report.screenshots += $name
}
function Assert-Title([string]$expected) {
    Ui @('wait-for','Todo.List.TitleButton','--value',$expected,'-t','10000') | Out-Null
}
function Assert-Selection([string[]]$expected) {
    # The list's native UI Automation SelectionPattern exposes the IDs actually selected.
    $probe = Join-Path $output 'selection.ps1'
    if (-not (Test-Path -LiteralPath $probe)) {
        Set-Content -LiteralPath $probe -Value @'
param([int]$App)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$scope = [System.Windows.Automation.TreeScope]
$element = [System.Windows.Automation.AutomationElement]
$root = $element::RootElement.FindFirst($scope::Children, [System.Windows.Automation.PropertyCondition]::new($element::ProcessIdProperty,$App))
$list = $root.FindFirst($scope::Descendants, [System.Windows.Automation.PropertyCondition]::new($element::AutomationIdProperty,'Todo.List.Tasks'))
$selection = $list.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern)
foreach ($row in $selection.Current.GetSelection()) { $row.Current.AutomationId }
'@
    }
    $ids = @(powershell.exe -NoProfile -NonInteractive -File $probe -App $appPid)
    if ($LASTEXITCODE -ne 0 -or (Compare-Object -ReferenceObject @($expected | Sort-Object) -DifferenceObject @($ids | Sort-Object))) {
        throw 'Native ListView selection did not match the expected rows.'
    }
}
try {
    $signature = Get-AuthenticodeSignature -LiteralPath $pkg
    if ($build.identity -cne 'Wintodo.Hybrid.Dev' -or $build.publisher -cne 'CN=AppPublisher' -or
        $signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ine $build.signerThumbprint -or
        (Get-FileHash -LiteralPath $pkg).Hash -ine $build.sha256) { throw 'Unverified DEV installer.' }
    if (Get-AppxPackage -Name $build.identity) { throw 'The clean guest already has this preview.' }
    Add-AppxPackage -Path $pkg
    $registration = Get-AppxPackage -Name $build.identity
    if ($registration.PackageFamilyName -cne 'Wintodo.Hybrid.Dev_1z32rh13vfry6' -or $registration.IsDevelopmentMode -or
        [string]$registration.Version -cne $build.version -or $registration.Status -ne 'Ok' -or
        $registration.Publisher -cne $build.publisher) { throw 'Signed DEV package registration mismatch.' }
    $aumid = $registration.PackageFamilyName+'!App'
    $report.checks += 'Signed installer, hash, package family, version and registration'
    # Activate the actual registered app as Start does; winapp run <folder> registers
    # a loose debug layout and cannot validate the installed MSIX or its LocalState.
    $shell = New-Object -ComObject Shell.Application
    $shell.ShellExecute('shell:AppsFolder\'+$aumid)
    $exe = Join-Path $registration.InstallLocation 'Wintodo.exe'
    $deadline = (Get-Date).AddSeconds(120)
    do {
        $app = Get-Process -Name Wintodo -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -ieq $exe -and $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
        if ($app) { break }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    if (-not $app) { throw 'No window for the installed DEV executable.' }
    $appPid = $app.Id
    $hwnd = $app.MainWindowHandle.ToInt64()
    Ui @('wait-for','Todo.Welcome.StartFresh','-t','30000') | Out-Null
    Ui @('invoke','Todo.Welcome.StartFresh') | Out-Null
    Ui @('wait-for','Todo.List.AddTaskBox','-t','15000') | Out-Null
    $report.checks += 'Start activation, first-run onboarding and task shell'
    Ui @('invoke','Todo.Nav.NewListButton') | Out-Null
    Ui @('wait-for','Todo.Nav.NewListNameBox','-t','10000') | Out-Null
    Ui @('set-value','Todo.Nav.NewListNameBox','Design review') | Out-Null
    Ui @('send-keys','enter','--target','Todo.Nav.NewListNameBox','--via','send-input') | Out-Null
    Assert-Title 'Design review'
    $titles = 'Review the release notes', 'Send the design draft', 'Prepare the project update', 'Pick a time for the walkthrough', 'Read the accessibility feedback'
    foreach ($title in $titles) {
        Ui @('focus','Todo.List.AddTaskBox') | Out-Null
        Ui @('set-value','Todo.List.AddTaskBox',$title) | Out-Null
        Ui @('invoke','Todo.Composer.AddButton') | Out-Null
        Ui @('wait-for',$title,'--type','ListItem','-t','10000') | Out-Null
    }
    $rows = @(Rows)
    if ($rows.Count -ne $titles.Count) { throw 'Incomplete task list.' }
    $report.rowSpacingPx = $rows[1].y - $rows[0].y
    Ui @('focus','Todo.List.MenuButton') | Out-Null
    Capture '01-cards'
    $selected = $rows[1].automationId
    Ui @('click',$selected) | Out-Null
    Ui @('wait-for','Todo.Detail.Note','-t','10000') | Out-Null
    Ui @('focus','Todo.List.MenuButton') | Out-Null
    Ui @('hover','Todo.List.MenuButton') | Out-Null
    Start-Sleep -Milliseconds 400
    Capture '02-selected'
    $report.checks += 'Five task cards, row click opens detail'
    Ui @('invoke','Todo.List.TitleButton') | Out-Null
    Ui @('wait-for','Todo.List.RenameBox','-t','10000') | Out-Null
    $editor = (Ui @('search','Todo.List.RenameBox','--json') | ConvertFrom-Json).matches[0]
    $report.editorSizePx = @($editor.width,$editor.height)
    Capture '03-title-editor'
    Assert-Selection @($selected)
    Ui @('set-value','Todo.List.RenameBox','Discarded title') | Out-Null
    Ui @('send-keys','esc','--target','Todo.List.RenameBox','--via','send-input') | Out-Null
    Assert-Title 'Design review'
    Ui @('invoke','Todo.List.TitleButton') | Out-Null
    Ui @('wait-for','Todo.List.RenameBox','-t','10000') | Out-Null
    Ui @('set-value','Todo.List.RenameBox','Confirmed title') | Out-Null
    Ui @('send-keys','enter','--target','Todo.List.RenameBox','--via','send-input') | Out-Null
    Assert-Title 'Confirmed title'
    Ui @('invoke','Todo.List.TitleButton') | Out-Null
    Ui @('wait-for','Todo.List.RenameBox','-t','10000') | Out-Null
    Ui @('set-value','Todo.List.RenameBox','Focus saved') | Out-Null
    Ui @('focus','Todo.List.MenuButton') | Out-Null
    Assert-Title 'Focus saved'
    $report.checks += 'List title: Escape discards, Enter and focus loss save'
    try {
        Ui @('click',$selected) | Out-Null
        Ui @('focus','Todo.List.MenuButton') | Out-Null
        Ui @('hover','Todo.List.MenuButton') | Out-Null
        Assert-Selection @($selected)
        & (Join-Path $PSScriptRoot 'DevPreviewContrast.ps1') -AppPid $appPid -ArtifactDirectory $output -SelectedRowId $selected
        $report.checks += 'High Contrast: selected marker on the retained card; guest appearance restored'
        $report.screenshots += '05-hc-selected'
    } catch {
        $report.contrastError = "$_"
    }
    Ui @('focus',$selected) | Out-Null
    Ui @('send-keys','ctrl+vk=0x41','--via','send-input') | Out-Null
    Start-Sleep -Milliseconds 300
    Assert-Selection @($rows.automationId)
    Capture '04-multiselect'
    Ui @('send-keys','esc','--via','send-input') | Out-Null
    Start-Sleep -Milliseconds 300
    Assert-Selection @()
    $report.checks += 'Native selection: one row, Ctrl+A selects five, Escape clears'
    if ($report.contrastError) { throw "High Contrast: $($report.contrastError)" }
    $report.status = 'PASS'
} catch {
    $report.error = "$_"
    throw
} finally {
    if ($appPid) {
        & winapp ui yield | Out-Null
        Stop-Process -Id $appPid -ErrorAction SilentlyContinue
    }
    $env:WINAPP_UI_WORKFLOW_ID = $previous
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'results.json')
    $report | ConvertTo-Json -Depth 5 | Write-Host
}
