param(
    [Parameter(Mandatory)]
    [int]$AppPid
)

$ErrorActionPreference = "Stop"
$pass = 0
$fail = 0
$results = @()
$artifactDirectory = Join-Path $PSScriptRoot "artifacts"

New-Item -ItemType Directory -Force -Path $artifactDirectory | Out-Null

function Test-UI {
    param(
        [Parameter(Mandatory)]
        [string]$Name,
        [Parameter(Mandatory)]
        [scriptblock]$Action
    )

    try {
        & $Action
        $script:pass++
        $script:results += @{ name = $Name; status = "PASS" }
    }
    catch {
        $script:fail++
        $script:results += @{
            name = $Name
            status = "FAIL"
            detail = "$_"
        }
    }
}

Test-UI "Packaged process is responding" {
    $process = Get-Process -Id $AppPid -ErrorAction Stop
    if (-not $process.Responding) {
        throw "Process $AppPid is not responding."
    }
}

Test-UI "Main window is visible and titled" {
    $windows = winapp ui list-windows -a $AppPid --json 2>$null | ConvertFrom-Json
    $window = $windows | Where-Object { $_.title -eq "Wintodo" } | Select-Object -First 1
    if (-not $window) {
        throw "No visible Wintodo window was found."
    }
}

Test-UI "Interactive app controls have AutomationId" {
    $inspection = winapp ui inspect -a $AppPid --interactive --json 2>$null | ConvertFrom-Json
    $interactive = @($inspection.elements | Where-Object {
        $_.type -match "Button|TextBox|ComboBox|CheckBox|ToggleSwitch|TabItem|Edit" -and
        $_.name -notmatch "Minimize|Maximize|Close|System"
    })
    $missingId = @($interactive | Where-Object { -not $_.automationId })
    if ($missingId.Count -gt 0) {
        $names = ($missingId | ForEach-Object { "$($_.type) '$($_.name)'" }) -join ", "
        throw "Missing AutomationId: $names"
    }
}

Test-UI "Initial screenshot captured" {
    $screenshotPath = Join-Path $artifactDirectory "m0-initial.png"
    winapp ui screenshot -a $AppPid -o $screenshotPath 2>$null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $screenshotPath)) {
        throw "Screenshot capture failed."
    }
}

$resultsPath = Join-Path $artifactDirectory "m0-results.json"
$results | ConvertTo-Json | Set-Content -LiteralPath $resultsPath

Write-Host "Passed: $pass | Failed: $fail"
$results | Where-Object status -eq "FAIL" | ForEach-Object {
    Write-Host "FAIL: $($_.name): $($_.detail)" -ForegroundColor Red
}

if ($fail -gt 0) {
    exit 1
}
