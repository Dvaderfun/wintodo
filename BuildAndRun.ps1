<#
.SYNOPSIS
Builds and runs the Wintodo app through project-mode `winapp run`.

.DESCRIPTION
Thin wrapper kept so there is one obvious entry point. WinApp CLI 0.7+ owns
restore, build, architecture selection, package registration, and launch. The
WinUI analyzer is a NuGet package reference on the app project, so plain
`winapp run`, `dotnet build`, the IDE, and CI all load it; nothing is injected
here.

Arguments are forwarded to `winapp run` unchanged. Attached runs get
`--debug-output` by default (first-chance exceptions and stowed-exception
triage). Invoke attached runs asynchronously: they stay attached to the app.

.EXAMPLE
.\BuildAndRun.ps1                                  # Debug, attached, --debug-output
.\BuildAndRun.ps1 --detach --json                  # launch and return the PID as JSON
.\BuildAndRun.ps1 -c Release --arch arm64
.\BuildAndRun.ps1 --symbols                        # richer native crash stacks
.\BuildAndRun.ps1 --args "--database-path C:\temp\scratch.db"
#>

$ErrorActionPreference = 'Stop'
$minimumWinAppVersion = [version]'0.7.0'
$project = Join-Path $PSScriptRoot 'src\Wintodo.App\Wintodo.csproj'
$arguments = @($args)

$winapp = Get-Command winapp -ErrorAction SilentlyContinue
if (-not $winapp) {
    Write-Error "WinApp CLI $minimumWinAppVersion or later is required. Run /winui-setup, then retry."
    exit 1
}

$installedWinAppVersion = $null
foreach ($line in @(& winapp --version 2>$null)) {
    $match = [regex]::Match([string]$line, '^\s*v?(?<version>\d+\.\d+\.\d+)')
    if ($match.Success) {
        $installedWinAppVersion = [version]$match.Groups['version'].Value
    }
}
if (-not $installedWinAppVersion -or $installedWinAppVersion -lt $minimumWinAppVersion) {
    Write-Error "WinApp CLI $minimumWinAppVersion or later is required; found '$installedWinAppVersion'. Run /winui-setup to upgrade."
    exit 1
}

$noDebugOutput = $arguments | Where-Object {
    $_ -match '^(?i)--(debug-output|json|no-launch|detach)(=.*)?$'
}
$runArgs = @($project) + $arguments
if (-not $noDebugOutput) {
    $runArgs += '--debug-output'
}

& winapp run @runArgs
exit $LASTEXITCODE
