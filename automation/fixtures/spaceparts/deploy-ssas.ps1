<#
Deploys and processes the fla_ SpaceParts SSAS database from the public template model.
Run load-sql.ps1 first. Only the named fla_ SSAS database is created or overwritten.
Row counts are checked against sql-counts.json and written to ssas-counts.json.
#>
param(
    [Parameter(Mandatory)] [string] $TemplateModel,
    [string] $Server = 'localhost',
    [string] $DatabaseName = 'fla_spaceparts',
    [string] $SqlDatabase = 'fla_spaceparts',
    [string] $Te = "$env:LOCALAPPDATA\Programs\te\te.exe"
)
$ErrorActionPreference = 'Stop'
foreach ($name in $DatabaseName, $SqlDatabase) { if ($name -notmatch '^fla_[A-Za-z0-9_]+$') { throw "Database name must start with fla_: $name" } }
$here = $PSScriptRoot
$bim = Join-Path $here 'spaceparts-ssas.bim'

node (Join-Path $here 'convert-ssas.mjs') $TemplateModel $bim $SqlDatabase
if ($LASTEXITCODE -ne 0) { throw 'Conversion failed' }

& $Te deploy $bim -s $Server -d $DatabaseName --force --skip-bpa --deploy-full --non-interactive
if ($LASTEXITCODE -ne 0) { throw 'te deploy failed' }
& $Te refresh -s $Server -d $DatabaseName --type full --no-progress --non-interactive
if ($LASTEXITCODE -ne 0) { throw 'te refresh failed' }

$sql = Get-Content (Join-Path $here 'sql-counts.json') -Raw | ConvertFrom-Json
$counts = [ordered]@{}
foreach ($pair in @(@('Invoices', 'Invoices'), @('Orders', 'Orders'), @('Budget', 'Budget'), @('Products', 'Products'), @('Customers', 'Customers'))) {
    $json = & $Te query -s $Server -d $DatabaseName -q "EVALUATE ROW(""Rows"", COUNTROWS('$($pair[0])'))" --output-format json --non-interactive 2>$null
    if ($LASTEXITCODE -ne 0) { throw "te query failed for $($pair[0])" }
    $rows = [long](($json | Where-Object { $_ -notmatch 'Early preview|tabulareditor.com' } | Out-String | ConvertFrom-Json).rows[0].'[Rows]')
    $expected = [long]$sql.tables.($pair[1]).rows
    $counts[$pair[0]] = [ordered]@{ rows = $rows; sqlRows = $expected }
    Write-Host ("{0,-10} {1,12:N0} rows (SQL {2:N0})" -f $pair[0], $rows, $expected)
    if ($rows -ne $expected) { throw "Row count mismatch for $($pair[0])" }
}
[ordered]@{ server = $Server; database = $DatabaseName; sqlDatabase = $SqlDatabase; processedAt = (Get-Date).ToString('o'); tables = $counts } |
    ConvertTo-Json -Depth 4 | Set-Content (Join-Path $here 'ssas-counts.json') -Encoding utf8
Write-Host "Deployed and processed $DatabaseName"
