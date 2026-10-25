# A pre-commit remediation hint is intended for copy/paste into PowerShell.
# Evaluate only its quoted argument expressions, never the remediation command.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'quote-powershell-literal.ps1')

$paths = @(
    "src/O'Hare;Write-Output injected;#.cs",
    'src/dollar$and`backtick;name.cs',
    'src/space and (parentheses).cs'
)
$quoted = @($paths | ForEach-Object { Quote-PowerShellLiteral $_ })
$reconstructed = @(& ([scriptblock]::Create('@(' + ($quoted -join ',') + ')')))
if ($reconstructed.Count -ne $paths.Count) {
    throw "The hint reconstructed $($reconstructed.Count) paths instead of $($paths.Count)."
}
for ($index = 0; $index -lt $paths.Count; $index++) {
    if ($reconstructed[$index] -cne $paths[$index]) {
        throw "The hint changed path at index $index."
    }
}

$hook = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'pre-commit.ps1')
if ($hook -notmatch 'Quote-PowerShellLiteral \$_') {
    throw 'The pre-commit hint must use Quote-PowerShellLiteral for each path.'
}
$global:LASTEXITCODE = 0
