function modpack {
    [CmdletBinding(PositionalBinding = $false)]
    param(
        [Parameter(Position = 0)][AllowEmptyString()][string]$Command = '',
        [Parameter(Position = 1, ValueFromRemainingArguments)][object[]]$Arguments = @()
    )

    $previousConsole = $script:MpConsole
    $previousMachine = $script:MpMachineContext
    $presentation = $null
    $tokens = @($(if ($PSBoundParameters.ContainsKey('Command')) { $Command })) + @($Arguments)
    $jsonRequested = @($tokens | Where-Object { [string]$_ -eq '--json' }).Count -gt 0
    try {
        $presentation = ConvertFrom-MpPresentationOptions $tokens
        $jsonRequested = [bool]$presentation.Json
        $Command = if ($presentation.Arguments.Count) { [string]$presentation.Arguments[0] } else { '' }
        $Arguments = @($presentation.Arguments | Select-Object -Skip 1)
        [void](Initialize-MpMachineContext -Enabled:$presentation.Json -NoHuman:$presentation.NoHuman -Command $Command -Arguments $Arguments)
        Initialize-MpConsole -Colour $presentation.Colour -Ascii:$presentation.Ascii -Invocation $MyInvocation -Json:$presentation.Json -NoHuman:$presentation.NoHuman

        if ($Command -eq '--version') {
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
            if ($key -eq 'use' -and $Arguments -notcontains '--help') {
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
        $PSCmdlet.ThrowTerminatingError($record)
    } finally {
        $script:MpConsole = $previousConsole
        $script:MpMachineContext = $previousMachine
    }
}
