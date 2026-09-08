[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$utf8 = [System.Text.UTF8Encoding]::new($false)
[Console]::InputEncoding = $utf8
[Console]::OutputEncoding = $utf8
$OutputEncoding = $utf8

$requestText = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($requestText)) { throw 'The Nushell bridge received an empty request.' }
$request = $requestText | ConvertFrom-Json
$arguments = @($request.arguments | ForEach-Object { [string]$_ })
if ($arguments -notcontains '--json') { $arguments += '--json' }
$modulePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'ModpackTools.psd1'
$exitCode = 0

try {
    Import-Module -Name $modulePath -Force
    & (Get-Command modpack -Module ModpackTools -ErrorAction Stop) @arguments
}
catch {
    # modpack --json already emitted the structured failure envelope on stdout.
    # The Nu wrapper turns that envelope into one native Nu error, so repeating
    # the PowerShell exception on stderr would duplicate the same diagnostic.
    $exitCode = 1
}

exit $exitCode
