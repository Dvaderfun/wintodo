$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$privateRoot = Join-Path $repositoryRoot ('artifacts/ci-security/' + [Guid]::NewGuid().ToString('N'))
$archive = Join-Path $privateRoot 'gitleaks.zip'
New-Item -ItemType Directory -Force -Path $privateRoot | Out-Null
try {
    foreach ($scope in @('history', 'tree')) {
        $summary = Join-Path $repositoryRoot "SafeTestResults/secrets-$scope.json"
        if (Test-Path -LiteralPath $summary) { Remove-Item -LiteralPath $summary }
    }
    $pin = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'gitleaks-pin.json') | ConvertFrom-Json
    if ($pin.version -notmatch '^\d+\.\d+\.\d+$' -or $pin.sha256 -cnotmatch '^[0-9a-f]{64}$') {
        throw 'Invalid scanner pin.'
    }
    $toolsRoot = Join-Path $privateRoot "gitleaks-$($pin.version)"
    Invoke-WebRequest -Uri "https://github.com/gitleaks/gitleaks/releases/download/v$($pin.version)/gitleaks_$($pin.version)_windows_x64.zip" -OutFile $archive
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $pin.sha256) {
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
