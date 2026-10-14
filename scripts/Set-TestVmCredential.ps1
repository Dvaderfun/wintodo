<#
.SYNOPSIS
Saves the WintodoTest guest account credential for unattended VM gate runs.

.DESCRIPTION
Owner-run, interactive. Prompts for the guest's local account (user name only,
for example `tester`) and stores it with DPAPI (Export-Clixml) at
%LOCALAPPDATA%\Wintodo\WintodoTest.credential.xml. Only this Windows account on
this machine can decrypt it. Never copy it into the repository.

If the VM is running, the credential is checked over PowerShell Direct.
#>
param([string]$VmName = 'WintodoTest')

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'TestVm.Common.ps1')

$credential = Get-Credential -Message "Local account on the $VmName guest (user name without a computer prefix)"
if (-not $credential) {
    throw 'No credential entered.'
}

if ((Get-VM -Name $VmName).State -eq 'Running') {
    $null = Invoke-Command -VMName $VmName -Credential $credential -ScriptBlock { $env:USERNAME }
    Write-Host "PowerShell Direct sign-in to $VmName succeeded."
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $TestVmCredentialPath) | Out-Null
$credential | Export-Clixml -LiteralPath $TestVmCredentialPath
Write-Host "Saved to $TestVmCredentialPath (DPAPI, current user only)."
