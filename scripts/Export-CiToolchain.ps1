param(
    [ValidateSet('x64', 'arm64')][string]$Architecture = 'x64',
    [switch]$RequireAot
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repositoryRoot 'SafeTestResults/toolchain.json'

function ConvertTo-CiToolBinaryRecord {
    param([Parameter(Mandatory)]$VersionInfo, [Parameter(Mandatory)][string]$Sha256)

    # Numeric version fields exclude arbitrary vendor descriptions and machine paths.
    $fileVersion = [string]$VersionInfo.FileVersionRaw
    $productVersion = [string]$VersionInfo.ProductVersionRaw
    if ($fileVersion -notmatch '^\d+(\.\d+){3}$' -or $fileVersion -eq '0.0.0.0' -or
        $productVersion -notmatch '^\d+(\.\d+){3}$' -or $productVersion -eq '0.0.0.0' -or
        $Sha256 -notmatch '^[a-fA-F0-9]{64}$') {
        throw 'WINTODO_TOOL_BINARY_EVIDENCE_INVALID'
    }
    return [ordered]@{
        FileVersion = $fileVersion
        ProductVersion = $productVersion
        Sha256 = $Sha256.ToLowerInvariant()
    }
}

function Get-CiToolBinaryRecord {
    param([Parameter(Mandatory)][string]$Path)

    try {
        $file = Get-Item -LiteralPath $Path
        if ($file.PSIsContainer) { throw 'Expected a file.' }
        return ConvertTo-CiToolBinaryRecord -VersionInfo $file.VersionInfo `
            -Sha256 (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    } catch {
        throw 'WINTODO_TOOL_BINARY_EVIDENCE_INVALID'
    }
}

function Get-CiConfiguredSdkTools {
    param([Parameter(Mandatory)]$Properties, [Parameter(Mandatory)][string]$PackageRoot)

    $tools = [ordered]@{}
    $packagePrefix = [IO.Path]::GetFullPath($PackageRoot).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    foreach ($selection in @(
        @{ Property = 'MakeAppxExeFullPath'; Tool = 'makeappx.exe' },
        @{ Property = 'MakePriExeFullPath'; Tool = 'makepri.exe' },
        @{ Property = 'RcExeFullPath'; Tool = 'rc.exe' }
    )) {
        $path = [string]$Properties[$selection.Property]
        if ([string]::IsNullOrWhiteSpace($path) -or -not [IO.Path]::IsPathFullyQualified($path) -or
            [IO.Path]::GetFileName($path) -ine $selection.Tool) {
            throw 'WINTODO_SDK_TOOL_SELECTION_INVALID'
        }
        $record = Get-CiToolBinaryRecord -Path $path
        $record.Source = if ([IO.Path]::GetFullPath($path).StartsWith($packagePrefix, [StringComparison]::OrdinalIgnoreCase)) {
            'restored-sdk-buildtools'
        } else { 'external-override' }
        $tools[$selection.Tool] = $record
    }
    return $tools
}

$stage = 'sdk'
try {
    if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output }
    $sdk = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $sdk -notmatch '^\d+\.\d+\.\d+$' -or [version]$sdk -lt [version]'10.0.403') {
        throw 'Supported patched SDK was not resolved.'
    }
    $sdkPolicy = Get-Content -LiteralPath (Join-Path $repositoryRoot 'global.json') -Raw | ConvertFrom-Json
    if ($sdkPolicy.sdk.rollForward -notin @('disable', 'patch', 'feature', 'minor', 'major', 'latestPatch', 'latestFeature', 'latestMinor', 'latestMajor')) {
        throw 'SDK roll-forward policy is unsupported.'
    }
    $stage = 'winapp'
    [xml]$centralPackages = Get-Content (Join-Path $repositoryRoot 'Directory.Packages.props')
    $winappVersion = ($centralPackages.Project.ItemGroup.PackageVersion |
        Where-Object Include -eq 'Microsoft.Windows.SDK.BuildTools.WinApp').Version
    $nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
    $winapp = Join-Path $nugetRoot "microsoft.windows.sdk.buildtools.winapp/$winappVersion/tools/win-x64/winapp.exe"
    if (-not (Test-Path -LiteralPath $winapp)) { throw 'Restored WinApp CLI is missing.' }
    $cliVersionOutput = & $winapp --version
    if ($LASTEXITCODE -ne 0 -or ($cliVersionOutput -join ' ') -notmatch [regex]::Escape($winappVersion)) {
        throw 'Restored WinApp CLI version could not be verified.'
    }
    $winappBinary = Get-CiToolBinaryRecord -Path $winapp
    $stage = 'native-tools'
    $installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer'
    $vswhere = Join-Path $installer 'vswhere.exe'
    $component = if ($Architecture -eq 'arm64') { 'Microsoft.VisualStudio.Component.VC.Tools.ARM64' }
                 else { 'Microsoft.VisualStudio.Component.VC.Tools.x86.x64' }
    $visualStudio = & $vswhere -latest -products '*' -requires $component -format json | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or @($visualStudio).Count -ne 1) { throw 'Native toolchain is unavailable.' }
    $msvc = (Get-Content (Join-Path $visualStudio[0].installationPath 'VC/Auxiliary/Build/Microsoft.VCToolsVersion.default.txt') -Raw).Trim()
    if ($msvc -notmatch '^\d+\.\d+\.\d+$' -or $visualStudio[0].installationVersion -notmatch '^\d+(\.\d+){2,3}$') {
        throw 'Native tool version evidence is invalid.'
    }
    $linker = Join-Path $visualStudio[0].installationPath "VC/Tools/MSVC/$msvc/bin/Hostx64/$Architecture/link.exe"
    if (-not (Test-Path -LiteralPath $linker)) { throw 'Target native linker is missing.' }
    $nativeTools = [ordered]@{}
    foreach ($tool in @('cl.exe', 'link.exe', 'lib.exe')) {
        $nativeTools[$tool] = Get-CiToolBinaryRecord -Path (Join-Path (Split-Path -Parent $linker) $tool)
    }
    $stage = 'windows-sdk'
    $windowsKits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10'
    $windowsSdk = Get-ChildItem -LiteralPath (Join-Path $windowsKits 'Include') -Directory |
        Where-Object { $_.Name -match '^10\.0\.\d+\.0$' -and [version]$_.Name -ge [version]'10.0.26100.0' } |
        Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if (-not $windowsSdk) { throw 'Supported Windows SDK is missing.' }
    $installedSdkTools = [ordered]@{}
    foreach ($tool in @('rc.exe', 'makeappx.exe', 'signtool.exe')) {
        $installedSdkTools[$tool] = Get-CiToolBinaryRecord -Path (Join-Path $windowsKits "bin/$($windowsSdk.Name)/x64/$tool")
    }
    $stage = 'source'
    $commit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[a-f0-9]{40}$') { throw 'Source commit was not resolved.' }
    $stage = 'packages'
    $packages = [ordered]@{}
    $assets = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src/Wintodo.App/obj/project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
    foreach ($library in ($assets.libraries.Keys | Sort-Object)) {
        if ($assets.libraries[$library].type -ne 'package') { continue }
        $parts = $library.Split('/')
        if ($parts.Count -ne 2 -or $parts[0] -notmatch '^[A-Za-z0-9_.-]+$' -or $parts[1] -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
            throw 'Package version evidence contains an unsupported value.'
        }
        $packages[$parts[0]] = $parts[1]
    }
    $targetRuntime = $null
    if ($RequireAot) {
        $stage = 'aot-packages'
        $rid = "win-$Architecture"
        if (-not ($assets.targets.Keys | Where-Object { $_.EndsWith("/$rid", [StringComparison]::Ordinal) })) {
            throw 'Target-specific package graph is missing.'
        }
        foreach ($compiler in @('Microsoft.DotNet.ILCompiler', "runtime.$rid.Microsoft.DotNet.ILCompiler")) {
            if (-not $packages.Contains($compiler) -or [version]$packages[$compiler] -lt [version]'10.0.14') {
                throw 'Patched target AOT compiler was not resolved.'
            }
        }
        $runtimeName = "Microsoft.NETCore.App.Runtime.$rid"
        $runtimeDownloads = @($assets.project.frameworks.Values | ForEach-Object { $_.downloadDependencies } |
            Where-Object { $_.name -eq $runtimeName })
        if ($runtimeDownloads.Count -ne 1 -or $runtimeDownloads[0].version -notmatch '^\[(\d+\.\d+\.\d+), \1\]$') {
            throw 'Exact target runtime package was not resolved.'
        }
        $targetRuntime = $Matches[1]
        if ([version]$targetRuntime -lt [version]'10.0.14' -or
            -not (Test-Path -LiteralPath (Join-Path $nugetRoot "$($runtimeName.ToLowerInvariant())/$targetRuntime/.nupkg.metadata"))) {
            throw 'Patched target runtime package is unavailable.'
        }
    }
    $stage = 'project-tool-selection'
    # Evaluation records configured paths without executing targets, restoring or
    # claiming that these binaries were invoked to build a delivered package.
    $project = Join-Path $repositoryRoot 'src/Wintodo.App/Wintodo.csproj'
    $toolProperties = & dotnet msbuild $project -nologo "-p:Platform=$Architecture" `
        -p:Configuration=Release "-p:RuntimeIdentifier=win-$Architecture" `
        "-p:PublishAot=$([bool]$RequireAot)" `
        -getProperty:MakeAppxExeFullPath,MakePriExeFullPath,RcExeFullPath 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'Project packaging tool configuration could not be evaluated.' }
    $toolConfiguration = ($toolProperties -join "`n") | ConvertFrom-Json -AsHashtable
    $buildToolsRoot = Join-Path $nugetRoot "microsoft.windows.sdk.buildtools/$($packages['Microsoft.Windows.SDK.BuildTools'])"
    $configuredSdkTools = Get-CiConfiguredSdkTools -Properties $toolConfiguration.Properties -PackageRoot $buildToolsRoot
    $evidence = [ordered]@{
        SourceCommit = $commit
        Architecture = $Architecture
        DotnetSdk = $sdk
        SdkRollForward = $sdkPolicy.sdk.rollForward
        RunnerImage = if ($env:ImageOS -match '^[a-zA-Z0-9_-]+$') { $env:ImageOS } else { 'local' }
        RunnerImageVersion = if ($env:ImageVersion -match '^[0-9.]+$') { $env:ImageVersion } else { 'unavailable' }
        VisualStudio = $visualStudio[0].installationVersion
        Msvc = $msvc
        MsvcEvidenceScope = 'latest-required-component-default-tools-available; invocation-not-verified'
        MsvcToolsAvailable = $nativeTools
        WindowsSdkAvailable = $windowsSdk.Name
        WindowsSdkToolsAvailable = $installedSdkTools
        ProjectToolConfiguration = [ordered]@{
            Scope = 'evaluated-release-configuration; invocation-not-verified'
            PublishAot = [bool]$RequireAot
            Tools = $configuredSdkTools
        }
        WinAppCli = $winappVersion
        WinAppBinary = $winappBinary
        AotGraphVerified = [bool]$RequireAot
        TargetRuntime = $targetRuntime
        Packages = $packages
    }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $output) | Out-Null
    $evidence | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $output -Encoding utf8
    Write-Host 'Toolchain and source versions verified; evidence: SafeTestResults/toolchain.json'
} catch {
    Write-Host "Toolchain verification failed at $stage; no machine paths were exported."
    exit 1
}
