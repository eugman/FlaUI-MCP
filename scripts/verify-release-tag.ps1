param(
    [Parameter(Mandatory)][string] $Tag,
    [Parameter(Mandatory)][string] $ExpectedCommit
)

$ErrorActionPreference = 'Stop'

if ($Tag -notmatch '^v\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
    throw "Invalid release tag '$Tag'."
}
if ($ExpectedCommit -notmatch '^[0-9a-fA-F]{40}$') {
    throw 'Expected commit must be a full Git SHA.'
}

$head = git rev-parse --verify HEAD
if ($LASTEXITCODE -ne 0) { throw 'Unable to resolve the checked-out commit.' }

git show-ref --verify --quiet "refs/tags/$Tag"
if ($LASTEXITCODE -ne 0) { throw "Release tag '$Tag' does not exist in this checkout." }

$tagCommit = git rev-parse --verify "refs/tags/$Tag^{commit}"
if ($LASTEXITCODE -ne 0) { throw "Release tag '$Tag' does not resolve to a commit." }

if ($head -ine $ExpectedCommit -or $tagCommit -ine $ExpectedCommit) {
    throw "Release tag '$Tag' resolves to $tagCommit, checkout is $head, but the workflow commit is $ExpectedCommit."
}

Write-Host "Verified release tag '$Tag' and checkout both resolve to $ExpectedCommit."
