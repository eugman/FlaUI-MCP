# Line and branch coverage of the no-focus tests, rendered as HTML into local/build-background/coverage.
# Needs the reportgenerator global tool: dotnet tool install -g dotnet-reportgenerator-globaltool
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)

$results = 'local/build-background/TestResults-coverage'
$report = 'local/build-background/coverage'
if (Test-Path $results) { Remove-Item -Recurse -Force $results }

dotnet test tests/FlaUI.Mcp.Tests --collect:"XPlat Code Coverage" --artifacts-path local/build-background --results-directory $results -m:1 -nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$cobertura = Get-ChildItem $results -Recurse -Filter coverage.cobertura.xml | Select-Object -First 1
reportgenerator "-reports:$($cobertura.FullName)" "-targetdir:$report" '-reporttypes:Html;TextSummary' | Out-Null
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Get-Content (Join-Path $report 'Summary.txt') -TotalCount 20
Write-Host "Report: $report/index.html"
