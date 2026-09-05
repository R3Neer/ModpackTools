Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:ActiveProjectId = $null
$script:ModuleRoot = $PSScriptRoot
$script:ModuleVersion = [string](Import-PowerShellDataFile -LiteralPath (Join-Path $PSScriptRoot 'ModpackTools.psd1')).ModuleVersion
$script:ConfigHomeOverride = $null
$script:MpCommandCatalog = $null
$script:DependencyManifestOverride = $null

foreach ($file in @(
    'Private/Errors.ps1'
    'Private/FileSystem.ps1'
    'Private/Configuration.ps1'
    'Private/Dependencies.ps1'
    'Private/Toml.ps1'
    'Private/Project.ps1'
    'Private/Metadata.ps1'
    'Private/DefaultOptions.ps1'
    'Private/Inventory.ps1'
    'Private/Modrinth.ps1'
    'Private/Compatibility.ps1'
    'Private/LoaderMetadata.ps1'
    'Private/ResourcePacks.ps1'
    'Private/Packwiz.ps1'
    'Private/Diff.ps1'
    'Private/Presentation.ps1'
    'Private/Rendering.ps1'
    'Private/Doctor.ps1'
    'Private/Help.ps1'
    'Private/Commands.ps1'
    'Private/Transaction.ps1'
    'Private/Batch.ps1'
    'Private/Graph.ps1'
    'Private/Resolver.ps1'
    'Private/Operations.ps1'
    'Private/Remove.ps1'
    'Private/Health.ps1'
    'Public/modpack.ps1'
)) {
    . (Join-Path $PSScriptRoot $file)
}

Export-ModuleMember -Function modpack
