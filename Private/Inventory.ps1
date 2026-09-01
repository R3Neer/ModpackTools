Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-PackwizItemId {
    param(
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][string]$RelativeMetadataPath
    )

    $modrinth = Get-TomlString -Text $Text -Section 'update.modrinth' -Key 'mod-id'
    if ($modrinth) { return "modrinth:$modrinth" }
    $curseforge = Get-TomlString -Text $Text -Section 'update.curseforge' -Key 'project-id'
    if ($curseforge) { return "curseforge:$curseforge" }
    $fallback = ($RelativeMetadataPath -replace '\\', '/').ToLowerInvariant()
    return "packwiz:$fallback"
}

function Get-PackwizItems {
    param(
        [Parameter(Mandatory)]$Project,
        [Parameter(Mandatory)][ValidateSet('mods', 'resourcepacks', 'shaderpacks')][string]$Directory,
        [Parameter(Mandatory)][ValidateSet('mod', 'resourcepack', 'shaderpack')][string]$Kind
    )

    $path = Join-Path $Project.Root $Directory
    if (-not (Test-Path -LiteralPath $path -PathType Container)) { return @() }
    return @(
        foreach ($file in Get-ChildItem -LiteralPath $path -Filter '*.pw.toml' -File -ErrorAction SilentlyContinue) {
            $text = Get-Content -Raw -LiteralPath $file.FullName -Encoding UTF8
            $relative = "$Directory/$($file.Name)"
            $name = Get-TomlString -Text $text -Key 'name'
            $filename = Get-TomlString -Text $text -Key 'filename'
            $side = Get-TomlString -Text $text -Key 'side'
            if (-not $name) { $name = $file.Name -replace '\.pw\.toml$', '' }
            if ($side) { $side = $side.ToLowerInvariant() } else { $side = 'unknown' }
            [pscustomobject]@{
                Id           = Get-PackwizItemId -Text $text -RelativeMetadataPath $relative
                Kind         = $Kind
                Name         = $name
                Filename     = $filename
                Side         = $side
                Source       = 'packwiz'
                MetadataPath = $file.FullName
                Category     = $null
            }
        }
    )
}

function Get-LocalJarMetadata {
    param([Parameter(Mandatory)][string]$Path)

    $name = [System.IO.Path]::GetFileNameWithoutExtension($Path)
    $side = 'unknown'
    $archive = $null
    try {
        $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
        $entry = $archive.GetEntry('fabric.mod.json')
        if ($entry) {
            $stream = $entry.Open()
            $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::UTF8)
            try { $json = $reader.ReadToEnd() | ConvertFrom-Json }
            finally { $reader.Dispose(); $stream.Dispose() }
            if ($json.name) { $name = [string]$json.name }
            $environment = [string]$json.environment
            $side = switch ($environment.ToLowerInvariant()) {
                'client' { 'client' }
                'server' { 'server' }
                '*'      { 'both' }
                ''       { 'both' }
                default  { 'unknown' }
            }
        }
    }
    catch {
        Write-Verbose "No se pudo leer fabric.mod.json de '$Path': $($_.Exception.Message)"
    }
    finally { if ($archive) { $archive.Dispose() } }
    return [pscustomobject]@{ Name = $name; Side = $side }
}

function Get-LocalModItems {
    param([Parameter(Mandatory)]$Project, [array]$KnownItems = @())

    $path = Join-Path $Project.Root 'mods'
    if (-not (Test-Path -LiteralPath $path -PathType Container)) { return @() }
    $knownFiles = @($KnownItems | ForEach-Object { $_.Filename })
    return @(
        foreach ($file in Get-ChildItem -LiteralPath $path -Filter '*.jar' -File -ErrorAction SilentlyContinue) {
            if ($knownFiles -contains $file.Name) { continue }
            $jar = Get-LocalJarMetadata -Path $file.FullName
            [pscustomobject]@{
                Id           = 'local:mods/' + $file.Name.ToLowerInvariant()
                Kind         = 'mod'
                Name         = $jar.Name
                Filename     = $file.Name
                Side         = $jar.Side
                Source       = 'local'
                MetadataPath = $null
                Category     = $null
            }
        }
    )
}

function Get-LocalZipItems {
    param(
        [Parameter(Mandatory)]$Project,
        [Parameter(Mandatory)][ValidateSet('resourcepacks', 'shaderpacks')][string]$Directory,
        [Parameter(Mandatory)][ValidateSet('resourcepack', 'shaderpack')][string]$Kind,
        [array]$KnownItems = @()
    )

    $path = Join-Path $Project.Root $Directory
    if (-not (Test-Path -LiteralPath $path -PathType Container)) { return @() }
    $knownFiles = @($KnownItems | ForEach-Object { $_.Filename })
    return @(
        foreach ($file in Get-ChildItem -LiteralPath $path -Filter '*.zip' -File -ErrorAction SilentlyContinue) {
            if ($knownFiles -contains $file.Name) { continue }
            [pscustomobject]@{
                Id           = "local:$Directory/$($file.Name.ToLowerInvariant())"
                Kind         = $Kind
                Name         = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
                Filename     = $file.Name
                Side         = $(if ($Kind -eq 'resourcepack') { 'client' } else { 'unknown' })
                Source       = 'local'
                MetadataPath = $null
                Category     = $null
            }
        }
    )
}

function Apply-ModMetadata {
    param([array]$Mods, [Parameter(Mandatory)]$Metadata)

    foreach ($mod in $Mods) {
        $entry = if ($Metadata.Mods.ContainsKey($mod.Id)) { $Metadata.Mods[$mod.Id] } else { $null }
        if ($entry -and $entry.ContainsKey('Name')) { $mod.Name = [string]$entry.Name }
        if ($entry -and $entry.ContainsKey('Category')) {
            $requested = [string]$entry.Category
            if ($Metadata.Categories.ContainsKey($requested)) {
                $mod.Category = $requested
                $mod | Add-Member -NotePropertyName InvalidCategory -NotePropertyValue $null -Force
            }
            else {
                $mod.Category = 'unclassified'
                $mod | Add-Member -NotePropertyName InvalidCategory -NotePropertyValue $requested -Force
            }
        }
        else {
            $mod.Category = 'unclassified'
            $mod | Add-Member -NotePropertyName InvalidCategory -NotePropertyValue $null -Force
        }
    }
    return $Mods
}

function Get-ModpackInventory {
    param([Parameter(Mandatory)]$Project)

    $metadata = Get-ModpackMetadata -Project $Project
    $remoteMods = @(Get-PackwizItems -Project $Project -Directory mods -Kind mod)
    $mods = @($remoteMods; Get-LocalModItems -Project $Project -KnownItems $remoteMods)
    $mods = @(Apply-ModMetadata -Mods $mods -Metadata $metadata)

    $remoteResources = @(Get-PackwizItems -Project $Project -Directory resourcepacks -Kind resourcepack)
    $resources = @($remoteResources; Get-LocalZipItems -Project $Project -Directory resourcepacks -Kind resourcepack -KnownItems $remoteResources)
    $remoteShaders = @(Get-PackwizItems -Project $Project -Directory shaderpacks -Kind shaderpack)
    $shaders = @($remoteShaders; Get-LocalZipItems -Project $Project -Directory shaderpacks -Kind shaderpack -KnownItems $remoteShaders)

    $activeIds = @(Get-DefaultResourcePackOrder -Project $Project)
    $activeResources = @(
        for ($i = 0; $i -lt $activeIds.Count; $i++) {
            $id = $activeIds[$i]
            $filename = if ($id.StartsWith('file/', [System.StringComparison]::OrdinalIgnoreCase)) { $id.Substring(5) } else { $null }
            $physical = if ($filename) { $resources | Where-Object Filename -eq $filename | Select-Object -First 1 } else { $null }
            $name = if ($physical) { $physical.Name } else { $(if ($filename) { $filename } else { $id }) }
            if ($metadata.ResourcePacks.ContainsKey($id) -and $metadata.ResourcePacks[$id].ContainsKey('Name')) {
                $name = [string]$metadata.ResourcePacks[$id].Name
            }
            [pscustomobject]@{
                Id       = $id
                Kind     = 'resourcepack'
                Name     = $name
                Filename = $filename
                Source   = $(if ($physical) { $physical.Source } else { $(if ($filename) { 'missing' } else { 'builtin' }) })
                Enabled  = $true
                Priority = $i + 1
            }
        }
    )
    $activeFilenames = @($activeResources | Where-Object Filename | ForEach-Object Filename)
    $inactiveResources = @($resources | Where-Object { $_.Filename -notin $activeFilenames } | Sort-Object Name)

    [pscustomobject]@{
        Project           = $Project
        Metadata          = $metadata
        Mods              = @($mods | Sort-Object Name)
        ResourcePacks     = $resources
        ActiveResources   = $activeResources
        InactiveResources = $inactiveResources
        Shaders           = @($shaders | Sort-Object Name)
    }
}
