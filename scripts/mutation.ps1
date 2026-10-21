# Mutation testing of the guard and protocol files in stryker-config.json, against the no-focus tests only.
# Stryker writes no report for a run it does not finish, so the files are tested in parts, each with its
# own -Minutes cap. Each part runs on a fresh worktree of HEAD under local/build-background, so commit
# first; reports and logs go to local/build-background/StrykerOutput/<part>. -Part runs one part.
# -Concurrency overrides the config's 2, e.g. 1 when other suites share the machine.
#
# Mutation testing must never run the integration tests: they launch apps and take the desktop. From the
# repository root Stryker finds FlaUI.Mcp.slnx and runs every test project in it, whatever test-projects
# says. So Stryker runs inside tests/FlaUI.Mcp.Tests with that project named explicitly, and the run is
# stopped before its first test when its log shows a solution context, another test project, or more
# tests than the offline project has.
param([string]$Part = 'all', [int]$Minutes = 45, [int]$Concurrency = 0)
$ErrorActionPreference = 'Stop'
$root = git rev-parse --show-toplevel
Set-Location $root

$offlineProject = 'FlaUI.Mcp.Tests.csproj'
$parts = [ordered]@{
    guards = 'GuardedInput', 'ProcessIntegrity', 'WindowMutation', 'ClickPoint', 'InputRefusal'
    state = 'ElementRegistry', 'PendingInvokeTracker', 'OperationContext'
    protocol = 'McpServer', 'McpLineTransport', 'ToolRegistry', 'ToolBase'
    pointer = 'ClickTool', 'ClickToolReplies', 'MouseTool'
    keyboard = 'SendKeysTool', 'TypeTools', 'BatchTool'
}

function RemoveWorktree($path) {
    if (Test-Path $path) {
        git worktree remove --force $path 2>$null
        if (Test-Path $path) { Remove-Item -Recurse -Force $path -ErrorAction SilentlyContinue }
    }

    git worktree prune
}

# The number of tests in the offline project, as vstest discovers them (theory rows included).
function OfflineTestCount {
    $listing = dotnet test tests/FlaUI.Mcp.Tests --artifacts-path local/build-background -m:1 -nologo --list-tests 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Could not list the offline tests:`n$($listing -join "`n")" }
    $names = $listing | Where-Object { $_ -match '^\s{4}\S' }
    return @($names).Count
}

# Why this run must stop, or $null while its log shows only the offline project.
function UnsafeRun($logs, $expected) {
    $text = Get-ChildItem $logs -Filter '*.txt' -ErrorAction SilentlyContinue |
        ForEach-Object { Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue }
    $text = $text -join "`n"
    if ($text -match 'IsSolutionContext: True') { return 'Stryker is in solution mode and would run every test project.' }
    if ($text -match 'IntegrationTests') { return 'The integration test project appears in the run.' }
    # Stryker logs its options before it builds anything. Until they are seen and name only the offline
    # project, nothing else may happen: a build or a test run first means the check came too late.
    if ($text -match 'TestProjects: \[([^\]]*)\]') {
        $projects = @($Matches[1] -split ',' | ForEach-Object { $_.Trim().Trim('"') } | Where-Object { $_ })
        $others = @($projects | Where-Object { (Split-Path $_ -Leaf) -ne $offlineProject })
        if ($projects.Count -eq 0 -or $others.Count -gt 0) { return "Stryker would run these test projects: $($projects -join ', ')." }
    }
    elseif ($text -match 'Building|Number of tests found') {
        return "Stryker started building before its options could be checked."
    }

    if ($text -match 'Number of tests found: (\d+)' -and [int]$Matches[1] -gt $expected) {
        return "Stryker found $($Matches[1]) tests; the offline project has $expected."
    }

    return $null
}

function RunPart($name, $files, $expected) {
    # A fresh name each run: a folder left busy by an earlier run must not break this one.
    $work = Join-Path $root "local/build-background/stryker-wt-$name-$(Get-Date -Format 'HHmmss')"
    $output = Join-Path $root "local/build-background/StrykerOutput/$name"
    RemoveWorktree $work
    if (Test-Path $output) { Remove-Item -Recurse -Force $output }
    git worktree add --detach $work HEAD | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path (Join-Path $work "tests/FlaUI.Mcp.Tests/$offlineProject"))) {
        throw "Could not create the worktree for part $name at $work."
    }

    $code = 1
    Push-Location (Join-Path $work 'tests/FlaUI.Mcp.Tests')
    try {
        dotnet tool restore | Out-Null
        $arguments = @(
            'stryker', '-f', '../../stryker-config.json', '--test-project', $offlineProject,
            '--output', $output, '--log-to-file'
        )
        foreach ($file in $files) { $arguments += @('--mutate', "**/$file.cs") }
        if ($Concurrency -gt 0) { $arguments += @('--concurrency', $Concurrency) }
        $stryker = Start-Process dotnet -ArgumentList $arguments -NoNewWindow -PassThru
        $null = $stryker.Handle
        $deadline = (Get-Date).AddMinutes($Minutes)
        $checked = $false
        while (-not $stryker.WaitForExit(200)) {
            # Until the discovered test count is known and safe, watch the log five times a second.
            if (-not $checked) {
                $reason = UnsafeRun (Join-Path $output 'logs') $expected
                if ($reason) {
                    $stryker.Kill($true)
                    $stryker.WaitForExit()
                    throw "Mutation testing ($name) aborted before running tests: $reason"
                }

                $logText = Get-ChildItem (Join-Path $output 'logs') -Filter '*.txt' -ErrorAction SilentlyContinue |
                    ForEach-Object { Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue }
                $checked = ($logText -join "`n") -match 'Number of tests found'
            }

            if ((Get-Date) -gt $deadline) {
                $stryker.Kill($true)
                $stryker.WaitForExit()
                Write-Host "Mutation testing ($name) stopped after $Minutes minutes; no report." -ForegroundColor Red
                break
            }
        }

        if ($stryker.HasExited -and (Get-Date) -le $deadline) { $code = $stryker.ExitCode }
    }
    finally {
        Pop-Location
        RemoveWorktree $work
    }

    Write-Host "Report ($name): $output/reports/mutation-report.html"
    return $code
}

# Stryker's builds must not leave a compiler server or build nodes running with a worktree as their
# working directory: that keeps the folder busy after the run.
# The offline tests also set this themselves; the real desktop then refuses every change.
$env:FLAUI_OFFLINE_TESTS = '1'
$env:UseSharedCompilation = 'false'
$env:MSBUILDDISABLENODEREUSE = '1'
$expected = OfflineTestCount
Write-Host "Offline project: $expected tests."
$failed = 0
foreach ($name in $parts.Keys) {
    if ($Part -ne 'all' -and $Part -ne $name) { continue }
    if ((RunPart $name $parts[$name] $expected) -ne 0) { $failed = 1 }
}

exit $failed
