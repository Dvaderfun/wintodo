param(
    [Parameter(Mandatory)][int]$AppPid,
    [Parameter(Mandatory)][string]$ArtifactDirectory
)
# Local desktop input requires explicit owner permission for this session.
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1') {
    throw 'Local UI automation is disabled on this main machine. Use an isolated VM/Sandbox, or obtain owner permission for this session before setting WINTODO_ALLOW_LOCAL_UI=1. See docs/guides/testing.md.'
}


$ErrorActionPreference = 'Stop'
function Invoke-Ui([string[]]$Arguments) {
    $output = winapp ui @Arguments -a $AppPid
    if ($LASTEXITCODE -ne 0) { throw "UI command failed: $($Arguments -join ' ')" }
    $output
}

try {
    Invoke-Ui @('wait-for', 'Todo.List.Tasks', '-t', '5000') | Out-Null
    $navigation = (Invoke-Ui @('search', 'M3 Gate', '--json') | ConvertFrom-Json).matches |
        Where-Object { $_.automationId -match '^Todo.Nav.UserList.' } |
        Select-Object -First 1
    if (-not $navigation) { throw 'M3 Gate list was not persisted.' }
    Invoke-Ui @('invoke', $navigation.automationId) | Out-Null
    Invoke-Ui @('wait-for', 'Gate task 5', '-t', '5000') | Out-Null
    function Get-Row([string]$Title) {
        (Invoke-Ui @('search', $Title, '--json') | ConvertFrom-Json).matches |
            Where-Object { $_.type -eq 'ListItem' -and $_.automationId -match '^Todo.Row.[0-9a-f-]+$' } |
            Select-Object -First 1
    }
    $task5 = Get-Row 'Gate task 5'
    $task3 = Get-Row 'Gate task 3'
    if (-not $task5 -or -not $task3 -or $task5.y -ge $task3.y) {
        throw 'Task 5 did not remain before task 3 after restart.'
    }
    Invoke-Ui @('screenshot', '-o', (Join-Path $ArtifactDirectory '06-restart-order.png')) | Out-Null
    @{ name = 'Task reorder survived restart'; status = 'PASS' } |
        ConvertTo-Json | Set-Content (Join-Path $ArtifactDirectory 'restart-results.json')
} catch {
    @{ name = 'Task reorder survived restart'; status = 'FAIL'; detail = "$_" } |
        ConvertTo-Json | Set-Content (Join-Path $ArtifactDirectory 'restart-results.json')
    throw
}
