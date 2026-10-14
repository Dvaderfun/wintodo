<#
.SYNOPSIS
Runs one UI gate (or Measure-Perf.ps1) inside the WintodoTest VM and returns its exit code.

.DESCRIPTION
1. Restores the `clean` checkpoint (signed-in guest desktop, SDK, nothing else).
2. Copies the repo's sources plus the build output the gate uses into C:\wintodo
   in the guest at repo-relative paths, so gates run unchanged:
   - tests\ui\run-*.ps1: the Debug x64 build; with -Aot also publish\aot\win-x64.
   - Measure-Perf.ps1: the Release x64 build and publish\aot\win-x64, and
     -SkipBuild is added (the guest has no Windows SDK/MSVC to build the app).
   - -Include: extra repo-relative files or folders (e.g. a baseline timings file).
3. If the gate calls `dotnet test`, builds tests\Wintodo.Data.Tests in the guest
   first (some gates pass --no-build). Then, unless -NoWarmup or Measure-Perf,
   launches the app once and resets its app data (Reset-AppxPackage): suites
   assume a warm launch, and a fresh guest's first launch is slower than their
   window waits.
4. Runs the gate with pwsh 7 in the guest's signed-in desktop session through an
   interactive scheduled task. WINTODO_ALLOW_LOCAL_UI=1 is set only in that guest
   process; it is never set on the host.
5. Copies the guest's tests/ui/artifacts back into tests/ui/artifacts/, the gate's
   console output to tests/ui/artifacts/vm/<gate>-<timestamp>.log, and
   docs/PERF.md if the gate changed it.
6. Turns the VM off (state is discarded at the next restore) unless -KeepRunning.

Nothing runs on the host desktop. Build first (see docs/guides/testing.md).

.EXAMPLE
.\scripts\Invoke-VmGate.ps1 -Gate run-m0-gate.ps1
.\scripts\Invoke-VmGate.ps1 -Gate run-m13-gate.ps1 -GateArguments '-Aot'
.\scripts\Invoke-VmGate.ps1 -Gate run-completion-motion-gate.ps1 `
    -GateArguments '-BaselineTimings', 'tests\ui\artifacts\measure\timings.json' `
    -Include 'tests\ui\artifacts\measure\timings.json'
.\scripts\Invoke-VmGate.ps1 -Gate Measure-Perf.ps1 -GateArguments '-Milestone', 'VM' -TimeoutMinutes 120
#>
param(
    [Parameter(Mandatory)]
    [string]$Gate,
    [string[]]$GateArguments = @(),
    [string[]]$Include = @(),
    [string]$VmName = 'WintodoTest',
    [int]$TimeoutMinutes = 30,
    [switch]$NoWarmup,
    [switch]$KeepRunning
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'TestVm.Common.ps1')

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$gateName = Split-Path -Leaf $Gate
$debugOutput = 'src\Wintodo.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64'
$releaseOutput = 'src\Wintodo.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64'
$aotOutput = 'publish\aot\win-x64'

if ($gateName -eq 'Measure-Perf.ps1') {
    $gateRelativePath = 'scripts\Measure-Perf.ps1'
    $appOutputs = @($releaseOutput, $aotOutput)
    if ($GateArguments -notcontains '-SkipBuild') { $GateArguments += '-SkipBuild' }
    $warmupOutput = $null   # cold launches are what it measures
} elseif ($gateName -like 'run-*.ps1') {
    $gateRelativePath = "tests\ui\$gateName"
    $appOutputs = @($debugOutput)
    $warmupOutput = $debugOutput
    if ($GateArguments -contains '-Aot') {
        $appOutputs += $aotOutput
        $warmupOutput = $aotOutput
    }
} else {
    throw "Gate must be a tests\ui\run-*.ps1 script or Measure-Perf.ps1; '$Gate' is not."
}
if ($NoWarmup) { $warmupOutput = $null }
$gatePath = Join-Path $repositoryRoot $gateRelativePath
if (-not (Test-Path -LiteralPath $gatePath)) {
    throw "Gate not found: $gateRelativePath"
}
foreach ($output in $appOutputs) {
    if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot "$output\Wintodo.exe"))) {
        throw "No build at $output. See docs/guides/testing.md, 'Running gates in the test VM'."
    }
}
if (-not (Get-VMCheckpoint -VMName $VmName -Name clean -ErrorAction SilentlyContinue)) {
    throw "$VmName has no 'clean' checkpoint. Run .\scripts\Initialize-TestVm.ps1 first."
}
$credential = Get-TestVmCredential
$gateText = Get-Content -LiteralPath $gatePath -Raw
$buildConfigurations = @()
if ($gateText -match 'dotnet\s+test|dotnet @arguments') {
    $buildConfigurations = if ($gateName -eq 'Measure-Perf.ps1') { @('Release') } else { @('Debug') }
}

# One gate at a time: every run restores the same VM.
$mutex = [Threading.Mutex]::new($false, 'Local\WintodoTestVmGate')
try {
    $acquired = $mutex.WaitOne(0)
} catch [Threading.AbandonedMutexException] {
    $acquired = $true   # a previous run was killed; the next restore discards its state
}
if (-not $acquired) {
    throw "Another Invoke-VmGate run is using $VmName."
}

$guestRoot = 'C:\wintodo'
$timestamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$hostArtifacts = Join-Path $repositoryRoot 'tests\ui\artifacts'
$logPath = Join-Path $hostArtifacts ("vm\{0}-{1}.log" -f [IO.Path]::GetFileNameWithoutExtension($gateName), $timestamp)
$stagingDirectory = Join-Path $repositoryRoot 'artifacts\vm-staging'
$payloadPath = Join-Path $stagingDirectory 'payload.zip'
$perfPath = Join-Path $repositoryRoot 'docs\PERF.md'
$perfHash = (Get-FileHash -LiteralPath $perfPath).Hash
$session = $null
$gateExitCode = 1

try {
    Write-Host 'Packing sources and build output'
    New-Item -ItemType Directory -Force -Path $stagingDirectory | Out-Null
    New-TestVmPayload -RepositoryRoot $repositoryRoot -Path $payloadPath -Include ($appOutputs + $Include)

    Write-Host "Restoring $VmName to 'clean'"
    if ((Get-VM -Name $VmName).State -ne 'Off') {
        Stop-VM -Name $VmName -TurnOff -Force
    }
    Restore-VMCheckpoint -VMName $VmName -Name clean -Confirm:$false
    Start-VM -Name $VmName
    $session = Wait-TestVmSession -VmName $VmName -Credential $credential
    Wait-TestVmDesktop -Session $session

    Write-Host 'Copying payload into the guest'
    Copy-TestVmPayload -Session $session -Path $payloadPath -Destination $guestRoot

    foreach ($configuration in $buildConfigurations) {
        Write-Host "Building tests\Wintodo.Data.Tests ($configuration) in the guest"
        $build = Invoke-Command -Session $session -ArgumentList $guestRoot, $configuration -ScriptBlock {
            param($GuestRoot, $Configuration)
            Set-Location -LiteralPath $GuestRoot
            $output = & dotnet build tests\Wintodo.Data.Tests -c $Configuration -p:Platform=x64 -nologo -v:q 2>&1
            [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = ($output | Out-String) }
        }
        if ($build.ExitCode -ne 0) {
            throw "Guest build of tests\Wintodo.Data.Tests ($configuration) failed:`n$($build.Output)"
        }
    }

    if ($warmupOutput) {
        # Gates were written against warm launches on a dev machine; a fresh guest's
        # first launch (package registration, cold file cache, Debug JIT) is slower
        # than their window waits. Launch once, then reset app data so the gate
        # still sees a first run.
        Write-Host 'Warm-up launch in the guest'
        $warmup = @"
`$app = '$guestRoot\$warmupOutput'
`$started = Get-Date
`$launch = winapp run `$app --detach --json -- --database-path '$guestRoot-warmup\warmup.db' | ConvertFrom-Json
if (`$LASTEXITCODE -ne 0 -or -not `$launch.ProcessId) { 'launch failed' | Set-Content '$guestRoot\warmup.txt'; exit 1 }
winapp ui wait-for 'Todo.List.AddTaskBox' -a `$launch.ProcessId -t 120000 | Out-Null
`$code = `$LASTEXITCODE
'{0:N1}' -f ((Get-Date) - `$started).TotalSeconds | Set-Content '$guestRoot\warmup.txt'
Stop-Process -Id `$launch.ProcessId -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
Get-AppxPackage -Name (([xml](Get-Content "`$app\AppxManifest.xml")).Package.Identity.Name) | Reset-AppxPackage
Remove-Item -LiteralPath '$guestRoot-warmup' -Recurse -Force -ErrorAction SilentlyContinue
exit `$code
"@
        Invoke-Command -Session $session -ArgumentList $guestRoot, $warmup -ScriptBlock {
            param($GuestRoot, $Warmup)
            New-Item -ItemType Directory -Force -Path "$GuestRoot-warmup" | Out-Null
            Set-Content -LiteralPath (Join-Path $GuestRoot 'warmup.ps1') -Value $Warmup -Encoding UTF8
        }
        $warmupResult = Invoke-TestVmInteractive -Session $session -TimeoutMinutes 5 `
            -Execute 'C:\Program Files\PowerShell\7\pwsh.exe' `
            -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Minimized -File $guestRoot\warmup.ps1" `
            -WorkingDirectory $guestRoot
        $warmupDetail = Invoke-Command -Session $session -ArgumentList $guestRoot -ScriptBlock {
            param($GuestRoot)
            Get-Content -LiteralPath (Join-Path $GuestRoot 'warmup.txt') -ErrorAction SilentlyContinue
        }
        if ($warmupResult -ne 0) {
            throw "Warm-up launch failed ($warmupDetail)."
        }
        Write-Host "Warm-up: main window after $warmupDetail s"
    }

    $argumentLiteral = ($GateArguments | ForEach-Object { "'" + ($_ -replace "'", "''") + "'" }) -join ', '
    $wrapper = @"
`$env:WINTODO_ALLOW_LOCAL_UI = '1'
Set-Location -LiteralPath '$guestRoot'
`$gateArguments = @($argumentLiteral)
& pwsh -NoProfile -ExecutionPolicy Bypass -File '$guestRoot\$gateRelativePath' @gateArguments *>&1 |
    Out-File -LiteralPath '$guestRoot\gate.log' -Encoding utf8
`$code = `$LASTEXITCODE
Set-Content -LiteralPath '$guestRoot\gate.exitcode' -Value `$code
exit `$code
"@
    Invoke-Command -Session $session -ArgumentList $guestRoot, $wrapper -ScriptBlock {
        param($GuestRoot, $Wrapper)
        Set-Content -LiteralPath (Join-Path $GuestRoot 'run-gate.ps1') -Value $Wrapper -Encoding UTF8
    }

    Write-Host "Running $gateName in the guest desktop session"
    $taskResult = Invoke-TestVmInteractive -Session $session -TimeoutMinutes $TimeoutMinutes `
        -Execute 'C:\Program Files\PowerShell\7\pwsh.exe' `
        -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Minimized -File $guestRoot\run-gate.ps1" `
        -WorkingDirectory $guestRoot
    $guestState = Invoke-Command -Session $session -ArgumentList $guestRoot -ScriptBlock {
        param($GuestRoot)
        [pscustomobject]@{
            ExitCode = Get-Content -LiteralPath (Join-Path $GuestRoot 'gate.exitcode') -ErrorAction SilentlyContinue
            HasArtifacts = Test-Path -LiteralPath (Join-Path $GuestRoot 'tests\ui\artifacts')
            PerfHash = (Get-FileHash -LiteralPath (Join-Path $GuestRoot 'docs\PERF.md')).Hash
        }
    }
    $gateExitCode = if ($guestState.ExitCode) { [int]$guestState.ExitCode } else { $taskResult }

    Write-Host 'Copying artifacts back'
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $logPath) | Out-Null
    Copy-Item -FromSession $session -LiteralPath "$guestRoot\gate.log" -Destination $logPath -ErrorAction SilentlyContinue
    if ($guestState.HasArtifacts) {
        Copy-Item -FromSession $session -Path "$guestRoot\tests\ui\artifacts\*" -Destination $hostArtifacts -Recurse -Force
    }
    if ($guestState.PerfHash -ne $perfHash) {
        if ((Get-FileHash -LiteralPath $perfPath).Hash -ne $perfHash) {
            $guestPerf = Join-Path (Split-Path -Parent $logPath) "PERF-$timestamp.md"
            Copy-Item -FromSession $session -LiteralPath "$guestRoot\docs\PERF.md" -Destination $guestPerf
            Write-Warning "docs/PERF.md changed on the host during the run; the guest's version is at $guestPerf."
        } else {
            Copy-Item -FromSession $session -LiteralPath "$guestRoot\docs\PERF.md" -Destination $perfPath -Force
            Write-Host 'docs/PERF.md updated from the guest.'
        }
    }
} finally {
    if ($session) {
        Remove-PSSession $session
    }
    if (-not $KeepRunning -and (Get-VM -Name $VmName).State -ne 'Off') {
        Stop-VM -Name $VmName -TurnOff -Force
    }
    Remove-Item -LiteralPath $payloadPath -ErrorAction SilentlyContinue
    $mutex.ReleaseMutex()
    $mutex.Dispose()
}

if (Test-Path -LiteralPath $logPath) {
    Get-Content -LiteralPath $logPath | Write-Host
    Write-Host "Log: $logPath"
}
Write-Host "$gateName in $VmName exited with $gateExitCode"
exit $gateExitCode
