param(
    [ValidateSet("FD", "AOT")]
    [string[]]$Builds = @("FD", "AOT"),
    [ValidateSet(0, 5000, 20000)]
    [int[]]$TaskCounts = @(0, 5000, 20000),
    [switch]$SkipBuild,
    [switch]$SkipTenMinuteSample,
    [switch]$SkipScrollGate,
    [string]$Milestone = "M9"
)
# Local desktop input requires explicit owner permission for this session.
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1') {
    throw 'Local UI automation is disabled on this main machine. Use an isolated VM/Sandbox, or obtain owner permission for this session before setting WINTODO_ALLOW_LOCAL_UI=1. See docs/guides/testing.md.'
}


Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$runId = [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
$artifactDirectory = Join-Path $repositoryRoot "tests\ui\artifacts\perf\$runId"
$frameworkDirectory = Join-Path $repositoryRoot (
    "src\Wintodo.App\bin\x64\Release\" +
    "net10.0-windows10.0.26100.0\win-x64"
)
$aotDirectory = Join-Path $repositoryRoot "publish\aot\win-x64"
$invariantCulture = [System.Globalization.CultureInfo]::InvariantCulture

New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null

if (-not ("WintodoPerfInput" -as [type])) {
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class WintodoPerfInput
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern void mouse_event(
        uint flags,
        uint dx,
        uint dy,
        int data,
        UIntPtr extraInfo);
}
"@
}

function Invoke-DotNet {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments,
        [Parameter(Mandatory)]
        [string]$FailureMessage
    )

    & dotnet @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw $FailureMessage
    }
}

function Initialize-Builds {
    if ($SkipBuild) {
        return
    }

    if ($Builds -contains "AOT") {
        & (Join-Path $PSScriptRoot "Publish-Aot.ps1") -Platforms x64
        if ($LASTEXITCODE -ne 0) {
            throw "Native AOT x64 publish failed."
        }
    }

    if ($Builds -contains "FD") {
        Invoke-DotNet -Arguments @(
            "build",
            (Join-Path $repositoryRoot "Wintodo.sln"),
            "-c", "Release",
            "-p:Platform=x64",
            "-p:PublishAot=false",
            "-p:SelfContained=false",
            "-p:WindowsAppSDKSelfContained=false",
            "-p:PublishTrimmed=false",
            "-warnaserror"
        ) -FailureMessage "Framework-dependent Release build failed."
    }
    else {
        Invoke-DotNet -Arguments @(
            "build",
            (Join-Path $repositoryRoot "tests\Wintodo.Data.Tests"),
            "-c", "Release",
            "-p:Platform=x64",
            "-warnaserror"
        ) -FailureMessage "Performance test build failed."
    }
}

function Get-AppDirectory {
    param([Parameter(Mandatory)][string]$Build)

    return $Build -eq "AOT" ? $aotDirectory : $frameworkDirectory
}

function Seed-Database {
    param(
        [Parameter(Mandatory)][string]$DatabasePath,
        [Parameter(Mandatory)][int]$TaskCount,
        [Parameter(Mandatory)][int]$ListCount
    )

    $env:WINTODO_PERF_DB_PATH = $DatabasePath
    $env:WINTODO_PERF_TASK_COUNT = $TaskCount.ToString($invariantCulture)
    $env:WINTODO_PERF_LIST_COUNT = $ListCount.ToString($invariantCulture)
    try {
        Invoke-DotNet -Arguments @(
            "test",
            "--project",
            (Join-Path $repositoryRoot "tests\Wintodo.Data.Tests"),
            "-c", "Release",
            "-p:Platform=x64",
            "--filter-method",
            "*PerformanceFixtureTests.SeedRequestedPerformanceDatabase",
            "--no-build",
            "--no-restore"
        ) -FailureMessage "Performance database seeding failed."
    }
    finally {
        Remove-Item Env:WINTODO_PERF_DB_PATH -ErrorAction SilentlyContinue
        Remove-Item Env:WINTODO_PERF_TASK_COUNT -ErrorAction SilentlyContinue
        Remove-Item Env:WINTODO_PERF_LIST_COUNT -ErrorAction SilentlyContinue
    }
}

function Start-WintodoRun {
    param(
        [Parameter(Mandatory)][string]$AppDirectory,
        [Parameter(Mandatory)][string]$DatabasePath,
        [Parameter(Mandatory)][string]$LogName,
        [string]$ScrollResultPath
    )

    $stdout = Join-Path $artifactDirectory "$LogName.log"
    $stderr = Join-Path $artifactDirectory "$LogName.err.log"
    $arguments = @(
        "run",
        $AppDirectory,
        "--debug-output",
        "--",
        "--database-path",
        $DatabasePath
    )
    if ($ScrollResultPath) {
        $arguments += @(
            "--perf-scroll-result",
            $ScrollResultPath,
            "--perf-scroll-items",
            "10000"
        )
    }

    $launchTimestamp = [System.Diagnostics.Stopwatch]::GetTimestamp()
    $winappProcess = Start-Process `
        -FilePath "winapp.exe" `
        -ArgumentList $arguments `
        -WorkingDirectory $repositoryRoot `
        -WindowStyle Hidden `
        -RedirectStandardOutput $stdout `
        -RedirectStandardError $stderr `
        -PassThru

    $deadline = [DateTimeOffset]::Now.AddMinutes(2)
    $appPid = 0
    $firstFrameMs = [double]::NaN
    $interactiveMs = [double]::NaN
    while ([DateTimeOffset]::Now -lt $deadline) {
        Start-Sleep -Milliseconds 100
        $output = Get-Content -LiteralPath $stdout -Raw -ErrorAction SilentlyContinue
        if ($appPid -eq 0 -and $output -match "launched \(PID: (?<pid>\d+)\)") {
            $appPid = [int]$Matches.pid
        }

        if ($output -match "WINTODO_PERF_FIRST_FRAME_MS=(?<time>[0-9.]+)") {
            $firstFrameMs = [double]::Parse($Matches.time, $invariantCulture)
        }

        if ($output -match "WINTODO_PERF_INTERACTIVE_MS=(?<time>[0-9.]+)") {
            $interactiveMs = [double]::Parse($Matches.time, $invariantCulture)
        }

        if ($appPid -ne 0 -and
            -not [double]::IsNaN($firstFrameMs) -and
            -not [double]::IsNaN($interactiveMs)) {
            break
        }

        if ($winappProcess.HasExited) {
            throw "winapp exited before Wintodo became interactive. See $stdout."
        }
    }

    if ($appPid -eq 0 -or
        [double]::IsNaN($firstFrameMs) -or
        [double]::IsNaN($interactiveMs)) {
        throw "Timed out waiting for Wintodo startup diagnostics in $stdout."
    }

    return [pscustomobject]@{
        AppPid = $appPid
        FirstFrameMs = $firstFrameMs
        InteractiveMs = $interactiveMs
        LaunchTimestamp = $launchTimestamp
        WinAppProcess = $winappProcess
        Stdout = $stdout
    }
}

function Stop-WintodoRun {
    param([Parameter(Mandatory)]$Run)

    Stop-Process -Id $Run.AppPid -ErrorAction SilentlyContinue
    if ($null -ne $Run.WinAppProcess -and -not $Run.WinAppProcess.HasExited) {
        $Run.WinAppProcess.WaitForExit(10000) | Out-Null
    }
}

function Wait-UntilRunOffset {
    param(
        [Parameter(Mandatory)]$Run,
        [Parameter(Mandatory)][TimeSpan]$Offset
    )

    $elapsed = [System.Diagnostics.Stopwatch]::GetElapsedTime($Run.LaunchTimestamp)
    $remaining = $Offset - $elapsed
    if ($remaining -gt [TimeSpan]::Zero) {
        Start-Sleep -Milliseconds ([int][Math]::Ceiling($remaining.TotalMilliseconds))
    }
}

function Get-MemorySample {
    param([Parameter(Mandatory)][int]$AppPid)

    $process = Get-Process -Id $AppPid
    return [pscustomobject]@{
        WorkingSetMb = [Math]::Round($process.WorkingSet64 / 1MB, 2)
        PrivateMb = [Math]::Round($process.PrivateMemorySize64 / 1MB, 2)
    }
}

function Get-BudgetFailures {
    param(
        [Parameter(Mandatory)][string]$Build,
        [Parameter(Mandatory)][int]$TaskCount,
        [Parameter(Mandatory)]$Result
    )

    # Budgets are the ADR-008 re-baseline of PLAN.md section 7. The
    # framework-dependent build is a development-loop build from M9 onward and is
    # measured but not gated.
    $failures = [System.Collections.Generic.List[string]]::new()
    if ($Build -ne "AOT") {
        return $failures.ToArray()
    }

    # App-owned startup work: first rendered frame to first interactive frame.
    if ($Result.ColdAppWorkMs -gt 700) {
        $failures.Add("cold app startup work $($Result.ColdAppWorkMs) ms > 700 ms")
    }
    if ($Result.WarmAppWorkMs -gt 550) {
        $failures.Add("warm app startup work $($Result.WarmAppWorkMs) ms > 550 ms")
    }

    # Total startup is a ceiling against gross regressions only; the pre-first-frame
    # share is first-use XAML runtime and window construction.
    if ($Result.ColdInteractiveMs -gt 3000) {
        $failures.Add("cold start $($Result.ColdInteractiveMs) ms > 3000 ms")
    }
    if ($Result.WarmInteractiveMs -gt 2300) {
        $failures.Add("warm start $($Result.WarmInteractiveMs) ms > 2300 ms")
    }

    # Working set is gated on the settled +30 s sample, not the +2 s sample, which
    # lands before the first virtualisation pass and before the GC heap settles.
    $workingSetBudget = switch ($TaskCount) {
        0 { 165 }
        5000 { 200 }
        20000 { 205 }
        default { $null }
    }
    if ($null -ne $workingSetBudget -and
        $Result.WorkingSet30sMb -gt $workingSetBudget) {
        $failures.Add(
            "working set at +30 s $($Result.WorkingSet30sMb) MB > $workingSetBudget MB")
    }

    if ($null -ne $Result.WorkingSet10MinMb) {
        if ($Result.WorkingSet10MinMb -gt ($Result.WorkingSet30sMb + 10)) {
            $failures.Add(
                "10-minute working set $($Result.WorkingSet10MinMb) MB exceeds the " +
                "+30 s sample $($Result.WorkingSet30sMb) MB by more than 10 MB")
        }
        if ($Result.WorkingSet2sMb -lt $Result.WorkingSet30sMb -and
            $Result.WorkingSet30sMb -lt $Result.WorkingSet10MinMb) {
            $failures.Add("working set grew monotonically across all samples")
        }
    }

    return $failures.ToArray()
}

function Measure-MatrixRow {
    param(
        [Parameter(Mandatory)][string]$Build,
        [Parameter(Mandatory)][int]$TaskCount
    )

    $listCount = $TaskCount -eq 0 ? 0 : 20
    $slug = "$($Build.ToLowerInvariant())-$TaskCount"
    $databasePath = Join-Path $artifactDirectory "$slug.db"
    $appDirectory = Get-AppDirectory -Build $Build
    Seed-Database `
        -DatabasePath $databasePath `
        -TaskCount $TaskCount `
        -ListCount $listCount

    $coldRun = Start-WintodoRun `
        -AppDirectory $appDirectory `
        -DatabasePath $databasePath `
        -LogName "$slug-cold"
    try {
        $coldFirstFrameMs = $coldRun.FirstFrameMs
        $coldInteractiveMs = $coldRun.InteractiveMs
    }
    finally {
        Stop-WintodoRun -Run $coldRun
    }

    $warmRun = Start-WintodoRun `
        -AppDirectory $appDirectory `
        -DatabasePath $databasePath `
        -LogName "$slug-warm"
    try {
        $warmFirstFrameMs = $warmRun.FirstFrameMs
        $warmInteractiveMs = $warmRun.InteractiveMs
    }
    finally {
        Stop-WintodoRun -Run $warmRun
    }

    $memoryRun = Start-WintodoRun `
        -AppDirectory $appDirectory `
        -DatabasePath $databasePath `
        -LogName "$slug-memory"
    try {
        Wait-UntilRunOffset -Run $memoryRun -Offset ([TimeSpan]::FromSeconds(2))
        $sample2 = Get-MemorySample -AppPid $memoryRun.AppPid
        Wait-UntilRunOffset -Run $memoryRun -Offset ([TimeSpan]::FromSeconds(30))
        $sample30 = Get-MemorySample -AppPid $memoryRun.AppPid
        $sample10Min = $null
        if (-not $SkipTenMinuteSample) {
            Wait-UntilRunOffset -Run $memoryRun -Offset ([TimeSpan]::FromMinutes(10))
            $sample10Min = Get-MemorySample -AppPid $memoryRun.AppPid
        }
    }
    finally {
        Stop-WintodoRun -Run $memoryRun
    }

    $result = [pscustomobject]@{
        Build = $Build
        TaskCount = $TaskCount
        ListCount = $listCount
        ColdFirstFrameMs = [Math]::Round($coldFirstFrameMs, 3)
        ColdInteractiveMs = [Math]::Round($coldInteractiveMs, 3)
        ColdAppWorkMs = [Math]::Round($coldInteractiveMs - $coldFirstFrameMs, 3)
        WarmFirstFrameMs = [Math]::Round($warmFirstFrameMs, 3)
        WarmInteractiveMs = [Math]::Round($warmInteractiveMs, 3)
        WarmAppWorkMs = [Math]::Round($warmInteractiveMs - $warmFirstFrameMs, 3)
        WorkingSet2sMb = $sample2.WorkingSetMb
        Private2sMb = $sample2.PrivateMb
        WorkingSet30sMb = $sample30.WorkingSetMb
        Private30sMb = $sample30.PrivateMb
        WorkingSet10MinMb = $null -eq $sample10Min ? $null : $sample10Min.WorkingSetMb
        Private10MinMb = $null -eq $sample10Min ? $null : $sample10Min.PrivateMb
        BudgetFailures = @()
        BudgetPassed = $false
    }
    $result.BudgetFailures = @(Get-BudgetFailures `
        -Build $Build `
        -TaskCount $TaskCount `
        -Result $result)
    $result.BudgetPassed = $result.BudgetFailures.Count -eq 0
    return $result
}

function Invoke-SustainedWheelScroll {
    param(
        [Parameter(Mandatory)][int]$AppPid,
        [Parameter(Mandatory)][TimeSpan]$Duration
    )

    $process = Get-Process -Id $AppPid
    $bounds = [WintodoPerfInput+Rect]::new()
    if (-not [WintodoPerfInput]::GetWindowRect(
            $process.MainWindowHandle,
            [ref]$bounds)) {
        throw "Could not read Wintodo window bounds."
    }

    $listX = $bounds.Left + [int](($bounds.Right - $bounds.Left) * 0.72)
    $listY = $bounds.Top + [int](($bounds.Bottom - $bounds.Top) * 0.60)
    if (-not [WintodoPerfInput]::SetCursorPos($listX, $listY)) {
        throw "Could not position the pointer over the task list."
    }

    $deadline = [DateTimeOffset]::Now.Add($Duration)
    while ([DateTimeOffset]::Now -lt $deadline) {
        [WintodoPerfInput]::mouse_event(
            0x0800,
            0,
            0,
            -720,
            [UIntPtr]::Zero)
        Start-Sleep -Milliseconds 200
    }
}

function Measure-Scroll {
    param([Parameter(Mandatory)][string]$Build)

    $slug = $Build.ToLowerInvariant()
    $databasePath = Join-Path $artifactDirectory "scroll-$slug-10k.db"
    $resultPath = Join-Path $artifactDirectory "scroll-$slug.json"
    $readyPath = "$resultPath.ready"
    Seed-Database -DatabasePath $databasePath -TaskCount 10000 -ListCount 1
    Remove-Item -LiteralPath $resultPath -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $readyPath -ErrorAction SilentlyContinue

    $run = Start-WintodoRun `
        -AppDirectory (Get-AppDirectory -Build $Build) `
        -DatabasePath $databasePath `
        -LogName "scroll-$slug" `
        -ScrollResultPath $resultPath
    try {
        $readyDeadline = [DateTimeOffset]::Now.AddMinutes(2)
        while (-not (Test-Path -LiteralPath $readyPath) -and
               [DateTimeOffset]::Now -lt $readyDeadline) {
            Start-Sleep -Milliseconds 100
        }
        if (-not (Test-Path -LiteralPath $readyPath)) {
            throw "The 10,000-task view did not finish hydrating."
        }

        Invoke-SustainedWheelScroll `
            -AppPid $run.AppPid `
            -Duration ([TimeSpan]::FromSeconds(9))
        $resultDeadline = [DateTimeOffset]::Now.AddSeconds(15)
        while (-not (Test-Path -LiteralPath $resultPath) -and
               [DateTimeOffset]::Now -lt $resultDeadline) {
            Start-Sleep -Milliseconds 100
        }
        if (-not (Test-Path -LiteralPath $resultPath)) {
            throw "Scroll performance probe did not write $resultPath."
        }

        $scroll = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
        return [pscustomobject]@{
            Build = $Build
            FramesPerSecond = $scroll.framesPerSecond
            DroppedFrames = $scroll.droppedFrames
            MaxFrameMilliseconds = $scroll.maxFrameMilliseconds
            CallbackAllocatedBytes = $scroll.callbackAllocatedBytes
            Passed = $scroll.framesPerSecond -ge 59.5 -and
                $scroll.droppedFrames -eq 0 -and
                $scroll.callbackAllocatedBytes -eq 0
            Error = $null
        }
    }
    finally {
        Stop-WintodoRun -Run $run
    }
}

function Measure-DataAccess {
    $logPath = Join-Path $artifactDirectory "data-access.log"
    $arguments = @(
        "test",
        "--project",
        (Join-Path $repositoryRoot "tests\Wintodo.Data.Tests"),
        "-c", "Release",
        "-p:Platform=x64",
        "--filter-class", "*SmartListPerformanceTests",
        "--output", "Detailed",
        "--show-live-output", "on",
        "--no-build",
        "--no-restore"
    )
    $lines = @(& dotnet @arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $lines | Out-File -LiteralPath $logPath -Encoding utf8
    if ($exitCode -ne 0) {
        throw "Data-access performance tests failed. See $logPath."
    }

    $text = $lines -join [Environment]::NewLine
    $queryMatches = [regex]::Matches(
        $text,
        "(?m)^\s*(?:My Day|Important|Planned|All|Completed):\s*(?<time>[0-9]+[.,][0-9]+)\s*ms\s*$")
    $searchMatch = [regex]::Match(
        $text,
        "FTS search worst warm query:\s*(?<time>[0-9]+[.,][0-9]+)\s*ms")
    if ($queryMatches.Count -ne 5 -or -not $searchMatch.Success) {
        throw "Could not parse data-access timings from $logPath."
    }

    $queryMax = ($queryMatches | ForEach-Object {
        [double]::Parse(
            $_.Groups["time"].Value.Replace(',', '.'),
            $invariantCulture)
    } | Measure-Object -Maximum).Maximum
    return [pscustomobject]@{
        QueryMaxMs = $queryMax
        SearchMs = [double]::Parse(
            $searchMatch.Groups["time"].Value.Replace(',', '.'),
            $invariantCulture)
        QueryPassed = $queryMax -le 5
        SearchPassed = [double]::Parse(
            $searchMatch.Groups["time"].Value.Replace(',', '.'),
            $invariantCulture) -le 60
    }
}

function Format-NullableMeasurement {
    param($Value, [string]$Suffix)

    if ($null -eq $Value) {
        return "skipped"
    }
    return $Value.ToString("F2", $invariantCulture) + $Suffix
}

function Write-PerfRows {
    param(
        [Parameter(Mandatory)][object[]]$Matrix,
        [Parameter(Mandatory)]$DataAccess
    )

    if ($SkipTenMinuteSample) {
        return
    }

    $perfPath = Join-Path $repositoryRoot "docs\PERF.md"
    $content = Get-Content -LiteralPath $perfPath -Raw
    $date = Get-Date -Format "yyyy-MM-dd"
    foreach ($result in $Matrix) {
        $taskText = if ($result.TaskCount -eq 0) {
            "0 / 1 list"
        }
        else {
            $result.TaskCount.ToString("N0", $invariantCulture) +
                " / $($result.ListCount) lists"
        }
        $row = "| $date | $Milestone | $($result.Build) | $taskText | " +
            "$($result.ColdInteractiveMs.ToString('F0', $invariantCulture)) ms | " +
            "$($result.WarmInteractiveMs.ToString('F0', $invariantCulture)) ms | " +
            "$($result.ColdAppWorkMs.ToString('F0', $invariantCulture)) ms | " +
            "$($result.WarmAppWorkMs.ToString('F0', $invariantCulture)) ms | " +
            "$(Format-NullableMeasurement $result.WorkingSet2sMb ' MB') | " +
            "$(Format-NullableMeasurement $result.WorkingSet30sMb ' MB') | " +
            "$(Format-NullableMeasurement $result.WorkingSet10MinMb ' MB') | " +
            "$($DataAccess.QueryMaxMs.ToString('F3', $invariantCulture)) ms | " +
            "$($DataAccess.SearchMs.ToString('F3', $invariantCulture)) ms |"
        # Each measurement is evidence, including a failed run before a fix.
        # Never replace an earlier row for the same milestone and dataset.
        $marker = [regex]::new("(?m)\r?\n\r?\n\*\*Build\*\*")
        $content = $marker.Replace(
            $content,
            "`r`n$row`r`n`r`n**Build**",
            1)
    }

    $content = $content.TrimEnd("`r", "`n") + "`r`n"
    Set-Content -LiteralPath $perfPath -Value $content -Encoding utf8 -NoNewline
}

Initialize-Builds
$dataAccess = Measure-DataAccess
$matrix = [System.Collections.Generic.List[object]]::new()
foreach ($build in $Builds) {
    foreach ($taskCount in $TaskCounts) {
        Write-Host "Measuring $build with $taskCount tasks..."
        $matrix.Add((Measure-MatrixRow -Build $build -TaskCount $taskCount))
    }
}

$scrollResults = [System.Collections.Generic.List[object]]::new()
if (-not $SkipScrollGate) {
    foreach ($build in $Builds) {
        try {
            $scrollResults.Add((Measure-Scroll -Build $build))
        }
        catch {
            $scrollResults.Add([pscustomobject]@{
                Build = $build
                FramesPerSecond = $null
                DroppedFrames = $null
                MaxFrameMilliseconds = $null
                CallbackAllocatedBytes = $null
                Passed = $false
                Error = 'WINTODO_PERF_MEASUREMENT_FAILED'
            })
        }
    }
}

Write-PerfRows -Matrix $matrix.ToArray() -DataAccess $dataAccess
$summary = [pscustomobject]@{
    MeasuredUtc = [DateTimeOffset]::UtcNow
    Milestone = $Milestone
    TenMinuteSampleSkipped = [bool]$SkipTenMinuteSample
    ScrollGateSkipped = [bool]$SkipScrollGate
    BuildSkipped = [bool]$SkipBuild
    DataAccess = $dataAccess
    Matrix = $matrix.ToArray()
    Scroll = $scrollResults.ToArray()
}
$summaryPath = Join-Path $artifactDirectory "summary.json"
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding utf8

$matrix | Select-Object `
    Build,
    TaskCount,
    ColdInteractiveMs,
    WarmInteractiveMs,
    ColdAppWorkMs,
    WarmAppWorkMs,
    WorkingSet2sMb,
    WorkingSet30sMb,
    WorkingSet10MinMb,
    BudgetPassed | Format-Table -AutoSize
if ($scrollResults.Count -gt 0) {
    $scrollResults | Format-Table -AutoSize
}
Write-Host "Summary: $summaryPath"
if ($matrix.Where({ -not $_.BudgetPassed }).Count -gt 0 -or
    -not $dataAccess.QueryPassed -or -not $dataAccess.SearchPassed -or
    $scrollResults.Where({ $_.Build -eq "AOT" -and -not $_.Passed }).Count -gt 0) {
    throw "Performance gate failed. Measurements were preserved in $summaryPath and docs/PERF.md."
}
