# Stops a TE3 mutation run when Stryker's startup output reports more than the selected offline test project.
# coverage-te3.ps1 checks each line before the initial test run and stops on another project, an
# *IntegrationTests* project, or more discovered tests than the offline project lists. This checks reported
# selection, not whether every selected test body avoids the desktop. Run directly, it checks one line:
# exit 1 with the reason, or exit 0.
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
