#requires -Version 7.0
<#
.SYNOPSIS
Build and sign a separate Wintodo DEV x64 Native AOT MSIX. Does not register,
launch or trust anything. Only the committed source is included.
#>
[CmdletBinding()]
param([string]$SigningThumbprint, [string]$Python = 'py', [string[]]$PythonArgs = @('-3.13'))

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
function Check-Native([string]$action) { if ($LASTEXITCODE -ne 0) { throw "$action failed ($LASTEXITCODE)." } }
$source = (git -C $repo rev-parse HEAD).Trim()
Check-Native 'Git revision'
$dirty = git -C $repo status --porcelain -- src scripts/dev_preview.py scripts/Build-DevPreview.ps1 Directory.Build.props Directory.Build.targets Directory.Packages.props global.json NuGet.Config
Check-Native 'Check source'
if ($dirty) { throw 'Commit the app and its build inputs first; a preview never silently drops edits.' }
$revision = [int](git -C $repo rev-list --count HEAD)
Check-Native 'Version'
if ($revision -notin 1..65535) { throw 'Unsupported preview version.' }
$sourceIdentity = ([xml](Get-Content -LiteralPath (Join-Path $repo 'src/Wintodo.App/Package.appxmanifest') -Raw)).Package.Identity
$baseVersion = [version]$sourceIdentity.GetAttribute('Version')
$version = "$($baseVersion.Major).$($baseVersion.Minor).$($baseVersion.Build).$revision"
# DEV uses an existing user code-signing key, never the Store publisher or an exported PFX.
$signers = @(Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object {
    $_.Subject -ceq 'CN=AppPublisher' -and $_.HasPrivateKey -and
    $_.NotAfter -gt (Get-Date) -and $_.NotBefore -le (Get-Date) -and
    (!$SigningThumbprint -or $_.Thumbprint -eq $SigningThumbprint)
})
if ($signers.Count -ne 1) { throw 'Select one existing, valid CN=AppPublisher code-signing certificate using -SigningThumbprint.' }
$cert = $signers[0]
$sdk = @(Get-ChildItem -Directory (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin') |
    Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' -and
        (Test-Path (Join-Path $_.FullName 'x64/signtool.exe')) -and
        (Test-Path (Join-Path $_.FullName 'x64/makepri.exe')) } |
    Sort-Object { [version]$_.Name } -Descending)[0]
if (!$sdk) { throw 'Windows SDK SignTool and MakePri are required.' }
$signTool = Join-Path $sdk.FullName 'x64/signtool.exe'
$makePri = Join-Path $sdk.FullName 'x64/makepri.exe'
& $Python @PythonArgs -c 'from PIL import Image'
Check-Native 'Python/Pillow prerequisite'
$common = (git -C $repo rev-parse --path-format=absolute --git-common-dir).Trim()
Check-Native 'Locate shared artifact directory'
$root = Join-Path (Split-Path -Parent $common) 'artifacts/dev-preview'
$build = Join-Path $root ('work/'+[guid]::NewGuid().ToString('N').Substring(0,10))
$stage = Join-Path $build 'src'
$dest = Join-Path $root ("$version-"+$source.Substring(0,8))
if (Test-Path -LiteralPath $dest) { throw "Preview output already exists: $dest" }
$null = New-Item -ItemType Directory -Path $stage
$zip = Join-Path $build 'source.zip'
git -C $repo archive --format=zip "--output=$zip" $source -- src .editorconfig Directory.Build.props Directory.Build.targets Directory.Packages.props NuGet.Config global.json
Check-Native 'Archive reviewed app sources'
Expand-Archive -LiteralPath $zip -DestinationPath $stage
Set-Content -LiteralPath (Join-Path $stage '.wintodo-preview-stage') -Value $source
$helper = Join-Path $PSScriptRoot 'dev_preview.py'
& $Python @PythonArgs $helper $stage $version
Check-Native 'Prepare isolated identity, resources and icons'
$fileName = "Wintodo-DEV-$version-x64.msix"
$package = Join-Path $build $fileName
$project = Join-Path $stage 'src/Wintodo.App/Wintodo.csproj'
$originalPath = $env:PATH
Push-Location -LiteralPath $stage
try {
    # Resolve the pinned SDK from the snapshot and MSVC from the installed VS toolchain.
    $env:PATH = (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer')+';'+$originalPath
    & dotnet restore $project -r win-x64 -p:Platform=x64 -p:Configuration=Release -p:PublishAot=true -p:SelfContained=true -p:WindowsAppSDKSelfContained=true -p:PublishTrimmed=true -p:PublishReadyToRun=false --locked-mode -p:NuGetAudit=true -p:NuGetAuditMode=all -warnaserror
    Check-Native 'Locked AOT restore'
    & winapp package $project --arch x64 --no-sign --self-contained --no-restore -p Platform=x64 -p PublishAot=true -p SelfContained=true -p PublishTrimmed=true -p PublishReadyToRun=false -p TreatWarningsAsErrors=true -p RestoreLockedMode=true --output $package
    Check-Native 'Private AOT package'
} finally {
    Pop-Location
    $env:PATH = $originalPath
}
& $signTool sign /fd SHA256 /s My /sha1 $cert.Thumbprint $package
Check-Native 'Local test signature'
& $signTool verify /pa $package
Check-Native 'Authenticode signature and trust'
$sig = Get-AuthenticodeSignature -LiteralPath $package
if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Thumbprint -ne $cert.Thumbprint) { throw 'Signed package verification failed.' }
& $Python @PythonArgs $helper $stage $version --verify $package --makepri $makePri
Check-Native 'Read back DEV identity, activation, artwork and compiled labels'
& $Python @PythonArgs (Join-Path $PSScriptRoot 'Test-ReleasePackage.py') $package --architecture x64 --expected-identity Wintodo.Hybrid.Dev --expected-publisher 'CN=AppPublisher' --expected-version $version --output (Join-Path $build 'structure.json')
Check-Native 'MSIX structural check'
& $Python @PythonArgs (Join-Path $PSScriptRoot 'Test-ReleasePrivacy.py') $package --require-architecture x64 --private-root $repo --private-root $stage --output (Join-Path $build 'privacy.json')
Check-Native 'Source-path privacy check'
$null = New-Item -ItemType Directory -Path $dest
Copy-Item -LiteralPath $package -Destination $dest
Copy-Item -LiteralPath (Join-Path $stage 'dev-icon.png') -Destination $dest
[IO.File]::WriteAllBytes((Join-Path $dest 'Wintodo-DEV-public.cer'), $cert.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
$homeTrust = Test-Path -LiteralPath ("Cert:\LocalMachine\TrustedPeople\"+$cert.Thumbprint)
[ordered]@{source=$source;file=$fileName;version=$version;architecture='x64';identity='Wintodo.Hybrid.Dev';
    publisher=$cert.Subject;signerThumbprint=$cert.Thumbprint;signerTrustedPeopleHere=$homeTrust;
    sha256=(Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $dest 'build.json') -Encoding utf8NoBOM
Write-Output "Verified: $(Join-Path $dest $fileName)"
