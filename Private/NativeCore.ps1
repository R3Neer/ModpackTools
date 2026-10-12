function Invoke-MpNativeVersion {
    param([Parameter(Mandatory)][string]$Executable, [string[]]$Arguments)
    if (-not [IO.File]::Exists($Executable)) {
        Throw-MpError -Message 'The configured ModpackTools native executable does not exist' -Hint 'correct MODPACKTOOLS_CORE_PATH' -ErrorId 'Core.ExecutableNotFound' -Category ObjectNotFound -TargetObject $Executable
    }
    $requestPath = Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-' + [guid]::NewGuid().ToString('N') + '.native.json')
    $process = $null
    try {
        $request = @{ schema_version=1; arguments=@($Arguments) } | ConvertTo-Json -Depth 4 -Compress
        [IO.File]::WriteAllText($requestPath, $request, [Text.UTF8Encoding]::new($false))
        $start = [Diagnostics.ProcessStartInfo]::new($Executable)
        $start.ArgumentList.Add('--request-path'); $start.ArgumentList.Add($requestPath)
        $start.UseShellExecute = $false
        $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
        $start.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
        $start.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
        $process = [Diagnostics.Process]::Start($start)
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(30000)) { $process.Kill($true); $process.WaitForExit(); Throw-MpNativeProtocolError 'Native version timed out' }
        $text = $stdout.GetAwaiter().GetResult()
        if ($stderr.GetAwaiter().GetResult() -cne '' -or ($text.Trim() -split '\r?\n').Count -ne 1) { Throw-MpNativeProtocolError 'Invalid native output transport' }
        $result = $text | ConvertFrom-Json -Depth 30
        if (($result.schema_version -isnot [long] -and $result.schema_version -isnot [int]) -or $result.schema_version -ne 1 -or $result.ok -isnot [bool] -or $result.command -isnot [string] -or $result.arguments -isnot [array]) { Throw-MpNativeProtocolError 'Invalid native result envelope' }
        if (@($result.arguments | Where-Object { $_ -isnot [string] }).Count) { Throw-MpNativeProtocolError 'Invalid native argument types' }
        if ($result.ok) {
            if ($process.ExitCode -ne 0 -or $result.command -ine '--version' -or $result.data.version.version -isnot [string] -or $result.PSObject.Properties['error']) { Throw-MpNativeProtocolError 'Invalid native version result' }
        } else {
            if ($process.ExitCode -notin @(1,2) -or $result.error.id -isnot [string] -or $result.error.message -isnot [string] -or $result.error.category -isnot [string]) { Throw-MpNativeProtocolError 'Invalid native error result' }
            if ($result.PSObject.Properties['data'] -or -not [Enum]::IsDefined([Management.Automation.ErrorCategory], [string]$result.error.category)) { Throw-MpNativeProtocolError 'Invalid native failure category or fields' }
        }
        return $result
    } catch {
        if (Test-MpExpectedError $_.Exception) { throw }
        Throw-MpError -Message 'The ModpackTools native version invocation failed' -Details $_.Exception.Message -Hint 'verify MODPACKTOOLS_CORE_PATH and the native package' -ErrorId 'Core.InvocationFailed' -Category InvalidData
    } finally {
        if ($process) { $process.Dispose() }
        try { [IO.File]::Delete($requestPath) }
        catch [IO.IOException] { Write-Verbose 'Could not remove the native request file' }
        catch [UnauthorizedAccessException] { Write-Verbose 'Could not remove the native request file' }
    }
}

function Throw-MpNativeProtocolError {
    param([string]$Message)
    Throw-MpError -Message $Message -Hint 'verify MODPACKTOOLS_CORE_PATH and the native package' -ErrorId 'Core.ProtocolViolation' -Category InvalidData
}
