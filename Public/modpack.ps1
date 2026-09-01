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
        build  = 'Invoke-MpBuild'
        diff   = 'Invoke-MpDiff'
        new    = 'Invoke-MpNew'
        add    = 'Invoke-MpAdd'
        config = 'Invoke-MpConfig'
    }

    $key = $Command.ToLowerInvariant()
    if (-not $commands.ContainsKey($key)) {
        throw "Unknown command '$Command'. Run 'modpack help'."
    }

    $handler = $commands[$key]
    & $handler @Arguments
}
