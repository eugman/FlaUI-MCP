# Brackets in a valid C# filename are wildcard syntax for Resolve-Path without -LiteralPath.
$ErrorActionPreference = 'Stop'
$fixtureDir = Join-Path ([IO.Path]::GetTempPath()) "mcp-format-path-test-$([guid]::NewGuid().ToString('N'))"
$file = Join-Path $fixtureDir "O'Hare [draft].cs"
New-Item -ItemType Directory -Path $fixtureDir | Out-Null
New-Item -ItemType File -Path $file | Out-Null

function dotnet {
    param([Parameter(ValueFromRemainingArguments = $true)] [string[]]$Arguments)
    $global:formatterCalls += ,@($Arguments)
    $global:LASTEXITCODE = 0
}

$global:formatterCalls = @()
try {
    Push-Location $fixtureDir
    try {
        & (Join-Path $PSScriptRoot 'format.ps1') $file
    }
    finally {
        Pop-Location
    }
    if ($global:formatterCalls.Count -ne 2) {
        throw "Expected two formatter calls, got $($global:formatterCalls.Count)."
    }
    foreach ($call in $global:formatterCalls) {
        if ($call[-1] -cne $file) { throw "The formatter changed the literal file path: $($call | ConvertTo-Json -Compress)." }
    }
}
finally {
    Remove-Item -LiteralPath $file -Force
    Remove-Item -LiteralPath $fixtureDir -Force
    Remove-Variable -Name formatterCalls -Scope Global -ErrorAction SilentlyContinue
}
$global:LASTEXITCODE = 0
