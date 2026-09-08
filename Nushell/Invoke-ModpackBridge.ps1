[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$requestText = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($requestText)) { throw 'The Nushell bridge received an empty request.' }
$request = $requestText | ConvertFrom-Json
$arguments = @($request.arguments | ForEach-Object { [string]$_ })
if ($arguments -notcontains '--json') { $arguments += '--json' }
$noHuman = $arguments -contains '--no-human'
$modulePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'ModpackTools.psd1'

try {
    Import-Module -Name $modulePath -Force
    & (Get-Command modpack -Module ModpackTools -ErrorAction Stop) @arguments
    exit 0
}
catch {
    if (-not $noHuman) { [Console]::Error.WriteLine($_.Exception.Message) }
    exit 1
}
