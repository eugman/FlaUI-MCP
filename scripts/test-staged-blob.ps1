$ErrorActionPreference = 'Stop'
$repo = Join-Path ([IO.Path]::GetTempPath()) "mcp-staged-blob-$([guid]::NewGuid().ToString('N'))"
$source = Join-Path $repo "O'Hare [draft].cs"
$output = Join-Path $repo 'staged.cs'
New-Item -ItemType Directory -Path $repo | Out-Null
try {
    git -C $repo init -q
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize fixture repository.' }
    git -C $repo config core.autocrlf false
    if ($LASTEXITCODE -ne 0) { throw 'Could not set fixture line-ending mode.' }
    $staged = [byte[]](0xEF, 0xBB, 0xBF, 0x61, 0x0D, 0x0A, 0x62, 0x20, 0x0D, 0x0A)
    [IO.File]::WriteAllBytes($source, $staged)
    git -C $repo add -- "O'Hare [draft].cs"
    if ($LASTEXITCODE -ne 0) { throw 'Could not stage fixture.' }
    [IO.File]::WriteAllText($source, "different working copy`n")

    . (Join-Path $PSScriptRoot 'staged-blob.ps1')
    Push-Location $repo
    try { Write-StagedBlob -Path "O'Hare [draft].cs" -Destination $output }
    finally { Pop-Location }

    if ([Convert]::ToHexString([IO.File]::ReadAllBytes($output)) -cne [Convert]::ToHexString($staged)) {
        throw 'Staged extraction changed BOM, CRLF, trailing space, or selected the working copy.'
    }
    $hook = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'pre-commit.ps1')
    if ($hook -notmatch 'Write-StagedBlob') { throw 'Pre-commit no longer checks the staged bytes.' }
}
finally {
    $resolved = [IO.Path]::GetFullPath($repo)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (-not $resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to remove a fixture outside the temporary directory.'
    }
    Remove-Item -LiteralPath $repo -Recurse -Force
}
$global:LASTEXITCODE = 0
