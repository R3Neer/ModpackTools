param([Parameter(Mandatory)][string]$Executable)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$capture = Invoke-NativeTestProcess $Executable @('--version')
$raw = $capture.Stdout
if ($capture.Exit -ne 0 -or $capture.Stderr -cne '' -or ($raw.Trim() -split '\r?\n').Count -ne 1) { throw 'Native version transport mismatch' }
$manifest = Import-PowerShellDataFile (Join-Path $PSScriptRoot '../../ModpackTools.psd1')
$expected = '{"schema_version":1,"ok":true,"command":"--version","arguments":[],"data":{"version":{"version":"' + $manifest.ModuleVersion + '"}}}'
if ($raw.Trim() -cne $expected) { throw 'Version fields, types or manifest version mismatch' }
'Native version contract passed'
