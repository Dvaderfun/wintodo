# WintodoTest only. Capture the selected synthetic task using the M8 gate's
# shipped Windows Desert palette, then restore the guest's appearance.
param(
    [Parameter(Mandatory,ParameterSetName='Row')][int]$AppPid,
    [Parameter(Mandatory)][string]$ArtifactDirectory,
    [Parameter(Mandatory,ParameterSetName='Row')][ValidatePattern('^Todo\.Row\.[a-fA-F0-9-]+$')][string]$SelectedRowId,
    [Parameter(Mandatory,ParameterSetName='Startup')][scriptblock]$StartupCapture
)
$ErrorActionPreference = 'Stop'
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1' -or $PSScriptRoot -ine 'C:\wintodo\tests\ui' -or
    (Get-ItemPropertyValue 'HKLM:\SOFTWARE\Microsoft\Virtual Machine\Guest\Parameters' VirtualMachineName) -cne 'WintodoTest') {
    throw 'Contrast capture is restricted to the WintodoTest guest.'
}
$pkg = Get-AppxPackage -Name Wintodo.Hybrid.Dev
if ($pkg.PackageFamilyName -cne 'Wintodo.Hybrid.Dev_1z32rh13vfry6' -or $pkg.IsDevelopmentMode -or $pkg.Status -ne 'Ok') {
    throw 'Expected the installed signed DEV preview.'
}
if ($StartupCapture) {
    $area='startup-placement'
    if (Get-Process -Name Wintodo -ErrorAction SilentlyContinue) { throw 'A fresh signed DEV launch is required.' }
} else {
    $area='dev-preview'
    $app = Get-Process -Id $AppPid
    if ($app.Path -ine (Join-Path $pkg.InstallLocation Wintodo.exe) -or $app.MainWindowHandle -eq [IntPtr]::Zero) { throw 'Expected the running signed DEV preview.' }
    $hwnd = $app.MainWindowHandle
}
$outDir = (Resolve-Path -LiteralPath $ArtifactDirectory).ProviderPath
if (!$outDir.StartsWith("C:\wintodo\tests\ui\artifacts\$area\", [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Expected the guest DEV gate artifact directory.'
}
if (!('PreviewContrastNative' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class PreviewContrastNative {
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct HC { public uint size, flags; public IntPtr scheme; }
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool SystemParametersInfo(uint action, uint size, ref HC value, uint flags);
    [DllImport("user32.dll")] public static extern uint GetSysColor(int index);
    [DllImport("user32.dll")] public static extern bool SetSysColors(int count, int[] indices, uint[] colors);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("shlwapi.dll", CharSet=CharSet.Unicode)] public static extern int SHLoadIndirectString(string src, StringBuilder output, uint size, IntPtr reserved);
}
'@
}
function Read-Contrast {
    $hc = New-Object 'PreviewContrastNative+HC'
    $hc.size = [Runtime.InteropServices.Marshal]::SizeOf([type]'PreviewContrastNative+HC')
    if (![PreviewContrastNative]::SystemParametersInfo(0x42,$hc.size,[ref]$hc,0)) { throw 'Cannot read guest contrast flags.' }
    @{ flags=$hc.flags; scheme=[Runtime.InteropServices.Marshal]::PtrToStringUni($hc.scheme) }
}
function Set-Contrast([uint32]$flags, [string]$scheme) {
    $hc = New-Object 'PreviewContrastNative+HC'
    $hc.size = [Runtime.InteropServices.Marshal]::SizeOf([type]'PreviewContrastNative+HC')
    $hc.flags = $flags
    if ($scheme) { $hc.scheme = [Runtime.InteropServices.Marshal]::StringToHGlobalUni($scheme) }
    try {
        if (![PreviewContrastNative]::SystemParametersInfo(0x43,$hc.size,[ref]$hc,3)) { throw 'Cannot set guest contrast flags.' }
    } finally { [Runtime.InteropServices.Marshal]::FreeHGlobal($hc.scheme) }
    Start-Sleep -Seconds 3
}
function Read-Marker([string]$path, [double]$scale, [uint32]$ink, [uint32]$face) {
    Add-Type -AssemblyName System.Drawing
    $bmp = [Drawing.Bitmap]::new($path)
    try {
        # The 3×16 DIP pill sits 4 DIP inside the card. Inspect its middle,
        # excluding the frame and completion button. Require the rendered
        # foreground AND an adjacent card-colour pixel on the same scan line.
        $mid = [int][Math]::Floor($bmp.Height / 2)
        $body = $bmp.GetPixel([int][Math]::Round(12*$scale),$mid)
        $bodyRgb = [int]$body.R -bor ([int]$body.G -shl 8) -bor ([int]$body.B -shl 16)
        $best = 0
        for ($x=[int][Math]::Ceiling(2*$scale); $x -le [Math]::Ceiling(10*$scale); $x++) {
            $run = 0
            for ($y=[Math]::Max(1,$mid-[int](12*$scale)); $y -le [Math]::Min($bmp.Height-2,$mid+[int](12*$scale)); $y++) {
                $p=$bmp.GetPixel($x,$y)
                if (([int]$p.R -bor ([int]$p.G -shl 8) -bor ([int]$p.B -shl 16)) -eq $ink) {
                    $run++; $best=[Math]::Max($run,$best)
                } else { $run=0 }
            }
        }
        if ($ink -eq $face -or $bodyRgb -ne $face -or $best -lt [Math]::Ceiling(8*$scale)) {
            throw "Selected card has no verified contrasting pill: foregroundRun=$best, adjacentCard=$bodyRgb, expected=$face."
        }
        return $best
    } finally { $bmp.Dispose() }
}
$reg = 'HKCU:\Control Panel\Accessibility\HighContrast'
$pref = Get-ItemPropertyValue -LiteralPath $reg -Name 'High Contrast Scheme'
$old = Read-Contrast
$ids = [int[]](0..30)
$colors = [uint32[]]@($ids|ForEach-Object { [PreviewContrastNative]::GetSysColor($_) })
$theme = Get-Content -LiteralPath (Join-Path $env:SystemRoot 'Resources/Ease of Access Themes/hcwhite.theme') -Raw
$names = 'Scrollbar','Background','ActiveTitle','InactiveTitle','Menu','Window','WindowFrame','MenuText','WindowText','TitleText','ActiveBorder','InactiveBorder','AppWorkspace','Hilight','HilightText','ButtonFace','ButtonShadow','GrayText','ButtonText','InactiveTitleText','ButtonHilight','ButtonDkShadow','ButtonLight','InfoText','InfoWindow','','HotTrackingColor','GradientActiveTitle','GradientInactiveTitle','MenuHilight','MenuBar'
$inds = [Collections.Generic.List[int]]::new(); $vals = [Collections.Generic.List[uint32]]::new()
foreach ($i in $ids) {
    if ($names[$i] -and $theme -match ('(?m)^'+$names[$i]+'=(\d+) (\d+) (\d+)\r?$')) {
        $inds.Add($i); $vals.Add([int]$Matches[1] -bor ([int]$Matches[2] -shl 8) -bor ([int]$Matches[3] -shl 16))
    }
}
$name = [Text.StringBuilder]::new(256)
if ([PreviewContrastNative]::SHLoadIndirectString('@themeui.dll,-2104',$name,256,[IntPtr]::Zero) -ne 0 -or
    !$inds.Contains(15) -or !$inds.Contains(18)) { throw 'Guest Desert palette is unavailable.' }
$details = [ordered]@{theme='Desert';active=$false;restored=$false}
try {
    Set-Contrast ($old.flags -bor 1) $name.ToString()
    if (![PreviewContrastNative]::SetSysColors($inds.Count,$inds.ToArray(),$vals.ToArray())) { throw 'Cannot apply shipped contrast colours.' }
    Start-Sleep -Milliseconds 750
    $active = Read-Contrast
    $details.active = [bool]($active.flags -band 1)
    $details.observedScheme = $active.scheme
    $details.expectedScheme = $name.ToString()
    $details.systemColorsCOLORREF = [ordered]@{}
    foreach ($i in 5,8,13,14,15,18) {
        $details.systemColorsCOLORREF[$names[$i]] = [PreviewContrastNative]::GetSysColor($i)
        if (!$inds.Contains($i) -or [PreviewContrastNative]::GetSysColor($i) -ne $vals[$inds.IndexOf($i)]) { throw "Guest system colour $i did not match Desert." }
    }
    if (!$details.active) { throw 'Windows High Contrast is not active.' }
    if ($StartupCapture) {
        & $StartupCapture
        return # finally always restores and verifies the original guest palette.
    }
    $png = Join-Path $outDir '05-hc-selected.png'; $crop = Join-Path $outDir '05-hc-row.png'
    & winapp ui screenshot -w $hwnd.ToInt64() -o $png | Out-Null
    if ($LASTEXITCODE -or (Get-Item -LiteralPath $png).Length -lt 1000) { throw 'Cannot capture contrast window.' }
    & winapp ui screenshot $SelectedRowId -w $hwnd.ToInt64() -o $crop | Out-Null
    if ($LASTEXITCODE -or !(Test-Path -LiteralPath $crop)) { throw 'Cannot capture selected row.' }
    $details.dpi = [PreviewContrastNative]::GetDpiForWindow($hwnd)
    $details.markerRunPixels = Read-Marker $crop ($details.dpi/96.0) ([PreviewContrastNative]::GetSysColor(18)) ([PreviewContrastNative]::GetSysColor(15))
} finally {
    Set-Contrast $old.flags $old.scheme
    if (!$old.scheme) { Set-ItemProperty -LiteralPath $reg -Name 'High Contrast Scheme' -Value $pref }
    if (![PreviewContrastNative]::SetSysColors(31,$ids,$colors)) { throw 'Could not restore guest system colours.' }
    $after = Read-Contrast
    # SPI caches a scheme name after disabling a previously empty, inactive preference (M8).
    $details.restored = $old.flags -eq $after.flags -and ((!$old.scheme -and !($old.flags -band 1)) -or $old.scheme -ceq $after.scheme) -and
        $pref -ceq (Get-ItemPropertyValue -LiteralPath $reg -Name 'High Contrast Scheme') -and
        @($ids | Where-Object { [PreviewContrastNative]::GetSysColor($_) -ne $colors[$_] }).Count -eq 0
    $details | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outDir 'contrast-smoke.json')
    if (!$details.restored) { throw 'Guest contrast settings were not restored exactly.' }
}
