<#
.SYNOPSIS
Prepares the WintodoTest guest for UI gates and takes the `clean` checkpoint.

.DESCRIPTION
Run from the host after `scripts/Install-TestVmWindows.ps1` (or a manual install
with a local administrator account plus `scripts/Set-TestVmCredential.ps1`).
Everything happens over PowerShell Direct; nothing runs on the host desktop.

Guest changes:
- Developer Mode (loose-layout registration used by `winapp run`).
- Automatic sign-in. The password is an LSA secret, not a registry value.
- No lock screen, screen saver, display/sleep timeout or sign-in on wake.
- No automatic Windows Update restarts; no post-sign-in welcome screens.
- 1920 x 1080 display (Hyper-V video setting, applied while the VM is off).
- PowerShell 7 (latest stable MSI), the Windows App Runtime matching
  Microsoft.WindowsAppSDK and the WinApp CLI matching
  Microsoft.Windows.SDK.BuildTools.WinApp, both read from Directory.Packages.props.
- The M10 test certificate (public part only) in LocalMachine\TrustedPeople.
- The .NET SDK pinned in global.json (official installer, SHA-512 checked), and
  a NuGet cache warmed by restoring tests\Wintodo.Data.Tests, which gates use to
  seed databases with `dotnet test`.

Then it waits for the signed-in desktop and takes a standard checkpoint named
`clean` (memory included, so a restore resumes signed in). With -Force it first
restores the existing `clean`, so nothing a gate left behind gets captured.

.EXAMPLE
.\scripts\Initialize-TestVm.ps1
.\scripts\Initialize-TestVm.ps1 -Force    # replace an existing `clean` checkpoint
#>
param(
    [string]$VmName = 'WintodoTest',
    [string]$CertificatePath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'publish\msix\m10-test.cer'),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'TestVm.Common.ps1')

$repositoryRoot = Split-Path -Parent $PSScriptRoot
[xml]$packages = Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Packages.props')
function Get-PackageVersion([string]$Id) {
    $version = ($packages.Project.ItemGroup.PackageVersion | Where-Object Include -eq $Id).Version
    if (-not $version) { throw "$Id is not in Directory.Packages.props." }
    $version
}
$appSdkVersion = [version](Get-PackageVersion 'Microsoft.WindowsAppSDK')
$winAppVersion = Get-PackageVersion 'Microsoft.Windows.SDK.BuildTools.WinApp'
$runtimeUrl = "https://aka.ms/windowsappsdk/$($appSdkVersion.ToString(2))/$appSdkVersion/windowsappruntimeinstall-x64.exe"
$winAppUrl = "https://github.com/microsoft/winappCli/releases/download/v$winAppVersion/winappcli_x64.msix"
$sdkVersion = (Get-Content -LiteralPath (Join-Path $repositoryRoot 'global.json') -Raw | ConvertFrom-Json).sdk.version
$sdkVersionParts = [version]$sdkVersion
$sdkMetadataUrl = "https://builds.dotnet.microsoft.com/dotnet/release-metadata/$($sdkVersionParts.ToString(2))/releases.json"
$setupDirectory = 'C:\WintodoSetup'

if (-not (Test-Path -LiteralPath $CertificatePath)) {
    throw "Certificate not found: $CertificatePath (see docs/guides/packaging.md, Local test signature)."
}
$existingCheckpoint = Get-VMCheckpoint -VMName $VmName -Name clean -ErrorAction SilentlyContinue
if ($existingCheckpoint -and -not $Force) {
    throw "Checkpoint 'clean' already exists on $VmName. Pass -Force to replace it."
}
$credential = Get-TestVmCredential
if ($existingCheckpoint) {
    Write-Host "Restoring the existing 'clean' as the starting point"
    if ((Get-VM -Name $VmName).State -ne 'Off') {
        Stop-VM -Name $VmName -TurnOff -Force
    }
    Restore-VMCheckpoint -VMName $VmName -Name clean -Confirm:$false
}
if ((Get-VM -Name $VmName).State -ne 'Running') {
    Start-VM -Name $VmName
}

Write-Host '[1/5] Guest settings, PowerShell 7, .NET SDK, downloads'
$session = Wait-TestVmSession -VmName $VmName -Credential $credential
Invoke-Command -Session $session -ScriptBlock { New-Item -ItemType Directory -Force -Path $using:setupDirectory | Out-Null }
Copy-Item -ToSession $session -LiteralPath $CertificatePath -Destination "$setupDirectory\test.cer"
Invoke-Command -Session $session -ArgumentList @(
    $credential.GetNetworkCredential().Password, $setupDirectory, $runtimeUrl, $winAppUrl, $sdkVersion, $sdkMetadataUrl
) -ScriptBlock {
    param($Password, $SetupDirectory, $RuntimeUrl, $WinAppUrl, $SdkVersion, $SdkMetadataUrl)
    $ErrorActionPreference = 'Stop'
    $ProgressPreference = 'SilentlyContinue'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    if (-not ([Security.Principal.WindowsPrincipal]$identity).IsInRole('Administrators')) {
        throw "$env:USERNAME is not an elevated administrator in the guest."
    }

    function Set-Value($Path, $Name, $Value, $Type = 'DWord') {
        if (-not (Test-Path $Path)) { New-Item -Path $Path -Force | Out-Null }
        New-ItemProperty -Path $Path -Name $Name -Value $Value -PropertyType $Type -Force | Out-Null
    }

    # Developer Mode: `winapp run` registers the loose Debug layout.
    Set-Value 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' 'AllowDevelopmentWithoutDevLicense' 1

    # Automatic sign-in; Winlogon reads the password from the LSA secret DefaultPassword.
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class WintodoLsaSecret {
    [StructLayout(LayoutKind.Sequential)]
    struct LsaUnicodeString { public ushort Length; public ushort MaximumLength; public IntPtr Buffer; }
    [StructLayout(LayoutKind.Sequential)]
    struct LsaObjectAttributes { public int Length; public IntPtr RootDirectory; public IntPtr ObjectName; public uint Attributes; public IntPtr SecurityDescriptor; public IntPtr SecurityQualityOfService; }
    [DllImport("advapi32.dll")] static extern uint LsaOpenPolicy(IntPtr systemName, ref LsaObjectAttributes attributes, uint access, out IntPtr policy);
    [DllImport("advapi32.dll")] static extern uint LsaStorePrivateData(IntPtr policy, ref LsaUnicodeString key, ref LsaUnicodeString data);
    [DllImport("advapi32.dll")] static extern uint LsaClose(IntPtr policy);
    [DllImport("advapi32.dll")] static extern int LsaNtStatusToWinError(uint status);
    static LsaUnicodeString Create(string value) {
        var result = new LsaUnicodeString();
        result.Buffer = Marshal.StringToHGlobalUni(value);
        result.Length = (ushort)(value.Length * 2);
        result.MaximumLength = (ushort)(value.Length * 2 + 2);
        return result;
    }
    public static void Store(string key, string value) {
        var attributes = new LsaObjectAttributes();
        attributes.Length = Marshal.SizeOf(attributes);
        IntPtr policy;
        uint status = LsaOpenPolicy(IntPtr.Zero, ref attributes, 0x000F0FFF, out policy);
        if (status != 0) throw new Win32Exception(LsaNtStatusToWinError(status));
        var keyString = Create(key);
        var dataString = Create(value);
        try {
            status = LsaStorePrivateData(policy, ref keyString, ref dataString);
            if (status != 0) throw new Win32Exception(LsaNtStatusToWinError(status));
        } finally {
            LsaClose(policy);
            Marshal.FreeHGlobal(keyString.Buffer);
            Marshal.ZeroFreeGlobalAllocUnicode(dataString.Buffer);
        }
    }
}
'@
    [WintodoLsaSecret]::Store('DefaultPassword', $Password)
    $winlogon = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'
    Set-Value $winlogon 'AutoAdminLogon' '1' 'String'
    Set-Value $winlogon 'DefaultUserName' $env:USERNAME 'String'
    Set-Value $winlogon 'DefaultDomainName' $env:COMPUTERNAME 'String'
    Remove-ItemProperty -Path $winlogon -Name DefaultPassword, AutoLogonCount -ErrorAction SilentlyContinue
    # "Only allow Windows Hello sign-in" hides password autologon.
    Set-Value 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\PasswordLess\Device' 'DevicePasswordLessBuildVersion' 0

    # Keep the console desktop unlocked and awake.
    Set-Value 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization' 'NoLockScreen' 1
    Set-Value 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' 'InactivityTimeoutSecs' 0
    Set-Value 'HKCU:\Control Panel\Desktop' 'ScreenSaveActive' '0' 'String'
    Set-Value 'HKCU:\Control Panel\Desktop' 'ScreenSaverIsSecure' '0' 'String'
    foreach ($setting in 'monitor-timeout-ac', 'standby-timeout-ac', 'hibernate-timeout-ac',
        'monitor-timeout-dc', 'standby-timeout-dc', 'hibernate-timeout-dc') {
        powercfg /change $setting 0
    }
    powercfg /setacvalueindex SCHEME_CURRENT SUB_NONE CONSOLELOCK 0
    powercfg /setdcvalueindex SCHEME_CURRENT SUB_NONE CONSOLELOCK 0
    powercfg /setactive SCHEME_CURRENT

    # No update restarts or welcome screens in the middle of a gate.
    Set-Value 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU' 'NoAutoUpdate' 1
    Set-Value 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU' 'NoAutoRebootWithLoggedOnUsers' 1
    Set-Value 'HKCU:\Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement' 'ScoobeSystemSettingEnabled' 0
    Set-Value 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager' 'SubscribedContent-310093Enabled' 0

    # Sideloading trust for MSIX packages signed with the M10 test certificate.
    Import-Certificate -FilePath (Join-Path $SetupDirectory 'test.cer') `
        -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null

    function Get-SignedDownload($Url, $Path) {
        Invoke-WebRequest -Uri $Url -OutFile $Path -UseBasicParsing
        $signature = Get-AuthenticodeSignature -FilePath $Path
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation') {
            throw "Signature check failed for $Url ($($signature.Status), $($signature.SignerCertificate.Subject))."
        }
    }

    $pwshPath = Join-Path $env:ProgramFiles 'PowerShell\7\pwsh.exe'
    if (-not (Test-Path $pwshPath)) {
        $release = Invoke-RestMethod -Uri 'https://api.github.com/repos/PowerShell/PowerShell/releases/latest' -UseBasicParsing
        $asset = $release.assets | Where-Object name -like 'PowerShell-*-win-x64.msi' | Select-Object -First 1
        $msi = Join-Path $SetupDirectory $asset.name
        Get-SignedDownload $asset.browser_download_url $msi
        $install = Start-Process msiexec.exe -Wait -PassThru -ArgumentList @(
            '/i', "`"$msi`"", '/qn', '/norestart', 'ADD_PATH=1',
            'ENABLE_PSREMOTING=0', 'REGISTER_MANIFEST=1', 'USE_MU=0', 'ENABLE_MU=0')
        if ($install.ExitCode -notin 0, 3010) {
            throw "PowerShell 7 install failed with exit code $($install.ExitCode)."
        }
    }

    $dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
    $installedSdks = if (Test-Path $dotnet) { & $dotnet --list-sdks } else { @() }
    if (-not ($installedSdks | Where-Object { $_ -like "$SdkVersion *" })) {
        $release = (Invoke-RestMethod -Uri $SdkMetadataUrl -UseBasicParsing).releases |
            Where-Object { $_.sdks.version -contains $SdkVersion } | Select-Object -First 1
        $file = ($release.sdks | Where-Object version -eq $SdkVersion).files |
            Where-Object name -eq 'dotnet-sdk-win-x64.exe'
        if (-not $file) { throw ".NET SDK $SdkVersion is not in $SdkMetadataUrl." }
        $installer = Join-Path $SetupDirectory 'dotnet-sdk-win-x64.exe'
        Get-SignedDownload $file.url $installer
        if ((Get-FileHash -LiteralPath $installer -Algorithm SHA512).Hash -ne $file.hash) {
            throw ".NET SDK installer hash does not match the release metadata."
        }
        $install = Start-Process $installer -Wait -PassThru -ArgumentList '/install', '/quiet', '/norestart'
        if ($install.ExitCode -notin 0, 3010) {
            throw ".NET SDK install failed with exit code $($install.ExitCode)."
        }
    }
    foreach ($name in 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_NOLOGO') {
        [Environment]::SetEnvironmentVariable($name, '1', 'Machine')
    }

    Get-SignedDownload $RuntimeUrl (Join-Path $SetupDirectory 'windowsappruntimeinstall-x64.exe')
    Invoke-WebRequest -Uri $WinAppUrl -OutFile (Join-Path $SetupDirectory 'winappcli_x64.msix') -UseBasicParsing
}
Remove-PSSession $session

Write-Host '[2/5] Display 1920 x 1080 and restart into automatic sign-in'
Stop-VM -Name $VmName
Set-VMVideo -VMName $VmName -ResolutionType Single -HorizontalResolution 1920 -VerticalResolution 1080
# Detach install media; the guest holds it while running. Keep one empty drive.
Get-VMDvdDrive -VMName $VmName | Select-Object -Skip 1 | Remove-VMDvdDrive
Get-VMDvdDrive -VMName $VmName | Set-VMDvdDrive -Path $null
Set-VMFirmware -VMName $VmName -FirstBootDevice (Get-VMHardDiskDrive -VMName $VmName | Select-Object -First 1)
Start-VM -Name $VmName
$session = Wait-TestVmSession -VmName $VmName -Credential $credential
Wait-TestVmDesktop -Session $session

Write-Host '[3/5] Windows App Runtime and WinApp CLI in the signed-in session'
# Package installs register per user, so they run in the desktop session.
$exitCode = Invoke-TestVmInteractive -Session $session -RunLevel Highest -TimeoutMinutes 10 `
    -Execute "$setupDirectory\windowsappruntimeinstall-x64.exe" -Argument '--quiet'
if ($exitCode -ne 0) { throw "Windows App Runtime installer failed with exit code $exitCode." }
$exitCode = Invoke-TestVmInteractive -Session $session -TimeoutMinutes 10 `
    -Execute 'powershell.exe' `
    -Argument "-NoProfile -ExecutionPolicy Bypass -Command Add-AppxPackage -Path $setupDirectory\winappcli_x64.msix"
if ($exitCode -ne 0) { throw "WinApp CLI install failed with exit code $exitCode." }

# Gates restore and build tests\Wintodo.Data.Tests on every run; a warm cache
# keeps that offline-capable and quick. Sources are removed again below.
Write-Host '      NuGet cache for tests\Wintodo.Data.Tests'
$payloadPath = Join-Path $repositoryRoot 'artifacts\vm-staging\sources.zip'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $payloadPath) | Out-Null
New-TestVmPayload -RepositoryRoot $repositoryRoot -Path $payloadPath
Copy-TestVmPayload -Session $session -Path $payloadPath -Destination "$setupDirectory\src"
Remove-Item -LiteralPath $payloadPath
$restore = Invoke-Command -Session $session -ArgumentList "$setupDirectory\src" -ScriptBlock {
    param($Source)
    $dotnet = "$env:ProgramFiles\dotnet\dotnet.exe"
    Push-Location -LiteralPath $Source
    try {
        $output = & $dotnet restore tests\Wintodo.Data.Tests -p:Platform=x64 2>&1
        [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output | Out-String) }
    } finally {
        Pop-Location
        & $dotnet build-server shutdown | Out-Null   # release handles on the sources
    }
}
if ($restore.ExitCode -ne 0) { throw "NuGet restore in the guest failed:`n$($restore.Output)" }

Write-Host '[4/5] Verify'
$exitCode = Invoke-TestVmInteractive -Session $session -TimeoutMinutes 5 `
    -Execute 'C:\Program Files\PowerShell\7\pwsh.exe' `
    -Argument "-NoProfile -Command `"winapp --version | Set-Content $setupDirectory\winapp-version.txt`""
$report = Invoke-Command -Session $session -ArgumentList $setupDirectory -ScriptBlock {
    param($SetupDirectory)
    $runtime = Get-AppxPackage -Name Microsoft.WindowsAppRuntime.2 |
        Where-Object Architecture -eq 'X64' | Sort-Object Version -Descending | Select-Object -First 1
    [pscustomobject]@{
        PowerShell = (& (Join-Path $env:ProgramFiles 'PowerShell\7\pwsh.exe') -NoProfile -Command '$PSVersionTable.PSVersion.ToString()')
        DotNetSdks = (& (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe') --list-sdks) -join '; '
        WindowsAppRuntime = [string]$runtime.Version
        WinApp = (Get-Content (Join-Path $SetupDirectory 'winapp-version.txt') -ErrorAction SilentlyContinue | Select-Object -Last 1)
        DeveloperMode = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock').AllowDevelopmentWithoutDevLicense
        AutoAdminLogon = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon').AutoAdminLogon
        TrustedCertificates = @(Get-ChildItem Cert:\LocalMachine\TrustedPeople).Count
    }
}
$report | Format-List PowerShell, DotNetSdks, WindowsAppRuntime, WinApp, DeveloperMode, AutoAdminLogon, TrustedCertificates
if ([string]$report.DotNetSdks -notmatch [regex]::Escape($sdkVersion)) {
    throw ".NET SDK $sdkVersion is not installed (got '$($report.DotNetSdks)')."
}
if ($exitCode -ne 0 -or [string]$report.WinApp -notmatch [regex]::Escape($winAppVersion)) {
    throw "WinApp CLI $winAppVersion is not callable in the desktop session (got '$($report.WinApp)')."
}
if (-not $report.WindowsAppRuntime -or [version]$report.WindowsAppRuntime -lt [version]"$appSdkVersion.0") {
    throw "Windows App Runtime $appSdkVersion is not installed (got '$($report.WindowsAppRuntime)')."
}
if (-not $report.PowerShell -or [version]$report.PowerShell -lt [version]'7.4') {
    throw "PowerShell 7 is not installed (got '$($report.PowerShell)')."
}

Write-Host '[5/5] Checkpoint clean'
Invoke-Command -Session $session -ArgumentList $setupDirectory -ScriptBlock {
    param($SetupDirectory)
    Set-Location -LiteralPath $env:SystemDrive\
    Remove-Item -LiteralPath $SetupDirectory -Recurse -Force
}
# Let first-sign-in work settle so every restore starts from a quiet desktop.
Start-Sleep -Seconds 60
Wait-TestVmDesktop -Session $session
Remove-PSSession $session
$screenPath = Join-Path $repositoryRoot 'tests\ui\artifacts\vm\clean-desktop.png'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $screenPath) | Out-Null
Save-TestVmScreen -VmName $VmName -Path $screenPath
Write-Host "Desktop captured for review: $screenPath"
if ($existingCheckpoint) {
    Remove-VMCheckpoint -VMName $VmName -Name clean
}
Checkpoint-VM -Name $VmName -SnapshotName clean
Get-VMCheckpoint -VMName $VmName -Name clean | Format-Table VMName, Name, CheckpointType, CreationTime
