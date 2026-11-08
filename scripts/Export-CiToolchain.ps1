param(
    [ValidateSet('x64', 'arm64')][string]$Architecture = 'x64',
    [switch]$RequireAot
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repositoryRoot 'SafeTestResults/toolchain.json'
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
    $stage = 'windows-sdk'
    $windowsKits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10'
    $windowsSdk = Get-ChildItem -LiteralPath (Join-Path $windowsKits 'Include') -Directory |
        Where-Object { $_.Name -match '^10\.0\.\d+\.0$' -and [version]$_.Name -ge [version]'10.0.26100.0' } |
        Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if (-not $windowsSdk) { throw 'Supported Windows SDK is missing.' }
    foreach ($tool in @('rc.exe', 'makeappx.exe', 'signtool.exe')) {
        if (-not (Test-Path -LiteralPath (Join-Path $windowsKits "bin/$($windowsSdk.Name)/x64/$tool"))) {
            throw 'A required Windows SDK tool is missing.'
        }
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
    $evidence = [ordered]@{
        SourceCommit = $commit
        Architecture = $Architecture
        DotnetSdk = $sdk
        SdkRollForward = $sdkPolicy.sdk.rollForward
        RunnerImage = if ($env:ImageOS -match '^[a-zA-Z0-9_-]+$') { $env:ImageOS } else { 'local' }
        RunnerImageVersion = if ($env:ImageVersion -match '^[0-9.]+$') { $env:ImageVersion } else { 'unavailable' }
        VisualStudio = $visualStudio[0].installationVersion
        Msvc = $msvc
        WindowsSdkAvailable = $windowsSdk.Name
        WinAppCli = $winappVersion
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
