# Line and branch coverage for the TE3 no-focus tests, and with -Mutation a Stryker run over the TE3 guard files.
# Everything builds into local/build-background with one MSBuild node; the integration tests are never run.
param(
    [switch]$Mutation,
    [int]$MutationMinutes = 30
)

$ErrorActionPreference = 'Stop'
$root = git rev-parse --show-toplevel
Set-Location $root
$out = Join-Path $root 'local/build-background'
$tests = 'tests/FlaUI.Mcp.Te3.Tests'

function Run([string]$name, [scriptblock]$command) {
    Write-Host "== $name" -ForegroundColor Cyan
    & $command
    if ($LASTEXITCODE -ne 0) { Write-Host "FAILED: $name" -ForegroundColor Red; exit $LASTEXITCODE }
}

$results = Join-Path $out 'TestResults'
Remove-Item $results -Recurse -Force -ErrorAction SilentlyContinue
Run 'Test FlaUI.Mcp.Te3.Tests with coverage' {
    dotnet test $tests --collect:'XPlat Code Coverage' --artifacts-path $out --results-directory $results -m:1 -nodeReuse:false -nologo -v q
}

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
    $stryker = Start-Process dotnet -ArgumentList 'stryker', '-f', 'stryker-config.json', '--output', 'StrykerOutput/run' `
        -NoNewWindow -PassThru
    # Read the handle now, or ExitCode stays empty after a timed wait.
    $null = $stryker.Handle
    if (-not $stryker.WaitForExit($MutationMinutes * 60 * 1000)) {
        Write-Host "Stryker exceeded $MutationMinutes minutes; stopping it." -ForegroundColor Red
        taskkill /PID $stryker.Id /T /F | Out-Null
        exit 1
    }

    if ($stryker.ExitCode -ne 0) { exit $stryker.ExitCode }
    Write-Host "Report: $work/StrykerOutput/run/reports/mutation-report.html"
}
finally {
    Pop-Location
}
