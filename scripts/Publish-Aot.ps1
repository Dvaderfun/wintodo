param(
    [ValidateSet("x64", "ARM64")]
    [string[]]$Platforms = @("x64", "ARM64"),
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot "src\Wintodo.App\Wintodo.csproj"
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot "publish\aot"
} elseif (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot $OutputDirectory
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$outputRoot = [System.IO.Path]::GetPathRoot($OutputDirectory)
if ($OutputDirectory.TrimEnd('\', '/') -eq $outputRoot.TrimEnd('\', '/')) {
    throw "OutputDirectory cannot be a filesystem root."
}

function Initialize-LinkerDiscovery {
    # ILCompiler locates the MSVC linker by shelling out to vswhere.exe and taking
    # whatever comes back on stdout. When vswhere is not on PATH the shell's "not
    # recognized" text lands in the linker command line instead, and the ARM64
    # publish fails with MSB3073 / exit code 123. Put the installer directory on
    # PATH so the script works from a plain shell, not only a developer prompt.
    if (Get-Command "vswhere.exe" -ErrorAction SilentlyContinue) {
        return
    }

    $installerDirectory = Join-Path ${env:ProgramFiles(x86)} `
        "Microsoft Visual Studio\Installer"
    if (-not (Test-Path -LiteralPath (Join-Path $installerDirectory "vswhere.exe"))) {
        throw "vswhere.exe was not found; the Native AOT linker cannot be located."
    }

    $env:PATH = "$installerDirectory;$env:PATH"
}

function Get-DirectorySize {
    param([Parameter(Mandatory)][string]$Path)

    $bytes = (Get-ChildItem -LiteralPath $Path -File -Recurse |
        Measure-Object -Property Length -Sum).Sum
    return [long]($bytes ?? 0)
}

function Copy-PackageLayoutFiles {
    param(
        [Parameter(Mandatory)][string]$Platform,
        [Parameter(Mandatory)][string]$RuntimeIdentifier,
        [Parameter(Mandatory)][string]$PublishDirectory
    )

    $buildDirectory = Join-Path $repositoryRoot (
        "src\Wintodo.App\bin\$Platform\Release\" +
        "net10.0-windows10.0.26100.0\$RuntimeIdentifier"
    )
    $manifest = Join-Path $buildDirectory "AppxManifest.xml"
    if (-not (Test-Path -LiteralPath $manifest)) {
        throw "Generated package manifest was not found for $Platform."
    }

    Copy-Item -LiteralPath $manifest -Destination $PublishDirectory -Force
    Copy-Item `
        -LiteralPath (Join-Path $repositoryRoot "src\Wintodo.App\Assets") `
        -Destination $PublishDirectory `
        -Recurse `
        -Force

    $buildRoot = (Resolve-Path -LiteralPath $buildDirectory).Path
    Get-ChildItem -LiteralPath $buildRoot -Filter "*.xbf" -File -Recurse |
        ForEach-Object {
            $relativePath = [System.IO.Path]::GetRelativePath(
                $buildRoot,
                $_.FullName)
            $destination = Join-Path $PublishDirectory $relativePath
            New-Item `
                -ItemType Directory `
                -Force `
                -Path (Split-Path -Parent $destination) | Out-Null
            Copy-Item -LiteralPath $_.FullName -Destination $destination -Force
        }
}

Initialize-LinkerDiscovery

$results = foreach ($platform in $Platforms) {
    $runtimeIdentifier = if ($platform -eq "ARM64") {
        "win-arm64"
    } else {
        "win-x64"
    }
    $publishDirectory = [System.IO.Path]::GetFullPath(
        (Join-Path $OutputDirectory $runtimeIdentifier))
    $requiredPrefix = $OutputDirectory.TrimEnd('\', '/') +
        [System.IO.Path]::DirectorySeparatorChar
    if (-not $publishDirectory.StartsWith(
            $requiredPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Publish directory escaped OutputDirectory."
    }
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force `
        -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $publishDirectory | Out-Null

    dotnet publish $projectPath `
        -c Release `
        -r $runtimeIdentifier `
        -p:Platform=$platform `
        -p:PublishAot=true `
        -p:SelfContained=true `
        -p:WindowsAppSDKSelfContained=true `
        -p:PublishTrimmed=true `
        -p:PublishReadyToRun=false `
        -warnaserror `
        --output $publishDirectory | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Native AOT publish failed for $platform."
    }

    $executable = Join-Path $publishDirectory "Wintodo.exe"
    if (-not (Test-Path -LiteralPath $executable)) {
        throw "Native AOT output for $platform does not contain Wintodo.exe."
    }

    Copy-PackageLayoutFiles `
        -Platform $platform `
        -RuntimeIdentifier $runtimeIdentifier `
        -PublishDirectory $publishDirectory

    $binaryBytes = (Get-Item -LiteralPath $executable).Length
    $publishBytes = Get-DirectorySize -Path $publishDirectory
    [pscustomobject]@{
        Platform = $platform
        RuntimeIdentifier = $runtimeIdentifier
        BinarySizeMb = [Math]::Round($binaryBytes / 1MB, 2)
        PublishSizeMb = [Math]::Round($publishBytes / 1MB, 2)
        # Locations in this public summary are relative to summary.json.
        PublishDirectory = $runtimeIdentifier
    }
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$summaryPath = Join-Path $OutputDirectory "summary.json"
$results | ConvertTo-Json | Set-Content -LiteralPath $summaryPath -Encoding utf8
$results | Format-Table -AutoSize
Write-Host "Summary: summary.json (relative to OutputDirectory)"
