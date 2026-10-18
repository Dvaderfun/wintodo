param(
    [Parameter(Mandatory)]
    [int]$AppPid,
    [Parameter(Mandatory)]
    [string]$ArtifactDirectory
)
# Local desktop input requires explicit owner permission for this session.
if ($env:WINTODO_ALLOW_LOCAL_UI -ne '1') {
    throw 'Local UI automation is disabled on this main machine. Use an isolated VM/Sandbox, or obtain owner permission for this session before setting WINTODO_ALLOW_LOCAL_UI=1. See docs/guides/testing.md.'
}


$ErrorActionPreference = "Stop"
winapp ui wait-for 'Todo.List.SortButton' -a $AppPid -t 15000 | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'The restarted app did not become ready within 15 seconds.' }
$MainHwnd = $null
for ($attempt = 0; $attempt -lt 10 -and -not $MainHwnd; $attempt++) {
    Start-Sleep -Milliseconds 300
    $MainHwnd = (winapp ui list-windows -a $AppPid --json 2>$null |
        ConvertFrom-Json |
        Where-Object { $_.title -eq "Wintodo" } |
        Select-Object -First 1).hwnd
}
if (-not $MainHwnd) {
    throw "The Wintodo main window was not found after restart."
}

function Get-SearchMatches {
    param([string]$Selector)

    return @(
        (winapp ui search $Selector -w $MainHwnd --json 2>$null |
            ConvertFrom-Json).matches
    )
}

function Invoke-List {
    param([string]$Name)

    $item = Get-SearchMatches $Name |
        Where-Object {
            $_.type -eq "ListItem" -and
            $_.automationId -match "^Todo\.Nav\.UserList\."
        } |
        Select-Object -First 1
    if (-not $item) {
        Start-Sleep -Milliseconds 300
        $item = Get-SearchMatches $Name |
            Where-Object {
                $_.type -eq "ListItem" -and
                $_.automationId -match "^Todo\.Nav\.UserList\."
            } |
            Select-Object -First 1
    }

    if (-not $item) {
        throw "Navigation item '$Name' was not found after restart."
    }

    winapp ui invoke $item.selector -a $AppPid
}

Invoke-List "Alpha"
winapp ui wait-for "Todo.List.SortLabel" `
    -w $MainHwnd `
    --value "Alphabetical, descending" `
    --contains `
    -t 5000

Invoke-List "Beta"
winapp ui wait-for "Todo.List.SortLabel" `
    -w $MainHwnd `
    --value "Due date, ascending" `
    --contains `
    -t 5000

foreach ($nav in 'Todo.Nav.MyDay', 'Todo.Nav.Important') {
    winapp ui invoke $nav -a $AppPid | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Could not navigate to '$nav' after restart." }
    Start-Sleep -Milliseconds 400
    foreach ($name in 'Needle beta', 'Beta later') {
        $rows = @(Get-SearchMatches $name | Where-Object {
            $_.type -eq 'ListItem' -and $_.automationId -match '^Todo\.Row\.[0-9a-f-]+$'
        })
        if ($rows.Count -ne 1) { throw "'$name' lost '$nav' membership after restart." }
    }
}

winapp ui screenshot -a $AppPid -o (
    Join-Path $ArtifactDirectory "03-restart-sort-persistence.png"
) 2>$null

@(
    @{ name = "Alpha sort survived restart"; status = "PASS" }
    @{ name = "Beta sort survived restart"; status = "PASS" }
    @{ name = "Dropped smart-list membership survived restart"; status = "PASS" }
) | ConvertTo-Json | Out-File (
    Join-Path $ArtifactDirectory "restart-results.json"
)
