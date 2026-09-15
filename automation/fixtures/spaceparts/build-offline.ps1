<#
Builds automation/fixtures/spaceparts/spaceparts-offline.bim from the public SpaceParts template:
copies the template, applies overlay.csx, saves a BIM, then runs check-offline.csx against it.
No server and no TE3 window are involved.
#>
param(
    [Parameter(Mandatory)] [string] $TemplateModel,
    [string] $Te = "$env:LOCALAPPDATA\Programs\te\te.exe"
)
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$output = Join-Path $here 'spaceparts-offline.bim'
$work = Join-Path ([IO.Path]::GetTempPath()) ("fla_spaceparts_" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $work | Out-Null
try {
    $copy = Join-Path $work 'model.bim'
    Copy-Item $TemplateModel $copy
    function Invoke-Te([string] $Step, [string[]] $Arguments) {
        # te writes errors to stderr; keep them so a failure says why.
        $text = & $Te @Arguments 2>&1 | ForEach-Object { "$_" } | Where-Object { $_ -notmatch 'Early preview|tabulareditor.com/downloads' }
        $text | Write-Host
        if ($LASTEXITCODE -ne 0) { throw "$Step failed: $($text -join ' ')" }
    }
    Invoke-Te 'overlay.csx' @('script', $copy, '--script', (Join-Path $here 'overlay.csx'), '--save-to', $output, '--serialization', 'bim', '--non-interactive')
    Invoke-Te 'check-offline.csx' @('script', $output, '--script', (Join-Path $here 'check-offline.csx'), '--non-interactive')
    & $Te --version 2>&1 | Where-Object { $_ -notmatch 'Early preview|tabulareditor.com' } | Select-Object -First 1 |
        ForEach-Object { Set-Content (Join-Path $here 'te-version.txt') $_ -Encoding utf8 }
    Write-Host "Built $output"
}
finally { Remove-Item -Recurse -Force $work }
