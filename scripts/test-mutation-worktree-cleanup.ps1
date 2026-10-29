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
$script:failGitAdd = $false
$script:registeredPaths = [Collections.Generic.List[string]]::new()
$script:outputPaths = [Collections.Generic.List[string]]::new()
$script:forcedWorkPath = $null
New-Item -ItemType Directory -Path $root | Out-Null
$pathFactory = $ast.Find({
    param($node)
    $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'NewWorktreePath'
}, $true)
if (-not $pathFactory) { throw 'NewWorktreePath function was not found.' }
Invoke-Expression $pathFactory.Extent.Text
$firstPath = NewWorktreePath 'same-second'
$secondPath = NewWorktreePath 'same-second'
if ($firstPath -eq $secondPath) { throw 'The production path factory reused a run path.' }

# Freeze the old HHmmss clock so a second call within the same second is
# deterministic even on a slow test machine.
function Get-Date {
    param([string]$Format)
    if ($Format -eq 'HHmmss') { return '123456' }
    throw "Unexpected Get-Date invocation: $Format"
}
function NewWorktreePath($name) {
    if ($script:forcedWorkPath) { return $script:forcedWorkPath }
    return Join-Path $root "local/build-background/stryker-wt-$name-$([guid]::NewGuid().ToString('N'))"
}
function Test-Path {
    param([Parameter(Position=0)][string]$Path, [string]$LiteralPath)
    $candidate = if ($PSBoundParameters.ContainsKey('LiteralPath')) { $LiteralPath } else { $Path }
    if ($candidate -like '*StrykerOutput*') { $script:outputPaths.Add($candidate) }
    return Microsoft.PowerShell.Management\Test-Path -LiteralPath $candidate
}

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
    $script:registeredPaths.Add($script:registered)
    New-Item -ItemType Directory -Path $script:registered | Out-Null
    if ($script:projectPresent) {
        $testDirectory = Join-Path $script:registered 'tests/FlaUI.Mcp.Tests'
        New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
        New-Item -ItemType File -Path (Join-Path $testDirectory $offlineProject) | Out-Null
    }
    $global:LASTEXITCODE = if ($script:failGitAdd) { 1 } else { 0 }
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
    if ($script:removeCount -ne 1) { throw "Expected cleanup after registration; got $script:removeCount calls." }

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
    if ($script:removeCount -ne 1) { throw "Expected cleanup after Push-Location failure; got $script:removeCount calls." }

    $script:projectPresent = $false
    $script:failPush = $false
    foreach ($number in 1..2) {
        try { RunPart 'same-second' @('GuardedInput') 1 | Out-Null }
        catch {
            if ($_.Exception.Message -notlike 'Could not create the worktree*') { throw }
        }
    }
    $sameSecond = @($script:registeredPaths | Where-Object { $_ -like '*stryker-wt-same-second-*' })
    if ($sameSecond.Count -ne 2 -or $sameSecond[0] -eq $sameSecond[1]) {
        throw 'Two runs of the same mutation part reused a worktree path.'
    }
    $sameOutput = @($script:outputPaths | Where-Object { $_ -like '*StrykerOutput*same-second*' })
    if ($sameOutput.Count -ne 2 -or $sameOutput[0] -eq $sameOutput[1]) {
        throw 'Two runs of the same mutation part reused an output path.'
    }

    $script:removeCount = 0
    $script:failGitAdd = $true
    try {
        RunPart 'partial-add' @('GuardedInput') 1 | Out-Null
        throw 'RunPart unexpectedly accepted a failed Git add.'
    }
    catch {
        if ($_.Exception.Message -notlike 'Could not create the worktree*') { throw }
    }
    if (Test-Path -LiteralPath $script:registered) { throw 'RunPart left a partial worktree after Git add failed.' }
    if ($script:removeCount -ne 1) { throw 'RunPart skipped cleanup after Git add partially created a worktree.' }
    $script:failGitAdd = $false

    $script:forcedWorkPath = Join-Path $root 'local/build-background/stryker-wt-sentinel-123456'
    New-Item -ItemType Directory -Path $script:forcedWorkPath -Force | Out-Null
    $sentinel = Join-Path $script:forcedWorkPath 'sentinel.txt'
    Set-Content -LiteralPath $sentinel -Value 'owned by another run'
    $addsBefore = $script:registeredPaths.Count
    $removesBefore = $script:removeCount
    try {
        RunPart 'sentinel' @('GuardedInput') 1 | Out-Null
        throw 'RunPart unexpectedly accepted a preexisting worktree path.'
    }
    catch {
        if ($_.Exception.Message -notlike '*already exists*') { throw }
    }
    if (-not (Test-Path -LiteralPath $sentinel)) { throw "RunPart deleted another run's preexisting worktree." }
    if ($script:registeredPaths.Count -ne $addsBefore -or $script:removeCount -ne $removesBefore) {
        throw "RunPart attempted setup or cleanup of another run's preexisting worktree."
    }
    Write-Host 'Mutation setup failures cleaned their worktrees.'
}
finally {
    if (-not $root.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unexpected temporary root: $root"
    }
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
$global:LASTEXITCODE = 0
