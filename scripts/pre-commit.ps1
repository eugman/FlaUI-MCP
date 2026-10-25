# Fast checks on what is being committed; the full gate runs on push (scripts/check.ps1).
# Checks the staged copy of each C# file, not the working copy, so what is committed is what is checked.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
# Source files are UTF-8; the console code page would mangle non-ASCII text piped through the formatter.
$utf8 = [System.Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = $utf8
$OutputEncoding = $utf8
. (Join-Path $PSScriptRoot 'quote-powershell-literal.ps1')
. (Join-Path $PSScriptRoot 'staged-blob.ps1')

$staged = @((git diff --cached --name-only --diff-filter=ACMR -z -- '*.cs') -split "`0" | Where-Object { $_ })
if ($LASTEXITCODE -ne 0) { throw 'Cannot list staged C# files.' }
if ($staged.Count -eq 0) {
    & (Join-Path $PSScriptRoot 'check-core-untouched.ps1') -Staged
    exit $LASTEXITCODE
}

# Files with no unstaged edits can be checked on disk in one formatter run. Partially staged files
# are checked from their staged copy, one at a time.
$unstaged = @((git diff --name-only -z -- '*.cs') -split "`0" | Where-Object { $_ })
if ($LASTEXITCODE -ne 0) { throw 'Cannot list partially staged C# files.' }
$partial = @($staged | Where-Object { $unstaged -ccontains $_ })
$whole = $staged | Where-Object { $_ -notin $partial }

$unformatted = @()
if ($whole) {
    dotnet csharpier check $whole *> $null
    if ($LASTEXITCODE -ne 0) {
        $unformatted += $whole | Where-Object { dotnet csharpier check $_ *> $null; $LASTEXITCODE -ne 0 }
    }
}

foreach ($file in $partial) {
    $directory = Split-Path -Parent $file
    if (-not $directory) { $directory = '.' }
    $snapshot = Join-Path $directory ".mcp-staged-$([guid]::NewGuid().ToString('N')).cs"
    try {
        Write-StagedBlob -Path $file -Destination $snapshot
        dotnet csharpier check $snapshot *> $null
        if ($LASTEXITCODE -ne 0) { $unformatted += $file }
    }
    finally { Remove-Item -LiteralPath $snapshot -Force -ErrorAction SilentlyContinue }
}

if ($unformatted) {
    Write-Host 'These staged files are not formatted:' -ForegroundColor Red
    $unformatted | ForEach-Object { Write-Host "  $_" }
    $quoted = ($unformatted | ForEach-Object { Quote-PowerShellLiteral $_ }) -join ' '
    Write-Host "Fix and re-stage: pwsh scripts/format.ps1 $quoted; git add -- $quoted"
    exit 1
}

& (Join-Path $PSScriptRoot 'check-core-untouched.ps1') -Staged
exit $LASTEXITCODE
