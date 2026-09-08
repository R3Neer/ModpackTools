[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$utf8 = [System.Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = $utf8
[Console]::OutputEncoding = $utf8
$OutputEncoding = $utf8

function New-BridgeErrorEnvelope {
    param(
        [Parameter(Mandatory)]$ErrorRecord,
        [string]$Command = '',
        [object[]]$Arguments = @()
    )

    [ordered]@{
        schema_version = 1
        ok = $false
        command = $Command
        arguments = @($Arguments | ForEach-Object { [string]$_ })
        error = [ordered]@{
            id = 'Bridge.InvocationFailed'
            message = $ErrorRecord.Exception.Message
            category = [string]$ErrorRecord.CategoryInfo.Category
            target = $ErrorRecord.TargetObject
        }
    } | ConvertTo-Json -Depth 10 -Compress -EnumsAsStrings
}

$machineOutput = [System.Collections.Generic.List[string]]::new()
$command = ''
$arguments = @()

try {
    $requestText = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($requestText)) { throw 'The Nushell bridge received an empty request.' }

    $request = $requestText | ConvertFrom-Json
    $arguments = @($request.arguments | ForEach-Object { [string]$_ })
    if ($arguments -notcontains '--json') { $arguments += '--json' }

    $command = if ($arguments.Count) { [string]$arguments[0] } else { '' }
    $modulePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'ModpackTools.psd1'
    Import-Module -Name $modulePath -Force

    & (Get-Command modpack -Module ModpackTools -ErrorAction Stop) @arguments |
        ForEach-Object { $machineOutput.Add([string]$_) }
}
catch {
    # Expected ModpackTools failures emit their JSON envelope before throwing.
    # Keep that envelope as the sole machine result. Only bridge/bootstrap failures
    # need a bridge-owned envelope.
    if ($machineOutput.Count -eq 0) {
        $machineOutput.Add((New-BridgeErrorEnvelope -ErrorRecord $_ -Command $command -Arguments $arguments))
    }
}

if ($machineOutput.Count -ne 1) {
    $protocolError = [System.Management.Automation.ErrorRecord]::new(
        [System.InvalidOperationException]::new("The ModpackTools bridge expected exactly one machine envelope but received $($machineOutput.Count)."),
        'Bridge.ProtocolViolation',
        [System.Management.Automation.ErrorCategory]::InvalidData,
        $machineOutput.Count
    )
    $machineOutput.Clear()
    $machineOutput.Add((New-BridgeErrorEnvelope -ErrorRecord $protocolError -Command $command -Arguments $arguments))
}

[Console]::Out.WriteLine($machineOutput[0])

# The JSON envelope is the process contract. Once one valid envelope exists,
# native exit status must not race Nushell's parser or hide structured errors.
exit 0
