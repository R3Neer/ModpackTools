param([Parameter(Mandatory)][string]$Executable)
$ErrorActionPreference = 'Stop'
$start = [Diagnostics.ProcessStartInfo]::new($Executable)
$start.ArgumentList.Add('--version')
$start.UseShellExecute = $false
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$process = [Diagnostics.Process]::Start($start)
try {
    $raw = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    if (-not $process.WaitForExit(10000)) { $process.Kill($true); throw 'Native version timed out' }
    if ($process.ExitCode -ne 0 -or $stderr -cne '' -or ($raw.Trim() -split '\r?\n').Count -ne 1) { throw 'Native version transport mismatch' }
} finally { $process.Dispose() }
$manifest = Import-PowerShellDataFile (Join-Path $PSScriptRoot '../../ModpackTools.psd1')
$expected = '{"schema_version":1,"ok":true,"command":"--version","arguments":[],"data":{"version":{"version":"' + $manifest.ModuleVersion + '"}}}'
if ($raw.Trim() -cne $expected) { throw 'Version fields, types or manifest version mismatch' }
'Native version contract passed'
