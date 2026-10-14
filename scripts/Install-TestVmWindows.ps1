<#
.SYNOPSIS
Installs Windows 11 Pro into the empty WintodoTest VM, unattended.

.DESCRIPTION
Run after scripts/New-TestVm.ps1. No input reaches the host desktop:
- An autounattend.xml on a second virtual DVD partitions the disk, installs the
  edition named by -ImageName, creates the local administrator -UserName and
  skips OOBE.
- "Press any key to boot from CD or DVD" is answered by typing Enter into the
  VM's own virtual keyboard (Hyper-V Msvm_Keyboard), not the host.

The guest password is generated here and saved with DPAPI to
%LOCALAPPDATA%\Wintodo\WintodoTest.credential.xml (see TestVm.Common.ps1),
unless that file already exists, in which case its credential is reused. The
answer file is deleted from the host once Windows is up; Windows Setup
masks passwords in its own copy in the guest.

Guest locale and time zone default to the host's so date-sensitive gates match
host behaviour; the UI language and keyboard stay en-US.

Next step: scripts/Initialize-TestVm.ps1.
#>
param(
    [string]$VmName = 'WintodoTest',
    [string]$UserName = 'tester',
    [string]$ImageName = 'Windows 11 Pro',
    [string]$UserLocale = (Get-Culture).Name,
    [string]$TimeZone = (Get-TimeZone).Id,
    [int]$TimeoutMinutes = 60
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'TestVm.Common.ps1')

$vm = Get-VM -Name $VmName
if ($vm.State -ne 'Off') {
    throw "$VmName must be off."
}
$disk = Get-VMHardDiskDrive -VM $vm | Select-Object -First 1 | Get-VHD
if ($disk.FileSize -gt 1GB) {
    throw "$($disk.Path) is not empty ($([math]::Round($disk.FileSize / 1GB, 1)) GB); refusing to reinstall."
}
$windowsDvd = Get-VMDvdDrive -VM $vm | Where-Object Path | Select-Object -First 1
if (-not $windowsDvd) {
    throw "$VmName has no Windows ISO attached. Run New-TestVm.ps1 -IsoPath <iso>."
}

if (Test-Path -LiteralPath $TestVmCredentialPath) {
    $credential = Get-TestVmCredential
    $UserName = $credential.UserName
} else {
    $alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'
    $characters = 1..20 | ForEach-Object { $alphabet[[Security.Cryptography.RandomNumberGenerator]::GetInt32($alphabet.Length)] }
    # Guarantee upper, lower and digit for the local password policy.
    $password = 'Aa2' + (-join $characters)
    $credential = [pscredential]::new($UserName, (ConvertTo-SecureString $password -AsPlainText -Force))
    Remove-Variable password
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $TestVmCredentialPath) | Out-Null
    $credential | Export-Clixml -LiteralPath $TestVmCredentialPath
    Write-Host "Generated the guest password; saved to $TestVmCredentialPath (DPAPI, current user only)."
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$stagingDirectory = Join-Path $repositoryRoot 'artifacts\vm-staging\unattend'
$unattendIso = Join-Path $repositoryRoot 'artifacts\vm-staging\autounattend.iso'
$unattendDvd = $null

function ConvertTo-XmlText([string]$Value) { [Security.SecurityElement]::Escape($Value) }

$escapedPassword = ConvertTo-XmlText $credential.GetNetworkCredential().Password
$escapedUser = ConvertTo-XmlText $UserName
$component = 'processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS"'
# VK7JG-… is Microsoft's published generic Pro installation key; it selects the
# edition and does not activate Windows.
$answerFile = @"
<?xml version="1.0" encoding="utf-8"?>
<unattend xmlns="urn:schemas-microsoft-com:unattend" xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State">
  <settings pass="windowsPE">
    <component name="Microsoft-Windows-International-Core-WinPE" $component>
      <SetupUILanguage><UILanguage>en-US</UILanguage></SetupUILanguage>
      <InputLocale>en-US</InputLocale>
      <SystemLocale>en-US</SystemLocale>
      <UILanguage>en-US</UILanguage>
      <UserLocale>en-US</UserLocale>
    </component>
    <component name="Microsoft-Windows-Setup" $component>
      <DiskConfiguration>
        <Disk wcm:action="add">
          <DiskID>0</DiskID>
          <WillWipeDisk>true</WillWipeDisk>
          <CreatePartitions>
            <CreatePartition wcm:action="add"><Order>1</Order><Type>EFI</Type><Size>300</Size></CreatePartition>
            <CreatePartition wcm:action="add"><Order>2</Order><Type>MSR</Type><Size>16</Size></CreatePartition>
            <CreatePartition wcm:action="add"><Order>3</Order><Type>Primary</Type><Extend>true</Extend></CreatePartition>
          </CreatePartitions>
          <ModifyPartitions>
            <ModifyPartition wcm:action="add"><Order>1</Order><PartitionID>1</PartitionID><Format>FAT32</Format><Label>System</Label></ModifyPartition>
            <ModifyPartition wcm:action="add"><Order>2</Order><PartitionID>2</PartitionID></ModifyPartition>
            <ModifyPartition wcm:action="add"><Order>3</Order><PartitionID>3</PartitionID><Format>NTFS</Format><Label>Windows</Label><Letter>C</Letter></ModifyPartition>
          </ModifyPartitions>
        </Disk>
      </DiskConfiguration>
      <ImageInstall>
        <OSImage>
          <InstallFrom><MetaData wcm:action="add"><Key>/IMAGE/NAME</Key><Value>$(ConvertTo-XmlText $ImageName)</Value></MetaData></InstallFrom>
          <InstallTo><DiskID>0</DiskID><PartitionID>3</PartitionID></InstallTo>
        </OSImage>
      </ImageInstall>
      <UserData>
        <AcceptEula>true</AcceptEula>
        <ProductKey><Key>VK7JG-NPHTM-C97JM-9MPGT-3V66T</Key><WillShowUI>Never</WillShowUI></ProductKey>
      </UserData>
    </component>
  </settings>
  <settings pass="specialize">
    <component name="Microsoft-Windows-Shell-Setup" $component>
      <ComputerName>$(ConvertTo-XmlText $VmName.ToUpperInvariant())</ComputerName>
      <TimeZone>$(ConvertTo-XmlText $TimeZone)</TimeZone>
    </component>
  </settings>
  <settings pass="oobeSystem">
    <component name="Microsoft-Windows-International-Core" $component>
      <InputLocale>en-US</InputLocale>
      <SystemLocale>en-US</SystemLocale>
      <UILanguage>en-US</UILanguage>
      <UserLocale>$(ConvertTo-XmlText $UserLocale)</UserLocale>
    </component>
    <component name="Microsoft-Windows-Shell-Setup" $component>
      <OOBE>
        <HideEULAPage>true</HideEULAPage>
        <HideOEMRegistrationScreen>true</HideOEMRegistrationScreen>
        <HideOnlineAccountScreens>true</HideOnlineAccountScreens>
        <HideWirelessSetupInOOBE>true</HideWirelessSetupInOOBE>
        <HideLocalAccountScreen>true</HideLocalAccountScreen>
        <ProtectYourPC>3</ProtectYourPC>
      </OOBE>
      <UserAccounts>
        <LocalAccounts>
          <LocalAccount wcm:action="add">
            <Name>$escapedUser</Name>
            <Group>Administrators</Group>
            <Password><Value>$escapedPassword</Value><PlainText>true</PlainText></Password>
          </LocalAccount>
        </LocalAccounts>
      </UserAccounts>
      <AutoLogon>
        <Enabled>true</Enabled>
        <Username>$escapedUser</Username>
        <Password><Value>$escapedPassword</Value><PlainText>true</PlainText></Password>
        <LogonCount>9999999</LogonCount>
      </AutoLogon>
      <TimeZone>$(ConvertTo-XmlText $TimeZone)</TimeZone>
    </component>
  </settings>
</unattend>
"@
Remove-Variable escapedPassword

Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
public static class WintodoIsoWriter {
    public static void Write(object imageStream, string path) {
        var stream = (IStream)imageStream;
        var buffer = new byte[1 << 16];
        IntPtr read = Marshal.AllocHGlobal(sizeof(int));
        try {
            using (var file = File.Create(path)) {
                while (true) {
                    stream.Read(buffer, buffer.Length, read);
                    int count = Marshal.ReadInt32(read);
                    if (count <= 0) break;
                    file.Write(buffer, 0, count);
                }
            }
        } finally {
            Marshal.FreeHGlobal(read);
        }
    }
}
'@

try {
    New-Item -ItemType Directory -Force -Path $stagingDirectory | Out-Null
    Set-Content -LiteralPath (Join-Path $stagingDirectory 'autounattend.xml') -Value $answerFile -Encoding utf8
    Remove-Variable answerFile
    $image = New-Object -ComObject IMAPI2FS.MsftFileSystemImage
    $image.FileSystemsToCreate = 3   # ISO 9660 + Joliet
    $image.VolumeName = 'UNATTEND'
    $image.Root.AddTree($stagingDirectory, $false)
    [WintodoIsoWriter]::Write($image.CreateResultImage().ImageStream, $unattendIso)
    Remove-Item -LiteralPath $stagingDirectory -Recurse -Force

    $unattendDvd = Add-VMDvdDrive -VM $vm -Path $unattendIso -Passthru
    Set-VMFirmware -VM $vm -FirstBootDevice $windowsDvd

    Write-Host "Starting $VmName and answering the boot prompt"
    Start-VM -VM $vm
    # The prompt shows a few seconds after power-on; stop before Setup's UI takes keys.
    $until = [DateTime]::UtcNow.AddSeconds(15)
    $keystrokes = 0
    while ([DateTime]::UtcNow -lt $until) {
        $keyboard = Get-CimInstance -Namespace root\virtualization\v2 -ClassName Msvm_ComputerSystem `
            -Filter "ElementName='$VmName'" | Get-CimAssociatedInstance -ResultClassName Msvm_Keyboard
        if ($keyboard) {
            $result = Invoke-CimMethod -InputObject $keyboard -MethodName TypeKey -Arguments @{ keyCode = [uint32]0x0D }
            if ($result.ReturnValue -eq 0) { $keystrokes++ }
        }
        Start-Sleep -Milliseconds 500
    }
    if ($keystrokes -eq 0) {
        throw "Could not type into the $VmName keyboard; the DVD boot prompt was not answered."
    }

    Write-Host "Installing Windows; waiting up to $TimeoutMinutes min for the signed-in desktop"
    # The account exists before Setup's last restart, so a session can open and
    # then drop; reconnect until the desktop is up or the deadline passes.
    $deadline = [DateTime]::UtcNow.AddMinutes($TimeoutMinutes)
    while ($true) {
        $remaining = [int][Math]::Max(30, ($deadline - [DateTime]::UtcNow).TotalSeconds)
        $session = Wait-TestVmSession -VmName $VmName -Credential $credential -TimeoutSeconds $remaining
        try {
            Wait-TestVmDesktop -Session $session -TimeoutSeconds 300
            break
        } catch {
            Remove-PSSession $session -ErrorAction SilentlyContinue
            if ([DateTime]::UtcNow -ge $deadline) {
                throw
            }
            Start-Sleep -Seconds 15
        }
    }
    Invoke-Command -Session $session -ScriptBlock {
        $os = Get-CimInstance Win32_OperatingSystem
        "$($os.Caption) $($os.Version) on $env:COMPUTERNAME, signed in as $env:USERNAME"
    }
    Remove-PSSession $session
} finally {
    if (Test-Path -LiteralPath $stagingDirectory) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
    if ($unattendDvd) {
        $drive = @{
            VMName = $VmName
            ControllerNumber = $unattendDvd.ControllerNumber
            ControllerLocation = $unattendDvd.ControllerLocation
        }
        # Eject first so the ISO is released even if the drive can't be hot-removed.
        Set-VMDvdDrive @drive -Path $null -ErrorAction SilentlyContinue
        Remove-VMDvdDrive @drive -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $unattendIso) {
        Remove-Item -LiteralPath $unattendIso -Force -ErrorAction SilentlyContinue
    }
}

# Boot from disk from now on; keep the DVD drive, empty.
Set-VMDvdDrive -VMName $VmName -ControllerNumber $windowsDvd.ControllerNumber `
    -ControllerLocation $windowsDvd.ControllerLocation -Path $null
Set-VMFirmware -VMName $VmName -FirstBootDevice (Get-VMHardDiskDrive -VMName $VmName | Select-Object -First 1)
Write-Host "Windows is installed and signed in. Next: .\scripts\Initialize-TestVm.ps1"
