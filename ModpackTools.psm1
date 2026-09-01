Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:ActiveProjectId = $null
$script:ModuleRoot = $PSScriptRoot
$script:ModuleVersion = [string](Import-PowerShellDataFile -LiteralPath (Join-Path $PSScriptRoot 'ModpackTools.psd1')).ModuleVersion
$script:ConfigHomeOverride = $null

foreach ($file in @(
    'Private/Configuration.ps1'
    'Private/Toml.ps1'
    'Private/Project.ps1'
    'Private/Metadata.ps1'
    'Private/DefaultOptions.ps1'
    'Private/Inventory.ps1'
    'Private/ResourcePacks.ps1'
    'Private/Packwiz.ps1'
    'Private/Rendering.ps1'
    'Private/Commands.ps1'
    'Public/modpack.ps1'
)) {
    . (Join-Path $PSScriptRoot $file)
}

Export-ModuleMember -Function modpack
