# Exercise RemoveWorktree against real Git metadata after its folder disappears.
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'mutation.ps1'
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors) { throw "Could not parse ${source}: $($parseErrors -join '; ')" }
$function = $ast.Find({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'RemoveWorktree'
}, $true)
if (-not $function) { throw 'RemoveWorktree function was not found.' }
Invoke-Expression $function.Extent.Text

$tempRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) "fla-ui-registration-$([guid]::NewGuid().ToString('N'))"))
$repo = Join-Path $tempRoot 'repo'
$root = $repo
$work = Join-Path $repo 'local/build-background/worktree'
$other = Join-Path $tempRoot 'unrelated-offline-worktree'
New-Item -ItemType Directory -Path $repo | Out-Null
function IsRegistered($path) {
    $expected = [IO.Path]::GetFullPath($path)
    $listing = git -C $repo worktree list --porcelain
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect temporary worktree registrations.' }
    return @($listing | Where-Object {
        $_.StartsWith('worktree ') -and
        [IO.Path]::GetFullPath($_.Substring(9)) -ieq $expected
    }).Count -gt 0
}
try {
    git -C $repo init -q
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize the temporary Git repo.' }
    git -C $repo -c user.name=Test -c user.email=test@example.invalid commit --allow-empty -qm seed
    if ($LASTEXITCODE -ne 0) { throw 'Could not commit the temporary Git fixture.' }
    New-Item -ItemType Directory -Path (Split-Path -Parent $work) -Force | Out-Null
    git -C $repo worktree add --detach $work HEAD 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not register the temporary worktree.' }
    git -C $repo worktree add --detach $other HEAD 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not register the unrelated worktree.' }

    # These folders disappeared while offline. Cleanup owns only $work.
    $tempPrefix = [IO.Path]::TrimEndingDirectorySeparator($tempRoot) + [IO.Path]::DirectorySeparatorChar
    if (-not [IO.Path]::GetFullPath($work).StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "The test worktree escaped its temporary root: $work"
    }
    Remove-Item -LiteralPath $work -Recurse -Force
    Remove-Item -LiteralPath $other -Recurse -Force
    Write-Host 'Checking cleanup after the owned worktree folder disappeared.'
    Push-Location $repo
    try { RemoveWorktree $work }
    finally { Pop-Location }

    if (IsRegistered $work) { throw 'RemoveWorktree left an orphaned Git worktree registration.' }
    if (-not (IsRegistered $other)) { throw 'RemoveWorktree pruned an unrelated offline worktree.' }

    # A locked entry makes `worktree remove --force` fail. The cleanup must
    # remove its own registration without pruning unrelated locked entries.
    git -C $repo worktree add --detach $work HEAD 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not register the locked worktree fixture.' }
    git -C $repo worktree lock $work
    if ($LASTEXITCODE -ne 0) { throw 'Could not lock the temporary worktree.' }
    Write-Host 'Checking cleanup of a locked owned worktree.'
    Push-Location $repo
    try { RemoveWorktree $work }
    finally { Pop-Location }
    if (IsRegistered $work) { throw 'RemoveWorktree left its locked Git worktree registration.' }
    if (-not (IsRegistered $other)) { throw 'RemoveWorktree pruned an unrelated worktree during locked cleanup.' }
    Write-Host 'Mutation cleanup removed its registrations and preserved the unrelated offline worktree.'
}
finally {
    $safePrefix = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath([IO.Path]::GetTempPath())) + [IO.Path]::DirectorySeparatorChar
    if (-not $tempRoot.StartsWith($safePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "The test root escaped the temporary directory: $tempRoot"
    }
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
}
$global:LASTEXITCODE = 0
