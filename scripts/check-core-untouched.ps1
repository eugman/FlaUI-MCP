# Fails when a product repo changes files owned by the shared core (see scripts/core-paths.txt).
# Core changes belong in FlaUI-MCP-Core; product repos receive them with `git merge core/main`.
# In the core repo itself there is nothing to check.
param([switch]$Staged, [string]$CoreRevision = $env:CORE_REVISION)
if (-not $CoreRevision) { $CoreRevision = "core/main" }

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# A product repo has a host project next to the core one: src/FlaUI.Mcp.<Product>.
$isProduct = [bool](Get-ChildItem (Join-Path $root 'src') -Directory | Where-Object Name -like 'FlaUI.Mcp.?*')
if (-not $isProduct) { exit 0 }

if ($CoreRevision -eq 'core/main' -and (git remote) -notcontains 'core') {
    Write-Host 'This product repo has no `core` remote, so core ownership cannot be checked.' -ForegroundColor Red
    Write-Host 'Run: pwsh scripts/setup.ps1'
    exit 1
}

if ($Staged) {
    # A merge commit is how core changes arrive. Anything extra it slips in is caught on push and in CI,
    # which compare the whole branch with core/main.
    git rev-parse -q --verify MERGE_HEAD *> $null
    if ($LASTEXITCODE -eq 0) { exit 0 }
    # --no-renames: a file moved out of a core folder must count as a change to the core.
    $changed = git diff --cached --name-only --no-renames
    $base = 'HEAD'
}
elseif ($CoreRevision -eq 'core/main') {
    git rev-parse -q --verify core/main *> $null
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'core/main is not fetched. Run: git fetch core' -ForegroundColor Red
        exit 1
    }

    # Compare with the point this branch last took from core/main, so a core release that is not merged
    # yet does not fail a branch that touched no core file.
    $base = git merge-base HEAD core/main
    if ($LASTEXITCODE -ne 0 -or -not $base) {
        # Without a merge-base the diff below would compare nothing and pass any change.
        Write-Host 'This branch has no common history with core/main, so core ownership cannot be checked.' -ForegroundColor Red
        Write-Host 'Run: git fetch core; git merge core/main'
        exit 1
    }

    $changed = git diff --name-only --no-renames $base HEAD
}
else {
    # An explicit revision (CORE_REVISION) is a local Core commit that is not on core/main yet.
    git rev-parse -q --verify "$CoreRevision^{commit}" *> $null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Core revision $CoreRevision is not available. Fetch the intended core commit." -ForegroundColor Red
        exit 1
    }

    # It must be a Core commit: a product commit (such as HEAD) contains its src/FlaUI.Mcp.<Product> host,
    # and comparing HEAD with itself would pass any change.
    $productHosts = git ls-tree --name-only "$CoreRevision" src/ | Where-Object { $_ -like 'src/FlaUI.Mcp.?*' }
    if ($productHosts) {
        Write-Host "Core revision $CoreRevision contains product code ($($productHosts -join ', ')); it is not a Core commit." -ForegroundColor Red
        exit 1
    }

    git merge-base --is-ancestor $CoreRevision HEAD
    if ($LASTEXITCODE -ne 0) {
        Write-Host "The selected core revision is not merged into this branch: $CoreRevision" -ForegroundColor Red
        exit 1
    }

    $changed = git diff --name-only --no-renames $CoreRevision HEAD
    $base = $CoreRevision
}

# Read the owned paths from the base, not from the branch under test: a branch that deletes lines from
# core-paths.txt must not hide its own changes to those paths.
$corePaths = git show "${base}:scripts/core-paths.txt" |
    Where-Object { $_ -and -not $_.StartsWith('#') }
if ($LASTEXITCODE -ne 0 -or -not $corePaths) {
    Write-Host "Cannot read scripts/core-paths.txt at $base." -ForegroundColor Red
    exit 1
}

$touched = $changed | Where-Object { $file = $_; $corePaths | Where-Object { $file.StartsWith($_) } }
if ($touched) {
    Write-Host 'These files belong to the shared core (FlaUI-MCP-Core):' -ForegroundColor Red
    $touched | ForEach-Object { Write-Host "  $_" }
    Write-Host 'Make the change in FlaUI-MCP-Core, then run: git fetch core; git merge core/main'
    exit 1
}

exit 0
