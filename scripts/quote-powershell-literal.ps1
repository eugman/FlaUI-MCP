function Quote-PowerShellLiteral([string]$Value) {
    # Within a single-quoted PowerShell string, an apostrophe is written twice.
    return "'" + $Value.Replace("'", "''") + "'"
}
