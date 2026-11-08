param([ValidateSet('local')][string]$Target = 'local')
# Run through scripts/Invoke-VmGate.ps1 in WintodoTest. Never opt in on the host.
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1') {
    throw 'Host UI automation is disabled. Use scripts/Invoke-VmGate.ps1 -Gate run-attachment-provenance-gate.ps1.'
}
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$appDirectory = Join-Path $repositoryRoot 'src/Wintodo.App/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64'
$artifactDirectory = Join-Path $PSScriptRoot ('artifacts/attachment-provenance/run-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $artifactDirectory | Out-Null
$fixture = Join-Path $artifactDirectory 'fixture'
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'tests/fixtures/mstodo/basic') -Destination $fixture -Recurse
$attachmentDirectory = Join-Path $fixture 'me/todo/lists/L-default/tasks/T3'
$metadataPath = Join-Path $attachmentDirectory 'attachments.json'
$metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
$metadata.value[0].name = '../wintodo-dep10-benign.txt'
$metadata.value[0].contentType = 'text/plain'
$document = "Synthetic benign DEP-10 document.`n"
$metadata.value[0].size = [Text.Encoding]::UTF8.GetByteCount($document)
$metadata | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $metadataPath
[IO.File]::WriteAllText((Join-Path $attachmentDirectory 'attachments/A1/content.bin'), $document)
$databasePath = Join-Path $artifactDirectory 'provenance.db'
$appPid = 0
$mainHwnd = ''
$localRoot = ''
$finalFile = ''
$results = [Collections.Generic.List[object]]::new()
$screenshots = [Collections.Generic.List[string]]::new()
$externalPids = [Collections.Generic.List[int]]::new()
$previousWorkflowId = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = [guid]::NewGuid().ToString('N')

function Invoke-WinAppChecked {
    param([string[]]$Arguments)
    $global:LASTEXITCODE = $null
    $output = @(& winapp @Arguments)
    if ($null -eq $LASTEXITCODE -or $LASTEXITCODE -ne 0) { throw "winapp failed: $($Arguments -join ' ')" }
    $output
}
function Invoke-Ui {
    param([string[]]$Arguments, [string]$Window = $mainHwnd)
    Invoke-WinAppChecked (@('ui') + $Arguments + @('--on', $Target, '-w', $Window))
}
function Get-Windows { @(Invoke-WinAppChecked @('ui', 'list-windows', '--on', $Target, '--json') | ConvertFrom-Json) }
function Save-Screenshot {
    param([string]$Name, [string]$Window = $mainHwnd)
    $path = Join-Path $artifactDirectory $Name
    Invoke-Ui @('screenshot', '--output', $path) -Window $Window | Out-Null
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -eq 0) {
        throw 'Screenshot was not written.'
    }
    $screenshots.Add($path)
}
function Test-UI {
    param([string]$Name, [scriptblock]$Script)
    try {
        & $Script | Out-Null
        $results.Add(@{ name = $Name; status = 'PASS' })
        Write-Host "PASS $Name"
    } catch {
        $results.Add(@{ name = $Name; status = 'FAIL'; detail = "$_" })
        Write-Host "FAIL $Name : $_"
        if ($mainHwnd) {
            try { Save-Screenshot ("failure-$($results.Count).png") } catch { Write-Warning 'Failure capture unavailable.' }
        }
    }
}
function Stop-App {
    if ($appPid -gt 0) {
        Stop-Process -Id $appPid -ErrorAction SilentlyContinue
        Wait-Process -Id $appPid -Timeout 10 -ErrorAction SilentlyContinue
        $script:appPid = 0
    }
    $script:mainHwnd = ''
}
function Start-App {
    param([string]$FixturePath = $fixture)
    Stop-App
    $launch = Invoke-WinAppChecked @('run', $appDirectory, '--detach', '--json', '--', '--database-path',
        $databasePath, '--show-onboarding', '--import-fixture', $FixturePath) | ConvertFrom-Json
    if (-not $launch.ProcessId -or [int]$launch.ProcessId -le 0) { throw 'Launch returned no app PID.' }
    $script:appPid = [int]$launch.ProcessId
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while (-not $mainHwnd -and $timer.ElapsedMilliseconds -lt 30000) {
        $window = Get-Windows | Where-Object { [int]$_.processId -eq $appPid -and $_.title -eq 'Wintodo' } |
            Select-Object -First 1
        if ($window) { $script:mainHwnd = [string]$window.hwnd }
        else { Start-Sleep -Milliseconds 250 }
    }
    if (-not $mainHwnd) { throw 'No main window belonging to the launched process.' }
    Invoke-Ui @('wait-for', 'Todo.Nav.Settings', '-t', '30000') | Out-Null
    $manifest = [xml](Get-Content -LiteralPath (Join-Path $appDirectory 'AppxManifest.xml') -Raw)
    $package = @(Get-AppxPackage -Name $manifest.Package.Identity.Name)
    if ($package.Count -ne 1) { throw 'Expected exactly one registered Wintodo package.' }
    $script:localRoot = Join-Path $env:LOCALAPPDATA ('Packages/' + $package[0].PackageFamilyName + '/LocalState')
}
function Start-Import {
    Invoke-Ui @('invoke', 'Todo.Welcome.MicrosoftTodo') | Out-Null
    Invoke-Ui @('wait-for', 'Todo.Import.SignIn', '-t', '5000') | Out-Null
    Invoke-Ui @('invoke', 'Todo.Import.SignIn') | Out-Null
    Invoke-Ui @('wait-for', 'Todo.Import.Continue', '-t', '15000') | Out-Null
    Invoke-Ui @('invoke', 'Todo.Import.Continue') | Out-Null
    Invoke-Ui @('wait-for', 'Todo.Import.Start', '-t', '10000') | Out-Null
    Invoke-Ui @('invoke', 'Todo.Import.Start') | Out-Null
}
function Get-AttachmentSelector {
    param([ValidateSet('Open', 'Remove')][string]$Action)
    $matches = (Invoke-Ui @('search', "Todo.Detail.Attachment$Action.", '--json') | ConvertFrom-Json).matches
    $elements = @($matches | Where-Object { $_.automationId -match "^Todo\.Detail\.Attachment$Action\.\d+$" })
    if ($elements.Count -ne 1) { throw "Expected one attachment $Action command." }
    [string]$elements[0].automationId
}

try {
    Test-UI 'Guest staging/final provenance, IO failures and failed-save cleanup regressions' {
        Push-Location -LiteralPath $repositoryRoot
        try {
            dotnet test --project tests/Wintodo.Import.Tests -p:Platform=x64 --filter-class '*AttachmentProvenanceTests' --report-trx
            if ($LASTEXITCODE -ne 0) { throw 'Guest provenance regressions failed.' }
        } finally { Pop-Location }
    }
    Start-App
    Test-UI 'Synthetic import preserves Internet origin and confines hostile filename' {
        Start-Import
        Invoke-Ui @('wait-for', 'Todo.Import.GoToTasks', '-t', '20000')
        $attachmentsRoot = Join-Path $localRoot 'attachments'
        $files = @(Get-ChildItem -LiteralPath $attachmentsRoot -Recurse -File -Filter '*wintodo-dep10-benign.txt')
        if ($files.Count -ne 1) { throw 'Expected one synthetic attachment.' }
        $script:finalFile = $files[0].FullName
        if (-not $finalFile.StartsWith($attachmentsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Attachment escaped its storage root.'
        }
        if ([IO.File]::ReadAllText($finalFile) -ne $document) { throw 'Imported attachment bytes changed.' }
        if ([IO.File]::ReadAllText($finalFile + ':Zone.Identifier') -ne "[ZoneTransfer]`r`nZoneId=3`r`n") {
            throw 'Internet zone metadata missing or includes unreviewed metadata.'
        }
        if (Test-Path -LiteralPath (Join-Path $localRoot 'wintodo-dep10-benign.txt')) { throw 'Traversal created an outside file.' }
        Invoke-Ui @('invoke', 'Todo.Import.GoToTasks')
        Invoke-Ui @('wait-for', 'Send the quarterly report', '-t', '8000')
        $row = (Invoke-Ui @('search', 'Send the quarterly report', '--json') | ConvertFrom-Json).matches |
            Where-Object { $_.type -eq 'ListItem' -and $_.automationId -match '^Todo\.Row\.' } | Select-Object -First 1
        if (-not $row) { throw 'Imported task row missing.' }
        Invoke-Ui @('invoke', $row.automationId)
        Invoke-Ui @('wait-for', 'Todo.Detail.Title', '-t', '5000')
        Get-AttachmentSelector Open
        Save-Screenshot '01-imported-attachment.png'
    }
    Test-UI 'App attachment command opens benign document through Windows warning and existing handler' {
        $before = @(Get-Windows | ForEach-Object { [int]$_.processId })
        Invoke-Ui @('invoke', (Get-AttachmentSelector Open))
        $timer = [Diagnostics.Stopwatch]::StartNew()
        $documentWindow = $null
        $warningHandled = $false
        while (-not $documentWindow -and $timer.ElapsedMilliseconds -lt 15000) {
            $windows = Get-Windows
            $documentWindow = $windows | Where-Object {
                $_.title -match 'wintodo-dep10-benign\.txt' -and [int]$_.processId -ne $appPid -and
                $_.title -notmatch 'Security Warning|Open File|Open file'
            } | Select-Object -First 1
            if (-not $documentWindow -and -not $warningHandled) {
                foreach ($window in ($windows | Where-Object { $_.title -match 'Security Warning|Open File|Open file|Wintodo' })) {
                    $inspection = Invoke-Ui @('search', 'wintodo-dep10-benign.txt', '--json') -Window "$($window.hwnd)" | ConvertFrom-Json
                    $buttons = (Invoke-Ui @('search', 'Open', '--type', 'Button', '--json') -Window "$($window.hwnd)" | ConvertFrom-Json).matches
                    $openButtons = @($buttons | Where-Object { $_.name -eq 'Open' })
                    if ($inspection.matches.Count -gt 0 -and $openButtons.Count -eq 1) {
                        Save-Screenshot '02-windows-warning.png' -Window "$($window.hwnd)"
                        Invoke-Ui @('invoke', $openButtons[0].slug) -Window "$($window.hwnd)" | Out-Null
                        $warningHandled = $true
                        break
                    }
                }
            }
            if (-not $documentWindow) { Start-Sleep -Milliseconds 250 }
        }
        if (-not $documentWindow) { throw 'No existing document handler opened the benign file; association/setup remains pending.' }
        if ($before -notcontains [int]$documentWindow.processId) { $externalPids.Add([int]$documentWindow.processId) }
        Save-Screenshot '03-benign-document-open.png' -Window "$($documentWindow.hwnd)"
        if ([IO.File]::ReadAllText($finalFile + ':Zone.Identifier') -ne "[ZoneTransfer]`r`nZoneId=3`r`n") {
            throw 'Opening removed Internet metadata.'
        }
    }
    Test-UI 'Attachment removal deletes file and zone metadata and persists across restart' {
        foreach ($externalPid in $externalPids) { Stop-Process -Id $externalPid -ErrorAction SilentlyContinue }
        $removeId = Get-AttachmentSelector Remove
        Invoke-Ui @('invoke', $removeId)
        Invoke-Ui @('wait-for', $removeId, '--gone', '-t', '5000')
        if (Test-Path -LiteralPath $finalFile) { throw 'Removed attachment file remains.' }
        if ([IO.File]::Exists($finalFile + ':Zone.Identifier')) { throw 'Removed attachment zone stream remains.' }
        Save-Screenshot '04-attachment-removed.png'
        Start-App
        Invoke-Ui @('wait-for', 'Send the quarterly report', '-t', '8000')
        $row = (Invoke-Ui @('search', 'Send the quarterly report', '--json') | ConvertFrom-Json).matches |
            Where-Object { $_.type -eq 'ListItem' -and $_.automationId -match '^Todo\.Row\.' } | Select-Object -First 1
        Invoke-Ui @('invoke', $row.automationId)
        Invoke-Ui @('wait-for', 'Todo.Detail.Title', '-t', '5000')
        $matches = (Invoke-Ui @('search', 'Todo.Detail.AttachmentOpen.', '--json') | ConvertFrom-Json).matches
        if (@($matches | Where-Object { $_.automationId -match '^Todo\.Detail\.AttachmentOpen\.\d+$' }).Count) {
            throw 'Removed attachment returned after restart.'
        }
    }
    Test-UI 'Failed wizard download leaves no staged or final synthetic attachment' {
        Stop-App
        $failedFixture = Join-Path $artifactDirectory 'missing-content-fixture'
        Copy-Item -LiteralPath $fixture -Destination $failedFixture -Recurse
        Remove-Item -LiteralPath (Join-Path $failedFixture 'me/todo/lists/L-default/tasks/T3/attachments/A1/content.bin')
        $script:databasePath = Join-Path $artifactDirectory 'failed.db'
        Start-App -FixturePath $failedFixture
        Start-Import
        Invoke-Ui @('wait-for', 'Todo.Import.ErrorAction', '-t', '20000')
        $stagingRoot = Join-Path $localRoot 'import-staging'
        if ((Test-Path -LiteralPath $stagingRoot) -and @(Get-ChildItem -LiteralPath $stagingRoot -Recurse -File).Count) {
            throw 'Failed import retained staged files.'
        }
        $attachmentsRoot = Join-Path $localRoot 'attachments'
        if (@(Get-ChildItem -LiteralPath $attachmentsRoot -Recurse -File -Filter '*wintodo-dep10-benign.txt').Count) {
            throw 'Failed import retained final synthetic attachment.'
        }
        Save-Screenshot '05-failed-import-cleanup.png'
    }
} catch {
    $results.Add(@{ name = 'Gate setup or execution'; status = 'FAIL'; detail = "$_" })
} finally {
    Stop-App
    foreach ($externalPid in $externalPids) { Stop-Process -Id $externalPid -ErrorAction SilentlyContinue }
    try { Invoke-WinAppChecked @('ui', 'yield', '--on', $Target) | Out-Null } catch { Write-Warning 'UI workflow yield failed.' }
    $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflowId
    $report = [ordered]@{
        results = @($results.ToArray()); screenshots = @($screenshots.ToArray())
        passed = @($results | Where-Object status -eq 'PASS').Count
        failed = @($results | Where-Object status -ne 'PASS').Count
    }
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $artifactDirectory 'results.json')
}
Write-Host "Attachment provenance gate: $($report.passed)/$($results.Count) passed"
Write-Host "Artifacts: $artifactDirectory"
if ($report.failed) { exit 1 }
exit 0
