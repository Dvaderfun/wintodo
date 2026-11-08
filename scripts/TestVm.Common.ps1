# Shared helpers for the WintodoTest Hyper-V VM. Dot-source; not a standalone script.
# Script blocks sent to the guest run in Windows PowerShell 5.1 (the PowerShell
# Direct endpoint), so they avoid PowerShell 7-only syntax.

$TestVmCredentialPath = Join-Path $env:LOCALAPPDATA 'Wintodo\WintodoTest.credential.xml'

function Get-TestVmCredential {
    if (-not (Test-Path -LiteralPath $TestVmCredentialPath)) {
        throw ("No saved guest credential at $TestVmCredentialPath. The owner runs " +
            ".\scripts\Set-TestVmCredential.ps1 once in their own terminal.")
    }
    Import-Clixml -LiteralPath $TestVmCredentialPath
}

function Wait-TestVmSession {
    param(
        [Parameter(Mandatory)][string]$VmName,
        [Parameter(Mandatory)][pscredential]$Credential,
        [int]$TimeoutSeconds = 300
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ($true) {
        try {
            return New-PSSession -VMName $VmName -Credential $Credential -ErrorAction Stop
        } catch {
            if ([DateTime]::UtcNow -ge $deadline) {
                throw "PowerShell Direct did not become available in $TimeoutSeconds s."
            }
            Start-Sleep -Seconds 5
        }
    }
}

# The gates need the guest's console session signed in and unlocked. An enhanced
# (RDP) connection moves the session away from the console and leaves it locked.
function Wait-TestVmDesktop {
    param(
        [Parameter(Mandatory)][System.Management.Automation.Runspaces.PSSession]$Session,
        [int]$TimeoutSeconds = 180
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ($true) {
        $state = Invoke-Command -Session $Session -ScriptBlock {
            $expected = "$env:COMPUTERNAME\$env:USERNAME"
            $console = (Get-CimInstance Win32_ComputerSystem).UserName
            $explorer = @(Get-Process explorer -IncludeUserName -ErrorAction SilentlyContinue |
                Where-Object { $_.UserName -eq $expected -and $_.SessionId -ne 0 })
            $locked = [bool](Get-Process LogonUI -ErrorAction SilentlyContinue)
            [pscustomobject]@{
                Ready = ($console -eq $expected) -and $explorer.Count -gt 0 -and -not $locked
                Detail = "console user '$console', expected '$expected', explorer $($explorer.Count), lock screen $locked"
            }
        }
        if ($state.Ready) {
            return
        }
        if ([DateTime]::UtcNow -ge $deadline) {
            throw ("The guest desktop is not signed in and unlocked ($($state.Detail)). " +
                "Use Basic session in VMConnect; an enhanced session takes over the console.")
        }
        Start-Sleep -Seconds 5
    }
}

# Zips repo files for the guest at their repo-relative paths: every tracked or
# untracked-but-not-ignored file, plus each -Include file or folder (build output,
# AOT publish, a baseline under tests/ui/artifacts). Build outputs drop AppX\ and
# *.appxrecipe: the recipe lists absolute host paths (obj\, NuGet cache), and
# without it `winapp run <folder>` lays out the folder itself in the guest.
function New-TestVmPayload {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$Path,
        [string[]]$Include = @()
    )

    $files = [Collections.Generic.List[string]]::new()
    $tracked = git -C $RepositoryRoot ls-files --cached --others --exclude-standard -z
    if ($LASTEXITCODE -ne 0) { throw 'git ls-files failed.' }
    foreach ($relative in ($tracked -split "`0")) {
        if ($relative) { $files.Add((Join-Path $RepositoryRoot $relative)) }
    }
    foreach ($relative in $Include) {
        $full = Join-Path $RepositoryRoot $relative
        if (Test-Path -LiteralPath $full -PathType Leaf) {
            $files.Add($full)
        } elseif (Test-Path -LiteralPath $full -PathType Container) {
            Get-ChildItem -LiteralPath $full -File -Recurse |
                Where-Object { $_.FullName -notmatch '\\AppX\\' -and $_.Extension -ne '.appxrecipe' } |
                ForEach-Object { $files.Add($_.FullName) }
        } else {
            throw "Payload path not found: $relative"
        }
    }

    Remove-Item -LiteralPath $Path -ErrorAction SilentlyContinue
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $zip = [IO.Compression.ZipFile]::Open($Path, 'Create')
    try {
        foreach ($file in $files) {
            if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { continue }   # deleted, not yet staged
            $entryName = [IO.Path]::GetRelativePath($RepositoryRoot, $file).Replace('\', '/')
            if ($seen.Add($entryName)) {
                [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                    $zip, $file, $entryName, [IO.Compression.CompressionLevel]::Fastest)
            }
        }
    } finally {
        $zip.Dispose()
    }
}

# Copies a payload zip into the guest and extracts it to $Destination.
function Copy-TestVmPayload {
    param(
        [Parameter(Mandatory)][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Destination
    )

    Copy-Item -ToSession $Session -LiteralPath $Path -Destination 'C:\wintodo-payload.zip'
    Invoke-Command -Session $Session -ArgumentList $Destination -ScriptBlock {
        param($Destination)
        $ErrorActionPreference = 'Stop'
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Recurse -Force }
        [IO.Compression.ZipFile]::ExtractToDirectory('C:\wintodo-payload.zip', $Destination)
        Remove-Item -LiteralPath 'C:\wintodo-payload.zip'
    }
}

# Saves the VM's current screen as a PNG through Hyper-V WMI (no VMConnect, no
# host desktop). Diagnostic only: low colour depth, scaled to Width x Height.
function Save-TestVmScreen {
    param(
        [Parameter(Mandatory)][string]$VmName,
        [Parameter(Mandatory)][string]$Path,
        [int]$Width = 960,
        [int]$Height = 540
    )

    $namespace = 'root\virtualization\v2'
    $service = Get-CimInstance -Namespace $namespace -ClassName Msvm_VirtualSystemManagementService
    $settings = Get-CimInstance -Namespace $namespace -ClassName Msvm_ComputerSystem -Filter "ElementName='$VmName'" |
        Get-CimAssociatedInstance -ResultClassName Msvm_VirtualSystemSettingData |
        Where-Object VirtualSystemType -eq 'Microsoft:Hyper-V:System:Realized'
    $result = Invoke-CimMethod -InputObject $service -MethodName GetVirtualSystemThumbnailImage -Arguments @{
        TargetSystem = $settings; WidthPixels = [uint16]$Width; HeightPixels = [uint16]$Height
    }
    $pixels = [byte[]]$result.ImageData
    if (-not $pixels) {
        throw "No screen image from $VmName (is it running?)."
    }

    Add-Type -AssemblyName System.Drawing
    $bitmap = [Drawing.Bitmap]::new($Width, $Height, [Drawing.Imaging.PixelFormat]::Format16bppRgb565)
    try {
        $data = $bitmap.LockBits([Drawing.Rectangle]::new(0, 0, $Width, $Height), 'WriteOnly', $bitmap.PixelFormat)
        try {
            $rowBytes = $Width * 2
            for ($row = 0; $row -lt $Height -and ($row + 1) * $rowBytes -le $pixels.Length; $row++) {
                [Runtime.InteropServices.Marshal]::Copy($pixels, $row * $rowBytes,
                    [IntPtr]::Add($data.Scan0, $row * $data.Stride), $rowBytes)
            }
        } finally {
            $bitmap.UnlockBits($data)
        }
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    } finally {
        $bitmap.Dispose()
    }
}

# Runs a program in the signed-in user's desktop session through a one-shot
# scheduled task (LogonType Interactive). A PowerShell Direct session has no
# desktop, so UI automation and per-user package installs go through here.
function Invoke-TestVmInteractive {
    param(
        [Parameter(Mandatory)][System.Management.Automation.Runspaces.PSSession]$Session,
        [Parameter(Mandatory)][string]$Execute,
        [string]$Argument = '',
        [string]$WorkingDirectory = 'C:\',
        [ValidateSet('Limited', 'Highest')][string]$RunLevel = 'Limited',
        [int]$TimeoutMinutes = 30
    )

    Invoke-Command -Session $Session -ArgumentList @(
        $Execute, $Argument, $WorkingDirectory, $RunLevel, $TimeoutMinutes
    ) -ScriptBlock {
        param($Execute, $Argument, $WorkingDirectory, $RunLevel, $TimeoutMinutes)
        $ErrorActionPreference = 'Stop'
        $taskName = 'WintodoInteractive'
        $user = "$env:COMPUTERNAME\$env:USERNAME"

        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
        $actionArguments = @{ Execute = $Execute; WorkingDirectory = $WorkingDirectory }
        if ($Argument) {
            $actionArguments.Argument = $Argument
        }
        $task = Register-ScheduledTask -TaskName $taskName `
            -Action (New-ScheduledTaskAction @actionArguments) `
            -Principal (New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel $RunLevel) `
            -Settings (New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
                -ExecutionTimeLimit (New-TimeSpan -Minutes $TimeoutMinutes))
        try {
            $started = (Get-Date).AddSeconds(-2)
            Start-ScheduledTask -TaskName $taskName
            $deadline = [DateTime]::UtcNow.AddMinutes($TimeoutMinutes + 1)
            do {
                Start-Sleep -Seconds 2
                $state = (Get-ScheduledTask -TaskName $taskName).State
                $info = Get-ScheduledTaskInfo -TaskName $taskName
                $finished = $state -ne 'Running' -and $state -ne 'Queued' -and $info.LastRunTime -ge $started
                if (-not $finished -and [DateTime]::UtcNow -ge $deadline) {
                    Stop-ScheduledTask -TaskName $taskName
                    throw "Interactive task did not finish within $TimeoutMinutes min: $Execute $Argument"
                }
            } until ($finished)
            [int]$info.LastTaskResult
        } finally {
            Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
        }
    }
}
