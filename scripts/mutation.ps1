# Mutation testing of the guard and protocol files listed in stryker-config.json, against the no-focus tests.
# It runs on a worktree of HEAD under local/build-background, so commit first; the report goes to
# local/build-background/StrykerOutput. Concurrency is 2 and the run stops after -Minutes.
param([int]$Minutes = 45)
$ErrorActionPreference = 'Stop'
$root = git rev-parse --show-toplevel
Set-Location $root

$work = Join-Path $root 'local/build-background/stryker-wt'
$output = Join-Path $root 'local/build-background/StrykerOutput'
if (Test-Path $work) { git worktree remove --force $work }
git worktree add --detach $work HEAD | Out-Null

$code = 1
Push-Location $work
try {
    dotnet tool restore | Out-Null
    $stryker = Start-Process dotnet -ArgumentList 'stryker', '-f', 'stryker-config.json', '--output', $output -NoNewWindow -PassThru
    $null = $stryker.Handle
    if ($stryker.WaitForExit($Minutes * 60 * 1000)) {
        $code = $stryker.ExitCode
    }
    else {
        $stryker.Kill($true)
        Write-Host "Mutation testing stopped after $Minutes minutes." -ForegroundColor Red
    }
}
finally {
    Pop-Location
    git worktree remove --force $work
}

Write-Host "Report: $output/reports/mutation-report.html"
exit $code
