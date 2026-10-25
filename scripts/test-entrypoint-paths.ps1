# The ownership gate must resolve its own repository when called by absolute path.
$ErrorActionPreference = 'Stop'
$outside = Join-Path ([IO.Path]::GetTempPath()) "mcp-script-path-test-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $outside | Out-Null
try {
    Push-Location $outside
    try {
        & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'check-core-untouched.ps1') -Staged
        if ($LASTEXITCODE -ne 0) {
            throw "check-core-untouched.ps1 failed from outside the repository: $LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }
}
finally {
    Remove-Item -LiteralPath $outside -Force
}
