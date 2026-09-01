[CmdletBinding()]
param([switch]$Force)

$ErrorActionPreference = 'Stop'
$manifest = Import-PowerShellDataFile -LiteralPath (Join-Path $PSScriptRoot 'ModpackTools.psd1')
$version = [string]$manifest.ModuleVersion
$modulePaths = @($env:PSModulePath -split ';' | Where-Object { $_ } | ForEach-Object { [System.IO.Path]::GetFullPath($_) })
$preferredRoot = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'PowerShell/Modules'
$userModuleRoot = $modulePaths | Where-Object { $_ -eq $preferredRoot } | Select-Object -First 1
if (-not $userModuleRoot) {
    $userModuleRoot = $modulePaths | Where-Object {
        $_.StartsWith([System.IO.Path]::GetFullPath($HOME), [System.StringComparison]::OrdinalIgnoreCase)
    } | Select-Object -First 1
}
if (-not $userModuleRoot) { throw 'No user module directory was found in PSModulePath.' }

$moduleBase = Join-Path $userModuleRoot 'ModpackTools'
$destination = $moduleBase
if ((Test-Path -LiteralPath $destination) -and -not $Force) {
    throw "ModpackTools is already installed at '$destination'. Use -Force to update it."
}
$temporary = Join-Path $userModuleRoot ('.ModpackTools.install-' + [guid]::NewGuid().ToString('N'))
$backup = Join-Path $userModuleRoot ('.ModpackTools.backup-' + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($temporary) | Out-Null
try {
    foreach ($name in @('Private', 'Public', 'ModpackTools.psd1', 'ModpackTools.psm1', 'README.md', 'theme.toml')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $temporary -Recurse -Force
    }
    if (Test-Path -LiteralPath $destination) { Move-Item -LiteralPath $destination -Destination $backup }
    Move-Item -LiteralPath $temporary -Destination $destination
    if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Recurse -Force }
}
catch {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
    if ((Test-Path -LiteralPath $backup) -and -not (Test-Path -LiteralPath $destination)) {
        Move-Item -LiteralPath $backup -Destination $destination
    }
    throw
}
Write-Host "ModpackTools $version installed at $destination"
Write-Host "Open a new session or run: Import-Module ModpackTools -Force"
