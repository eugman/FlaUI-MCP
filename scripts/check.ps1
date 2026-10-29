# The full local gate, also run by the pre-push hook and by PR/manual CI: formatting, analyzers, no-focus tests.
# Integration tests are excluded because they launch apps and take desktop focus.
# -Mutation also runs mutation testing (scripts/mutation.ps1), which can take up to 45 minutes.
# -IncludeLocalRecipes opts into compiling personal, gitignored recipes and tests.
param([switch]$Mutation, [switch]$IncludeLocalRecipes)

$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
# The tracked-source gate must give the same result on machines with different
# personal recipes and inherited MSBuild settings. Opt in to local recipes separately.
$env:IncludeLocalRecipes = if ($IncludeLocalRecipes) { 'true' } else { 'false' }

function Step($name, [scriptblock]$command) {
    Write-Host "== $name" -ForegroundColor Cyan
    & $command
    if ($LASTEXITCODE -ne 0) { Write-Host "FAILED: $name" -ForegroundColor Red; exit $LASTEXITCODE }
}

# Every way to exempt code from the analyzers, counted at a revision: rules downgraded in any
# .editorconfig or .globalconfig, pragmas, SuppressMessage attributes and NoWarn. The total may only shrink.
function DebtCount($revision) {
    $patterns = @(
        @('^\s*dotnet_diagnostic\.\S+\.severity\s*=\s*(none|silent|suggestion)', '*.editorconfig', '*.globalconfig'),
        @('#pragma warning disable', '*.cs'),
        @('SuppressMessage\(', '*.cs'),
        @('<NoWarn>', '*.csproj', '*.props', '*.targets')
    )
    $count = 0
    foreach ($pattern in $patterns) {
        $hits = git grep -c -E $pattern[0] $revision -- $pattern[1..($pattern.Count - 1)]
        $count += ($hits | ForEach-Object { [int]($_ -split ':')[-1] } | Measure-Object -Sum).Sum
    }

    return $count
}

# The revision to compare with: CI passes DEBT_BASE (the previous push, or a pull request's base).
# Locally it is the upstream branch, or for a new branch the point where it left the default branch.
function DebtBase {
    $candidates = @($env:DEBT_BASE, '@{upstream}', 'origin/HEAD')
    foreach ($candidate in $candidates | Where-Object { $_ -and $_ -notmatch '^0+$' }) {
        $resolved = git rev-parse -q --verify "$candidate^{commit}" 2>$null
        if ($resolved) {
            return git merge-base HEAD $resolved
        }
    }

    return $null
}

function CheckDebt {
    $base = DebtBase
    $global:LASTEXITCODE = 0
    if (-not $base) {
        Write-Host 'Nothing to compare with; skipped.'
        return
    }

    $before = DebtCount $base
    $after = DebtCount 'HEAD'
    # git grep exits 1 when nothing matches, so set the result explicitly.
    $global:LASTEXITCODE = 0
    if ($after -gt $before) {
        Write-Host "Known debt grew from $before to $after exemptions since $base." -ForegroundColor Red
        Write-Host 'Fix the code instead of exempting it.'
        $global:LASTEXITCODE = 1
    }
}

Step 'Restore tools' { dotnet tool restore | Out-Null }
Step 'Direct script entrypoints' { & ./scripts/test-entrypoint-paths.ps1 }
Step 'Pre-commit hint quoting' { & ./scripts/test-pre-commit-quoting.ps1 }
Step 'Staged blob fidelity' { & ./scripts/test-staged-blob.ps1 }
Step 'Push tip identity' { & ./scripts/test-push-tip.ps1 }
Step 'Formatter literal paths' { & ./scripts/test-format-literal-paths.ps1 }
Step 'Release tag identity' { & ./scripts/test-release-tag.ps1 }
Step 'Mutation worktree cleanup' { & ./scripts/test-mutation-worktree-cleanup.ps1 }
Step 'Formatting (fix with: pwsh scripts/format.ps1)' { dotnet csharpier check . }
Step 'Shared core untouched' { & ./scripts/check-core-untouched.ps1 }
Step 'Known debt does not grow' { CheckDebt }
# Capped so a check doesn't saturate a shared desktop, and leaves no idle build nodes behind.
Step 'Build with analyzers' { dotnet build FlaUI.Mcp.slnx -nologo -v q -m:2 -nodeReuse:false }

# Only reviewed no-focus projects may run in the background gate.
$approvedTests = @('FlaUI.Mcp.Tests', 'FlaUI.Mcp.Te3.Tests', 'FlaUI.Mcp.PowerBi.Tests', 'FlaUI.Mcp.Camtasia.Tests')
$unitTests = Get-ChildItem tests -Filter *.csproj -Recurse |
    Where-Object { $_.BaseName -in $approvedTests }
$unknownTests = Get-ChildItem tests -Filter '*Tests.csproj' -Recurse |
    Where-Object { $_.BaseName -notin $approvedTests -and $_.BaseName -notlike '*IntegrationTests*' }
if ($unknownTests) { throw 'Unclassified test project: review desktop requirements before adding it to the background allowlist.' }
foreach ($project in $unitTests) {
    Step "Test $($project.BaseName)" { dotnet test $project.FullName --no-build -nologo -v q }
}

if ($Mutation) {
    Step 'Mutation testing' { & ./scripts/mutation.ps1 }
}
