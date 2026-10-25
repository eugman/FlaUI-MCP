# Verify the release gate both as a Git operation and as a wired workflow step.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$guard = Join-Path $PSScriptRoot 'verify-release-tag.ps1'
$workflow = Get-Content -Raw -LiteralPath (Join-Path $repo '.github/workflows/release.yml')
if ([regex]::Matches($workflow, 'scripts/verify-release-tag\.ps1').Count -ne 2) {
    throw 'Both release jobs must verify the requested tag before publishing.'
}
if ([regex]::Matches($workflow, 'fetch-depth:\s*0').Count -lt 2) {
    throw 'Both release jobs must fetch tags.'
}

$temporary = Join-Path ([IO.Path]::GetTempPath()) "mcp-release-tag-test-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    Push-Location $temporary
    try {
        git init -q
        git config user.name 'Release Test'
        git config user.email 'release-test@example.invalid'
        Set-Content -LiteralPath sample.txt -Value 'first'
        git add sample.txt
        git commit -qm 'first'
        $first = git rev-parse HEAD
        git tag -a v1.0.0 -m 'release'
        & $guard -Tag v1.0.0 -ExpectedCommit $first
        Set-Content -LiteralPath sample.txt -Value 'second'
        git add sample.txt
        git commit -qm 'second'
        $second = git rev-parse HEAD
        foreach ($case in @(
            @{ tag = 'v1.0.0'; commit = $second },
            @{ tag = 'v1.0.0'; commit = $first },
            @{ tag = 'v9.9.9'; commit = $second }
        )) {
            $rejected = $false
            try { & $guard -Tag $case.tag -ExpectedCommit $case.commit }
            catch { $rejected = $true }
            if (-not $rejected) { throw "Mismatched tag/checkout was accepted: $($case.tag)." }
        }
    }
    finally { Pop-Location }
}
finally {
    # The target was created by this test under the system temp directory.
    $resolved = [IO.Path]::GetFullPath($temporary)
    if (-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),
                                  [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to remove a test directory outside temp.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
$global:LASTEXITCODE = 0
