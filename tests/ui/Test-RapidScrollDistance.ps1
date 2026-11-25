# Dot-sourced by the guest gate after recording. No changes to Windows wheel preferences.
function Test-RapidScrollDistance([int]$TargetPid,$Report) {
    $v=Wait-VisibleTitles 'distance-current'
    $row='Todo.Row.d0000000-0000-4000-8000-{0:D12}' -f [int]$v.titles[0].name.Substring(0,4)
    Ui @('focus',$row) | Out-Null
    Ui @('wait-for',$row,'-p','HasKeyboardFocus','--value','True','-t','5000') | Out-Null
    Ui @('send-keys','home','--via','send-input') | Out-Null
    $until=[DateTime]::UtcNow.AddSeconds(10)
    do {
        $v=Wait-VisibleTitles 'distance-start'
        $rows=@($v.list.children | Where-Object type -eq 'ListItem')
        $first=@($rows | Where-Object automationId -eq 'Todo.Row.d0000000-0000-4000-8000-000000000001')
        $atTop=$v.titles[0].name -ceq '0001 Scroll sample' -and $first.Count -eq 1 -and
            [Math]::Abs($first[0].y-$v.list.y) -le 1 -and $first[0].height -ge ($rows | Measure-Object height -Maximum).Maximum-1
        if (!$atTop) { Start-Sleep -Milliseconds 120 }
    } while (!$atTop -and [DateTime]::UtcNow -lt $until)
    if (!$atTop) { throw 'Home did not restore the complete first fixture row.' }
    $scale=$v.scale; $height=$v.list.height; $titleHeight=($v.titles | Measure-Object height -Maximum).Maximum
    $full=@($v.titles | Where-Object height -ge ($titleHeight-1))
    if ($full.Count -lt 3) { throw 'Not enough complete titles to measure row pitch.' }
    $pitch=($full[-1].y-$full[0].y)/([int]$full[-1].name.Substring(0,4)-[int]$full[0].name.Substring(0,4))
    function Position($view) {
        if ($view.list.height -ne $height -or $view.scale -ne $scale) { throw 'The viewport changed during the wheel-distance test.' }
        # Include the partial-row phase, even when UIA clips the topmost title.
        $offsets=@(foreach ($t in $view.titles) {
            if ($t.height -ge $titleHeight-1) { ([int]$t.name.Substring(0,4)-1)*$pitch-($t.y-$view.list.y) }
        })
        $range=$offsets | Measure-Object -Minimum -Maximum
        if ($offsets.Count -lt 3 -or $range.Maximum-$range.Minimum -gt 2) { throw 'Title indices disagree with the measured row pitch.' }
        ($range.Minimum+$range.Maximum)/2
    }
    $lines=[RapidScrollWheel]::GetSystemWheelScrollLines()
    if (!$lines) { throw 'Windows is configured not to scroll the wheel.' }
    $viewport=$height/$scale
    $unit=if ($lines -eq [uint32]::MaxValue) { $height } else { $scale*[Math]::Min($viewport,[Math]::Max(48,[Math]::Round(0.15*$viewport))*$lines/3) }
    if (32*$unit+$height -ge 999*$pitch) { throw 'This wheel setting would reach the end of the fixture.' }
    $Report.wheelDistanceLegs=@(); $Report.systemWheelLines=[long]$lines; $Report.distancePitchPx=$pitch
    $x=[int]($v.list.x+$v.list.width/2); $y=[int]($v.list.y+$height/2); $previous=Position $v
    foreach ($leg in @(@(-8,4),@(8,2),@(-1,1))) {
        $notches,$packets=$leg
        if ([RapidScrollWheel]::SendBurst($TargetPid,$x,$y,$notches,$packets) -ne $packets) { throw 'Incomplete wheel burst.' }
        $watch=[Diagnostics.Stopwatch]::StartNew(); $prior=$previous; $delta=$null; $quietSince=0; $settled=$false
        do {
            try { $view=VisibleTitles "distance-$notches" }
            catch { $quietSince=$watch.ElapsedMilliseconds; Start-Sleep -Milliseconds 120; continue }
            $position=Position $view; $delta=$position-$previous
            if ([Math]::Abs($position-$prior) -gt 1) { $quietSince=$watch.ElapsedMilliseconds }
            $prior=$position
            $settled=$delta*[Math]::Sign(-$notches) -gt 1 -and $watch.ElapsedMilliseconds-$quietSince -ge 350
            if (!$settled) { Start-Sleep -Milliseconds 120 }
        } while (!$settled -and $watch.ElapsedMilliseconds -lt 15000)
        $expected=if ([Math]::Abs($notches) -gt 1) { -$notches*$packets*$unit } else { $null }
        $tolerance=[Math]::Min(0.45*$pitch,(2+[Math]::Abs($notches)*$packets)*$scale)
        # Native single notches have their own distance curve. A normal step at the
        # default (or lower) line count must not jump more than an entire viewport.
        $nativeLimit=if ($null -eq $expected -and ($lines -le 3 -or $lines -eq [uint32]::MaxValue)) { $height+$pitch } else { $null }
        $Report.wheelDistanceLegs+=@(@{notches=$notches;packets=$packets;expectedPx=$expected;actualPx=$delta;tolerancePx=$tolerance;nativeLimitPx=$nativeLimit})
        if (!$settled -or $view.titles[0].name -ceq '0001 Scroll sample' -or $view.titles[-1].name -ceq '1000 Scroll sample' -or
            ($null -ne $nativeLimit -and [Math]::Abs($delta) -gt $nativeLimit) -or
            ($null -ne $expected -and [Math]::Abs($expected-$delta) -gt $tolerance)) { throw "Wheel $packets x $notches missed its expected interior position." }
        $previous=$position
    }
    $Report.wheelDistance='PASS'
}
