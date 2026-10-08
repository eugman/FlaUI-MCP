<#
Prepares the SSAS database that engine recipes (measure-edit, dax-query, bpa-correction, ...) use.

Applies automation/fixture.csx, which replaces the synthetic Sales and Comparison tables and the pinned BPA
rule, so running it again resets whatever an earlier run changed. Use -Create the first time, to deploy a
new empty model under that name. The database name must start with fla_; nothing else is ever touched.

Example:
  pwsh automation/reset-engine.ps1 -Create              # first time
  pwsh automation/reset-engine.ps1                      # before each engine run
#>
param(
    [string] $Server = 'localhost',
    [string] $DatabaseName = 'fla_te3_small',
    [string] $Te = "$env:LOCALAPPDATA\Programs\te\te.exe",
    [switch] $Create
)

$ErrorActionPreference = 'Stop'
if ($DatabaseName -notmatch '^fla_[A-Za-z0-9_]+$') {
    throw "Database name must start with fla_: $DatabaseName"
}

$fixture = Join-Path $PSScriptRoot 'fixture.csx'

function Invoke-Te([string] $step, [string[]] $arguments) {
    & $Te @arguments --non-interactive
    if ($LASTEXITCODE -ne 0) {
        throw "te failed: $step"
    }
}

if ($Create) {
    $folder = Join-Path ([IO.Path]::GetTempPath()) "fla-model-$([guid]::NewGuid().ToString('N'))"
    $model = Join-Path $folder 'model.bim'
    New-Item -ItemType Directory $folder | Out-Null
    try {
        Invoke-Te 'create model' @('init', $model, '--serialization', 'bim', '--name', $DatabaseName, '--compatibility-mode', 'AnalysisServices')
        Invoke-Te 'apply fixture' @('script', '--model', $model, '--file', $fixture, '--save')
        Invoke-Te 'deploy' @('deploy', '--model', $model, '--target-server', $Server, '--target-database', $DatabaseName, '--create-only', '--skip-bpa', '--execute', '--force')
    }
    finally {
        Remove-Item $folder -Recurse -Force -ErrorAction SilentlyContinue
    }
}
else {
    try {
        Invoke-Te 'apply fixture' @('script', '-s', $Server, '-d', $DatabaseName, '--file', $fixture, '--save', '--force')
    }
    catch {
        throw "$($_.Exception.Message). If $DatabaseName doesn't exist yet, run this script with -Create first; otherwise check the server and the te path."
    }
}

Invoke-Te 'calculate' @('refresh', '-s', $Server, '-d', $DatabaseName, '--type', 'calculate', '--execute', '--force', '--no-progress')
Write-Host "Ready: $DatabaseName on $Server"
