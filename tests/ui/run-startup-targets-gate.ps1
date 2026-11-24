# Supply the signed candidate as for run-startup-placement-gate.ps1. Reuse its returning-user fixture.
param([Parameter(Mandatory)][string]$InputDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1' -or $root -ine 'C:\wintodo' -or
    (Get-ItemPropertyValue 'HKLM:\SOFTWARE\Microsoft\Virtual Machine\Guest\Parameters' VirtualMachineName) -cne 'WintodoTest') {
    throw 'This test requires the WintodoTest guest.'
}
& powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'run-startup-placement-gate.ps1') -InputDirectory $InputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Signed startup placement gate failed; see its summary.' }
$build=Get-Content -LiteralPath (Join-Path (Join-Path $root $InputDirectory) 'build.json') -Raw | ConvertFrom-Json
$reg=Get-AppxPackage -Name Wintodo.Hybrid.Dev
if (-not $reg -or $reg.IsDevelopmentMode -or $reg.Status -ne 'Ok' -or $reg.PackageFamilyName -cne 'Wintodo.Hybrid.Dev_1z32rh13vfry6' -or
    $reg.Publisher -cne 'CN=AppPublisher' -or [string]$reg.Version -cne $build.version) { throw 'Run the signed placement gate first.' }
$exe=Join-Path $reg.InstallLocation Wintodo.exe
$aumid=$reg.PackageFamilyName+'!App'
$out=(New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot ('artifacts/startup-targets/'+[guid]::NewGuid().ToString('N'))) -Force).FullName
$report=[ordered]@{status='FAIL';version=$build.version;source=$build.source;checks=@()}
$app=$null
$env:WINAPP_UI_WORKFLOW_ID=[guid]::NewGuid().ToString('N')
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
[ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")] class ActivationManager { }
[ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IActivate {
    [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string aumid,
        [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options, out int pid);
}
public static class StartupTargetActivation {
    public static int Launch(string aumid, string args) {
        int pid; var manager=(IActivate)new ActivationManager();
        try { Marshal.ThrowExceptionForHR(manager.ActivateApplication(aumid,args,0,out pid)); return pid; }
        finally { Marshal.FinalReleaseComObject(manager); }
    }
}
'@
function Launch([string]$Arguments) {
    if (Get-Process -Name Wintodo -ErrorAction SilentlyContinue) { throw 'A fresh Wintodo process is required.' }
    $script:app=Get-Process -Id ([StartupTargetActivation]::Launch($aumid,$Arguments))
    if ($app.Path -ine $exe) { throw 'Activation returned the wrong executable.' }
}
function Close-App {
    $app.Refresh()
    if (-not $app.CloseMainWindow() -or -not $app.WaitForExit(15000)) { throw 'Normal close failed.' }
    $app.Dispose(); $script:app=$null
}
function Ui([string[]]$Arguments) {
    $result=@(& winapp ui @Arguments -a $app.Id)
    if ($LASTEXITCODE -ne 0) { throw "winapp ui $($Arguments -join ' ') failed: $result" }
    $result
}
try {
    Launch ''
    Ui @('wait-for','Startup observation fixture','--type','ListItem','-t','20000') | Out-Null
    $system=Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
    $wantLight=$null -ne $system.AppsUseLightTheme -and $system.AppsUseLightTheme -eq 0
    $theme=if ($wantLight) { 'Light' } else { 'Dark' }
    $report.requestedTheme=$theme
    Ui @('invoke','Todo.Nav.Settings') | Out-Null
    Ui @('wait-for','Todo.Settings.Theme','-t','10000') | Out-Null
    Ui @('focus','Todo.Settings.Theme') | Out-Null
    $keys=if ($wantLight) { 'home down enter' } else { 'home down down enter' }
    Ui @('send-keys',$keys,'--via','send-input') | Out-Null
    Ui @('wait-for','Todo.Settings.Theme','--value',$theme,'-t','5000') | Out-Null
    Ui @('invoke','Todo.Nav.Completed') | Out-Null
    Ui @('wait-for','Todo.Shell.PageTitle','--value','Completed','-t','5000') | Out-Null
    Close-App
    Launch ''
    Ui @('wait-for','Todo.Shell.PageTitle','--value','Completed','-t','20000') | Out-Null
    $report.checks+='Returning user normally opens in Completed'
    Close-App

    # Activate the installed AUMID with EXACTLY the argument stored by JumpListItem.CreateWithArguments.
    # Observe the route and UIA focus before interacting with any control in this process.
    Launch 'wintodo:new-task'
    Ui @('wait-for','Todo.Shell.PageTitle','--value','Tasks','-t','20000') | Out-Null
    $report.checks+='New Task activation opens Tasks instead of the saved Completed view'
    Ui @('wait-for','Todo.List.AddTaskBox','--property','HasKeyboardFocus','--value','True','-t','10000') | Out-Null
    $report.checks+='The composer reports keyboard focus before any UI input'
    $task='QuickAdd'+[guid]::NewGuid().ToString('N').Substring(0,8)
    Ui @('set-value','Todo.List.AddTaskBox',$task) | Out-Null
    Ui @('invoke','Todo.Composer.AddButton') | Out-Null
    Ui @('wait-for',$task,'--type','ListItem','-t','10000') | Out-Null
    $report.checks+='The composer commits the task through UI Automation value and invoke'
    Ui @('invoke','Todo.Nav.Settings') | Out-Null
    Ui @('wait-for','Todo.Settings.Theme','--value',$theme,'-t','10000') | Out-Null
    $report.checks+='Explicit appearance theme survives the returning-user launch'
    Close-App
    $report.status='PASS'
} catch { $report.error=$_.Exception.Message; throw } finally {
    $report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $out 'results.json') -Encoding UTF8
    Write-Host "Startup targets: $($report.status), $out"
    & winapp ui yield | Out-Null
}
