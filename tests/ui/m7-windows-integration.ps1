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
$mainHwnd = $null
for ($attempt = 0; $attempt -lt 40 -and -not $mainHwnd; $attempt++) {
    Start-Sleep -Milliseconds 250
    $mainHwnd = (winapp ui list-windows -a $AppPid --json 2>$null |
        ConvertFrom-Json |
        Where-Object { $_.title -eq "Wintodo" } |
        Select-Object -First 1).hwnd
}
if (-not $mainHwnd) {
    throw "The Wintodo main window was not found."
}

winapp ui invoke "Todo.Nav.Settings" -w $mainHwnd | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Settings could not be opened."
}

winapp ui wait-for "Todo.Settings.LaunchAtLogin" `
    -w $mainHwnd `
    -t 10000 | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "The launch-at-login setting is missing."
}

$screenshot = Join-Path $ArtifactDirectory "settings-windows.png"
winapp ui screenshot -w $mainHwnd -o $screenshot | Out-Null
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $screenshot)) {
    throw "The Settings screenshot could not be captured."
}

Write-Host "M7 UI preflight: PASS"
