param([ValidateSet('local')][string]$Target = 'local')
# Onboarding + Microsoft To Do import (IMPORT-PLAN, ADR-016). The Debug-only
# --import-fixture argument swaps Graph sign-in for tests/fixtures/mstodo/basic,
# so the real client, mapper, store and wizard run without a Microsoft account.
# Local desktop input requires explicit owner permission for this session.
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1') {
    throw 'Local UI automation is disabled on this main machine. Use an isolated VM/Sandbox, or obtain owner permission for this session before setting WINTODO_ALLOW_LOCAL_UI=1. See docs/guides/testing.md.'
}

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$appDirectory = Join-Path $repositoryRoot 'src/Wintodo.App/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64'
$fixture = Join-Path $repositoryRoot 'tests/fixtures/mstodo/basic'
$artifactDirectory = Join-Path $PSScriptRoot ('artifacts/import/run-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null
$clockPath = Join-Path $artifactDirectory 'clock.txt'
'2026-10-19T12:00:00+00:00' | Set-Content -LiteralPath $clockPath
$previousWorkflowId = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = [guid]::NewGuid().ToString('N')
$appPid = 0
$mainHwnd = ''
$databasePath = ''
$tracePath = ''
$results = [Collections.Generic.List[object]]::new()

function Invoke-WinAppChecked {
    param([string[]]$Arguments)
    $output = @(& winapp @Arguments)
    if ($LASTEXITCODE -ne 0) { throw "winapp $($Arguments -join ' ') failed: $output" }
    $output
}
function Invoke-Ui {
    param([string[]]$Arguments)
    Invoke-WinAppChecked (@('ui') + $Arguments + @('--on', $Target, '-a', "$appPid"))
}
function Start-App {
    param([switch]$ShowOnboarding, [switch]$ExpireFirstSignIn)
    $script:tracePath = [IO.Path]::ChangeExtension($databasePath, ('trace-' + [DateTime]::UtcNow.ToString('HHmmss') + '.log'))
    $arguments = @('run', $appDirectory, '--detach', '--json', '--',
        '--database-path', $databasePath, '--clock-file', $clockPath, '--import-fixture', $fixture,
        '--trace-file', $tracePath)
    if ($ShowOnboarding) { $arguments += '--show-onboarding' }
    if ($ExpireFirstSignIn) { $arguments += '--import-fixture-expire-once' }
    $launch = Invoke-WinAppChecked $arguments | ConvertFrom-Json
    if (-not $launch.ProcessId) { throw 'Import gate launch did not return a PID.' }
    $script:appPid = [int]$launch.ProcessId
    Invoke-Ui @('wait-for', 'Todo.Nav.Settings', '-t', '30000') | Out-Null
    $script:mainHwnd = [string]((Invoke-Ui @('list-windows', '--json') | ConvertFrom-Json |
        Where-Object title -eq 'Wintodo' | Select-Object -First 1).hwnd)
    if (-not $mainHwnd) { throw 'No app window.' }
}
function Stop-App {
    if ($appPid -ne 0) {
        Start-Sleep -Milliseconds 500
        Stop-Process -Id $appPid -ErrorAction SilentlyContinue
        Wait-Process -Id $appPid -Timeout 10 -ErrorAction SilentlyContinue
        $script:appPid = 0
    }
}

function Test-UI {
    param([string]$Name, [scriptblock]$Script)
    try {
        & $Script | Out-Null
        $results.Add(@{ name = $Name; status = 'PASS' })
        Write-Host "PASS  $Name"
    } catch {
        $results.Add(@{ name = $Name; status = 'FAIL'; detail = "$_" })
        Write-Host "FAIL  $Name : $_"
        try {
            Invoke-Ui @('inspect', '--interactive', '--json', '--depth', '18') |
                Set-Content -LiteralPath (Join-Path $artifactDirectory ("failure-" + $results.Count + ".json"))
            Save-Screenshot ("failure-" + $results.Count + ".png")
        } catch { Write-Warning "Could not capture failure state: $_" }
    }
}
function Save-Screenshot {
    param([string]$Name)
    $path = Join-Path $artifactDirectory $Name
    Invoke-WinAppChecked @('ui', 'screenshot', '-w', $mainHwnd, '--on', $Target, '--output', $path) | Out-Null
}
function Get-Nodes {
    param([object[]]$Nodes)
    foreach ($node in $Nodes) { $node; if ($node.children) { Get-Nodes @($node.children) } }
}
function Get-Tree {
    $inspection = Invoke-Ui @('inspect', '--json', '--depth', '18') | ConvertFrom-Json
    @(Get-Nodes @($inspection.windows | ForEach-Object { $_.elements }))
}
function Assert-Present {
    param([string]$AutomationId)
    if (-not (Get-Tree | Where-Object automationId -eq $AutomationId)) { throw "Missing $AutomationId" }
}
function Assert-Absent {
    param([string]$AutomationId)
    if (Get-Tree | Where-Object automationId -eq $AutomationId) { throw "Unexpected $AutomationId" }
}
function Assert-Text {
    param([string]$Text)
    if (-not (Get-Tree | Where-Object { $_.name -eq $Text })) { throw "Missing text: $Text" }
}
function Assert-NoText {
    param([string]$Text)
    if (Get-Tree | Where-Object { $_.name -eq $Text }) { throw "Unexpected text: $Text" }
}
function Get-RowCount {
    param([string]$Title)
    @(Get-Tree | Where-Object { $_.type -eq 'ListItem' -and $_.automationId -match '^Todo\.Row\.' -and
        ($_.name -split ',')[0] -eq $Title }).Count
}
function Assert-Checked {
    param([string]$AutomationId, [bool]$Expected)
    $node = Get-Tree | Where-Object automationId -eq $AutomationId | Select-Object -First 1
    if (-not $node) { throw "Missing $AutomationId" }
    $state = (Invoke-Ui @('get-property', $AutomationId, '--json') | ConvertFrom-Json).properties
    $json = $state | ConvertTo-Json -Compress -Depth 5
    $isOn = $json -match '"ToggleState"\s*:\s*"?(On|1)"?' -or $json -match '"value"\s*:\s*"On"'
    if ($isOn -ne $Expected) { throw "$AutomationId expected checked=$Expected; properties $json" }
}

function Assert-AccessibilityClean {
    param([string]$Name)
    $inspection = Invoke-Ui @('inspect', '--interactive', '--json', '--depth', '18') | ConvertFrom-Json
    $all = @(Get-Nodes @($inspection.windows | ForEach-Object { $_.elements }))
    $controls = @($all | Where-Object {
        $_.type -match 'Button|CheckBox|Edit|ComboBox' -and
        $_.automationId -match '^Todo\.(Import|Welcome)\.' -or
        ($_.type -match 'Button|CheckBox' -and $_.className -match 'SettingsCard|CheckBox|ToggleSwitch' -and
            $_.name -notmatch 'Minimize|Maximize|Close|System')
    })
    if ($controls.Count -eq 0) { throw "No $Name controls audited." }
    $missingId = @($controls | Where-Object { -not $_.automationId })
    $missingName = @($controls | Where-Object { [string]::IsNullOrWhiteSpace($_.name) })
    if ($missingId.Count) { throw "Missing AutomationIds: $(($missingId | ForEach-Object { $_.name }) -join ', ')" }
    if ($missingName.Count) { throw "Missing names: $(($missingName | ForEach-Object { $_.automationId }) -join ', ')" }
    @{ surface = $Name; controls = $controls.Count; errors = 0 } | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $artifactDirectory "accessibility-$Name.json")
}

try {
    $script:databasePath = Join-Path $artifactDirectory 'import.db'
    Start-App -ShowOnboarding

    Test-UI 'Fresh database shows the Welcome surface' {
        Invoke-Ui @('wait-for', 'Todo.Welcome.Title', '-t', '5000')
        Assert-Present 'Todo.Welcome.MicrosoftTodo'
        Assert-Present 'Todo.Welcome.Ical'
        Assert-Present 'Todo.Welcome.StartFresh'
        Assert-AccessibilityClean 'welcome'
        Save-Screenshot '01-welcome.png'
    }
    Test-UI 'Microsoft To Do card opens the wizard on its connect step' {
        Invoke-Ui @('invoke', 'Todo.Welcome.MicrosoftTodo')
        Invoke-Ui @('wait-for', 'Todo.Import.SignIn', '-t', '5000')
        Assert-Present 'Todo.Import.ReadOnlyInfo'
        Assert-Absent 'Todo.Welcome.Title'
        Save-Screenshot '02-connect.png'
    }
    Test-UI 'Sign-in reads every list; flagged email starts unselected' {
        Invoke-Ui @('invoke', 'Todo.Import.SignIn')
        Invoke-Ui @('wait-for', 'Todo.Import.Continue', '-t', '15000')
        Assert-Text 'Signed in as fixture.person@example.com'
        foreach ($index in 0..3) { Assert-Present "Todo.Import.List.$index" }
        Assert-Checked 'Todo.Import.List.0' $true
        Assert-Checked 'Todo.Import.List.2' $false
        Assert-Present 'Todo.Import.LimitsInfo'
        Assert-Absent 'Todo.Import.OverwriteLocalEdits'
        Assert-Absent 'Todo.Import.NoLists'
        Assert-AccessibilityClean 'choose'
        Save-Screenshot '03-choose.png'
    }
    Test-UI 'Review shows the dry-run counts' {
        Invoke-Ui @('invoke', 'Todo.Import.Continue')
        Invoke-Ui @('wait-for', 'Todo.Import.Start', '-t', '10000')
        Assert-Text 'New lists: 2'
        Assert-Text 'Lists already in Wintodo: 1'
        Assert-Text 'New tasks: 5'
        Assert-Text 'New steps: 2'
        Assert-Text 'Attachments: 1'
        Save-Screenshot '04-review.png'
    }
    Test-UI 'Import completes and offers removal' {
        Invoke-Ui @('invoke', 'Todo.Import.Start')
        Invoke-Ui @('wait-for', 'Todo.Import.GoToTasks', '-t', '20000')
        Assert-Text 'Import complete'
        Assert-Text 'New tasks: 5'
        Assert-Present 'Todo.Import.RemoveImported'
        Assert-AccessibilityClean 'done'
        Save-Screenshot '05-done.png'
    }
    Test-UI 'Imported tasks land in the default Tasks list' {
        Invoke-Ui @('invoke', 'Todo.Import.GoToTasks')
        Invoke-Ui @('wait-for', 'Water the plants', '-t', '8000')
        Assert-Absent 'Todo.Import.Title'
        if ((Get-RowCount 'Water the plants') -ne 1) { throw 'Expected exactly one "Water the plants" row.' }
        if ((Get-RowCount 'Send the quarterly report') -ne 1) { throw 'Expected the HTML-note task.' }
        Assert-Text 'Groceries'
        Save-Screenshot '06-tasks.png'
    }

    Stop-App
    Start-App -ShowOnboarding
    Test-UI 'Restart after importing does not show Welcome again' {
        Invoke-Ui @('wait-for', 'Todo.Nav.DefaultList', '-t', '5000')
        Start-Sleep -Milliseconds 800
        Assert-Absent 'Todo.Welcome.Title'
    }
    Test-UI 'Second import from Settings duplicates nothing' {
        Invoke-Ui @('invoke', 'Todo.Nav.Settings')
        Invoke-Ui @('wait-for', 'Todo.Settings.ScrollViewer', '-t', '5000')
        Invoke-Ui @('scroll', 'Todo.Settings.ScrollViewer', '--to', 'bottom')
        Invoke-Ui @('wait-for', 'Todo.Settings.ImportMicrosoftTodo', '-t', '5000')
        Invoke-Ui @('invoke', 'Todo.Settings.ImportMicrosoftTodo')
        Invoke-Ui @('wait-for', 'Todo.Import.LastImport', '-t', '5000')
        Invoke-Ui @('invoke', 'Todo.Import.SignIn')
        Invoke-Ui @('wait-for', 'Todo.Import.Continue', '-t', '15000')
        Assert-Present 'Todo.Import.OverwriteLocalEdits'
        Invoke-Ui @('invoke', 'Todo.Import.Continue')
        Invoke-Ui @('wait-for', 'Todo.Import.Start', '-t', '10000')
        Assert-Text 'Already imported, skipped: 5'
        Assert-Text 'Everything is already here. Nothing will be duplicated.'
        Assert-NoText 'Attachments: 1'
        Save-Screenshot '07-second-review.png'
        Invoke-Ui @('invoke', 'Todo.Import.Start')
        Invoke-Ui @('wait-for', 'Todo.Import.GoToTasks', '-t', '20000')
        Assert-Text 'Already up to date'
        Assert-Absent 'Todo.Import.RemoveImported'
        if (Select-String -LiteralPath $tracePath -Pattern 'attachment=raw|attachment=contentBytes') { throw 'Re-run downloaded an attachment.' }
        Invoke-Ui @('invoke', 'Todo.Import.GoToTasks')
        Invoke-Ui @('wait-for', 'Water the plants', '-t', '8000')
        if ((Get-RowCount 'Water the plants') -ne 1) { throw 'Second import duplicated "Water the plants".' }
        Save-Screenshot '08-no-duplicates.png'
    }
    # Reopening the wizard in the same session re-creates both x:Load surfaces;
    # this once failed inside the button's command (stale wizard repeaters).
    Test-UI 'Cancel from Settings returns to Settings' {
        Invoke-Ui @('invoke', 'Todo.Nav.Settings')
        Invoke-Ui @('wait-for', 'Todo.Settings.ScrollViewer', '-t', '5000')
        Invoke-Ui @('scroll', 'Todo.Settings.ScrollViewer', '--to', 'bottom')
        Invoke-Ui @('wait-for', 'Todo.Settings.ImportMicrosoftTodo', '-t', '5000')
        Invoke-Ui @('invoke', 'Todo.Settings.ImportMicrosoftTodo')
        Invoke-Ui @('wait-for', 'Todo.Import.Cancel', '-t', '5000')
        Invoke-Ui @('invoke', 'Todo.Import.Cancel')
        Invoke-Ui @('wait-for', 'Todo.Settings.Title', '-t', '5000')
        Assert-Absent 'Todo.Import.Title'
    }
    Stop-App

    # Live finding: a sign-in that expired on the review step failed the
    # attachment download with 401 and was reported as "access denied".
    $script:databasePath = Join-Path $artifactDirectory 'expired.db'
    Start-App -ShowOnboarding -ExpireFirstSignIn
    Test-UI 'Expired sign-in asks to sign in again and keeps list choices' {
        Invoke-Ui @('wait-for', 'Todo.Welcome.MicrosoftTodo', '-t', '5000')
        Invoke-Ui @('invoke', 'Todo.Welcome.MicrosoftTodo')
        Invoke-Ui @('wait-for', 'Todo.Import.SignIn', '-t', '5000')
        Invoke-Ui @('invoke', 'Todo.Import.SignIn')
        Invoke-Ui @('wait-for', 'Todo.Import.Continue', '-t', '15000')
        Invoke-Ui @('invoke', 'Todo.Import.List.1')
        Assert-Checked 'Todo.Import.List.1' $false
        Invoke-Ui @('invoke', 'Todo.Import.Continue')
        Invoke-Ui @('wait-for', 'Todo.Import.Start', '-t', '10000')
        Invoke-Ui @('invoke', 'Todo.Import.Start')
        Invoke-Ui @('wait-for', 'Todo.Import.SignIn', '-t', '20000')
        Assert-Text 'Your Microsoft sign-in has expired. Sign in again to continue. Your choices are kept.'
        Assert-Absent 'Todo.Import.GoToTasks'
        Save-Screenshot '09-sign-in-expired.png'
        Invoke-Ui @('invoke', 'Todo.Import.SignIn')
        Invoke-Ui @('wait-for', 'Todo.Import.Start', '-t', '10000')
        Assert-Text 'Signed in again. Select Import to continue.'
        if (@(Select-String -LiteralPath $tracePath -Pattern 'WINTODO_IMPORT_READ lists=').Count -ne 1) { throw 'Expired sign-in read lists again.' }
        Invoke-Ui @('invoke', 'Todo.Import.Start')
        Invoke-Ui @('wait-for', 'Todo.Import.GoToTasks', '-t', '20000')
        Assert-Text 'Import complete'
        Save-Screenshot '10-after-sign-in-again.png'
    }
    Stop-App

    $script:databasePath = Join-Path $artifactDirectory 'fresh.db'
    Start-App -ShowOnboarding
    Test-UI 'Start fresh dismisses Welcome for good' {
        Invoke-Ui @('wait-for', 'Todo.Welcome.StartFresh', '-t', '5000')
        Invoke-Ui @('invoke', 'Todo.Welcome.StartFresh')
        Invoke-Ui @('wait-for', 'Todo.Welcome.Title', '--gone', '-t', '5000')
        Assert-Present 'Todo.List.AddTaskBox'
        Stop-App
        Start-App -ShowOnboarding
        Invoke-Ui @('wait-for', 'Todo.Nav.DefaultList', '-t', '5000')
        Start-Sleep -Milliseconds 800
        Assert-Absent 'Todo.Welcome.Title'
    }
    Stop-App

    $script:databasePath = Join-Path $artifactDirectory 'gate-default.db'
    Start-App
    Test-UI 'Scratch databases without --show-onboarding skip Welcome' {
        Invoke-Ui @('wait-for', 'Todo.Nav.DefaultList', '-t', '5000')
        Start-Sleep -Milliseconds 800
        Assert-Absent 'Todo.Welcome.Title'
        Assert-Present 'Todo.List.AddTaskBox'
    }
} finally {
    Stop-App
    if ($null -eq $previousWorkflowId) { Remove-Item Env:\WINAPP_UI_WORKFLOW_ID -ErrorAction SilentlyContinue }
    else { $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflowId }
    $results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $artifactDirectory 'results.json')
}

$failed = @($results | Where-Object status -ne 'PASS')
Write-Host ("Import gate: {0}/{1} passed" -f ($results.Count - $failed.Count), $results.Count)
Write-Host "Artifacts: $artifactDirectory"
if ($failed.Count -gt 0) { exit 1 }
exit 0
