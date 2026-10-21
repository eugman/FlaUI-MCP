# Stops a TE3 mutation run that could reach more than the offline TE3 tests. coverage-te3.ps1 checks each line Stryker
# prints at startup, before its initial test run, and kills Stryker on the first line that shows a second test
# project, a project other than FlaUI.Mcp.Te3.Tests (any *IntegrationTests* drives the desktop), or more tests than
# the offline project has. Run directly, it checks one line: exit 1 with the reason, or exit 0.
param(
    [string]$Line,
    [int]$ExpectedTests
)

function Test-StrykerStartupLine([string]$Line, [int]$ExpectedTests) {
    if ($Line -match 'Analyzing (\d+) test project' -and [int]$Matches[1] -ne 1) {
        return "Stryker found $($Matches[1]) test projects; only FlaUI.Mcp.Te3.Tests may run."
    }

    if ($Line -match 'Building test project (.+?\.csproj)') {
        $project = $Matches[1]
        if ($project -match 'IntegrationTests' -or (Split-Path $project -Leaf) -ne 'FlaUI.Mcp.Te3.Tests.csproj') {
            return "Stryker would test $project; only FlaUI.Mcp.Te3.Tests may run."
        }
    }

    if ($Line -match 'Number of tests found: (\d+)' -and [int]$Matches[1] -gt $ExpectedTests) {
        return "Stryker found $($Matches[1]) tests, more than the $ExpectedTests offline TE3 tests."
    }

    return $null
}

if ($MyInvocation.InvocationName -ne '.') {
    $reason = Test-StrykerStartupLine $Line $ExpectedTests
    if ($reason) {
        Write-Host $reason -ForegroundColor Red
        exit 1
    }

    exit 0
}
