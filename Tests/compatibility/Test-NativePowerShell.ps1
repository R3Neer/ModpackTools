param([Parameter(Mandatory)][string]$Executable)
$ErrorActionPreference = 'Stop'
$expectedVersion=[string](Import-PowerShellDataFile (Join-Path $PSScriptRoot '../../ModpackTools.psd1')).ModuleVersion
$previous = $env:MODPACKTOOLS_CORE_PATH
try {
    $env:MODPACKTOOLS_CORE_PATH = $Executable
    Import-Module (Join-Path $PSScriptRoot '../../ModpackTools.psd1') -Force
    $module = Get-Module ModpackTools
    # Fail if the native machine path accidentally initializes presentation.
    & $module { function script:Initialize-MpConsole { throw 'Renderer must not be initialized' } }
    foreach ($alias in @('--version','-V','--VERSION')) {
        $raw = @(modpack $alias --json --no-human)
        if ($raw.Count -ne 1 -or ($raw[0] | ConvertFrom-Json).data.version.version -cne $expectedVersion) { throw 'Native PS version mismatch' }
    }
    $output = [Collections.Generic.List[string]]::new()
    try { modpack --version extra --json --no-human | ForEach-Object { $output.Add($_) }; throw 'Domain failure not thrown' }
    catch {
        if ($_.FullyQualifiedErrorId -notlike 'ModpackTools.Command.InvalidArguments*') { throw }
    }
    if ($output.Count -ne 1) { throw 'Native PS failure emitted multiple envelopes' }
    $failure = $output[0] | ConvertFrom-Json
    if ($failure.ok -or $failure.arguments -isnot [array] -or $failure.arguments[0] -cne 'extra') { throw 'Native PS error mismatch' }
    'Native PS version and domain failure passed without renderer initialization'
} finally {
    $env:MODPACKTOOLS_CORE_PATH = $previous
    Remove-Module ModpackTools -ErrorAction SilentlyContinue
}
