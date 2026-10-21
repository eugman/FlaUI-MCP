# Mutation testing of the guard and protocol files in stryker-config.json, against the no-focus tests.
# Stryker writes no report for a run it does not finish, so the files are tested in parts, each with its
# own -Minutes cap. Each part runs on a fresh worktree of HEAD under local/build-background, so commit
# first; reports and logs go to local/build-background/StrykerOutput/<part>. -Part runs one part.
# -Concurrency overrides the config's 2, e.g. 1 when other suites share the machine.
param([string]$Part = 'all', [int]$Minutes = 45, [int]$Concurrency = 0)
$ErrorActionPreference = 'Stop'
$root = git rev-parse --show-toplevel
Set-Location $root

$parts = [ordered]@{
    guards = 'GuardedInput', 'ProcessIntegrity', 'WindowMutation', 'ClickPoint', 'InputRefusal'
    state = 'ElementRegistry', 'PendingInvokeTracker', 'OperationContext'
    protocol = 'McpServer', 'McpLineTransport', 'ToolRegistry'
    pointer = 'ClickTool', 'MouseTool'
    keyboard = 'SendKeysTool', 'TypeTools', 'BatchTool'
}

function RemoveWorktree($path) {
    if (Test-Path $path) {
        git worktree remove --force $path 2>$null
        if (Test-Path $path) { Remove-Item -Recurse -Force $path -ErrorAction SilentlyContinue }
    }

    git worktree prune
}

function RunPart($name, $files) {
    $work = Join-Path $root "local/build-background/stryker-wt-$name"
    $output = Join-Path $root "local/build-background/StrykerOutput/$name"
    RemoveWorktree $work
    git worktree add --detach $work HEAD | Out-Null
    $code = 1
    # Run from the offline test project: from the repo root Stryker finds FlaUI.Mcp.slnx and runs every
    # test project in it, including the integration tests, which launch apps and take the desktop.
    Push-Location (Join-Path $work 'tests/FlaUI.Mcp.Tests')
    try {
        dotnet tool restore | Out-Null
        $arguments = @('stryker', '-f', '../../stryker-config.json', '--output', $output, '--log-to-file')
        foreach ($file in $files) { $arguments += @('--mutate', "**/$file.cs") }
        if ($Concurrency -gt 0) { $arguments += @('--concurrency', $Concurrency) }
        $stryker = Start-Process dotnet -ArgumentList $arguments -NoNewWindow -PassThru
        $null = $stryker.Handle
        if ($stryker.WaitForExit($Minutes * 60 * 1000)) {
            $code = $stryker.ExitCode
        }
        else {
            $stryker.Kill($true)
            $stryker.WaitForExit()
            Write-Host "Mutation testing ($name) stopped after $Minutes minutes; no report." -ForegroundColor Red
        }
    }
    finally {
        Pop-Location
        RemoveWorktree $work
    }

    Write-Host "Report ($name): $output/reports/mutation-report.html"
    return $code
}

$failed = 0
foreach ($name in $parts.Keys) {
    if ($Part -ne 'all' -and $Part -ne $name) { continue }
    if ((RunPart $name $parts[$name]) -ne 0) { $failed = 1 }
}

exit $failed
