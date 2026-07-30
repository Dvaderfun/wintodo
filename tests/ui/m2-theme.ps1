param(
    [Parameter(Mandatory)]
    [int]$AppPid
)

$ErrorActionPreference = "Stop"
$artifactDirectory = Join-Path $PSScriptRoot "artifacts\m2"
$themeRegistryPath =
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"

New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null

Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class ThemeTestNative
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct HIGHCONTRAST
    {
        public uint cbSize;
        public uint dwFlags;
        public IntPtr lpszDefaultScheme;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint message,
        UIntPtr wParam,
        string lParam,
        uint flags,
        uint timeout,
        out UIntPtr result);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SystemParametersInfo(
        uint action,
        uint parameter,
        ref HIGHCONTRAST value,
        uint flags);
}
"@

$spiGetHighContrast = 0x0042
$spiSetHighContrast = 0x0043
$highContrastOn = 0x0001
$sendChange = 0x0003
$highContrastSize = [Runtime.InteropServices.Marshal]::SizeOf(
    [type]"ThemeTestNative+HIGHCONTRAST")

function Get-HighContrast {
    $state = New-Object ThemeTestNative+HIGHCONTRAST
    $state.cbSize = $highContrastSize
    if (-not [ThemeTestNative]::SystemParametersInfo(
        $spiGetHighContrast,
        $highContrastSize,
        [ref]$state,
        0)) {
        throw "Could not read the High Contrast state."
    }

    $scheme = if ($state.lpszDefaultScheme -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::PtrToStringUni(
            $state.lpszDefaultScheme)
    } else {
        ""
    }

    return @{
        Flags = $state.dwFlags
        Scheme = $scheme
    }
}

function Set-HighContrast {
    param(
        [uint32]$Flags,
        [string]$Scheme
    )

    $schemePointer = if ($Scheme) {
        [Runtime.InteropServices.Marshal]::StringToHGlobalUni($Scheme)
    } else {
        [IntPtr]::Zero
    }

    try {
        $state = New-Object ThemeTestNative+HIGHCONTRAST
        $state.cbSize = $highContrastSize
        $state.dwFlags = $Flags
        $state.lpszDefaultScheme = $schemePointer
        if (-not [ThemeTestNative]::SystemParametersInfo(
            $spiSetHighContrast,
            $highContrastSize,
            [ref]$state,
            $sendChange)) {
            throw "Could not set the High Contrast state."
        }
    } finally {
        if ($schemePointer -ne [IntPtr]::Zero) {
            [Runtime.InteropServices.Marshal]::FreeHGlobal($schemePointer)
        }
    }

    Start-Sleep -Seconds 3
}

function Set-AppTheme {
    param([int]$UseLightTheme)

    Set-ItemProperty `
        -LiteralPath $themeRegistryPath `
        -Name AppsUseLightTheme `
        -Value $UseLightTheme
    $result = [UIntPtr]::Zero
    [void][ThemeTestNative]::SendMessageTimeout(
        [IntPtr]0xffff,
        0x001a,
        [UIntPtr]::Zero,
        "ImmersiveColorSet",
        0x0002,
        5000,
        [ref]$result)
    Start-Sleep -Seconds 2
}

function Test-Theme {
    param(
        [string]$Name,
        [string]$ScreenshotName
    )

    winapp ui wait-for "Todo.TitleBar.SearchBox" -a $AppPid -t 3000
    winapp ui wait-for "Todo.Nav.Important" -a $AppPid -t 3000
    winapp ui focus "Todo.Nav.Important" -a $AppPid
    winapp ui get-focused -a $AppPid --json | Out-Null
    winapp ui screenshot `
        -a $AppPid `
        -o (Join-Path $artifactDirectory $ScreenshotName) | Out-Null
    Write-Host "PASS: $Name"
}

$originalTheme = (
    Get-ItemProperty -LiteralPath $themeRegistryPath
).AppsUseLightTheme
$originalHighContrast = Get-HighContrast
$highContrastScheme = if ($originalHighContrast.Scheme) {
    $originalHighContrast.Scheme
} else {
    "High Contrast Black"
}

try {
    Set-HighContrast `
        -Flags ($originalHighContrast.Flags -band (-bnot $highContrastOn)) `
        -Scheme $originalHighContrast.Scheme
    Set-AppTheme -UseLightTheme 1
    Test-Theme -Name "Light" -ScreenshotName "05-light.png"

    Set-AppTheme -UseLightTheme 0
    Test-Theme -Name "Dark" -ScreenshotName "06-dark.png"

    Set-HighContrast `
        -Flags ($originalHighContrast.Flags -bor $highContrastOn) `
        -Scheme $highContrastScheme
    Test-Theme `
        -Name "High Contrast" `
        -ScreenshotName "07-high-contrast.png"
} finally {
    Set-HighContrast `
        -Flags $originalHighContrast.Flags `
        -Scheme $originalHighContrast.Scheme
    Set-AppTheme -UseLightTheme $originalTheme
}

$restoredHighContrast = Get-HighContrast
if ($restoredHighContrast.Flags -ne $originalHighContrast.Flags) {
    throw "The original High Contrast state was not restored."
}

Write-Host "Theme gate passed; original OS appearance restored."
