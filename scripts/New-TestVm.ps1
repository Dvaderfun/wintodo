<#
.SYNOPSIS
Creates the WintodoTest Hyper-V VM that runs UI gates, perf rows and M10 checks.

.DESCRIPTION
Generation 2, 4 vCPUs, 8 GB static memory, 80 GB dynamic VHDX on the Default
Switch, Secure Boot (Microsoft Windows template) and a virtual TPM so Windows 11
installs. Standard checkpoints (they keep the signed-in desktop), no automatic
checkpoints. Boots from the Windows 11 ISO. Next steps:
`scripts/Install-TestVmWindows.ps1`, then `scripts/Initialize-TestVm.ps1`.

Runs without elevation for members of Hyper-V Administrators. Enhanced session
mode is a host-wide Hyper-V setting with no per-VM switch; this script only
reports it. See docs/guides/testing.md, "Running gates in the test VM".

If any step fails, the half-built VM and its disk are removed.

.EXAMPLE
.\scripts\New-TestVm.ps1 -IsoPath D:\iso\Win11_25H2_English_x64.iso
#>
param(
    [Parameter(Mandatory)]
    [string]$IsoPath,
    [string]$VmName = 'WintodoTest',
    [string]$VhdDirectory = (Get-VMHost).VirtualHardDiskPath,
    [string]$SwitchName = 'Default Switch'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $IsoPath -PathType Leaf)) {
    throw "ISO not found: $IsoPath"
}
$IsoPath = (Resolve-Path -LiteralPath $IsoPath).Path
if (Get-VM -Name $VmName -ErrorAction SilentlyContinue) {
    throw "A VM named '$VmName' already exists. Remove it first or pass -VmName."
}
if (-not (Get-VMSwitch -Name $SwitchName -ErrorAction SilentlyContinue)) {
    throw "Virtual switch '$SwitchName' was not found."
}
$vhdPath = Join-Path $VhdDirectory "$VmName.vhdx"
if (Test-Path -LiteralPath $vhdPath) {
    throw "Disk already exists: $vhdPath"
}

$vm = $null
try {
    $vm = New-VM -Name $VmName -Generation 2 `
        -MemoryStartupBytes 8GB `
        -NewVHDPath $vhdPath -NewVHDSizeBytes 80GB `
        -SwitchName $SwitchName
    Set-VM -VM $vm -ProcessorCount 4 -StaticMemory `
        -CheckpointType Standard -AutomaticCheckpointsEnabled $false `
        -AutomaticStopAction ShutDown
    Set-VMFirmware -VM $vm -EnableSecureBoot On -SecureBootTemplate MicrosoftWindows

    Set-VMKeyProtector -VM $vm -NewLocalKeyProtector
    Enable-VMTPM -VM $vm

    $dvd = Add-VMDvdDrive -VM $vm -Path $IsoPath -Passthru
    Set-VMFirmware -VM $vm -FirstBootDevice $dvd
} catch {
    $failure = $_
    if ($vm) {
        Remove-VM -VM $vm -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $vhdPath) {
        Remove-Item -LiteralPath $vhdPath -Force -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $vhdPath) {
            Write-Warning "Could not delete $vhdPath; remove it manually."
        }
    }
    throw $failure
}

Get-VM -Name $VmName | Format-List Name, Generation, ProcessorCount, MemoryStartup,
    DynamicMemoryEnabled, CheckpointType, State
Get-VMSecurity -VMName $VmName | Format-List TpmEnabled
Get-VMFirmware -VMName $VmName | Format-List SecureBoot, SecureBootTemplate

if ((Get-VMHost).EnableEnhancedSessionMode) {
    Write-Warning ("Host enhanced session mode is ON. Connecting in enhanced mode " +
        "moves the guest's console session into RDP and locks the desktop the gates " +
        "need. Turn it off host-wide (Set-VMHost -EnableEnhancedSessionMode `$false) " +
        "or always use Basic session for $VmName.")
}

Write-Host "Created $VmName. Next: .\scripts\Install-TestVmWindows.ps1"
