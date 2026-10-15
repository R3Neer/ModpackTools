function Invoke-NativeTestProcess {
    param([string]$Executable, [string[]]$Arguments, [int]$TimeoutMilliseconds = 10000, [switch]$CloseInput)
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.RedirectStandardInput = $CloseInput
    $process = [Diagnostics.Process]::Start($start)
    try {
        if ($CloseInput) { $process.StandardInput.Close() }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutMilliseconds)) { $process.Kill($true); $process.WaitForExit(); throw "Process timed out after ${TimeoutMilliseconds}ms: '$Executable' $($Arguments -join ' ')" }
        [pscustomobject]@{ Stdout=$stdout.GetAwaiter().GetResult(); Stderr=$stderr.GetAwaiter().GetResult(); Exit=$process.ExitCode }
    } finally { $process.Dispose() }
}
