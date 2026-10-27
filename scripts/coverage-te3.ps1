# Line and branch coverage for the TE3 no-focus tests, and with -Mutation a Stryker run over the TE3 guard files.
# Everything builds into local/build-background with one MSBuild node; the integration tests are never run.
param(
    [switch]$Mutation,
    [int]$MutationMinutes = 30
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
# The offline gate must not compile untracked personal recipes.
$env:IncludeLocalRecipes = 'false'
$out = Join-Path $root 'local/build-background'
$tests = 'tests/FlaUI.Mcp.Te3.Tests'

function Run([string]$name, [scriptblock]$command) {
    Write-Host "== $name" -ForegroundColor Cyan
    & $command
    if ($LASTEXITCODE -ne 0) { Write-Host "FAILED: $name" -ForegroundColor Red; exit $LASTEXITCODE }
}

$results = Join-Path $out 'TestResults'
Remove-Item $results -Recurse -Force -ErrorAction SilentlyContinue
Write-Host '== Test FlaUI.Mcp.Te3.Tests with coverage' -ForegroundColor Cyan
$testOutput = dotnet test $tests --collect:'XPlat Code Coverage' --artifacts-path $out --results-directory $results -m:1 -nodeReuse:false -nologo -v q 2>&1
$testOutput | ForEach-Object { Write-Host $_ }
if ($LASTEXITCODE -ne 0) { Write-Host 'FAILED: Test FlaUI.Mcp.Te3.Tests with coverage' -ForegroundColor Red; exit $LASTEXITCODE }
# The offline project's test count; a mutation run that finds more is testing something else.
$offlineTests = ($testOutput | Select-String 'Total:\s+(\d+)' | Select-Object -Last 1).Matches.Groups[1].Value

if (-not (Get-Command reportgenerator -ErrorAction SilentlyContinue)) {
    throw 'reportgenerator is not installed: dotnet tool install -g dotnet-reportgenerator-globaltool'
}

$report = Join-Path $out 'coverage'
Run 'Coverage report' {
    reportgenerator "-reports:$results/**/coverage.cobertura.xml" "-targetdir:$report" '-reporttypes:Html;TextSummary' | Out-Null
}
Get-Content (Join-Path $report 'Summary.txt') | Select-Object -First 16
Write-Host "Report: $report/index.html"

if (-not $Mutation) { return }

# Stryker builds in place, so it runs on an exported copy of HEAD under local/build-background.
# Commit first: uncommitted changes are not mutated. One test runner, and a hard wall-clock cap.
# The guard polls Stryker's reported startup project and test count and stops on another project, any
# *IntegrationTests* project, or more tests than the offline run above. Log polling cannot guarantee
# detection before Stryker's first test or prove that every selected test body avoids the desktop.
. (Join-Path $root 'scripts/stryker-guard.ps1')
if (-not $offlineTests) { throw 'Could not read the offline test count; not starting Stryker.' }
$copy = Join-Path $out 'stryker-te3'
Remove-Item $copy -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $copy | Out-Null
$archive = "$copy.zip"
Run 'Export HEAD for Stryker' { git archive --format=zip -o $archive HEAD }
Expand-Archive $archive $copy
Remove-Item $archive
$work = Join-Path $copy $tests
Push-Location $work
try {
    Run 'Restore Stryker' { dotnet tool restore | Out-Null }
    $log = Join-Path $out 'stryker-te3.log'
    $stryker = Start-Process dotnet -ArgumentList 'stryker', '-f', 'stryker-config.json', '--output', 'StrykerOutput/run' `
        -NoNewWindow -PassThru -RedirectStandardOutput $log -RedirectStandardError "$log.err"
    # Read the handle now, or ExitCode stays empty after a timed wait.
    $null = $stryker.Handle
    $deadline = (Get-Date).AddMinutes($MutationMinutes)
    $seen = 0
    do {
        $done = $stryker.WaitForExit(250)
        $lines = @(Get-Content $log -ErrorAction SilentlyContinue)
        # The last line may still be half-written while Stryker runs; check it once it is complete.
        $complete = if ($done) { $lines.Count } else { $lines.Count - 1 }
        for (; $seen -lt $complete; $seen++) {
            Write-Host $lines[$seen]
            $reason = Test-StrykerStartupLine $lines[$seen] ([int]$offlineTests)
            if ($reason) {
                Write-Host "Stopped Stryker after an unsafe startup line; tests may already have started: $reason" -ForegroundColor Red
                taskkill /PID $stryker.Id /T /F | Out-Null
                exit 1
            }
        }

        if (-not $done -and (Get-Date) -gt $deadline) {
            Write-Host "Stryker exceeded $MutationMinutes minutes; stopping it." -ForegroundColor Red
            taskkill /PID $stryker.Id /T /F | Out-Null
            exit 1
        }
    } until ($done)

    if ($stryker.ExitCode -ne 0) { exit $stryker.ExitCode }
    Write-Host "Report: $work/StrykerOutput/run/reports/mutation-report.html"
}
finally {
    Pop-Location
}
