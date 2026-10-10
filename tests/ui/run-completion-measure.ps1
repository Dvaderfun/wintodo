param([string]$AppDirectory,[string]$ArtifactDirectory,[int]$SampleCount=15)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$ArtifactDirectory=[IO.Path]::GetFullPath($ArtifactDirectory)
New-Item -ItemType Directory -Force -Path $ArtifactDirectory | Out-Null
$db=Join-Path $ArtifactDirectory 'timing.db'
if(Test-Path $db){throw 'Measurement needs a fresh fixture.'}
$env:WINTODO_PERF_DB_PATH=$db
$env:WINTODO_PERF_TASK_COUNT='200'
$env:WINTODO_PERF_LIST_COUNT='20'
try {
 dotnet test --project (Join-Path $root 'tests/Wintodo.Data.Tests') -p:Platform=x64 --filter-class '*PerformanceFixtureTests' | Out-Null
 if($LASTEXITCODE -ne 0){throw 'Seed failed'}
} finally {Remove-Item Env:\WINTODO_PERF_DB_PATH,Env:\WINTODO_PERF_TASK_COUNT,Env:\WINTODO_PERF_LIST_COUNT -ErrorAction SilentlyContinue}
$launch=winapp run $AppDirectory --detach --json -- --database-path $db | ConvertFrom-Json
if($LASTEXITCODE -ne 0 -or -not $launch.ProcessId){throw 'Launch failed'}
$previousWorkflow=$env:WINAPP_UI_WORKFLOW_ID
$env:WINAPP_UI_WORKFLOW_ID=[guid]::NewGuid().ToString('N')
function Ui([string[]]$Arguments){
 $output=winapp ui @Arguments -a $launch.ProcessId
 if($LASTEXITCODE -ne 0){throw "Failed $Arguments"}
 $output
}
try {
 Ui @('wait-for','Todo.Row.CompleteToggle.perf-task-00001','-t','10000') | Out-Null
 Ui @('click','Todo.Row.CompleteToggle.perf-task-00001') | Out-Null
 Ui @('wait-for','Todo.List.CompletedHeader','-t','5000') | Out-Null
 Ui @('invoke','Todo.List.CompletedHeader','--action','toggle-on') | Out-Null
 Ui @('wait-for','Todo.Row.CompleteToggle.perf-task-00001','--value','On','-t','5000') | Out-Null
 powershell.exe -NoProfile -File (Join-Path $PSScriptRoot 'measure-completion.ps1') -AppPid $launch.ProcessId -Output (Join-Path $ArtifactDirectory 'timings.json') -SampleCount $SampleCount
 if($LASTEXITCODE -ne 0){throw 'Measurement failed'}
} finally {
 winapp ui yield | Out-Null
 $env:WINAPP_UI_WORKFLOW_ID=$previousWorkflow
 Stop-Process -Id $launch.ProcessId -ErrorAction SilentlyContinue
}
Get-Content (Join-Path $ArtifactDirectory 'timings.json') -Raw | ConvertFrom-Json | Select-Object medianMs,meanMs,sampleCount | ConvertTo-Json
