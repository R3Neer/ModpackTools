function modpack {
    $previousConsole = $script:MpConsole
    $previousMachine = $script:MpMachineContext
    $previousProject = $script:CommandProjectId
    $presentation = $null
    $Command = ''
    $Arguments = @()
    $tokens = @($args)
    $jsonRequested = @($tokens | Where-Object { [string]$_ -eq '--json' }).Count -gt 0
    try {
        $presentation = ConvertFrom-MpPresentationOptions $tokens
        $jsonRequested = [bool]$presentation.Json
        $Command = if ($presentation.Arguments.Count) { [string]$presentation.Arguments[0] } else { '' }
        $Arguments = @($presentation.Arguments | Select-Object -Skip 1)
        $script:CommandProjectId = $presentation.Project
        [void](Initialize-MpMachineContext -Enabled:$presentation.Json -NoHuman:$presentation.NoHuman -Command $Command -Arguments $Arguments)
        $nativePath = [Environment]::GetEnvironmentVariable('MODPACKTOOLS_CORE_PATH')
        $nativeCapability = if ($Command -eq '--version') { 'version' }
            elseif ($Command -eq 'project' -and $Arguments.Count -gt 0 -and $Arguments[0] -eq 'list' -and $Arguments -notcontains '--help') { 'project-list' }
            elseif ($Command -eq 'project' -and $Arguments.Count -gt 0 -and $Arguments[0] -eq 'current' -and $Arguments -notcontains '--help') { 'project-current' }
            elseif ($Command -eq 'project' -and $Arguments.Count -gt 0 -and $Arguments[0] -eq 'use' -and $Arguments -notcontains '--help') { 'project-use' }
            else { $null }
        if ($presentation.Json -and $presentation.NoHuman -and $nativeCapability -and -not [string]::IsNullOrWhiteSpace($nativePath)) {
            $native = Invoke-MpNativeCommand -Executable $nativePath -Arguments $tokens -Capability $nativeCapability -ActiveProject $script:ActiveProjectId
            if ($native.ok) {
                if ($native.command -cne $Command -or $native.arguments.Count -ne $Arguments.Count) { Throw-MpNativeProtocolError 'Native response does not match the requested command' }
                for ($index=0; $index -lt $Arguments.Count; $index++) {
                    if ($native.arguments[$index] -cne $Arguments[$index]) { Throw-MpNativeProtocolError 'Native response does not match the requested arguments' }
                }
            }
            if ($nativeCapability -eq 'project-use' -and $native.ok) { $script:ActiveProjectId = $native.data.active_project }
            if (-not $native.ok -and $nativeCapability -in @('project-list','project-current','project-use') -and $native.error.id -ceq 'ModpackTools.Command.InvalidArguments') {
                $positionalTargets = @($Arguments | Select-Object -Skip 1)
                if ($positionalTargets.Count -eq 0) { $native.error.target = $null }
                elseif ($positionalTargets.Count -eq 1) { $native.error.target = $positionalTargets[0] }
                else { $native.error.target = $positionalTargets }
            }
            Write-MpMachineEnvelope $native
            if (-not $native.ok) {
                if ($nativeCapability -in @('project-list','project-current','project-use') -and $native.error.id -ceq 'ModpackTools.Command.InvalidArguments') {
                    # PS exposes the positional array in its ErrorRecord even when
                    # legacy public JSON unwraps that array to null or a scalar.
                    Throw-MpNativeFailure $native.error -TargetOverride @($Arguments | Select-Object -Skip 1)
                }
                Throw-MpNativeFailure $native.error
            }
            return
        }
        Initialize-MpConsole -Colour $presentation.Colour -Ascii:$presentation.Ascii -Invocation $MyInvocation -Json:$presentation.Json -NoHuman:$presentation.NoHuman

        if ($Command -eq '--version') {
            if ($script:CommandProjectId) { Throw-MpError -Message "Option '--project' cannot be used with --version" -Hint 'modpack --version' -ErrorId 'Option.ForbiddenCombination' -Category InvalidArgument }
            $captured = @((Invoke-MpVersion -Arguments $Arguments))
            if ($presentation.Json) {
                Set-MpMachineData version ([ordered]@{ version=$script:ModuleVersion })
                if ($captured.Count) { Set-MpMachineData raw $captured }
                Write-MpMachineEnvelope (New-MpMachineSuccessEnvelope)
            }
            return
        }
        if (-not $Command -or $Command -eq '--help') {
            if ($Arguments.Count) {
                Throw-MpError -Message 'The global help option does not accept additional arguments' -Hint 'modpack --help' -ErrorId 'Command.InvalidArguments' -Category InvalidArgument -TargetObject $Arguments
            }
            Show-MpHelp
            if ($presentation.Json) {
                Set-MpMachineData help ([ordered]@{ command=$null })
                Write-MpMachineEnvelope (New-MpMachineSuccessEnvelope)
            }
            return
        }
        $key = $Command.ToLowerInvariant()
        $commands = Get-MpCommandCatalog
        if (-not $commands.Contains($key)) {
            Throw-MpError -Message "Command '$Command' is not recognized" -Hint 'modpack --help' -ErrorId 'Command.Unknown' -Category InvalidArgument -TargetObject $Command
        }
        $handler = $commands[$key].Handler
        if ($presentation.Json) {
            $captured = @(& $handler @Arguments)
            if ($captured.Count) { Set-MpMachineData raw $captured }
            if ($key -eq 'project' -and $Arguments.Count -and $Arguments[0] -in @('use','current') -and $Arguments -notcontains '--help') {
                Set-MpMachineData active_project $script:ActiveProjectId
            }
        }
        else { & $handler @Arguments }
        if ($Arguments -notcontains '--help') { Write-R3Line (Get-MpConsole) }
        if ($presentation.Json) { Write-MpMachineEnvelope (New-MpMachineSuccessEnvelope) }
    } catch {
        if ($jsonRequested) {
            if (-not $script:MpMachineContext) {
                [void](Initialize-MpMachineContext -Enabled -Command $Command -Arguments $Arguments)
            }
            if (-not $script:MpMachineContext.Emitted) {
                Write-MpMachineEnvelope (New-MpMachineErrorEnvelope $_)
            }
        }
        if (-not (Test-MpExpectedError -Exception $_.Exception)) { throw }
        $errorId = [string]$_.Exception.Data['ModpackTools.ErrorId']
        $category = [System.Management.Automation.ErrorCategory][int]$_.Exception.Data['ModpackTools.ErrorCategory']
        $target = $_.Exception.Data['ModpackTools.TargetObject']
        $record = [System.Management.Automation.ErrorRecord]::new($_.Exception, $errorId, $category, $target)
        throw $record
    } finally {
        $script:MpConsole = $previousConsole
        $script:MpMachineContext = $previousMachine
        $script:CommandProjectId = $previousProject
    }
}
