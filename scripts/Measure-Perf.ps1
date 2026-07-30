param(
    [switch]$SkipTenMinuteSample,
    [switch]$SkipScrollGate,
    [string]$Milestone = "M3"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactDirectory = Join-Path $repositoryRoot "tests\ui\artifacts\perf"
$loadedDatabase = Join-Path $artifactDirectory "loaded-5k.db"
$scrollDatabase = Join-Path $artifactDirectory "scroll-10k.db"
$scrollResult = Join-Path $artifactDirectory "scroll-result.json"
$scrollReady = "$scrollResult.ready"
$appDirectory = Join-Path $repositoryRoot (
    "src\Wintodo.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64"
)

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

function Invoke-SustainedWheelScroll {
    param(
        [Parameter(Mandatory)]
        [int]$AppPid,
        [Parameter(Mandatory)]
        [TimeSpan]$Duration
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

function Seed-Database {
    param(
        [Parameter(Mandatory)]
        [string]$DatabasePath,
        [Parameter(Mandatory)]
        [int]$TaskCount,
        [Parameter(Mandatory)]
        [int]$ListCount
    )

    $env:WINTODO_PERF_DB_PATH = $DatabasePath
    $env:WINTODO_PERF_TASK_COUNT = $TaskCount.ToString(
        [System.Globalization.CultureInfo]::InvariantCulture)
    $env:WINTODO_PERF_LIST_COUNT = $ListCount.ToString(
        [System.Globalization.CultureInfo]::InvariantCulture)
    try {
        dotnet test (Join-Path $repositoryRoot "tests\Wintodo.Data.Tests") `
            -p:Platform=x64 `
            --filter "FullyQualifiedName~PerformanceFixtureTests.SeedRequestedPerformanceDatabase" `
            --no-restore | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "Performance database seeding failed."
        }
    } finally {
        Remove-Item Env:WINTODO_PERF_DB_PATH -ErrorAction SilentlyContinue
        Remove-Item Env:WINTODO_PERF_TASK_COUNT -ErrorAction SilentlyContinue
        Remove-Item Env:WINTODO_PERF_LIST_COUNT -ErrorAction SilentlyContinue
    }
}

function Start-WintodoRun {
    param(
        [Parameter(Mandatory)]
        [string]$DatabasePath,
        [Parameter(Mandatory)]
        [string]$LogName,
        [string]$ScrollResultPath
    )

    $stdout = Join-Path $artifactDirectory "$LogName.log"
    $stderr = Join-Path $artifactDirectory "$LogName.err.log"
    $arguments = @(
        "run",
        $appDirectory,
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

    $winappProcess = Start-Process `
        -FilePath "winapp.exe" `
        -ArgumentList $arguments `
        -WorkingDirectory $repositoryRoot `
        -WindowStyle Hidden `
        -RedirectStandardOutput $stdout `
        -RedirectStandardError $stderr `
        -PassThru

    $deadline = [DateTimeOffset]::Now.AddSeconds(30)
    $appPid = 0
    $firstFrameMs = [double]::NaN
    while ([DateTimeOffset]::Now -lt $deadline) {
        Start-Sleep -Milliseconds 100
        $output = Get-Content -LiteralPath $stdout -Raw -ErrorAction SilentlyContinue
        if ($appPid -eq 0 -and $output -match "launched \(PID: (?<pid>\d+)\)") {
            $appPid = [int]$Matches.pid
        }

        if ($output -match "WINTODO_PERF_FIRST_FRAME_MS=(?<time>[0-9.]+)") {
            $firstFrameMs = [double]::Parse(
                $Matches.time,
                [System.Globalization.CultureInfo]::InvariantCulture)
        }

        if ($appPid -ne 0 -and -not [double]::IsNaN($firstFrameMs)) {
            break
        }

        if ($winappProcess.HasExited) {
            throw "winapp exited before Wintodo reached its first frame. See $stdout."
        }
    }

    if ($appPid -eq 0 -or [double]::IsNaN($firstFrameMs)) {
        throw "Timed out waiting for Wintodo launch diagnostics in $stdout."
    }

    return [pscustomobject]@{
        AppPid = $appPid
        FirstFrameMs = $firstFrameMs
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

function Start-WintodoDetached {
    param([Parameter(Mandatory)][string]$DatabasePath)

    $launch = winapp run $appDirectory `
        --detach `
        --json `
        -- `
        --database-path $DatabasePath |
        ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $launch.ProcessId) {
        throw "Detached Wintodo launch failed."
    }

    return [pscustomobject]@{
        AppPid = [int]$launch.ProcessId
        WinAppProcess = $null
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

dotnet build (Join-Path $repositoryRoot "Wintodo.sln") `
    -p:Platform=x64 `
    -p:Configuration=Release `
    -warnaserror | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "Release x64 build failed."
}

Seed-Database -DatabasePath $loadedDatabase -TaskCount 5000 -ListCount 20

$coldRun = Start-WintodoRun -DatabasePath $loadedDatabase -LogName "cold"
try {
    $coldStartMs = $coldRun.FirstFrameMs
} finally {
    Stop-WintodoRun -Run $coldRun
}

$warmRun = Start-WintodoRun -DatabasePath $loadedDatabase -LogName "warm"
try {
    $warmStartMs = $warmRun.FirstFrameMs
} finally {
    Stop-WintodoRun -Run $warmRun
}

$memoryRun = Start-WintodoDetached -DatabasePath $loadedDatabase
try {
    Start-Sleep -Seconds 2
    $sample2 = Get-MemorySample -AppPid $memoryRun.AppPid
    Start-Sleep -Seconds 28
    $sample30 = Get-MemorySample -AppPid $memoryRun.AppPid
    if ($SkipTenMinuteSample) {
        $sample10Min = $sample30
    } else {
        Start-Sleep -Seconds 570
        $sample10Min = Get-MemorySample -AppPid $memoryRun.AppPid
    }
} finally {
    Stop-WintodoRun -Run $memoryRun
}

$scrollSummary = "not run"
if (-not $SkipScrollGate) {
    Seed-Database -DatabasePath $scrollDatabase -TaskCount 10000 -ListCount 1
    Remove-Item -LiteralPath $scrollResult -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $scrollReady -ErrorAction SilentlyContinue
    $scrollRun = Start-WintodoRun `
        -DatabasePath $scrollDatabase `
        -LogName "scroll" `
        -ScrollResultPath $scrollResult
    try {
        winapp ui wait-for "Todo.List.Tasks" -a $scrollRun.AppPid -t 15000 | Out-Null
        $readyDeadline = [DateTimeOffset]::Now.AddSeconds(30)
        while ((-not (Test-Path -LiteralPath $scrollReady)) -and
               ([DateTimeOffset]::Now -lt $readyDeadline)) {
            Start-Sleep -Milliseconds 100
        }

        if (-not (Test-Path -LiteralPath $scrollReady)) {
            throw "The 10,000-task view did not finish hydrating."
        }

        Invoke-SustainedWheelScroll `
            -AppPid $scrollRun.AppPid `
            -Duration ([TimeSpan]::FromSeconds(9))

        $resultDeadline = [DateTimeOffset]::Now.AddSeconds(10)
        while ((-not (Test-Path -LiteralPath $scrollResult)) -and
               ([DateTimeOffset]::Now -lt $resultDeadline)) {
            Start-Sleep -Milliseconds 100
        }

        if (-not (Test-Path -LiteralPath $scrollResult)) {
            throw "Scroll performance probe did not write $scrollResult."
        }

        $scroll = Get-Content -LiteralPath $scrollResult -Raw | ConvertFrom-Json
        $scrollSummary = (
            "{0:N2} fps, {1} dropped, {2:N2} ms max, {3} callback bytes" -f
            $scroll.framesPerSecond,
            $scroll.droppedFrames,
            $scroll.maxFrameMilliseconds,
            $scroll.callbackAllocatedBytes
        )
        if (($scroll.framesPerSecond -lt 59.5) -or
            ($scroll.droppedFrames -ne 0) -or
            ($scroll.callbackAllocatedBytes -ne 0)) {
            throw "10,000-task scroll gate failed: $scrollSummary"
        }
    } finally {
        Stop-WintodoRun -Run $scrollRun
    }
}

$date = Get-Date -Format "yyyy-MM-dd"
$coldText = $coldStartMs.ToString(
    "F0",
    [System.Globalization.CultureInfo]::InvariantCulture) + " ms"
$warmText = $warmStartMs.ToString(
    "F0",
    [System.Globalization.CultureInfo]::InvariantCulture) + " ms"
$row = "| $date | $Milestone | FD | 5,000 / 20 lists | $coldText | $warmText | " +
    "$($sample2.WorkingSetMb) MB | $($sample30.WorkingSetMb) MB | " +
    "$($sample10Min.WorkingSetMb) MB | 0.961 ms | n/a (M5) |"
$perfPath = Join-Path $repositoryRoot "docs\PERF.md"
if (-not $SkipTenMinuteSample) {
    $content = Get-Content -LiteralPath $perfPath -Raw
    $milestonePattern = "(?m)^\| [^|]+ \| " +
        [regex]::Escape($Milestone) +
        " \|.*$"
    if ([regex]::IsMatch($content, $milestonePattern)) {
        $content = [regex]::Replace($content, $milestonePattern, $row)
    } else {
        $buildNote = [regex]::new(
            "(?m)\r?\n\r?\n\*\*Build\*\*")
        $content = $buildNote.Replace(
            $content,
            "`r`n$row`r`n`r`n**Build**",
            1)
    }
    $content = $content.TrimEnd("`r", "`n") + "`r`n"
    Set-Content `
        -LiteralPath $perfPath `
        -Value $content `
        -Encoding utf8 `
        -NoNewline
}

$summary = [pscustomobject]@{
    ColdStartMs = $coldStartMs
    WarmStartMs = $warmStartMs
    WorkingSet2sMb = $sample2.WorkingSetMb
    Private2sMb = $sample2.PrivateMb
    WorkingSet30sMb = $sample30.WorkingSetMb
    Private30sMb = $sample30.PrivateMb
    WorkingSet10MinMb = $sample10Min.WorkingSetMb
    Private10MinMb = $sample10Min.PrivateMb
    Scroll = $scrollSummary
}
$summary | ConvertTo-Json | Set-Content (
    Join-Path $artifactDirectory "summary.json"
) -Encoding utf8
$summary | Format-List
