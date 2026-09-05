[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$WorkRoot,
    [Parameter(Mandatory)][string]$PackwizPath,
    [string]$ModulePath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'ModpackTools.psd1')
)
$ErrorActionPreference = 'Stop'
Import-Module $ModulePath -Force
$runRoot = Join-Path ([IO.Path]::GetFullPath($WorkRoot)) ('integration-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($runRoot)
& (Get-Module ModpackTools) {
    param($RunRoot, $Packwiz)
    $script:ConfigHomeOverride = Join-Path $RunRoot 'settings'
    $projects = Join-Path $RunRoot 'projects'; $root = Join-Path $projects 'fixture'
    [void][IO.Directory]::CreateDirectory($root)
    [void](Set-ModpackToolsConfigValue -Name root -Value $projects)
    [void](Set-ModpackToolsConfigValue -Name packwiz -Value $Packwiz)
    [IO.File]::WriteAllText((Join-Path $root 'pack.toml'), "name = `"Integration`"`nversion = `"1`"`npack-format = `"packwiz:1.1.0`"`n[index]`nfile = `"index.toml`"`nhash-format = `"sha256`"`nhash = `"`"`n[versions]`nminecraft = `"1.21.1`"`nfabric = `"0.16.14`"`n")
    [IO.File]::WriteAllText((Join-Path $root 'index.toml'),'hash-format = "sha256"')
    Write-ModpackRegistrationFiles $root @{ SchemaVersion=1; Id='fixture'; JavaVersion='21'; OutputName='integration.mrpack' }
    [IO.File]::WriteAllText((Join-Path $root '.packwizignore'), ".modpack/`ndist/`n")
    $project = Read-ModpackProject $root
    Invoke-Packwiz -Arguments @('refresh') -WorkingDirectory $root | Out-Null
    $script:ActiveProjectId = 'fixture'
    $before = Get-MpTreeState $root
    modpack add lithium sodium --dry-run
    if (@(Get-MpTreeChanges $before (Get-MpTreeState $root)).Count) { throw 'Dry run changed the project' }
    modpack add lithium sodium
    modpack pin lithium
    $health = Get-MpProjectHealth $project -Check
    if ($health.Errors.Count) { throw ($health.Errors.Message -join '; ') }
    $build = Build-ModpackProject $project
    $archive = [IO.Compression.ZipFile]::OpenRead($build.Path)
    try {
        $manifest = (Read-MpZipText $archive 'modrinth.index.json') | ConvertFrom-Json
        if ($manifest.game -ne 'minecraft' -or @($manifest.files).Count -lt 2) { throw 'Exported manifest does not contain the planned content' }
        $inventory = Get-ModpackInventory $project
        foreach ($mod in $inventory.Mods) {
            if (-not @($manifest.files | Where-Object path -eq ('mods/' + $mod.Filename)).Count) { throw "Export is missing $($mod.Filename)" }
        }
    }
    finally { $archive.Dispose() }
    # Exercise removal through the public CLI and real Packwiz, including a
    # dependency-only installed item and a local owner with actual JAR metadata.
    modpack unpin lithium
    $inventory = Get-ModpackInventory $project
    $lithium = @($inventory.Mods | Where-Object Name -eq Lithium)[0]
    $metadata = Get-ModpackMetadata $project
    $metadata.Content[$lithium.Id].Intent = 'transitive'
    Write-PowerShellDataFileAtomic $metadata (Join-Path $root '.modpack/metadata.psd1')
    $ownerPath = Join-Path $root 'mods/removal-owner.jar'
    $owner = [IO.Compression.ZipFile]::Open($ownerPath, 'Create')
    try {
        $writer = [IO.StreamWriter]::new($owner.CreateEntry('fabric.mod.json').Open())
        try { $writer.Write('{"schemaVersion":1,"id":"removal_owner","name":"Removal Owner","version":"1","depends":{"lithium":"*"}}') }
        finally { $writer.Dispose() }
    } finally { $owner.Dispose() }
    Invoke-Packwiz -Arguments @('refresh') -WorkingDirectory $root | Out-Null
    $beforeRemove = Get-MpTreeState $root
    modpack remove removal-owner.jar --autoremove --dry-run
    if (@(Get-MpTreeChanges $beforeRemove (Get-MpTreeState $root)).Count) { throw 'Removal dry run changed the project' }
    modpack remove removal-owner.jar --autoremove --yes
    $remaining = Get-ModpackInventory $project
    if ($remaining.Mods.Count -ne 1 -or $remaining.Mods[0].Name -ne 'Sodium' -or [IO.File]::Exists($ownerPath)) { throw 'Autoremove did not preserve only Sodium' }
    $afterRemovalBuild = Build-ModpackProject $project
    $archive = [IO.Compression.ZipFile]::OpenRead($afterRemovalBuild.Path)
    try {
        $afterManifest = (Read-MpZipText $archive 'modrinth.index.json') | ConvertFrom-Json
        if (@($afterManifest.files).Count -ne 1 -or $afterManifest.files[0].path -ne ('mods/' + $remaining.Mods[0].Filename)) { throw 'Export retained removed content' }
    } finally { $archive.Dispose() }
    $result = [pscustomobject]@{ Project = $root; Artifact = $afterRemovalBuild.Path; Files = $afterManifest.files.Count; RemovalVerified = $true; Issues = $health.Errors.Count; Unverified = $health.Unknown.Count; PackwizSha256 = (Get-FileHash $Packwiz).Hash }
    $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $RunRoot 'result.json')
    $result
} $runRoot ([IO.Path]::GetFullPath($PackwizPath))
