$ErrorActionPreference = 'Stop'
$repo = Join-Path ([IO.Path]::GetTempPath()) "mcp-push-tip-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $repo | Out-Null
try {
    git -C $repo init -q
    git -C $repo config user.name 'Hook Test'
    git -C $repo config user.email 'hook@example.invalid'
    [IO.File]::WriteAllText((Join-Path $repo 'sample.txt'), "clean`n")
    git -C $repo add sample.txt
    git -C $repo commit -qm base
    if ($LASTEXITCODE -ne 0) { throw 'Could not create push-tip fixture.' }
    $head = (git -C $repo rev-parse HEAD).Trim()
    $branch = (git -C $repo symbolic-ref HEAD).Trim()
    $line = "$branch $head refs/heads/main $('0' * 40)"

    . (Join-Path $PSScriptRoot 'push-tip.ps1')
    Push-Location $repo
    try {
        Assert-PushTip @($line)
        [IO.File]::WriteAllText((Join-Path $repo 'sample.txt'), "dirty`n")
        $refused = $false
        try { Assert-PushTip @($line) }
        catch { $refused = $true }
        if (-not $refused) { throw 'A dirty worktree passed the push-tip check.' }

        git checkout -- sample.txt
        $refused = $false
        try { Assert-PushTip @("$branch $('1' * 40) refs/heads/main $('0' * 40)") }
        catch { $refused = $true }
        if (-not $refused) { throw 'A push ref unlike HEAD passed the push-tip check.' }

        $scripts = Join-Path $repo 'scripts'
        New-Item -ItemType Directory -Path $scripts | Out-Null
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'push-tip.ps1') -Destination $scripts
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'pre-push.ps1') -Destination $scripts
        [IO.File]::WriteAllText((Join-Path $scripts 'check.ps1'), 'exit 0')
        [IO.File]::WriteAllText((Join-Path $scripts 'pre-push-extra.ps1'),
            "[IO.File]::WriteAllText('pre-push-extra-ran.txt', 'ran')")
        git add scripts
        git commit -qm hooks
        if ($LASTEXITCODE -ne 0) { throw 'Could not commit hook fixture.' }
        $head = (git rev-parse HEAD).Trim()
        $line = "$branch $head refs/heads/main $('0' * 40)"
        $line | & pwsh -NoProfile -File (Join-Path $scripts 'pre-push.ps1')
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath 'pre-push-extra-ran.txt')) {
            throw 'Pre-push did not run the product-specific extra gate.'
        }
        Remove-Item -LiteralPath 'pre-push-extra-ran.txt' -Force
        [IO.File]::WriteAllText((Join-Path $scripts 'check.ps1'), 'exit 7')
        git add scripts/check.ps1
        git commit -qm 'failing gate'
        if ($LASTEXITCODE -ne 0) { throw 'Could not commit failure fixture.' }
        $head = (git rev-parse HEAD).Trim()
        $line = "$branch $head refs/heads/main $('0' * 40)"
        $line | & pwsh -NoProfile -File (Join-Path $scripts 'pre-push.ps1')
        if ($LASTEXITCODE -ne 7 -or (Test-Path -LiteralPath 'pre-push-extra-ran.txt')) {
            throw 'A failing full gate did not fail closed before the product extra gate.'
        }
    }
    finally { Pop-Location }
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
