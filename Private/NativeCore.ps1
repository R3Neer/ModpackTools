function Invoke-MpNativeCommand {
    param([Parameter(Mandatory)][string]$Executable, [string[]]$Arguments,
        [ValidateSet('version','project-list','project-current')][string]$Capability,
        [AllowNull()][object]$ActiveProject)
    if (-not [IO.File]::Exists($Executable)) {
        Throw-MpError -Message 'The configured ModpackTools native executable does not exist' -Hint 'correct MODPACKTOOLS_CORE_PATH' -ErrorId 'Core.ExecutableNotFound' -Category ObjectNotFound -TargetObject $Executable
    }
    $requestPath = Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-' + [guid]::NewGuid().ToString('N') + '.native.json')
    $process = $null
    try {
        $requestData = @{ schema_version=1; arguments=@($Arguments) }
        if ($Capability -eq 'project-current') {
            if ($null -ne $ActiveProject -and $ActiveProject -isnot [string]) { Throw-MpNativeProtocolError 'Invalid session snapshot' }
            $requestData.context = @{ active_project=$ActiveProject }
        }
        $request = $requestData | ConvertTo-Json -Depth 4 -Compress
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
        if (-not $process.WaitForExit(30000)) { $process.Kill($true); $process.WaitForExit(); Throw-MpNativeProtocolError 'Native command timed out' }
        $text = $stdout.GetAwaiter().GetResult()
        if ($stderr.GetAwaiter().GetResult() -cne '' -or ($text.Trim() -split '\r?\n').Count -ne 1) { Throw-MpNativeProtocolError 'Invalid native output transport' }
        $result = $text | ConvertFrom-Json -Depth 30
        if (($result.schema_version -isnot [long] -and $result.schema_version -isnot [int]) -or $result.schema_version -ne 1 -or $result.ok -isnot [bool] -or $result.command -isnot [string] -or $result.arguments -isnot [array]) { Throw-MpNativeProtocolError 'Invalid native result envelope' }
        if (@($result.arguments | Where-Object { $_ -isnot [string] }).Count) { Throw-MpNativeProtocolError 'Invalid native argument types' }
        if ($result.ok) {
            if ($process.ExitCode -ne 0 -or $result.PSObject.Properties['error']) { Throw-MpNativeProtocolError 'Invalid native success result' }
            if ($Capability -eq 'version') {
                if ($result.command -ine '--version' -or $result.data.version.version -isnot [string]) { Throw-MpNativeProtocolError 'Invalid native version result' }
            } elseif ($Capability -eq 'project-current') {
                if ($result.command -ine 'project' -or $result.arguments.Count -ne 1 -or $result.arguments[0] -ine 'current' -or -not $result.data.PSObject.Properties['active_project'] -or ($null -ne $result.data.active_project -and $result.data.active_project -isnot [string])) { Throw-MpNativeProtocolError 'Invalid native session result' }
            } else {
                if ($result.command -ine 'project' -or $result.arguments.Count -ne 1 -or $result.arguments[0] -ine 'list' -or $result.data.projects.root -isnot [string] -or $result.data.projects.items -isnot [array]) { Throw-MpNativeProtocolError 'Invalid native project catalog' }
                foreach ($item in $result.data.projects.items) {
                    foreach ($name in @('id','root','output_name')) {
                        if ($item.$name -isnot [string]) { Throw-MpNativeProtocolError 'Invalid native project field' }
                    }
                    foreach ($name in @('name','version','minecraft','loader','loader_version')) {
                        if ($null -ne $item.$name -and $item.$name -isnot [string]) { Throw-MpNativeProtocolError 'Invalid native nullable project field' }
                    }
                }
            }
        } else {
            if ($process.ExitCode -notin @(1,2) -or $result.error.id -isnot [string] -or $result.error.message -isnot [string] -or $result.error.category -isnot [string]) { Throw-MpNativeProtocolError 'Invalid native error result' }
            if ($result.PSObject.Properties['data'] -or -not [Enum]::IsDefined([Management.Automation.ErrorCategory], [string]$result.error.category)) { Throw-MpNativeProtocolError 'Invalid native failure category or fields' }
        }
        return $result
    } catch {
        if (Test-MpExpectedError $_.Exception) { throw }
        Throw-MpError -Message 'The ModpackTools native invocation failed' -Details $_.Exception.Message -Hint 'verify MODPACKTOOLS_CORE_PATH and the native package' -ErrorId 'Core.InvocationFailed' -Category InvalidData
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
