# Copy a stage-0 Git blob byte for byte. PowerShell's text pipeline would alter line endings and BOMs.
function Write-StagedBlob {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Destination)

    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = (Get-Command git -ErrorAction Stop).Source
    $start.WorkingDirectory = (Get-Location).Path
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    foreach ($argument in @('cat-file', 'blob', ":$Path")) { [void]$start.ArgumentList.Add($argument) }

    $process = [Diagnostics.Process]::Start($start)
    if (-not $process) { throw 'Git did not start for the staged blob.' }
    try {
        $destinationStream = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
        try { $process.StandardOutput.BaseStream.CopyTo($destinationStream) }
        finally { $destinationStream.Dispose() }
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) { throw "Cannot read staged blob for $Path (git exit $($process.ExitCode))." }
    }
    finally { $process.Dispose() }
}
