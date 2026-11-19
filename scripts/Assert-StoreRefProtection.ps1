# Called only by the tag job. Maintenance builds have no Store submission authority.
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_EVENT_NAME -cne 'push' -or $env:GITHUB_REF -cnotlike 'refs/tags/v*' -or
    $env:GITHUB_REF_PROTECTED -cne 'true') {
    throw 'Store candidates require a protected GitHub release tag.'
}
$protected = gh api 'repos/{owner}/{repo}/branches/main' --jq '.protected'
if ($LASTEXITCODE -ne 0 -or $protected -cne 'true') {
    throw 'GitHub must confirm that main is protected before building a tagged Store candidate.'
}
