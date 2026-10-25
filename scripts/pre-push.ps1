$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
. (Join-Path $PSScriptRoot 'push-tip.ps1')
$lines = @([Console]::In.ReadToEnd() -split "`r?`n" | Where-Object { $_ })
Assert-PushTip -Lines $lines
& (Join-Path $PSScriptRoot 'check.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$extra = Join-Path $PSScriptRoot 'pre-push-extra.ps1'
if (Test-Path -LiteralPath $extra) { & $extra }
exit $LASTEXITCODE
