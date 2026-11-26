# Inspect the composited pixels of a checked task, using its UIA bounds so DPI
# and the theme's actual accent can vary between guest machines.
function Row-Luminance([Drawing.Color]$Color) {
    $rgb = foreach ($component in $Color.R,$Color.G,$Color.B) {
        $v = $component/255.0
        if ($v -le 0.04045) { $v/12.92 } else { [Math]::Pow(($v+0.055)/1.055,2.4) }
    }
    0.2126*$rgb[0] + 0.7152*$rgb[1] + 0.0722*$rgb[2]
}
function Row-Contrast([Drawing.Color]$A,[Drawing.Color]$B) {
    $l1=Row-Luminance $A; $l2=Row-Luminance $B
    ([Math]::Max($l1,$l2)+0.05)/([Math]::Min($l1,$l2)+0.05)
}
function Read-CompletedRowPalette([string]$Uid,[string]$Label) {
    $rowId="Todo.Row.$Uid"; $toggleId="Todo.Row.CompleteToggle.$Uid"
    $row = (Ui @('search',$rowId,'--json') | ConvertFrom-Json).matches | Where-Object automationId -eq $rowId | Select-Object -First 1
    $toggle = (Ui @('search',$toggleId,'--json') | ConvertFrom-Json).matches | Where-Object automationId -eq $toggleId | Select-Object -First 1
    if (!$row -or !$toggle -or $row.isOffscreen -or $toggle.isOffscreen) { throw "No visible checked fixture row for $Label." }
    $path = Join-Path $artifacts "row-$Label.png"
    Ui @('screenshot',$rowId,'-o',$path) | Out-Null
    $bmp = [Drawing.Bitmap]::new($path)
    try {
        if ([Math]::Abs($row.width-$bmp.Width) -gt 2 -or [Math]::Abs($row.height-$bmp.Height) -gt 2) { throw "Row and screenshot bounds differ for $Label." }
        $cx = $toggle.x-$row.x+$toggle.width/2; $cy = $toggle.y-$row.y+$toggle.height/2
        $radius = 8*$scale
        $inside = @(for ($x=[int]($cx-$radius); $x -le $cx+$radius; $x++) {
            for ($y=[int]($cy-$radius); $y -le $cy+$radius; $y++) {
                if (($x-$cx)*($x-$cx)+($y-$cy)*($y-$cy) -le $radius*$radius) { $bmp.GetPixel($x,$y).ToArgb() }
            }
        })
        $mode = $inside | Group-Object | Sort-Object Count -Descending | Select-Object -First 1
        if ($mode.Count -lt 30) { throw "No solid completion circle in $Label." }
        $fill = [Drawing.Color]::FromArgb([int]$mode.Name)
        $dark = $Label.EndsWith('Dark')
        # Pick the fourth most contrasting ink pixel so a stray pixel cannot impersonate a check.
        $inks = @($inside | ForEach-Object { [Drawing.Color]::FromArgb($_) } | Sort-Object { Row-Luminance $_ } -Descending:(!$dark))
        $check = $inks[3]
        $surface = $bmp.GetPixel([int]($bmp.Width-50*$scale),[int]$cy)
        $titleInks = @(for ($x=[int]($cx+25*$scale); $x -lt [Math]::Min($bmp.Width-50*$scale,$cx+155*$scale); $x++) {
            for ($y=[int][Math]::Max(0,$cy-19*$scale); $y -lt $cy; $y++) { $bmp.GetPixel($x,$y).ToArgb() }
        })
        # Use repeatedly painted ink from the rendered title + strikethrough (ignore subpixel fringes).
        $titles = @($titleInks | Group-Object | Where-Object Count -ge 8 | ForEach-Object { [Drawing.Color]::FromArgb([int]$_.Name) } | Sort-Object { Row-Luminance $_ } -Descending:$dark)
        if (!$titles.Count) { throw "No repeatable title ink in $Label." }
        $title = $titles[0]
        [ordered]@{fill=$fill.Name; check=$check.Name; title=$title.Name; surface=$surface.Name;
            checkContrast=[Math]::Round((Row-Contrast $fill $check),2); fillContrast=[Math]::Round((Row-Contrast $fill $surface),2);
            titleContrast=[Math]::Round((Row-Contrast $title $surface),2)}
    } finally { $bmp.Dispose() }
}
function Assert-CompletedRowPalettes($Rows) {
    foreach ($name in $Rows.Keys) {
        $sample=$Rows[$name]
        if ($sample.checkContrast -lt 3 -or $sample.fillContrast -lt 3 -or $sample.titleContrast -lt 3) {
            throw "Completed row loses contrast in ${name}: $($sample | ConvertTo-Json -Compress)"
        }
    }
    foreach ($part in 'fill','check','title') {
        $cold = [Drawing.ColorTranslator]::FromHtml('#'+$Rows.ColdLight[$part].Substring(2))
        $switched = [Drawing.ColorTranslator]::FromHtml('#'+$Rows.SwitchedLight[$part].Substring(2))
        $dark = [Drawing.ColorTranslator]::FromHtml('#'+$Rows.SwitchedDark[$part].Substring(2))
        if ((Color-Distance $cold $switched) -gt 45 -or (Color-Distance $dark $switched) -lt 65) {
            throw "Completed row $part has incorrect cold/switch palettes: $cold / $switched / $dark."
        }
    }
}
