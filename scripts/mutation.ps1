# Mutation testing of the guard and protocol files in stryker-config.json, against the no-focus tests only.
# Stryker writes no report for a run it does not finish, so the files are tested in parts, each with its
# own -Minutes cap. Each part runs on a fresh worktree of HEAD under local/build-background, so commit
# first; reports and logs go to local/build-background/StrykerOutput/<part>. -Part runs one part.
# -Concurrency overrides the config's 2, e.g. 1 when other suites share the machine.
#
# The default offline parts must not select integration tests: they launch apps and take the desktop. From the
# repository root Stryker finds FlaUI.Mcp.slnx and runs every test project in it, whatever test-projects
# says. So Stryker runs inside tests/FlaUI.Mcp.Tests with that project named explicitly, and the script
# polls its log for a solution context, another test project, or more tests than the offline project has.
# Log polling can detect and stop a bad run, but cannot guarantee detection before its first test. Project
# selection does not prove every test body avoids the desktop or cover an InputDesktop seam bypass.
#
# The one deliberate exception is the pointer-integration work, in two parts: -Part clicks and -Part input
# (-Part pointer-integration runs both, one after the other). The pointer code's mutants need real UI
# Automation, so these parts mutate the pointer files against tests/FlaUI.Mcp.IntegrationTests, which
# launches the WinForms and WPF test apps and takes the desktop. Run them only with the desktop free and the
# person at the machine agreeing. They are never part of -Part all, use stryker-config.pointer-integration.json
# (one test session, 180 s extra per-mutant timeout), default to a 3-hour cap each, run with the real desktop
# (FLAUI_OFFLINE_TESTS unset), and close any test app left behind when each ends. The whole set would take
# about six hours of desktop, past one part's cap; Stryker writes no report for a run it does not finish.
param([string]$Part = 'all', [int]$Minutes = 0, [int]$Concurrency = 0)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$offlineProject = 'FlaUI.Mcp.Tests.csproj'
$integrationProject = 'FlaUI.Mcp.IntegrationTests.csproj'
$testApps = 'WinFormsTestApp', 'WpfTestApp'
$parts = [ordered]@{
    guards = 'GuardedInput', 'GuardedInput.Foreground', 'GuardedDesktop', 'ProcessIntegrity', 'WindowMutation', 'ClickPoint', 'ClickHitTest', 'FocusRule', 'InteractionGrant', 'InteractionGrantObserve', 'InteractionGrantActivation', 'Heartbeat', 'MutationGuard', 'ProcessPolicy', 'WindowsToolAdapter', 'HostPath', 'DesktopLease', 'AutomationRefusal', 'InputRefusal', 'WindowBaseline', 'Win32WindowEnumeration', 'Diagnostics'
    state = 'ElementRegistry', 'PendingInvokeTracker', 'OperationContext', 'WaitOutcome'
    protocol = 'McpServer', 'McpLineTransport', 'ToolRegistry', 'ToolRegistryOutcomeFailure', 'ToolRegistrySchemas', 'ToolRegistryStrand', 'ToolRegistryObservers', 'ToolBase', 'ToolFacts', 'Proof'
    pointer = 'ClickTool', 'ClickToolReplies', 'MouseTool', 'MouseToolReplies', 'MouseDrag', 'RefPoints', 'PointerActions'
    keyboard = 'SendKeysTool', 'TypeTools', 'FillToolReplies', 'PasteTool', 'BatchTool'
}
# The pointer files against the integration tests, split so each part finishes inside its own cap.
$integrationParts = [ordered]@{
    clicks = 'ClickTool', 'ClickToolReplies', 'ClickPoint', 'ClickHitTest'
    input = 'GuardedInput', 'MouseTool', 'PointerActions'
}

function RemoveWorktree($path) {
    if (Test-Path $path) {
        git worktree remove --force $path 2>$null
        if (Test-Path $path) { Remove-Item -Recurse -Force $path -ErrorAction SilentlyContinue }
    }

    git worktree prune
}

# The number of tests in a project, as vstest discovers them (theory rows included).
function TestCount($project, [string[]]$extra) {
    $listing = dotnet test $project -m:1 -nologo --list-tests @extra 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Could not list the tests of ${project}:`n$($listing -join "`n")" }
    $names = $listing | Where-Object { $_ -match '^\s{4}\S' }
    return @($names).Count
}

function OfflineTestCount { TestCount 'tests/FlaUI.Mcp.Tests' @('--artifacts-path', 'local/build-background') }

# Why this run must stop, or $null while its log shows only the expected test project.
function UnsafeRun($logs, $expected, $allowed = $offlineProject) {
    $text = Get-ChildItem $logs -Filter '*.txt' -ErrorAction SilentlyContinue |
        ForEach-Object { Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue }
    $text = $text -join "`n"
    if ($text -match 'IsSolutionContext: True') { return 'Stryker is in solution mode and would run every test project.' }
    if ($allowed -eq $offlineProject -and $text -match 'IntegrationTests') { return 'The integration test project appears in the run.' }
    # The log is polled after Stryker starts. If these markers appear first in the text read, reject the run;
    # polling cannot guarantee that the process has not advanced to a build or test before this check.
    if ($text -match 'TestProjects: \[([^\]]*)\]') {
        $projects = @($Matches[1] -split ',' | ForEach-Object { $_.Trim().Trim('"') } | Where-Object { $_ })
        $others = @($projects | Where-Object { (Split-Path $_ -Leaf) -ne $allowed })
        if ($projects.Count -eq 0 -or $others.Count -gt 0) { return "Stryker would run these test projects: $($projects -join ', ')." }
    }
    elseif ($text -match 'Building|Number of tests found') {
        return "Stryker started building before its options could be checked."
    }

    if ($text -match 'Number of tests found: (\d+)' -and [int]$Matches[1] -gt $expected) {
        return "Stryker found $($Matches[1]) tests; the project has $expected."
    }

    return $null
}

# Close test apps a timed-out test host left behind: only those built under this checkout (its worktrees
# included), never another checkout's, whose own suite may be running.
function CloseTestApps {
    $prefix = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($root)) + [IO.Path]::DirectorySeparatorChar
    foreach ($app in $testApps) {
        Get-Process -Name $app -ErrorAction SilentlyContinue |
            Where-Object { $_.Path -and $_.Path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) } |
            ForEach-Object {
                $_.Kill($true)
                $null = $_.WaitForExit(5000)
            }
    }
}

function RunPart($name, $files, $expected, $testDir = 'tests/FlaUI.Mcp.Tests', $project = $offlineProject, $config = 'stryker-config.json', $cap = 45) {
    # A fresh name each run: a folder left busy by an earlier run must not break this one.
    $work = Join-Path $root "local/build-background/stryker-wt-$name-$(Get-Date -Format 'HHmmss')"
    $output = Join-Path $root "local/build-background/StrykerOutput/$name"
    RemoveWorktree $work
    if (Test-Path $output) { Remove-Item -Recurse -Force $output }
    $code = 1
    $pushed = $false
    try {
        git worktree add --detach $work HEAD | Out-Null
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path (Join-Path $work "$testDir/$project"))) {
            throw "Could not create the worktree for part $name at $work."
        }

        if ($project -eq $offlineProject) {
            # Run from the offline test project with that project named explicitly.
            Push-Location (Join-Path $work 'tests/FlaUI.Mcp.Tests')
            $arguments = @('stryker', '-f', "../../$config", '--test-project', $offlineProject)
        }
        else {
            Push-Location (Join-Path $work $testDir)
            $arguments = @('stryker', '-f', "../../$config", '--test-project', $project)
        }
        $pushed = $true

        dotnet tool restore | Out-Null
        $arguments += @('--output', $output, '--log-to-file')
        foreach ($file in $files) { $arguments += @('--mutate', "**/$file.cs") }
        if ($Concurrency -gt 0) { $arguments += @('--concurrency', $Concurrency) }
        $stryker = Start-Process dotnet -ArgumentList $arguments -NoNewWindow -PassThru
        $null = $stryker.Handle
        $limit = if ($Minutes -gt 0) { $Minutes } else { $cap }
        $deadline = (Get-Date).AddMinutes($limit)
        $checked = $false
        while (-not $stryker.WaitForExit(200)) {
            # Until the discovered test count is known and safe, watch the log five times a second.
            if (-not $checked) {
                $reason = UnsafeRun (Join-Path $output 'logs') $expected $project
                if ($reason) {
                    $stryker.Kill($true)
                    $stryker.WaitForExit()
                    throw "Mutation testing ($name) stopped after an unsafe startup log was observed; tests may already have started: $reason"
                }

                $logText = Get-ChildItem (Join-Path $output 'logs') -Filter '*.txt' -ErrorAction SilentlyContinue |
                    ForEach-Object { Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue }
                $checked = ($logText -join "`n") -match 'Number of tests found'
            }

            if ((Get-Date) -gt $deadline) {
                $stryker.Kill($true)
                $stryker.WaitForExit()
                Write-Host "Mutation testing ($name) stopped after $limit minutes; no report." -ForegroundColor Red
                break
            }
        }

        if ($stryker.HasExited -and (Get-Date) -le $deadline) { $code = $stryker.ExitCode }
    }
    finally {
        try {
            if ($pushed) { Pop-Location }
            if ($project -ne $offlineProject) { CloseTestApps }
        }
        finally { RemoveWorktree $work }
    }

    Write-Host "Report ($name): $output/reports/mutation-report.html"
    return $code
}

# Stryker's builds must not leave a compiler server or build nodes running with a worktree as their
# working directory: that keeps the folder busy after the run. The offline tests also set
# FLAUI_OFFLINE_TESTS themselves; the real desktop then refuses every change. All three are restored after.
$saved = @{
    FLAUI_OFFLINE_TESTS = $env:FLAUI_OFFLINE_TESTS
    UseSharedCompilation = $env:UseSharedCompilation
    MSBUILDDISABLENODEREUSE = $env:MSBUILDDISABLENODEREUSE
}
$failed = 0
try {
    $env:UseSharedCompilation = 'false'
    $env:MSBUILDDISABLENODEREUSE = '1'
    if ($integrationParts.Contains($Part) -or $Part -eq 'pointer-integration') {
        # The deliberate exception: real UI Automation and the real desktop, so the switch is unset.
        $env:FLAUI_OFFLINE_TESTS = $null
        $expected = TestCount 'tests/FlaUI.Mcp.IntegrationTests' @()
        Write-Host "Integration project: $expected tests. These parts take the desktop."
        foreach ($name in $integrationParts.Keys) {
            if ($Part -ne 'pointer-integration' -and $Part -ne $name) { continue }
            $code = RunPart $name $integrationParts[$name] $expected 'tests/FlaUI.Mcp.IntegrationTests' $integrationProject 'stryker-config.pointer-integration.json' 180
            if ($code -ne 0) { $failed = 1 }
        }
    }
    else {
        $env:FLAUI_OFFLINE_TESTS = '1'
        $expected = OfflineTestCount
        Write-Host "Offline project: $expected tests."
        foreach ($name in $parts.Keys) {
            if ($Part -ne 'all' -and $Part -ne $name) { continue }
            if ((RunPart $name $parts[$name] $expected) -ne 0) { $failed = 1 }
        }
    }
}
finally {
    $env:FLAUI_OFFLINE_TESTS = $saved.FLAUI_OFFLINE_TESTS
    $env:UseSharedCompilation = $saved.UseSharedCompilation
    $env:MSBUILDDISABLENODEREUSE = $saved.MSBUILDDISABLENODEREUSE
}

exit $failed
