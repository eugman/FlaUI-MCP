# Check that the full working-tree gate will inspect the exact commit Git is about to push.
function Assert-PushTip {
    param([Parameter(Mandatory)][string[]]$Lines)

    $head = (git rev-parse --verify HEAD)
    if ($LASTEXITCODE -ne 0 -or -not $head) { throw 'Cannot determine HEAD for the push gate.' }
    $status = @(git status --porcelain=v1 --untracked-files=normal)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine worktree status for the push gate.' }
    if ($status.Count -gt 0) { throw 'The push gate needs a clean worktree so check.ps1 tests exactly the pushed bytes.' }

    if ($Lines.Count -eq 0) { throw 'No push ref was supplied to the gate.' }
    foreach ($line in $Lines) {
        $parts = $line.Trim() -split '\s+'
        if ($parts.Count -ne 4) { throw 'Cannot parse a push ref update.' }
        $localRef, $localSha = $parts[0..1]
        if ($localSha -notmatch '^[0-9a-fA-F]{40,64}$') { throw 'Cannot verify a push ref update.' }
        if ($localSha -match '^0+$') { continue } # A ref deletion sends no new code.
        $actual = (git rev-parse --verify "$localRef^{object}" 2>$null)
        if ($LASTEXITCODE -ne 0 -or $actual -cne $localSha) { throw 'The push ref no longer names its advertised object.' }
        $commit = (git rev-parse --verify "$localSha^{commit}" 2>$null)
        if ($LASTEXITCODE -ne 0 -or $commit -cne $head) {
            throw 'The pushed ref is not HEAD; check out that commit and rerun the gate before pushing.'
        }
    }
}
