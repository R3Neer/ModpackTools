function modpack {
    [CmdletBinding(PositionalBinding = $false)]
    param(
        [Parameter(Position = 0)][AllowEmptyString()][string]$Command = '',
        [Parameter(Position = 1, ValueFromRemainingArguments)][object[]]$Arguments = @()
    )

    $previousConsole = $script:MpConsole
    try {
        $tokens = @($(if ($PSBoundParameters.ContainsKey('Command')) { $Command })) + @($Arguments)
        $presentation = ConvertFrom-MpPresentationOptions $tokens
        $Command = if ($presentation.Arguments.Count) { [string]$presentation.Arguments[0] } else { '' }
        $Arguments = @($presentation.Arguments | Select-Object -Skip 1)
        Initialize-MpConsole -Colour $presentation.Colour -Ascii:$presentation.Ascii -Invocation $MyInvocation
        if ($Command -eq '--version') {
            Invoke-MpVersion -Arguments $Arguments
            return
        }
        if (-not $Command -or $Command -eq '--help') {
            if ($Arguments.Count) {
                Throw-MpError -Message 'The global help option does not accept additional arguments' -Hint 'modpack --help' -ErrorId 'Command.InvalidArguments' -Category InvalidArgument -TargetObject $Arguments
            }
            Show-MpHelp
            return
        }
        $key = $Command.ToLowerInvariant()
        $commands = Get-MpCommandCatalog
        if (-not $commands.Contains($key)) {
            Throw-MpError -Message "Command '$Command' is not recognized" -Hint 'modpack --help' -ErrorId 'Command.Unknown' -Category InvalidArgument -TargetObject $Command
        }
        $handler = $commands[$key].Handler
        & $handler @Arguments
        if ($Arguments -notcontains '--help') { Write-R3Line (Get-MpConsole) }
    } catch {
        if (-not (Test-MpExpectedError -Exception $_.Exception)) { throw }
        $errorId = [string]$_.Exception.Data['ModpackTools.ErrorId']
        $category = [System.Management.Automation.ErrorCategory][int]$_.Exception.Data['ModpackTools.ErrorCategory']
        $target = $_.Exception.Data['ModpackTools.TargetObject']
        $record = [System.Management.Automation.ErrorRecord]::new($_.Exception, $errorId, $category, $target)
        $PSCmdlet.ThrowTerminatingError($record)
    } finally { $script:MpConsole = $previousConsole }
}
