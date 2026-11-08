<#
.SYNOPSIS
Checks an already installed, signed package in the approved Hyper-V guest.
.DESCRIPTION
Preflight is the default. -Execute runs four smoke assertions on the supplied
installed PID. No package installation, activation, trust or VM changes occur.
Do not use Invoke-VmGate: it restores and registers a development layout.
#>
param(
    [Parameter(Mandatory)][string]$PackagePath,
    [Parameter(Mandatory)][string]$SourceCommit,
    [Parameter(Mandatory)][string]$PackageSha256,
    [Parameter(Mandatory)][string]$ExecutableSha256,
    [Parameter(Mandatory)][string]$SignerThumbprint,
    [Parameter(Mandatory)][string]$ExpectedIdentity,
    [Parameter(Mandatory)][string]$ExpectedPublisher,
    [Parameter(Mandatory)][string]$ExpectedVersion,
    [Parameter(Mandatory)][ValidateSet('x64', 'ARM64')][string]$Architecture,
    [Parameter(Mandatory)][int]$AppPid,
    [Parameter(Mandatory)][guid]$ExpectedVmId,
    [Parameter(Mandatory)][string]$ExpectedTitle,
    [string]$ArtifactDirectory,
    [switch]$Execute
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

function Assert-InstalledSmokeGuest {
    param($Snapshot, [guid]$VmId, [string]$Architecture)
    $observedVmId = [guid]::Empty
    if ($VmId -eq [guid]::Empty -or $Snapshot.VmName -cne 'WintodoTest' -or
        -not [guid]::TryParse([string]$Snapshot.VmId, [ref]$observedVmId) -or $observedVmId -ne $VmId -or
        $Snapshot.Manufacturer -cne 'Microsoft Corporation' -or $Snapshot.Model -cne 'Virtual Machine' -or
        [string]$Snapshot.Architecture -ine $Architecture -or $Snapshot.DeveloperMode -ne 0 -or
        $Snapshot.OsBuild -lt 26100 -or -not $Snapshot.Interactive) {
        throw 'WINTODO_INSTALLED_SMOKE_GUEST_REJECTED'
    }
}

function Assert-InstalledSmokeRegistration {
    param($Registration, $ProcessSnapshot, $Expected)
    $installedExe = [IO.Path]::GetFullPath((Join-Path $Registration.InstallLocation 'Wintodo.exe'))
    if ($Registration.Name -cne $Expected.Identity -or $Registration.Publisher -cne $Expected.Publisher -or
        [string]$Registration.Version -cne $Expected.Version -or
        [string]$Registration.Architecture -ine $Expected.Architecture -or
        $Registration.IsDevelopmentMode -ne $false -or [string]$Registration.Status -cne 'Ok' -or
        [string]$Registration.SignatureKind -notin @('Enterprise', 'Store') -or
        $ProcessSnapshot.Id -ne $Expected.AppPid -or -not $ProcessSnapshot.Responding -or
        $ProcessSnapshot.SessionId -ne $Expected.SessionId -or
        [IO.Path]::GetFullPath($ProcessSnapshot.Path) -ine $installedExe) {
        throw 'WINTODO_INSTALLED_SMOKE_REGISTRATION_REJECTED'
    }
    $installedExe
}

function Assert-InstalledSmokeInspection {
    param($Inspection)
    $pending = [Collections.Generic.Queue[object]]::new()
    foreach ($node in @($Inspection.elements)) { if ($null -ne $node) { $pending.Enqueue($node) } }
    foreach ($window in @($Inspection.windows)) {
        foreach ($node in @($window.elements)) { if ($null -ne $node) { $pending.Enqueue($node) } }
    }
    $total = 0
    $interactive = 0
    while ($pending.Count) {
        $node = $pending.Dequeue()
        $total++
        if ($total -gt 4096) { throw 'WINTODO_INSTALLED_SMOKE_INSPECTION_REJECTED' }
        if ($node.type -match '^(Button|TextBox|ComboBox|CheckBox|ToggleSwitch|TabItem|Edit)$' -and
            $node.name -notmatch '^(Minimize|Maximize|Restore|Close|System)$') {
            $interactive++
            if ([string]::IsNullOrWhiteSpace($node.automationId)) {
                throw 'WINTODO_INSTALLED_SMOKE_INSPECTION_REJECTED'
            }
        }
        foreach ($child in @($node.children)) { if ($null -ne $child) { $pending.Enqueue($child) } }
    }
    if (-not $total -or -not $interactive) { throw 'WINTODO_INSTALLED_SMOKE_INSPECTION_REJECTED' }
    $interactive
}

function Invoke-InstalledSmokeUi {
    param([string[]]$Arguments)
    $global:LASTEXITCODE = $null
    $selector = @(if ($Arguments[0] -cne 'yield') { '-a'; "$AppPid" })
    try { $output = @(& winapp ui @Arguments @selector 2>&1) }
    catch { throw 'WINTODO_INSTALLED_SMOKE_UI_COMMAND_FAILED' }
    if ($null -eq $global:LASTEXITCODE -or $global:LASTEXITCODE -ne 0) {
        throw 'WINTODO_INSTALLED_SMOKE_UI_COMMAND_FAILED'
    }
    $output
}

function Get-InstalledSmokeProcess {
    param([int]$ProcessId)
    $process = Get-Process -Id $ProcessId -ErrorAction Stop
    [pscustomobject]@{
        Id = $process.Id; Path = $process.Path; SessionId = $process.SessionId
        StartTime = $process.StartTime.ToUniversalTime().Ticks; Responding = $process.Responding
    }
}

function Assert-InstalledSmokeProcessUnchanged {
    param($Before, $After)
    if ($After.Id -ne $Before.Id -or $After.StartTime -ne $Before.StartTime -or
        $After.Path -ine $Before.Path -or $After.SessionId -ne $Before.SessionId -or -not $After.Responding) {
        throw 'WINTODO_INSTALLED_SMOKE_PROCESS_CHANGED'
    }
}

function Assert-InstalledSmokeSignature {
    param($Signature, [string]$Thumbprint)
    if ([string]$Signature.Status -cne 'Valid' -or $null -eq $Signature.SignerCertificate -or
        $Signature.SignerCertificate.Thumbprint -ine $Thumbprint) {
        throw 'WINTODO_INSTALLED_SMOKE_SIGNATURE_REJECTED'
    }
}

function Assert-InstalledSmokeScreenshot {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf) -or (Get-Item -LiteralPath $Path).Length -le 0) {
        throw 'WINTODO_INSTALLED_SMOKE_SCREENSHOT_MISSING'
    }
}

$report = [ordered]@{
    status = 'FAIL'; scope = 'installed_package_smoke_preflight'; architecture = $Architecture
    source_commit = $null
    package_sha256 = $null; executable_sha256 = $null; preflight = 'PENDING'
    automation = 'PENDING'; visual_review = 'PENDING'; deployment_acceptance = 'PENDING'
    release_authorized = $false; results = @(); findings = @()
}
$stage = 'guest_boundary'
$outputPath = $null
$packageStream = $null
$archive = $null
$executableStream = $null
$uiStarted = $false
$mutex = $null
$mutexAcquired = $false
$previousWorkflow = $env:WINAPP_UI_WORKFLOW_ID
try {
    # HypervisorPresent alone also holds on the owner host: require guest-only
    # integration metadata and the VM GUID independently pinned on that host.
    $guest = Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Virtual Machine\Guest\Parameters'
    $computer = Get-CimInstance -ClassName Win32_ComputerSystem
    $os = Get-CimInstance -ClassName Win32_OperatingSystem
    $developer = if (Test-Path -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock') {
        Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock'
    } else { $null }
    $developerMode = if ($null -eq $developer.AllowDevelopmentWithoutDevLicense) { 0 } else { $developer.AllowDevelopmentWithoutDevLicense }
    Assert-InstalledSmokeGuest @{
        VmName = $guest.VirtualMachineName; VmId = $guest.VirtualMachineId
        Manufacturer = $computer.Manufacturer; Model = $computer.Model
        Architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        DeveloperMode = $developerMode; OsBuild = [int]$os.BuildNumber
        Interactive = [Environment]::UserInteractive
    } -VmId $ExpectedVmId -Architecture $Architecture

    $stage = 'input_policy'
    if ($SourceCommit -cnotmatch '^[a-f0-9]{40}$' -or $SourceCommit -ceq ('0' * 40) -or
        $PackageSha256 -cnotmatch '^[a-f0-9]{64}$' -or $ExecutableSha256 -cnotmatch '^[a-f0-9]{64}$' -or
        $SignerThumbprint -notmatch '^[a-fA-F0-9]{40}$' -or $AppPid -le 0 -or
        [string]::IsNullOrWhiteSpace($ExpectedIdentity) -or [string]::IsNullOrWhiteSpace($ExpectedPublisher) -or
        [string]::IsNullOrWhiteSpace($ExpectedTitle) -or
        $ExpectedVersion -cnotmatch '^\d+\.\d+\.\d+\.\d+$' -or [IO.Path]::GetExtension($PackagePath) -ine '.msix') {
        throw 'WINTODO_INSTALLED_SMOKE_INPUT_REJECTED'
    }
    $report.source_commit = $SourceCommit
    # Every run uses a fresh ignored evidence directory; old PASS and screenshots
    # cannot be mistaken for this run, and no existing output is overwritten.
    $allowedRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'artifacts/installed-smoke'))
    if (-not $ArtifactDirectory) { $ArtifactDirectory = Join-Path $allowedRoot ([guid]::NewGuid().ToString('N')) }
    $ArtifactDirectory = [IO.Path]::GetFullPath($ArtifactDirectory)
    if (-not $ArtifactDirectory.StartsWith($allowedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        (Test-Path -LiteralPath $ArtifactDirectory)) { throw 'WINTODO_INSTALLED_SMOKE_OUTPUT_REJECTED' }
    $ancestor = Split-Path -Parent $ArtifactDirectory
    while ($ancestor -and $ancestor.Length -ge $PSScriptRoot.Length) {
        if ((Test-Path -LiteralPath $ancestor) -and
            ((Get-Item -LiteralPath $ancestor).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'WINTODO_INSTALLED_SMOKE_OUTPUT_REJECTED'
        }
        $ancestor = Split-Path -Parent $ancestor
    }
    New-Item -ItemType Directory -Path $ArtifactDirectory | Out-Null
    $outputPath = Join-Path $ArtifactDirectory 'results.json'

    $stage = 'package_structure'
    $packageStream = [IO.File]::Open($PackagePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $actualHash = (Get-FileHash -InputStream $packageStream -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -cne $PackageSha256) { throw 'WINTODO_INSTALLED_SMOKE_PACKAGE_HASH_MISMATCH' }
    $packageStream.Position = 0
    $report.package_sha256 = $actualHash
    $repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $structurePath = Join-Path $ArtifactDirectory 'structure.json'
    $global:LASTEXITCODE = $null
    $null = & python -B (Join-Path $repositoryRoot 'scripts/Test-ReleasePackage.py') $PackagePath `
        --architecture $Architecture --expected-identity $ExpectedIdentity --expected-publisher $ExpectedPublisher `
        --expected-version $ExpectedVersion --output $structurePath 2>&1
    if ($null -eq $global:LASTEXITCODE -or $global:LASTEXITCODE -ne 0) { throw 'WINTODO_INSTALLED_SMOKE_STRUCTURE_REJECTED' }
    $structure = Get-Content -LiteralPath $structurePath -Raw | ConvertFrom-Json
    if ($structure.status -cne 'PASS' -or $structure.package_sha256 -cne $PackageSha256 -or
        $structure.identity_expectations -cne 'CHECKED' -or @($structure.findings).Count) {
        throw 'WINTODO_INSTALLED_SMOKE_STRUCTURE_REJECTED'
    }
    $archive = [IO.Compression.ZipArchive]::new($packageStream, [IO.Compression.ZipArchiveMode]::Read, $true)
    $signatureEntry = $archive.GetEntry('AppxSignature.p7x')
    $exeEntry = $archive.GetEntry('Wintodo.exe')
    if ($null -eq $signatureEntry -or $signatureEntry.Length -le 0 -or $null -eq $exeEntry) {
        throw 'WINTODO_INSTALLED_SMOKE_SIGNATURE_OR_EXECUTABLE_MISSING'
    }
    $entryStream = $exeEntry.Open()
    try { $entryHash = (Get-FileHash -InputStream $entryStream -Algorithm SHA256).Hash.ToLowerInvariant() }
    finally { $entryStream.Dispose() }
    if ($entryHash -cne $ExecutableSha256) { throw 'WINTODO_INSTALLED_SMOKE_EXECUTABLE_HASH_MISMATCH' }

    $stage = 'signature_trust'
    $signature = Get-AuthenticodeSignature -LiteralPath $PackagePath
    Assert-InstalledSmokeSignature $signature -Thumbprint $SignerThumbprint

    $stage = 'installed_association'
    $registrations = @(Get-AppxPackage -Name $ExpectedIdentity | Where-Object Name -CEQ $ExpectedIdentity)
    if ($registrations.Count -ne 1) { throw 'WINTODO_INSTALLED_SMOKE_REGISTRATION_REJECTED' }
    $processBefore = Get-InstalledSmokeProcess -ProcessId $AppPid
    $sessionId = (Get-Process -Id $PID).SessionId
    $installedExe = Assert-InstalledSmokeRegistration $registrations[0] $processBefore @{
        Identity = $ExpectedIdentity; Publisher = $ExpectedPublisher; Version = $ExpectedVersion
        Architecture = $Architecture; AppPid = $AppPid; SessionId = $sessionId
    }
    $executableStream = [IO.File]::Open($installedExe, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $installedHash = (Get-FileHash -InputStream $executableStream -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($installedHash -cne $ExecutableSha256) { throw 'WINTODO_INSTALLED_SMOKE_EXECUTABLE_HASH_MISMATCH' }
    $report.executable_sha256 = $installedHash
    Assert-InstalledSmokeProcessUnchanged $processBefore (Get-InstalledSmokeProcess -ProcessId $AppPid)
    $report.preflight = 'PASS'
    $report.status = 'PASS'

    if ($Execute) {
        $stage = 'ui_opt_in'
        if ($env:WINTODO_ALLOW_LOCAL_UI -cne '1') { throw 'WINTODO_INSTALLED_SMOKE_UI_SESSION_NOT_APPROVED' }
        $mutex = [Threading.Mutex]::new($false, 'Local\WintodoInstalledPackageSmokeGate')
        try { $mutexAcquired = $mutex.WaitOne(0) }
        catch [Threading.AbandonedMutexException] { $mutexAcquired = $true }
        if (-not $mutexAcquired) { throw 'WINTODO_INSTALLED_SMOKE_CONCURRENT_WORKFLOW' }
        $env:WINAPP_UI_WORKFLOW_ID = [guid]::NewGuid().ToString('N')
        $uiStarted = $true
        $report.scope = 'installed_package_smoke_automation'
        $report.automation = 'FAIL'
        $checks = [Collections.Generic.List[object]]::new()
        $stage = 'smoke_assertions'
        foreach ($assertion in @('responding', 'visible_window', 'automation_ids', 'screenshot')) {
            Assert-InstalledSmokeProcessUnchanged $processBefore (Get-InstalledSmokeProcess -ProcessId $AppPid)
            switch ($assertion) {
                'responding' { }
                'visible_window' {
                    $windows = @(Invoke-InstalledSmokeUi @('list-windows', '--json') | ConvertFrom-Json)
                    $mainWindow = @($windows | Where-Object { $_.title -ceq $ExpectedTitle -and $_.hwnd -and $_.hwnd -ne '0' })
                    if ($mainWindow.Count -ne 1) { throw 'WINTODO_INSTALLED_SMOKE_WINDOW_REJECTED' }
                }
                'automation_ids' {
                    $inspection = Invoke-InstalledSmokeUi @('inspect', '--interactive', '--json') | ConvertFrom-Json
                    $null = Assert-InstalledSmokeInspection $inspection
                }
                'screenshot' {
                    $screenshotPath = Join-Path $ArtifactDirectory 'initial.png'
                    $null = Invoke-InstalledSmokeUi @('screenshot', '--window', [string]$mainWindow[0].hwnd, '--output', $screenshotPath)
                    Assert-InstalledSmokeScreenshot -Path $screenshotPath
                }
            }
            $checks.Add(@{ assertion = $assertion; status = 'PASS' })
            $report.results = @($checks.ToArray())
        }
        Assert-InstalledSmokeProcessUnchanged $processBefore (Get-InstalledSmokeProcess -ProcessId $AppPid)
        $report.automation = 'PASS'
    }
} catch {
    $report.status = 'FAIL'
    $report.findings = @(@{ category = $stage })
} finally {
    if ($uiStarted) {
        try { $null = Invoke-InstalledSmokeUi @('yield') }
        catch { $report.status = 'FAIL'; $report.findings += @{ category = 'workflow_release' } }
    }
    $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflow
    if ($mutexAcquired) { $mutex.ReleaseMutex() }
    if ($null -ne $mutex) { $mutex.Dispose() }
    if ($null -ne $executableStream) { $executableStream.Dispose() }
    if ($null -ne $archive) { $archive.Dispose() }
    if ($null -ne $packageStream) { $packageStream.Dispose() }
}
if ($outputPath) {
    try { $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $outputPath -Encoding utf8 }
    catch { $report.status = 'FAIL'; $report.findings += @{ category = 'output_write' } }
}
@{ status = $report.status; preflight = $report.preflight; automation = $report.automation; findings = $report.findings } |
    ConvertTo-Json -Depth 4 -Compress | Write-Output
if ($report.status -ne 'PASS') { exit 1 }
