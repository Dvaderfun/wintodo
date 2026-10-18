$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root 'tests\ui\artifacts\share-source'
New-Item -ItemType Directory -Force $output | Out-Null
$vcvars = 'C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Auxiliary\Build\vcvarsall.bat'
if (-not (Test-Path $vcvars)) { throw 'Share source needs the existing MSVC x64 build tools.' }
$source = Join-Path $root 'tests\fixtures\share-source\share-source.cpp'
$batch = Join-Path $output 'build.cmd'
@"
@call "$vcvars" x64
@if errorlevel 1 exit /b 1
@cl /nologo /std:c++20 /EHsc /MT /W4 /WX /DUNICODE /D_UNICODE "$source" /Fe:"$output\share-source.exe" /Fo:"$output\share-source.obj" /link windowsapp.lib user32.lib shell32.lib /SUBSYSTEM:WINDOWS
"@ | Set-Content $batch
& cmd.exe /c $batch
if ($LASTEXITCODE -ne 0) { throw 'Share source compilation failed.' }
