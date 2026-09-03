function Get-ModpackMetadata {
    param([Parameter(Mandatory)]$Project)

    $path = Join-Path $Project.Root '.modpack/metadata.psd1'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        return @{ Categories = @{}; Mods = @{}; ResourcePacks = @{} }
    }

    try {
        $metadata = Import-PowerShellDataFile -LiteralPath $path
    }
    catch {
        Throw-MpError -Message "Metadata file '$path' is not a valid PSD1 file" -Details $_.Exception.Message -Hint 'repair .modpack/metadata.psd1' -ErrorId 'Metadata.InvalidFile' -Category InvalidData -TargetObject $path
    }

    foreach ($section in @('Categories', 'Mods', 'ResourcePacks')) {
        if (-not $metadata.ContainsKey($section)) { $metadata[$section] = @{} }
        if ($metadata[$section] -isnot [System.Collections.IDictionary]) {
            Throw-MpError -Message "Section '$section' in metadata file '$path' must be a table" -Hint 'repair .modpack/metadata.psd1' -ErrorId 'Metadata.InvalidSection' -Category InvalidData -TargetObject $section
        }
    }
    if ($metadata.ContainsKey('ContentSchemaVersion') -and $metadata.ContentSchemaVersion -ne 1) {
        Throw-MpError -Message 'Content intent metadata uses an unsupported schema' -Hint 'upgrade ModpackTools before modifying this project' -ErrorId 'Metadata.UnsupportedContentSchema' -Category InvalidData
    }
    if ($metadata.ContainsKey('Content') -and $metadata.Content -isnot [System.Collections.IDictionary]) {
        Throw-MpError -Message 'Content intent metadata must be a table' -Hint 'repair the Content section of metadata.psd1' -ErrorId 'Metadata.InvalidContentSection' -Category InvalidData
    }
    return $metadata
}

function ConvertTo-Psd1Scalar {
    param([AllowNull()]$Value)

    if ($null -eq $Value) { return '$null' }
    if ($Value -is [bool]) { return $(if ($Value) { '$true' } else { '$false' }) }
    if ($Value -is [byte] -or $Value -is [int16] -or $Value -is [int32] -or $Value -is [int64] -or
        $Value -is [uint16] -or $Value -is [uint32] -or $Value -is [uint64] -or
        $Value -is [single] -or $Value -is [double] -or $Value -is [decimal]) {
        return [string]$Value
    }
    return "'$( ([string]$Value).Replace("'", "''") )'"
}

function ConvertTo-Psd1Text {
    param(
        [Parameter(Mandatory)]$Data,
        [int]$Indent = 0
    )

    $pad = ' ' * $Indent
    $childPad = ' ' * ($Indent + 4)
    if ($Data -is [System.Collections.IDictionary]) {
        $lines = [System.Collections.Generic.List[string]]::new()
        $lines.Add('@{')
        foreach ($key in @($Data.Keys | Sort-Object { [string]$_ })) {
            $literalKey = ConvertTo-Psd1Scalar ([string]$key)
            $value = $Data[$key]
            if ($value -is [System.Collections.IDictionary] -or ($value -is [System.Collections.IEnumerable] -and $value -isnot [string])) {
                $nested = ConvertTo-Psd1Text -Data $value -Indent ($Indent + 4)
                $nestedLines = $nested -split "`r?`n"
                $lines.Add("$childPad$literalKey = $($nestedLines[0])")
                foreach ($line in $nestedLines[1..($nestedLines.Count - 1)]) { $lines.Add($line) }
            }
            else {
                $lines.Add("$childPad$literalKey = $(ConvertTo-Psd1Scalar $value)")
            }
        }
        $lines.Add("$pad}")
        return ($lines -join [Environment]::NewLine)
    }

    if ($Data -is [System.Collections.IEnumerable] -and $Data -isnot [string]) {
        $items = @($Data)
        if ($items.Count -eq 0) { return '@()' }
        $lines = [System.Collections.Generic.List[string]]::new()
        $lines.Add('@(')
        foreach ($item in $items) {
            $nested = if ($item -is [System.Collections.IDictionary]) {
                ConvertTo-Psd1Text -Data $item -Indent ($Indent + 4)
            } else { "$childPad$(ConvertTo-Psd1Scalar $item)" }
            foreach ($line in ($nested -split "`r?`n")) { $lines.Add($line) }
        }
        $lines.Add("$pad)")
        return ($lines -join [Environment]::NewLine)
    }

    return (ConvertTo-Psd1Scalar $Data)
}

function Write-PowerShellDataFileAtomic {
    param(
        [Parameter(Mandatory)]$Data,
        [Parameter(Mandatory)][string]$Path
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $directory = Split-Path -Parent $fullPath
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    $temporary = Join-Path $directory ('.' + [System.IO.Path]::GetFileName($fullPath) + '.' + [guid]::NewGuid().ToString('N') + '.tmp')
    $text = (ConvertTo-Psd1Text -Data $Data) + [Environment]::NewLine
    try {
        [System.IO.File]::WriteAllText($temporary, $text, [System.Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $temporary -Destination $fullPath -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
}

function Set-ModMetadataCategory {
    param(
        [Parameter(Mandatory)]$Project,
        [Parameter(Mandatory)][string]$ModId,
        [Parameter(Mandatory)][string]$Category
    )

    $metadata = Get-ModpackMetadata -Project $Project
    $categoryId = $metadata.Categories.Keys | Where-Object { ([string]$_).Equals($Category, [System.StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
    if (-not $categoryId) {
        Throw-MpError -Message "Category '$Category' is not defined for project '$($Project.Id)'" -Hint 'modpack classify list' -ErrorId 'Metadata.UnknownCategory' -Category InvalidArgument -TargetObject $Category
    }
    if (-not $metadata.Mods.ContainsKey($ModId)) { $metadata.Mods[$ModId] = @{} }
    $metadata.Mods[$ModId]['Category'] = [string]$categoryId
    Write-PowerShellDataFileAtomic -Data $metadata -Path (Join-Path $Project.Root '.modpack/metadata.psd1')
}

function Get-ModpackCategoryCachePath {
    Join-Path (Get-ModpackToolsConfigDirectory) 'last-categories.json'
}

function Get-ModpackCategoryView {
    param([Parameter(Mandatory)]$Project)
    $metadata = Get-ModpackMetadata -Project $Project
    $categories = @(
        foreach ($key in $metadata.Categories.Keys) {
            $definition = $metadata.Categories[$key]
            $count = @($metadata.Mods.Keys | Where-Object {
                $entry = $metadata.Mods[$_]
                $entry.ContainsKey('Category') -and ([string]$entry.Category).Equals([string]$key, [System.StringComparison]::OrdinalIgnoreCase)
            }).Count
            [pscustomobject]@{
                Id = [string]$key
                Name = $(if ($definition.ContainsKey('Name')) { [string]$definition.Name } else { ([string]$key).ToUpperInvariant() })
                Order = $(if ($definition.ContainsKey('Order')) { [int]$definition.Order } else { 1000 })
                ModCount = $count
            }
        }
    ) | Sort-Object Order, Name, Id
    $categories = @($categories)
    $definedCategoryCount = $categories.Count
    $inventory = Get-ModpackInventory -Project $Project
    $categories += [pscustomobject]@{
        Id = 'unclassified'
        Name = 'UNCLASSIFIED'
        Order = $null
        ModCount = @($inventory.Mods | Where-Object Category -eq 'unclassified').Count
        IsUnclassified = $true
    }
    $index = 0
    foreach ($category in $categories) {
        $index++
        $category | Add-Member -NotePropertyName ReferenceNumber -NotePropertyValue $index
    }
    return [pscustomobject]@{
        Project = $Project
        Categories = @($categories)
        DefinedCategoryCount = $definedCategoryCount
    }
}

function Write-ModpackCategoryCache {
    param([Parameter(Mandatory)]$View)
    $cache = [pscustomobject]@{
        SchemaVersion = 1
        CreatedUtc = [datetime]::UtcNow.ToString('o')
        ProjectId = [string]$View.Project.Id
        Categories = @($View.Categories | ForEach-Object {
            [pscustomobject]@{ Index = $_.ReferenceNumber; Id = $_.Id; Name = $_.Name }
        })
    }
    Write-Utf8TextFileAtomic -Path (Get-ModpackCategoryCachePath) -Text ($cache | ConvertTo-Json -Depth 5)
}

function Read-ModpackCategoryCache {
    $path = Get-ModpackCategoryCachePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    try { return Get-Content -Raw -LiteralPath $path -Encoding UTF8 | ConvertFrom-Json }
    catch {
        Throw-MpError -Message "Category reference cache '$path' is invalid" -Hint 'modpack classify list' -ErrorId 'Metadata.InvalidCategoryCache' -Category InvalidData -TargetObject $path
    }
}

function Resolve-ModpackCategoryId {
    param(
        [Parameter(Mandatory)]$Project,
        [Parameter(Mandatory)][string]$Selector,
        [switch]$AllowUnclassified
    )
    if ($Selector.Equals('unclassified', [System.StringComparison]::OrdinalIgnoreCase)) {
        if ($AllowUnclassified) { return 'unclassified' }
        Throw-MpError -Message "The 'unclassified' classification cannot be removed" -Hint 'assign affected mods to a category with modpack classify set' -ErrorId 'Metadata.ReservedClassification' -Category InvalidOperation -TargetObject $Selector
    }
    $metadata = Get-ModpackMetadata -Project $Project
    if ($Selector -match '^[1-9][0-9]*$') {
        $cache = Read-ModpackCategoryCache
        if (-not $cache) { Throw-MpError -Message "Category number '$Selector' cannot be resolved because no category list has been saved" -Hint 'modpack classify list' -ErrorId 'Metadata.CategoryCacheNotFound' -Category ObjectNotFound -TargetObject $Selector }
        if (-not (Test-MpCacheTimestamp -CreatedUtc $cache.CreatedUtc)) { Throw-MpError -Message 'The saved category list has expired' -Hint 'modpack classify list' -ErrorId 'Metadata.CategoryCacheExpired' -Category InvalidData }
        if (-not ([string]$cache.ProjectId).Equals($Project.Id, [System.StringComparison]::OrdinalIgnoreCase)) {
            Throw-MpError -Message "The saved category list belongs to project '$($cache.ProjectId)', not '$($Project.Id)'" -Hint "modpack classify list --project $($Project.Id)" -ErrorId 'Metadata.CategoryProjectMismatch' -Category InvalidData -TargetObject $Selector
        }
        $match = @($cache.Categories | Where-Object { [int]$_.Index -eq [int]$Selector })
        if ($match.Count -ne 1) {
            $maximum = @($cache.Categories).Count
            if ($maximum -eq 0) {
                Throw-MpError -Message "Category number '$Selector' cannot be resolved because the saved list contains no categories" -Hint 'modpack classify create <id>' -ErrorId 'Metadata.EmptyCategoryCache' -Category ObjectNotFound -TargetObject $Selector
            }
            Throw-MpError -Message "Category number '$Selector' does not exist; available range: 1-$maximum" -Hint 'modpack classify list' -ErrorId 'Metadata.CategoryReferenceOutOfRange' -Category InvalidArgument -TargetObject $Selector
        }
        $Selector = [string]$match[0].Id
        if ($Selector.Equals('unclassified', [System.StringComparison]::OrdinalIgnoreCase)) {
            if ($AllowUnclassified) { return 'unclassified' }
            Throw-MpError -Message "The 'unclassified' classification cannot be removed" -Hint 'assign affected mods to a category with modpack classify set' -ErrorId 'Metadata.ReservedClassification' -Category InvalidOperation -TargetObject $Selector
        }
    }
    $categoryId = $metadata.Categories.Keys | Where-Object { ([string]$_).Equals($Selector, [System.StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
    if (-not $categoryId) { Throw-MpError -Message "Category '$Selector' is not defined for project '$($Project.Id)'" -Hint 'modpack classify list' -ErrorId 'Metadata.UnknownCategory' -Category InvalidArgument -TargetObject $Selector }
    return [string]$categoryId
}

function New-ModpackCategory {
    param(
        [Parameter(Mandatory)]$Project,
        [Parameter(Mandatory)][string]$Id,
        [string]$Name,
        [Nullable[int]]$Order
    )
    if ($Id -notmatch '^[a-z][a-z0-9-]*$') { Throw-MpError -Message "Category ID '$Id' is invalid; allowed characters: lowercase letters, numbers, hyphens" -Hint 'choose an ID such as world-generation' -ErrorId 'Metadata.InvalidCategoryId' -Category InvalidArgument -TargetObject $Id }
    if ($Id -eq 'unclassified') { Throw-MpError -Message "Category ID 'unclassified' is reserved" -Hint 'choose a different category ID' -ErrorId 'Metadata.ReservedCategoryId' -Category InvalidArgument -TargetObject $Id }
    $metadata = Get-ModpackMetadata -Project $Project
    $existing = $metadata.Categories.Keys | Where-Object { ([string]$_).Equals($Id, [System.StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
    if ($existing) { Throw-MpError -Message "Category '$Id' already exists in project '$($Project.Id)'" -Hint 'modpack classify list' -ErrorId 'Metadata.CategoryAlreadyExists' -Category ResourceExists -TargetObject $Id }
    if ([string]::IsNullOrWhiteSpace($Name)) { $Name = $Id.ToUpperInvariant() }
    if ($null -eq $Order) {
        $orders = @($metadata.Categories.Values | ForEach-Object { if ($_.ContainsKey('Order')) { [int]$_.Order } })
        $Order = if ($orders.Count) { ($orders | Measure-Object -Maximum).Maximum + 10 } else { 10 }
    }
    $metadata.Categories[$Id] = [ordered]@{ Name = $Name; Order = [int]$Order }
    Write-PowerShellDataFileAtomic -Data $metadata -Path (Join-Path $Project.Root '.modpack/metadata.psd1')
    return [pscustomobject]@{ Id = $Id; Name = $Name; Order = [int]$Order }
}

function Remove-ModpackCategory {
    param(
        [Parameter(Mandatory)]$Project,
        [Parameter(Mandatory)][string]$Selector,
        [switch]$Unclassify
    )
    $categoryId = Resolve-ModpackCategoryId -Project $Project -Selector $Selector
    $metadata = Get-ModpackMetadata -Project $Project
    $assigned = @($metadata.Mods.Keys | Where-Object {
        $entry = $metadata.Mods[$_]
        $entry.ContainsKey('Category') -and ([string]$entry.Category).Equals($categoryId, [System.StringComparison]::OrdinalIgnoreCase)
    })
    if ($assigned.Count -and -not $Unclassify) {
        Throw-MpError -Message "Category '$categoryId' is assigned to $($assigned.Count) mod(s) and cannot be removed safely" -Details (@($assigned | Select-Object -First 8) -join ', ') -Hint "modpack classify remove $categoryId --unclassify" -ErrorId 'Metadata.CategoryInUse' -Category InvalidOperation -TargetObject $categoryId
    }
    foreach ($modId in $assigned) {
        $entry = $metadata.Mods[$modId]
        [void]$entry.Remove('Category')
        if ($entry.Count -eq 0) { [void]$metadata.Mods.Remove($modId) }
    }
    [void]$metadata.Categories.Remove($categoryId)
    Write-PowerShellDataFileAtomic -Data $metadata -Path (Join-Path $Project.Root '.modpack/metadata.psd1')
    return [pscustomobject]@{ Id = $categoryId; UnclassifiedCount = $assigned.Count }
}

function Resolve-ModpackModForClassification {
    param(
        [Parameter(Mandatory)]$Project,
        [Parameter(Mandatory)][string]$Selector
    )

    $inventory = Get-ModpackInventory -Project $Project
    $matches = @(
        $inventory.Mods | Where-Object {
            $stem = if ($_.MetadataPath) { [System.IO.Path]::GetFileName($_.MetadataPath) -replace '\.pw\.toml$', '' } else { $null }
            @($_.Name, $_.Id, $_.Filename, $stem) | Where-Object {
                $_ -and ([string]$_).Equals($Selector, [System.StringComparison]::OrdinalIgnoreCase)
            }
        }
    )
    if ($matches.Count -eq 0) {
        Throw-MpError -Message "Mod '$Selector' was not found in project '$($Project.Id)'" -Hint 'modpack inventory --type mod' -ErrorId 'Metadata.ModNotFound' -Category ObjectNotFound -TargetObject $Selector
    }
    if ($matches.Count -gt 1) {
        $ids = @($matches | ForEach-Object Id | Sort-Object -Unique) -join ', '
        Throw-MpError -Message "Mod selector '$Selector' matches more than one mod" -Details "Matching IDs: $ids" -Hint 'use an exact ID or filename' -ErrorId 'Metadata.AmbiguousMod' -Category InvalidArgument -TargetObject $Selector
    }
    return $matches[0]
}

function Set-ModpackModClassification {
    param(
        [Parameter(Mandatory)]$Project,
        [Parameter(Mandatory)][string]$Selector,
        [Parameter(Mandatory)][string]$Category
    )

    $item = Resolve-ModpackModForClassification -Project $Project -Selector $Selector
    $metadata = Get-ModpackMetadata -Project $Project
    $previous = $item.Category
    if ($Category.Equals('unclassified', [System.StringComparison]::OrdinalIgnoreCase)) {
        if ($metadata.Mods.ContainsKey($item.Id)) {
            $entry = $metadata.Mods[$item.Id]
            [void]$entry.Remove('Category')
            if ($entry.Count -eq 0) { [void]$metadata.Mods.Remove($item.Id) }
        }
        $normalizedCategory = 'unclassified'
    }
    else {
        $categoryId = Resolve-ModpackCategoryId -Project $Project -Selector $Category
        if (-not $metadata.Mods.ContainsKey($item.Id)) { $metadata.Mods[$item.Id] = @{} }
        $metadata.Mods[$item.Id]['Category'] = [string]$categoryId
        $normalizedCategory = [string]$categoryId
    }
    Write-PowerShellDataFileAtomic -Data $metadata -Path (Join-Path $Project.Root '.modpack/metadata.psd1')
    $updated = (Get-ModpackInventory -Project $Project).Mods | Where-Object Id -eq $item.Id | Select-Object -First 1
    return [pscustomobject]@{ Item = $updated; PreviousCategory = $previous; Category = $normalizedCategory }
}
