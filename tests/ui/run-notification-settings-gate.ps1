$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$appDirectory = Join-Path $repositoryRoot 'src/Wintodo.App/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64'
$artifactDirectory = Join-Path $PSScriptRoot ('artifacts/notification-settings/run-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null
$databasePath = Join-Path $artifactDirectory 'm7-gate.db'
$statePath = Join-Path $artifactDirectory 'notification-state.xml'
$previousWorkflowId = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = [guid]::NewGuid().ToString('N')
$appPid = 0
$mainHwnd = ''
$results = [Collections.Generic.List[object]]::new()
$screenshots = [Collections.Generic.List[string]]::new()
function Invoke-Checked {
    param([string[]]$Arguments)
    $output = @(& winapp @Arguments)
    if ($LASTEXITCODE -ne 0) { throw "winapp $($Arguments -join ' ') failed: $output" }
    $output
}
function Invoke-Ui {
    param([string[]]$Arguments)
    $selector = if ($mainHwnd -and $Arguments[0] -notin @('list-windows','inspect') -and
        ($Arguments.Count -lt 2 -or $Arguments[1] -notmatch 'Snooze10|Snooze60|SnoozeTomorrow')) {
        @('-w',$mainHwnd)
    } else { @('-a',"$appPid") }
    Invoke-Checked (@('ui') + $Arguments + @('--on','local') + $selector)
}
function Start-App {
    $launch = Invoke-Checked @('run',$appDirectory,'--detach','--json','--','--database-path',$databasePath) | ConvertFrom-Json
    if (-not $launch.ProcessId) { throw 'No local app PID.' }
    $script:appPid = [int]$launch.ProcessId
    $script:mainHwnd = ''
    Invoke-Ui @('wait-for','Todo.Shell.PageTitle','-t','10000') | Out-Null
    $script:mainHwnd = [string]((Invoke-Ui @('list-windows','--json') | ConvertFrom-Json |
        Where-Object title -eq 'Wintodo' | Select-Object -First 1).hwnd)
    if (-not $mainHwnd) { throw 'No window for validation.' }
}
function Test-UI {
    param([string]$Name,[scriptblock]$Script)
    try { & $Script | Out-Null; $results.Add(@{ name=$Name; status='PASS' }) }
    catch { $results.Add(@{ name=$Name; status='FAIL'; detail="$_" }) }
}
function Save-Screenshot {
    param([string]$Name)
    $path = Join-Path $artifactDirectory $Name
    Invoke-Checked @('ui','screenshot','-w',$mainHwnd,'--output',$path) | Out-Null
    if (-not (Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).Length -eq 0) { throw 'Screenshot missing.' }
    $screenshots.Add($path)
}
function Open-Settings {
    Invoke-Ui @('invoke','Todo.Nav.Settings') | Out-Null
    Invoke-Ui @('wait-for','Todo.Settings.Notifications.Enabled','-t','5000') | Out-Null
    Invoke-Ui @('scroll','Todo.Settings.ScrollViewer','--wheel','30') | Out-Null
    Scroll-To 'Todo.Settings.Notifications.Enabled'

}
function Scroll-To {
    param([string]$Id)
    for ($step = 0; $step -lt 20; $step++) {
        $tree = Invoke-Ui @('inspect',$Id,'--json','--depth','1') | ConvertFrom-Json
        $element = @($tree.windows | ForEach-Object { $_.elements }) | Select-Object -First 1
        if ($element -and -not $element.isOffscreen -and $element.width -gt 0) { return }
        Invoke-Ui @('scroll','Todo.Settings.ScrollViewer','--wheel','-2') | Out-Null
    }
    throw "Could not scroll to $Id"
}
function Assert-Visible {
    param([string]$Id)
    $tree = Invoke-Ui @('inspect',$Id,'--json','--depth','1') | ConvertFrom-Json
    $element = @($tree.windows | ForEach-Object { $_.elements }) | Select-Object -First 1
    if (-not $element -or $element.isOffscreen -or $element.width -le 0) { throw "$Id is not visible." }
}
function Assert-Schedule {
    param([int]$Count,[string]$Snooze)
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        if (Test-Path -LiteralPath $statePath) {
            try {
                [xml]$state = Get-Content -LiteralPath $statePath -Raw
                $items = @($state.SelectNodes('/scheduled/notification'))
                $defaults = @($state.SelectNodes("//input[@id='snooze']/@defaultInput") | ForEach-Object Value)
                if ($items.Count -eq $Count -and (-not $Snooze -or ($defaults.Count -eq $Count -and
                    @($defaults | Where-Object { $_ -ne $Snooze }).Count -eq 0))) { return }
            } catch { }
        }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Scheduled state did not reach count=$Count, snooze=$Snooze. $(Get-Content -LiteralPath $statePath -Raw -ErrorAction SilentlyContinue)"
}
function Get-Nodes {
    param([object[]]$Nodes)
    foreach ($node in $Nodes) { $node; if ($node.children) { Get-Nodes @($node.children) } }
}
try {
    $env:WINTODO_NOTIFICATION_SETTINGS_GATE_DB_PATH = $databasePath
    try {
        dotnet test --project (Join-Path $repositoryRoot 'tests/Wintodo.Data.Tests') -p:Platform=x64 --no-build --filter-method '*NotificationSettingsGateTests.SeedRequestedDatabase'
        if ($LASTEXITCODE -ne 0) { throw 'Seed failed.' }
    } finally { Remove-Item Env:WINTODO_NOTIFICATION_SETTINGS_GATE_DB_PATH -ErrorAction SilentlyContinue }
    Start-App
    Test-UI 'Default reminders schedule with ten-minute snooze' {
        Assert-Schedule 1 '10'
        Open-Settings
        Invoke-Ui @('wait-for','Todo.Settings.Notifications.Enabled','--value','On','-t','3000')
        Invoke-Ui @('wait-for','Todo.Settings.Notifications.DefaultSnooze','--value','10 minutes','-t','3000')
        Save-Screenshot '01-notifications-default.png'
    }
    Test-UI 'Disable cancels and enable restores scheduled toast' {
        Invoke-Ui @('click','Todo.Settings.Notifications.Enabled')
        Invoke-Ui @('wait-for','Todo.Settings.Notifications.Enabled','--value','Off','-t','3000')
        Assert-Schedule 0
        Copy-Item -LiteralPath $statePath -Destination (Join-Path $artifactDirectory 'disabled.xml')
        Invoke-Ui @('click','Todo.Settings.Notifications.Enabled')
        Invoke-Ui @('wait-for','Todo.Settings.Notifications.Enabled','--value','On','-t','3000')
        Assert-Schedule 1 '10'
    }
    Test-UI 'Snooze choices update actual pending toast payload' {
        Invoke-Ui @('invoke','Todo.Settings.Notifications.DefaultSnooze')
        Invoke-Ui @('invoke','Todo.Settings.Notifications.Snooze60')
        Assert-Schedule 1 '60'
        Copy-Item -LiteralPath $statePath -Destination (Join-Path $artifactDirectory 'snooze60.xml')
        Invoke-Ui @('invoke','Todo.Settings.Notifications.DefaultSnooze')
        Invoke-Ui @('invoke','Todo.Settings.Notifications.SnoozeTomorrow')
        Assert-Schedule 1 'tomorrow'
        Save-Screenshot '02-notifications-changed.png'
    }
    Test-UI 'Notification settings persist across restart' {
        Invoke-Ui @('click','Todo.Settings.Notifications.Enabled')
        Invoke-Ui @('wait-for','Todo.Settings.Notifications.Enabled','--value','Off','-t','3000')
        Assert-Schedule 0
        Start-Sleep -Milliseconds 400
        Stop-Process -Id $appPid
        $script:appPid = 0
        Start-App
        Open-Settings
        Invoke-Ui @('wait-for','Todo.Settings.Notifications.Enabled','--value','Off','-t','3000')
        Invoke-Ui @('wait-for','Todo.Settings.Notifications.DefaultSnooze','--value','Tomorrow','-t','3000')
        Assert-Schedule 0
        Invoke-Ui @('click','Todo.Settings.Notifications.Enabled')
        Invoke-Ui @('wait-for','Todo.Settings.Notifications.Enabled','--value','On','-t','3000')
        Assert-Schedule 1 'tomorrow'
        Copy-Item -LiteralPath $statePath -Destination (Join-Path $artifactDirectory 'restored.xml')
    }
    Test-UI 'Data and About expose database information and bundled licences' {
        Invoke-Ui @('scroll','Todo.Settings.ScrollViewer','--wheel','-30')
        Invoke-Ui @('wait-for','Todo.Settings.Data.ShowInExplorer','-t','3000')
        Invoke-Ui @('wait-for','Todo.Settings.Data.DatabasePath','--value',$databasePath,'-t','3000')
        Invoke-Ui @('wait-for','Todo.Settings.Data.DatabaseSize','--value','KB','--contains','-t','3000')
        Invoke-Ui @('wait-for','Todo.Settings.About.Repository','-t','3000')
        Save-Screenshot '03-data-about.png'
        Invoke-Ui @('click','Todo.Settings.About.Licences.Toggle')
        Invoke-Ui @('scroll','Todo.Settings.ScrollViewer','--wheel','-5')
        Invoke-Ui @('wait-for','Todo.Settings.About.LicenceList','--value','CommunityToolkit.Mvvm 8.4.2','--contains','-t','3000')
        Scroll-To 'Todo.Settings.About.LicenceList'
        Assert-Visible 'Todo.Settings.About.LicenceList'
        Save-Screenshot '04-licences.png'
        Invoke-Ui @('click','Todo.Settings.About.Licences.Toggle')
    }
    Test-UI 'Authored settings controls pass accessibility audit' {
        $inspection = Invoke-Ui @('inspect','--interactive','--json','--depth','16') | ConvertFrom-Json
        $all = @(Get-Nodes @($inspection.windows | ForEach-Object { $_.elements }))
        if (-not $all.Count) { throw 'No elements audited.' }
        $controls = @($all | Where-Object {
            $_.type -match 'Button|CheckBox|Edit|ListItem|MenuItem|ComboBox|Spinner|Hyperlink' -and
            $_.name -notmatch 'Minimize|Maximize|Close|System|Scroll' -and
            $_.className -notmatch 'PopupHost|TitleBar|ReunionWindowing|ScrollBar' -and
            -not ($_.type -eq 'Button' -and -not $_.name) -and
            -not ($_.type -eq 'MenuItem' -and [string]::IsNullOrWhiteSpace($_.className))
        })
        $missing = @($controls | Where-Object { -not $_.automationId })
        if (-not $controls.Count -or $missing.Count) { throw "Accessibility errors: $(($missing.name) -join ', ')" }
        @{ controls=$controls.Count; errors=0 } | ConvertTo-Json | Set-Content (Join-Path $artifactDirectory 'accessibility.json')
    }
} catch { $results.Add(@{ name='Gate setup'; status='FAIL'; detail="$_" }) }
finally {
    if ($appPid) { Stop-Process -Id $appPid -ErrorAction SilentlyContinue }
    Invoke-Checked @('ui','yield') | Out-Null
    $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflowId
}
$failed = @($results | Where-Object status -eq 'FAIL').Count
@{ passed=$results.Count-$failed; failed=$failed; results=$results.ToArray(); screenshots=$screenshots.ToArray() } |
    ConvertTo-Json -Depth 6 | Set-Content (Join-Path $artifactDirectory 'results.json')
$results | Format-Table -Wrap -AutoSize
Write-Host "Notification settings artifacts: $artifactDirectory"
if ($failed) { throw 'Notification settings gate failed.' }
