# Mutation testing of the guard and protocol files, against the no-focus tests. The files are split in two
# so each run fits its time cap: stryker-config.json (Core and Mcp) and stryker-config.tools.json (Tools).
# Each run uses a worktree of HEAD under local/build-background, so commit first, and stops after -Minutes;
# reports go to local/build-background/StrykerOutput/<part>. -Part runs one half. -Concurrency overrides
# the config's 2, e.g. 1 when other suites share the machine.
param([ValidateSet('all', 'core', 'tools')][string]$Part = 'all', [int]$Minutes = 45, [int]$Concurrency = 0)
$ErrorActionPreference = 'Stop'
$root = git rev-parse --show-toplevel
Set-Location $root

$configs = [ordered]@{ core = 'stryker-config.json'; tools = 'stryker-config.tools.json' }
$work = Join-Path $root 'local/build-background/stryker-wt'

function RunPart($name, $config) {
    $output = Join-Path $root "local/build-background/StrykerOutput/$name"
    if (Test-Path $work) { git worktree remove --force $work }
    git worktree add --detach $work HEAD | Out-Null
    $code = 1
    Push-Location $work
    try {
        dotnet tool restore | Out-Null
        $arguments = @('stryker', '-f', $config, '--output', $output)
        if ($Concurrency -gt 0) { $arguments += @('--concurrency', $Concurrency) }
        $stryker = Start-Process dotnet -ArgumentList $arguments -NoNewWindow -PassThru
        $null = $stryker.Handle
        if ($stryker.WaitForExit($Minutes * 60 * 1000)) {
            $code = $stryker.ExitCode
        }
        else {
            $stryker.Kill($true)
            Write-Host "Mutation testing ($name) stopped after $Minutes minutes; no report." -ForegroundColor Red
        }
    }
    finally {
        Pop-Location
        git worktree remove --force $work
    }

    Write-Host "Report ($name): $output/reports/mutation-report.html"
    return $code
}

$failed = 0
foreach ($name in $configs.Keys) {
    if ($Part -ne 'all' -and $Part -ne $name) { continue }
    if ((RunPart $name $configs[$name]) -ne 0) { $failed = 1 }
}

exit $failed
