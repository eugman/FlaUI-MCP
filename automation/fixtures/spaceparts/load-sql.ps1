<#
Builds the fla_ SpaceParts SQL database from the public SpaceParts CSVs.
Drops and recreates only the named fla_ database, bulk-loads every CSV, then checks row counts
against the CSV line counts and writes sql-counts.json next to this script.
#>
param(
    [Parameter(Mandatory)] [string] $CsvFolder,
    [string] $Server = 'localhost',
    [string] $DatabaseName = 'fla_spaceparts'
)
$ErrorActionPreference = 'Stop'
if ($DatabaseName -notmatch '^fla_[A-Za-z0-9_]+$') { throw "Database name must start with fla_: $DatabaseName" }
$here = $PSScriptRoot
$tables = [ordered]@{
    Brands = 'Brands.csv'; BudgetRate = 'BudgetRate.csv'; Customers = 'Customers.csv'; Employees = 'Employees.csv'
    ExchangeRate = 'ExchangeRate.csv'; InvoiceDocumentType = 'InvoiceDocumentType.csv'; OrderDocumentType = 'OrderDocumentType.csv'
    OrderStatus = 'OrderStatus.csv'; Products = 'Products.csv'; Regions = 'Regions.csv'; Budget = 'Budget.csv'
    Forecast = 'Forecast.csv'; Orders = 'Orders.csv'; Invoices = 'Invoices.csv'
}
foreach ($file in $tables.Values) { if (-not (Test-Path (Join-Path $CsvFolder $file))) { throw "Missing CSV: $file" } }

function Invoke-Sql([string] $Query) {
    $output = sqlcmd -S $Server -E -C -b -d $DatabaseName -h -1 -W -Q "SET NOCOUNT ON; $Query" 2>&1
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed: $output" }
    return $output
}

sqlcmd -S $Server -E -C -b -i (Join-Path $here 'create-sql.sql') -v DatabaseName=$DatabaseName
if ($LASTEXITCODE -ne 0) { throw 'create-sql.sql failed' }

$counts = [ordered]@{}
foreach ($table in $tables.Keys) {
    $path = (Resolve-Path (Join-Path $CsvFolder $tables[$table])).Path
    # Lines, minus the header, are the expected row count. No SpaceParts field contains a line break.
    $lines = 0; $reader = [IO.StreamReader]::new($path)
    $buffer = New-Object char[] 1048576; $crlf = $false; $first = $true
    while (($read = $reader.Read($buffer, 0, $buffer.Length)) -gt 0) {
        if ($first) { $crlf = ([string]::new($buffer, 0, [Math]::Min($read, 4096))).Contains("`r`n"); $first = $false }
        for ($i = 0; $i -lt $read; $i++) { if ($buffer[$i] -eq "`n") { $lines++ } }
    }
    $reader.Close()
    $terminator = if ($crlf) { '0x0d0a' } else { '0x0a' }
    $started = Get-Date
    # FORMAT = 'CSV' already uses " as the field quote; spelling it out breaks sqlcmd argument quoting.
    Invoke-Sql "BULK INSERT data.[$table] FROM N'$path' WITH (FORMAT = 'CSV', FIRSTROW = 2, CODEPAGE = '65001', ROWTERMINATOR = '$terminator', TABLOCK, BATCHSIZE = 500000);" | Out-Null
    $rows = [long](Invoke-Sql "SELECT COUNT_BIG(*) FROM data.[$table];" | Select-Object -First 1)
    $expected = $lines - 1
    $counts[$table] = [ordered]@{ rows = $rows; csvRows = $expected; seconds = [int]((Get-Date) - $started).TotalSeconds }
    Write-Host ("{0,-20} {1,12:N0} rows (CSV {2:N0}) in {3}s" -f $table, $rows, $expected, $counts[$table].seconds)
    if ($rows -ne $expected) { throw "Row count mismatch for ${table}: loaded $rows, CSV has $expected" }
}

# Every view the model reads must return rows.
foreach ($view in 'Dimview.[Brands]', 'Dimview.[Products]', 'Factview.[Budget]', 'Factview.[Orders]', 'Factview.[Invoices]') {
    Invoke-Sql "SELECT TOP (1) * FROM $view;" | Out-Null
}
[ordered]@{ server = $Server; database = $DatabaseName; loadedAt = (Get-Date).ToString('o'); tables = $counts } |
    ConvertTo-Json -Depth 4 | Set-Content (Join-Path $here 'sql-counts.json') -Encoding utf8
Write-Host "Loaded $DatabaseName"
