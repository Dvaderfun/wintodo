$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$privateRoot = Join-Path $repositoryRoot ('artifacts/ci-security/' + [Guid]::NewGuid().ToString('N'))
$toolsRoot = Join-Path $privateRoot 'gitleaks-8.30.1'
$archive = Join-Path $privateRoot 'gitleaks.zip'
$expectedHash = 'D29144DEFF3A68AA93CED33DDDF84B7FDC26070ADD4AA0F4513094C8332AFC4E'
New-Item -ItemType Directory -Force -Path $privateRoot | Out-Null
try {
    foreach ($scope in @('history', 'tree')) {
        $summary = Join-Path $repositoryRoot "SafeTestResults/secrets-$scope.json"
        if (Test-Path -LiteralPath $summary) { Remove-Item -LiteralPath $summary }
    }
    Invoke-WebRequest -Uri 'https://github.com/gitleaks/gitleaks/releases/download/v8.30.1/gitleaks_8.30.1_windows_x64.zip' -OutFile $archive
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedHash) {
        throw 'Scanner checksum mismatch.'
    }
    Expand-Archive -LiteralPath $archive -DestinationPath $toolsRoot -Force
    $scanner = Join-Path $toolsRoot 'gitleaks.exe'
    $reports = @('history', 'tree')
    foreach ($scope in $reports) {
        $report = Join-Path $privateRoot "$scope.json"
        # --redact hides matched values; raw reports/logs still contain private
        # attribution and filenames and must never be uploaded.
        $arguments = if ($scope -eq 'history') {
            @('git', $repositoryRoot, '--log-opts=--all')
        } else {
            $archivePath = Join-Path $privateRoot 'source.tar'
            $treeRoot = Join-Path $privateRoot 'source'
            git -C $repositoryRoot archive --format=tar HEAD -o $archivePath
            if ($LASTEXITCODE -ne 0) { throw 'Source archive failed.' }
            New-Item -ItemType Directory -Force -Path $treeRoot | Out-Null
            # Python's data filter rejects archive traversal and escaping links.
            # Do not depend on whichever GNU/Windows tar appears first on PATH.
            python -c 'import sys,tarfile; t=tarfile.open(sys.argv[1]); t.extractall(sys.argv[2],filter="data"); t.close()' `
                $archivePath $treeRoot *> (Join-Path $privateRoot 'archive.log')
            if ($LASTEXITCODE -ne 0) { throw 'Source extraction failed.' }
            @('dir', $treeRoot)
        }
        & $scanner @arguments --redact=100 --no-banner --report-format json --report-path $report *> (Join-Path $privateRoot "$scope.log")
        $scanExit = $LASTEXITCODE
        python (Join-Path $PSScriptRoot 'Test-CiSecurity.py') secrets $report --exit-code $scanExit `
            --output (Join-Path $repositoryRoot "SafeTestResults/secrets-$scope.json")
        if ($LASTEXITCODE -ne 0) { throw 'Secret scan did not pass; review private findings.' }
    }
} catch {
    # Tool/download exceptions may include URLs and machine paths.
    Write-Host 'CI secret scan failed; no finding text was exported.'
    exit 1
}
