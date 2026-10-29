# Run RunPart's actual function body with a fake worktree command. A missing
# project makes setup fail after registration, before Stryker can start.
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'mutation.ps1'
$tokens = $null
$parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($source, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors) { throw "Could not parse ${source}: $($parseErrors -join '; ')" }
$function = $ast.Find({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'RunPart'
}, $true)
if (-not $function) { throw 'RunPart function was not found.' }
Invoke-Expression $function.Extent.Text

$root = Join-Path ([IO.Path]::GetTempPath()) "fla-ui-mutation-cleanup-$([guid]::NewGuid().ToString('N'))"
$offlineProject = 'FlaUI.Mcp.Tests.csproj'
$script:registered = $null
$script:removeCount = 0
$script:projectPresent = $false
$script:failPush = $false
New-Item -ItemType Directory -Path $root | Out-Null

function RemoveWorktree($path) {
    $script:removeCount++
    $rootPrefix = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($root)) + [IO.Path]::DirectorySeparatorChar
    if (-not [IO.Path]::GetFullPath($path).StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a path outside the test root: $path"
    }
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}
function git {
    if ($args.Count -ne 5 -or $args[0] -ne 'worktree' -or $args[1] -ne 'add') {
        throw "Unexpected git call: $args"
    }
    $script:registered = $args[3]
    New-Item -ItemType Directory -Path $script:registered | Out-Null
    if ($script:projectPresent) {
        $testDirectory = Join-Path $script:registered 'tests/FlaUI.Mcp.Tests'
        New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
        New-Item -ItemType File -Path (Join-Path $testDirectory $offlineProject) | Out-Null
    }
    $global:LASTEXITCODE = 0
}
function Push-Location {
    if ($script:failPush) { throw 'Synthetic Push-Location failure.' }
    throw 'Push-Location unexpectedly reached in the missing-project case.'
}

try {
    try {
        RunPart 'missing-project' @('GuardedInput') 1 | Out-Null
        throw 'RunPart unexpectedly accepted a worktree without its test project.'
    }
    catch {
        if ($_.Exception.Message -notlike 'Could not create the worktree*') { throw }
    }
    if (-not $script:registered) { throw 'The fake worktree was not registered.' }
    if (Test-Path -LiteralPath $script:registered) { throw 'RunPart left a worktree after setup failed.' }
    if ($script:removeCount -ne 2) { throw "Expected cleanup before and after registration; got $script:removeCount calls." }

    $script:registered = $null
    $script:removeCount = 0
    $script:projectPresent = $true
    $script:failPush = $true
    try {
        RunPart 'push-failure' @('GuardedInput') 1 | Out-Null
        throw 'RunPart unexpectedly continued after Push-Location failed.'
    }
    catch {
        if ($_.Exception.Message -ne 'Synthetic Push-Location failure.') { throw }
    }
    if (-not $script:registered) { throw 'The fake worktree was not registered for the push failure.' }
    if (Test-Path -LiteralPath $script:registered) { throw 'RunPart left a worktree after Push-Location failed.' }
    if ($script:removeCount -ne 2) { throw "Expected cleanup after Push-Location failure; got $script:removeCount calls." }
    Write-Host 'Mutation setup failures cleaned their worktrees.'
}
finally {
    if (-not $root.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unexpected temporary root: $root"
    }
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
