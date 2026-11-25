# A detail pane can stay open while shortcuts change the same task in the list.
# Check the committed row by UID before and after Tomorrow, even if its title reverts.
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1' -or $PSScriptRoot -ine 'C:\wintodo\tests\ui') {
    throw 'Run this gate in WintodoTest with scripts/Invoke-VmGate.ps1.'
}
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SuggestionsFixtureSql.ps1')
$repository = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$app = Join-Path $repository 'src/Wintodo.App/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64'
$artifacts = Join-Path $PSScriptRoot ('artifacts/detail-row-edits/run-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $artifacts | Out-Null
$database = Join-Path $artifacts 'row-edits.db'
$clock = Join-Path $artifacts 'clock.txt'
([DateTimeOffset]::new([DateTime]::new(2026,11,25,12,0,0,[DateTimeKind]::Local))).ToString('o') |
    Set-Content -LiteralPath $clock
$previousWorkflow = $env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID = [guid]::NewGuid().ToString('N')
$appPid = 0
$result = [ordered]@{ uid = $null; beforeDue = $null; afterDue = $null; status = 'FAIL' }

function Run-WinApp([string[]]$Arguments) {
    $output = @(& winapp @Arguments)
    if ($LASTEXITCODE -ne 0) { throw "winapp $($Arguments -join ' ') failed: $output" }
    $output
}
function Invoke-Main([string[]]$Arguments) {
    Run-WinApp (@('ui') + $Arguments + @('--on','local','-w',$mainHwnd)) | Out-Null
}
function Wait-Sql([string]$Sql, [string]$Expected, [switch]$AnyValue) {
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        $actual = Invoke-SuggestionsFixtureSql $database $Sql
        if (($AnyValue -and $actual) -or (!$AnyValue -and $actual -ceq $Expected)) { return $actual }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Database did not reach expected value '$Expected'; actual '$actual'. Query: $Sql"
}

try {
    $launch = Run-WinApp @('run',$app,'--detach','--json','--','--database-path',$database,'--clock-file',$clock) | ConvertFrom-Json
    if (!$launch.ProcessId -or ($launch.ProcessScope -and $launch.ProcessScope -ne 'local')) { throw 'No local app PID.' }
    $appPid = [int]$launch.ProcessId
    Run-WinApp @('ui','wait-for','Todo.Shell.PageTitle','--on','local','-a',"$appPid",'-t','10000') | Out-Null
    $mainHwnd = [string]((Run-WinApp @('ui','list-windows','--on','local','-a',"$appPid",'--json') |
        ConvertFrom-Json | Where-Object title -eq 'Wintodo' | Select-Object -First 1).hwnd)
    if (!$mainHwnd) { throw 'No Wintodo main window.' }
    Invoke-Main @('invoke','Todo.Nav.DefaultList')
    Invoke-Main @('wait-for','Todo.List.AddTaskBox','-p','IsOffscreen','--value','False','-t','5000')
    Invoke-Main @('set-value','Todo.List.AddTaskBox','Before row edit')
    Invoke-Main @('wait-for','Todo.Composer.AddButton','-p','IsEnabled','--value','True','-t','5000')
    Invoke-Main @('invoke','Todo.Composer.AddButton')
    $uid = [guid]::Parse((Wait-Sql "SELECT uid FROM tasks WHERE summary='Before row edit'" -AnyValue)).ToString()
    $result.uid = $uid
    $row = "Todo.Row.$uid"
    $editor = "Todo.Row.TitleEdit.$uid"
    $fields = "SELECT summary || '|' || priority || '|' || COALESCE(due_utc,'') || '|' || COALESCE(my_day_date,'') FROM tasks WHERE uid='$uid'"
    Invoke-Main @('wait-for',$row,'-t','5000')
    Invoke-Main @('focus',$row)
    Invoke-Main @('send-keys','enter','--via','send-input')
    Invoke-Main @('wait-for','Todo.Detail.Title','-p','IsEnabled','--value','True','-t','5000')
    Invoke-Main @('send-keys','ctrl+vk=0x49','--via','send-input')
    Wait-Sql "SELECT priority FROM tasks WHERE uid='$uid'" '1' | Out-Null
    Invoke-Main @('send-keys','ctrl+vk=0x54','--via','send-input')
    Wait-Sql "SELECT my_day_date FROM tasks WHERE uid='$uid'" '2026-11-25' | Out-Null
    Invoke-Main @('send-keys','f2','--via','send-input')
    Invoke-Main @('wait-for',$editor,'-p','HasKeyboardFocus','--value','True','-t','5000')
    Invoke-Main @('set-value',$editor,'Discard this draft')
    Invoke-Main @('send-keys','escape','--via','send-input')
    Invoke-Main @('wait-for',$editor,'--gone','-t','5000')
    Wait-Sql $fields 'Before row edit|1||2026-11-25' | Out-Null
    Invoke-Main @('focus',$row)
    Invoke-Main @('send-keys','f2','--via','send-input')
    Invoke-Main @('wait-for',$editor,'--value','Before row edit','-t','5000')
    Invoke-Main @('wait-for',$editor,'-p','HasKeyboardFocus','--value','True','-t','5000')
    Invoke-Main @('set-value',$editor,'Saved with Enter')
    Invoke-Main @('send-keys','enter','--via','send-input')
    Invoke-Main @('wait-for',$editor,'--gone','-t','5000')
    Wait-Sql $fields 'Saved with Enter|1||2026-11-25' | Out-Null
    Invoke-Main @('focus',$row)
    Invoke-Main @('send-keys','f2','--via','send-input')
    Invoke-Main @('wait-for',$editor,'-p','HasKeyboardFocus','--value','True','-t','5000')
    Invoke-Main @('set-value',$editor,'After row edit')
    Invoke-Main @('focus','Todo.List.AddTaskBox')
    Invoke-Main @('wait-for',$editor,'--gone','-t','5000')
    $result.beforeDue = Wait-Sql $fields 'After row edit|1||2026-11-25'

    Invoke-Main @('invoke','Todo.Detail.Due')
    # MenuFlyout is a separate popup window owned by the same application.
    Run-WinApp @('ui','wait-for','Todo.Detail.Due.Tomorrow','--on','local','-a',"$appPid",'-t','5000') | Out-Null
    Run-WinApp @('ui','invoke','Todo.Detail.Due.Tomorrow','--on','local','-a',"$appPid") | Out-Null
    Wait-Sql "SELECT due_utc FROM tasks WHERE uid='$uid'" '2026-11-26' | Out-Null
    $result.afterDue = Invoke-SuggestionsFixtureSql $database $fields
    if ($result.afterDue -cne 'After row edit|1|2026-11-26|2026-11-25') {
        throw "Tomorrow overwrote saved row edits for task ${uid}: before '$($result.beforeDue)', after '$($result.afterDue)'."
    }
    $result.status = 'PASS'
} catch {
    $result.detail = "$_"
    throw
} finally {
    $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifacts 'result.json')
    if ($appPid) { Stop-Process -Id $appPid -ErrorAction SilentlyContinue }
    try { Run-WinApp @('ui','yield','--on','local') | Out-Null }
    finally { $env:WINAPP_UI_WORKFLOW_ID = $previousWorkflow }
    Write-Host "Detail/row edit gate artifacts: $artifacts"
}
