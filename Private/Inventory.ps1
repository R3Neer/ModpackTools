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
        Write-Verbose "Could not read fabric.mod.json from '$Path': $($_.Exception.Message)"
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

function Resolve-InventoryType {
    param([AllowNull()][AllowEmptyString()][string]$Type)
    switch (($Type ?? 'all').ToLowerInvariant()) {
        'all'           { 'all' }
        'mod'           { 'mod' }
        'mods'          { 'mod' }
        'resource'      { 'resourcepack' }
        'resources'     { 'resourcepack' }
        'resourcepack'  { 'resourcepack' }
        'resourcepacks' { 'resourcepack' }
        'shader'        { 'shaderpack' }
        'shaders'       { 'shaderpack' }
        'shaderpack'    { 'shaderpack' }
        'shaderpacks'   { 'shaderpack' }
        default { Throw-MpError -Message "Inventory type '$Type' is not recognized; allowed values: all, mod, resourcepack, shaderpack" -Hint '--type <all|mod|resourcepack|shaderpack>' -ErrorId 'Inventory.InvalidType' -Category InvalidArgument -TargetObject $Type }
    }
}

function Select-ModpackInventory {
    param(
        [Parameter(Mandatory)]$Inventory,
        [string]$Type = 'all',
        [string]$Category,
        [string]$Side,
        [string]$Source,
        [string]$State = 'all',
        [string]$Search
    )

    $effectiveType = Resolve-InventoryType $Type
    $normalizedSide = if ($Side) { $Side.ToLowerInvariant() } else { $null }
    if ($normalizedSide -eq 'host') { $normalizedSide = 'server' }
    if ($normalizedSide -and $normalizedSide -notin @('client', 'server', 'both', 'unknown')) {
        Throw-MpError -Message "Side '$Side' is not recognized; allowed values: client, host, server, both, unknown" -Hint '--side <client|host|both|unknown>' -ErrorId 'Inventory.InvalidSide' -Category InvalidArgument -TargetObject $Side
    }
    $normalizedSource = if ($Source) { $Source.ToLowerInvariant() } else { $null }
    if ($normalizedSource -and $normalizedSource -notin @('packwiz', 'local', 'builtin', 'missing')) {
        Throw-MpError -Message "Source '$Source' is not recognized; allowed values: packwiz, local, builtin, missing" -Hint '--source <packwiz|local|builtin|missing>' -ErrorId 'Inventory.InvalidSource' -Category InvalidArgument -TargetObject $Source
    }
    $normalizedState = ($State ?? 'all').ToLowerInvariant()
    if ($normalizedState -notin @('all', 'active', 'inactive')) {
        Throw-MpError -Message "State '$State' is not recognized; allowed values: all, active, inactive" -Hint '--state <all|active|inactive>' -ErrorId 'Inventory.InvalidState' -Category InvalidArgument -TargetObject $State
    }

    if ($Category -or $normalizedSide) {
        if ($effectiveType -notin @('all', 'mod')) { Throw-MpError -Message "Options '--category' and '--side' apply only to mods" -Hint 'use --type mod or remove the mod-only filters' -ErrorId 'Inventory.ModFilterTypeConflict' -Category InvalidArgument }
        if ($normalizedState -ne 'all') { Throw-MpError -Message "Mod filters cannot be combined with '--state'" -Hint 'remove --state or the mod-only filters' -ErrorId 'Inventory.FilterConflict' -Category InvalidArgument }
        $effectiveType = 'mod'
    }
    if ($normalizedState -ne 'all') {
        if ($effectiveType -notin @('all', 'resourcepack')) { Throw-MpError -Message "Option '--state' applies only to resource packs" -Hint 'use --type resourcepack or remove --state' -ErrorId 'Inventory.StateFilterTypeConflict' -Category InvalidArgument }
        $effectiveType = 'resourcepack'
    }
    if ($Category -and $Category -ne 'unclassified' -and -not $Inventory.Metadata.Categories.ContainsKey($Category)) {
        $available = @($Inventory.Metadata.Categories.Keys | Sort-Object) -join ', '
        Throw-MpError -Message "Category '$Category' is not defined; available categories: $available, unclassified" -Hint 'modpack inventory --type mod' -ErrorId 'Inventory.UnknownCategory' -Category InvalidArgument -TargetObject $Category
    }

    function Test-InventoryCommonFilter {
        param($Item)
        if ($normalizedSource -and [string]$Item.Source -ne $normalizedSource) { return $false }
        if ($Search) {
            $haystack = @($Item.Name, $Item.Id, $Item.Filename) -join ' '
            if ($haystack.IndexOf($Search, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) { return $false }
        }
        return $true
    }

    $mods = @(if ($effectiveType -in @('all', 'mod')) {
        $Inventory.Mods | Where-Object {
            (Test-InventoryCommonFilter $_) -and
            (-not $Category -or $_.Category -eq $Category) -and
            (-not $normalizedSide -or $_.Side -eq $normalizedSide)
        }
    })

    $activeResources = @(if ($effectiveType -in @('all', 'resourcepack') -and $normalizedState -ne 'inactive') {
        $Inventory.ActiveResources | Where-Object { Test-InventoryCommonFilter $_ }
    })
    $inactiveResources = @(if ($effectiveType -in @('all', 'resourcepack') -and $normalizedState -ne 'active') {
        $Inventory.InactiveResources | Where-Object { Test-InventoryCommonFilter $_ }
    })
    $shaders = @(if ($effectiveType -in @('all', 'shaderpack')) {
        $Inventory.Shaders | Where-Object { Test-InventoryCommonFilter $_ }
    })

    $includedTypes = switch ($effectiveType) {
        'mod'          { @('mod') }
        'resourcepack' { @('resourcepack') }
        'shaderpack'   { @('shaderpack') }
        default        { @('mod', 'resourcepack', 'shaderpack') }
    }
    $filterLabels = [System.Collections.Generic.List[string]]::new()
    if ($effectiveType -ne 'all') { $filterLabels.Add("type=$effectiveType") }
    if ($Category) { $filterLabels.Add("category=$Category") }
    if ($normalizedSide) { $filterLabels.Add("side=$(if ($normalizedSide -eq 'server') { 'host' } else { $normalizedSide })") }
    if ($normalizedSource) { $filterLabels.Add("source=$normalizedSource") }
    if ($normalizedState -ne 'all') { $filterLabels.Add("state=$normalizedState") }
    if ($Search) { $filterLabels.Add("search=$Search") }

    [pscustomobject]@{
        Project           = $Inventory.Project
        Metadata          = $Inventory.Metadata
        Mods              = @($mods)
        ActiveResources   = @($activeResources)
        InactiveResources = @($inactiveResources)
        Shaders           = @($shaders)
        IncludedTypes     = $includedTypes
        Filters           = @($filterLabels)
        TotalMatches      = $mods.Count + $activeResources.Count + $inactiveResources.Count + $shaders.Count
    }
}

function Get-ModpackInventoryReferenceItems {
    param([Parameter(Mandatory)]$View)

    $items = [System.Collections.Generic.List[object]]::new()
    $orderedCategories = @(
        foreach ($key in $View.Metadata.Categories.Keys) {
            $value = $View.Metadata.Categories[$key]
            [pscustomobject]@{
                Key   = [string]$key
                Name  = $(if ($value.ContainsKey('Name')) { [string]$value.Name } else { ([string]$key).ToUpperInvariant() })
                Order = $(if ($value.ContainsKey('Order')) { [int]$value.Order } else { 1000 })
            }
        }
    ) | Sort-Object Order, Name

    foreach ($category in $orderedCategories) {
        foreach ($item in @($View.Mods | Where-Object Category -eq $category.Key | Sort-Object Name)) { $items.Add($item) }
    }
    foreach ($item in @($View.Mods | Where-Object Category -eq 'unclassified' | Sort-Object Name)) { $items.Add($item) }
    foreach ($item in @($View.ActiveResources)) { $items.Add($item) }
    foreach ($item in @($View.InactiveResources | Sort-Object Name)) { $items.Add($item) }
    foreach ($item in @($View.Shaders | Sort-Object Name)) { $items.Add($item) }
    return $items.ToArray()
}

function Get-ModpackInventoryCachePath {
    Join-Path (Get-ModpackToolsConfigDirectory) 'last-inventory.json'
}

function Set-ModpackInventoryReferences {
    param([Parameter(Mandatory)]$View)

    $results = @(
        $index = 0
        foreach ($item in @(Get-ModpackInventoryReferenceItems -View $View)) {
            $index++
            $item | Add-Member -NotePropertyName ReferenceNumber -NotePropertyValue $index -Force
            $selector = if ($item.Kind -eq 'resourcepack' -and $item.Filename) { [string]$item.Filename } else { [string]$item.Id }
            [pscustomobject]@{
                Index    = $index
                Id       = [string]$item.Id
                Kind     = [string]$item.Kind
                Name     = [string]$item.Name
                Filename = [string]$item.Filename
                Source   = [string]$item.Source
                Selector = $selector
            }
        }
    )
    $cache = [pscustomobject]@{
        SchemaVersion = 1
        CreatedUtc    = [datetime]::UtcNow.ToString('o')
        ProjectId     = [string]$View.Project.Id
        Results       = @($results)
    }
    $path = Get-ModpackInventoryCachePath
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($path)) | Out-Null
    Write-Utf8TextFileAtomic -Path $path -Text ($cache | ConvertTo-Json -Depth 5)
    return $View
}

function Read-ModpackInventoryReferenceCache {
    $path = Get-ModpackInventoryCachePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    try {
        return Get-Content -Raw -LiteralPath $path -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        Throw-MpError -Message "Inventory reference cache '$path' is invalid" -Hint 'modpack inventory' -ErrorId 'Inventory.InvalidReferenceCache' -Category InvalidData -TargetObject $path
    }
}

function Resolve-ModpackInventoryNumber {
    param(
        [Parameter(Mandatory)][string]$Selector,
        [Parameter(Mandatory)]$Project,
        [string[]]$AllowedKinds = @('mod', 'resourcepack', 'shaderpack'),
        [switch]$RequirePackwiz
    )

    if ($Selector -notmatch '^[1-9][0-9]*$') { return $null }
    $cache = Read-ModpackInventoryReferenceCache
    if (-not $cache) {
        Throw-MpError -Message "Inventory reference number '$Selector' cannot be resolved because no inventory has been saved" -Hint 'modpack inventory' -ErrorId 'Inventory.ReferenceCacheNotFound' -Category ObjectNotFound -TargetObject $Selector
    }
    if ([int]$cache.SchemaVersion -ne 1 -or $null -eq $cache.Results -or [string]::IsNullOrWhiteSpace([string]$cache.ProjectId)) {
        Throw-MpError -Message 'The saved inventory reference cache has an unsupported or incomplete structure' -Hint 'modpack inventory' -ErrorId 'Inventory.InvalidReferenceCache' -Category InvalidData
    }
    if (-not (Test-MpCacheTimestamp -CreatedUtc $cache.CreatedUtc)) {
        Throw-MpError -Message 'The saved inventory references have expired' -Hint 'modpack inventory' -ErrorId 'Inventory.ReferenceCacheExpired' -Category InvalidData
    }
    if (-not ([string]$cache.ProjectId).Equals($Project.Id, [System.StringComparison]::OrdinalIgnoreCase)) {
        Throw-MpError -Message "The saved inventory belongs to project '$($cache.ProjectId)', not '$($Project.Id)'" -Hint "modpack inventory --project $($Project.Id)" -ErrorId 'Inventory.ReferenceProjectMismatch' -Category InvalidData -TargetObject $Selector
    }
    $result = @($cache.Results | Where-Object { [int]$_.Index -eq [int]$Selector })
    if ($result.Count -ne 1) {
        $maximum = @($cache.Results).Count
        if ($maximum -eq 0) {
            Throw-MpError -Message 'The saved inventory view contains no numbered items' -Hint 'modpack inventory without filters, or use broader filters' -ErrorId 'Inventory.ReferenceOutOfRange' -Category InvalidArgument -TargetObject $Selector
        }
        Throw-MpError -Message "Inventory reference number '$Selector' does not exist; available range: 1-$maximum" -Hint 'choose a number shown by the latest modpack inventory' -ErrorId 'Inventory.ReferenceOutOfRange' -Category InvalidArgument -TargetObject $Selector
    }
    $item = $result[0]
    if ([string]$item.Kind -notin $AllowedKinds) {
        Throw-MpError -Message "Inventory reference '$Selector' points to '$($item.Kind)', which is not accepted by this command" -Hint 'choose a compatible number from modpack inventory' -ErrorId 'Inventory.ReferenceKindMismatch' -Category InvalidArgument -TargetObject $Selector
    }
    if ($RequirePackwiz -and [string]$item.Source -ne 'packwiz') {
        Throw-MpError -Message "Inventory reference '$Selector' points to '$($item.Source)' content, which Packwiz cannot update" -Hint 'choose Packwiz-managed content from modpack inventory --source packwiz' -ErrorId 'Inventory.ReferenceNotUpdatable' -Category InvalidOperation -TargetObject $Selector
    }
    return $item
}
