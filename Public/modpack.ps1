function modpack {
    [CmdletBinding(PositionalBinding = $false)]
    param(
        [Parameter(Position = 0)][string]$Command = 'help',
        [Parameter(Position = 1, ValueFromRemainingArguments)][object[]]$Arguments = @()
    )

    $commands = @{
        help   = 'Invoke-MpHelp'
        list   = 'Invoke-MpList'
        use    = 'Invoke-MpUse'
        status = 'Invoke-MpStatus'
        inventory = 'Invoke-MpInventory'
        resource = 'Invoke-MpResource'
        search = 'Invoke-MpSearch'
        build  = 'Invoke-MpBuild'
        diff   = 'Invoke-MpDiff'
        new    = 'Invoke-MpNew'
        add    = 'Invoke-MpAdd'
        classify = 'Invoke-MpClassify'
        update = 'Invoke-MpUpdate'
        config = 'Invoke-MpConfig'
    }

    try {
        $key = $Command.ToLowerInvariant()
        if (-not $commands.ContainsKey($key)) {
            Throw-MpError -Message "Command '$Command' is not recognized" -Hint 'modpack help' -ErrorId 'Command.Unknown' -Category InvalidArgument -TargetObject $Command
        }
        $handler = $commands[$key]
        & $handler @Arguments
    } catch {
        if (-not (Test-MpExpectedError -Exception $_.Exception)) { throw }
        $errorId = [string]$_.Exception.Data['ModpackTools.ErrorId']
        $category = [System.Management.Automation.ErrorCategory][int]$_.Exception.Data['ModpackTools.ErrorCategory']
        $target = $_.Exception.Data['ModpackTools.TargetObject']
        $record = [System.Management.Automation.ErrorRecord]::new($_.Exception, $errorId, $category, $target)
        $PSCmdlet.ThrowTerminatingError($record)
    }
}
