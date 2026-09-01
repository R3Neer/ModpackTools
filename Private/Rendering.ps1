$script:Palette = @{
    Client    = $PSStyle.Foreground.FromRgb(116, 143, 252)
    Host      = $PSStyle.Foreground.FromRgb(190, 112, 255)
    Success   = $PSStyle.Foreground.FromRgb(80, 200, 120)
    Error     = $PSStyle.Foreground.FromRgb(245, 90, 90)
    Process   = $PSStyle.Foreground.FromRgb(245, 200, 80)
    Secondary = $PSStyle.Foreground.FromRgb(145, 150, 160)
    Heading   = $PSStyle.Foreground.FromRgb(80, 205, 220)
    Local     = $PSStyle.Foreground.FromRgb(255, 145, 205)
    Accent    = $PSStyle.Foreground.FromRgb(255, 170, 70)
    Value     = $PSStyle.Foreground.FromRgb(235, 238, 245)
    Reset     = $PSStyle.Reset
}

function Write-MpTitle {
    param([Parameter(Mandatory)][string]$Text)
    Write-Host ''
    Write-Host "$($script:Palette.Heading)$Text$($script:Palette.Reset)"
}

function Write-MpBanner {
    param([Parameter(Mandatory)][string]$Text)
    $line = '═' * 68
    Write-Host ''
    Write-Host "$($script:Palette.Secondary)$line$($script:Palette.Reset)"
    Write-Host " $($script:Palette.Heading)$($PSStyle.Bold)$Text$($PSStyle.Reset)"
    Write-Host "$($script:Palette.Secondary)$line$($script:Palette.Reset)"
}

function Write-MpSection {
    param([Parameter(Mandatory)][string]$Title, [Parameter(Mandatory)][int]$Count)
    $prefix = "  $Title"
    $countText = [string]$Count
    $padding = [Math]::Max(1, 66 - $prefix.Length - $countText.Length)
    Write-Host ''
    Write-Host "$($script:Palette.Heading)$prefix$($script:Palette.Reset)" -NoNewline
    Write-Host (' ' * $padding) -NoNewline
    Write-Host "$($script:Palette.Accent)$countText$($script:Palette.Reset)"
    Write-Host "$($script:Palette.Secondary)  $('─' * 64)$($script:Palette.Reset)"
}

function Write-MpStep {
    param([Parameter(Mandatory)][string]$Text)
    Write-Host "$($script:Palette.Process)→$($script:Palette.Reset) $Text"
}

function Write-MpSuccess {
    param([Parameter(Mandatory)][string]$Text)
    Write-Host "$($script:Palette.Success)✓$($script:Palette.Reset) $Text"
}

function Write-MpWarning {
    param([Parameter(Mandatory)][string]$Text)
    Write-Warning $Text
}

function Write-MpSideLegend {
    Write-Host ''
    Write-Host '  ' -NoNewline
    Write-Host "$($script:Palette.Client)[C]$($script:Palette.Reset)" -NoNewline
    Write-Host "$($script:Palette.Secondary) Cliente    $($script:Palette.Reset)" -NoNewline
    Write-Host "$($script:Palette.Host)[H]$($script:Palette.Reset)" -NoNewline
    Write-Host "$($script:Palette.Secondary) Host    $($script:Palette.Reset)" -NoNewline
    Write-Host "$($script:Palette.Client)[C]$($script:Palette.Reset)$($script:Palette.Host)[H]$($script:Palette.Reset)" -NoNewline
    Write-Host "$($script:Palette.Secondary) Ambos$($script:Palette.Reset)"
}

function Write-MpSideEntry {
    param([Parameter(Mandatory)]$Item)

    Write-Host '  ' -NoNewline
    switch ($Item.Side) {
        'client' {
            Write-Host "$($script:Palette.Client)[C]$($script:Palette.Reset)    " -NoNewline
        }
        'server' {
            Write-Host "$($script:Palette.Host)[H]$($script:Palette.Reset)    " -NoNewline
        }
        'both' {
            Write-Host "$($script:Palette.Client)[C]$($script:Palette.Reset)$($script:Palette.Host)[H]$($script:Palette.Reset) " -NoNewline
        }
        default {
            Write-Host "$($script:Palette.Secondary)[?]$($script:Palette.Reset)    " -NoNewline
        }
    }
    Write-Host "$($script:Palette.Success)✓$($script:Palette.Reset) $($PSStyle.Bold)$($Item.Name)$($PSStyle.Reset)" -NoNewline
    if ($Item.Source -eq 'local') {
        Write-Host "  $($script:Palette.Local)LOCAL$($script:Palette.Reset)" -NoNewline
    }
    if ($Item.Filename) { Write-Host "  $($script:Palette.Secondary)$($Item.Filename)$($script:Palette.Reset)" }
    else { Write-Host '' }
}

function Write-ModInventory {
    param([Parameter(Mandatory)]$Inventory)

    $metadata = $Inventory.Metadata
    $orderedCategories = @(
        foreach ($key in $metadata.Categories.Keys) {
            $value = $metadata.Categories[$key]
            [pscustomobject]@{
                Key   = [string]$key
                Name  = $(if ($value.ContainsKey('Name')) { [string]$value.Name } else { ([string]$key).ToUpperInvariant() })
                Order = $(if ($value.ContainsKey('Order')) { [int]$value.Order } else { 1000 })
            }
        }
    ) | Sort-Object Order, Name

    if ($Inventory.Mods.Count -eq 0) {
        Write-MpSection 'MODS' 0
        Write-Host "$($script:Palette.Secondary)  · Ninguno$($script:Palette.Reset)"
        return
    }
    Write-MpSideLegend
    foreach ($category in $orderedCategories) {
        $items = @($Inventory.Mods | Where-Object Category -eq $category.Key | Sort-Object Name)
        if ($items.Count -eq 0) { continue }
        Write-MpSection "MODS · $($category.Name)" $items.Count
        foreach ($item in $items) { Write-MpSideEntry $item }
    }

    $unclassified = @($Inventory.Mods | Where-Object Category -eq 'unclassified' | Sort-Object Name)
    if ($unclassified.Count -gt 0) {
        Write-MpSection 'MODS · SIN CLASIFICAR' $unclassified.Count
        foreach ($item in $unclassified) {
            if ($item.InvalidCategory) {
                Write-MpWarning "'$($item.Name)' referencia la categoría inexistente '$($item.InvalidCategory)'; se muestra sin clasificar."
            }
            Write-MpSideEntry $item
        }
    }
}

function Write-ResourcePackInventory {
    param([Parameter(Mandatory)]$Inventory, [switch]$HideEmptySections)

    if (-not $HideEmptySections -or $Inventory.ActiveResources.Count -gt 0 -or $Inventory.InactiveResources.Count -eq 0) {
        Write-MpSection 'RESOURCE PACKS · PRIORIDAD REAL' $Inventory.ActiveResources.Count
        if ($Inventory.ActiveResources.Count -eq 0) {
            Write-Host "$($script:Palette.Secondary)  · Ninguno$($script:Palette.Reset)"
        }
    }
    foreach ($item in $Inventory.ActiveResources) {
        $source = switch ($item.Source) {
            'builtin' { 'integrado' }
            'local'   { $item.Filename }
            'missing' { 'archivo no encontrado' }
            default   { $item.Filename }
        }
        Write-Host "$($script:Palette.Secondary)$('{0,3}. ' -f $item.Priority)$($script:Palette.Reset)" -NoNewline
        Write-Host "$($script:Palette.Success)✓$($script:Palette.Reset) $($PSStyle.Bold)$($item.Name)$($PSStyle.Reset)" -NoNewline
        if ($item.Source -eq 'local') { Write-Host "  $($script:Palette.Local)LOCAL$($script:Palette.Reset)" -NoNewline }
        if ($source) { Write-Host "  $($script:Palette.Secondary)$source$($script:Palette.Reset)" }
        else { Write-Host '' }
    }

    if ($Inventory.InactiveResources.Count -gt 0) {
        Write-MpSection 'RESOURCE PACKS · PRESENTES PERO INACTIVOS' $Inventory.InactiveResources.Count
        foreach ($item in $Inventory.InactiveResources) {
            Write-Host "  $($script:Palette.Secondary)○$($script:Palette.Reset) $($PSStyle.Bold)$($item.Name)$($PSStyle.Reset)" -NoNewline
            if ($item.Source -eq 'local') { Write-Host "  $($script:Palette.Local)LOCAL$($script:Palette.Reset)" -NoNewline }
            if ($item.Filename) { Write-Host "  $($script:Palette.Secondary)$($item.Filename)$($script:Palette.Reset)" }
            else { Write-Host '' }
        }
    }
}

function Write-ShaderInventory {
    param([Parameter(Mandatory)]$Inventory)
    Write-MpSection 'SHADER PACKS' $Inventory.Shaders.Count
    if ($Inventory.Shaders.Count -eq 0) {
        Write-Host "$($script:Palette.Secondary)  · Ninguno$($script:Palette.Reset)"
        return
    }
    foreach ($item in $Inventory.Shaders) {
        $shaderEntry = [pscustomobject]@{ Side = 'client'; Name = $item.Name; Source = $item.Source; Filename = $item.Filename }
        Write-MpSideEntry $shaderEntry
    }
}

function Write-InventoryView {
    param([Parameter(Mandatory)]$View, [switch]$ShowFilters)
    if ($ShowFilters) {
        $description = if ($View.Filters.Count) { $View.Filters -join ' · ' } else { 'ninguno' }
        Write-Host ''
        Write-Host "$($script:Palette.Accent)FILTROS$($script:Palette.Reset)  $($script:Palette.Secondary)$description$($script:Palette.Reset)"
        Write-Host "$($script:Palette.Accent)COINCIDENCIAS$($script:Palette.Reset)  $($View.TotalMatches)"
    }
    if ($View.TotalMatches -eq 0) {
        Write-Host ''
        Write-Host "$($script:Palette.Process)No hay elementos que coincidan con los filtros.$($script:Palette.Reset)"
        return
    }
    $hideEmpty = $View.Filters.Count -gt 0
    if (($View.IncludedTypes -contains 'mod') -and (-not $hideEmpty -or $View.Mods.Count -gt 0)) {
        Write-ModInventory $View
    }
    $resourceCount = $View.ActiveResources.Count + $View.InactiveResources.Count
    if (($View.IncludedTypes -contains 'resourcepack') -and (-not $hideEmpty -or $resourceCount -gt 0)) {
        Write-ResourcePackInventory $View -HideEmptySections:$hideEmpty
    }
    if (($View.IncludedTypes -contains 'shaderpack') -and (-not $hideEmpty -or $View.Shaders.Count -gt 0)) {
        Write-ShaderInventory $View
    }
}

function Write-ModpackHeader {
    param([Parameter(Mandatory)]$Project, [Parameter(Mandatory)]$Inventory)

    Write-MpBanner "$($Project.DisplayName) $($Project.DisplayVersion)"
    $rows = [ordered]@{
        ID        = $Project.Id
        Minecraft = $Project.MinecraftVersion
        Loader    = $(if ($Project.Loader) { (Get-Culture).TextInfo.ToTitleCase($Project.Loader) } else { '?' })
        Root      = $Project.Root
        Mods      = $Inventory.Mods.Count
        Resources = "$($Inventory.ActiveResources.Count) activos"
        Shaders   = $Inventory.Shaders.Count
    }
    foreach ($key in $rows.Keys) {
        Write-Host "$($script:Palette.Secondary)$('{0,-11}' -f $key)$($script:Palette.Reset) " -NoNewline
        Write-Host "$($script:Palette.Value)$($rows[$key])$($script:Palette.Reset)"
    }
}

function Write-ModpackList {
    param([Parameter(Mandatory)][array]$Projects, [Parameter(Mandatory)][string]$Root)
    Write-MpBanner 'MODPACKS REGISTRADOS'
    Write-Host "$($script:Palette.Secondary)Root: $Root$($script:Palette.Reset)"
    Write-Host ''
    Write-Host "$($script:Palette.Secondary)$('{0,-10} {1,-24} {2,-10} {3}' -f 'ID','NAME','MC','LOADER')$($script:Palette.Reset)"
    Write-Host "$($script:Palette.Secondary)$('─' * 60)$($script:Palette.Reset)"
    foreach ($project in $Projects) {
        Write-Host "$($script:Palette.Accent)$('{0,-10}' -f $project.Id)$($script:Palette.Reset) " -NoNewline
        Write-Host "$($PSStyle.Bold)$('{0,-24}' -f $project.DisplayName)$($PSStyle.Reset) " -NoNewline
        Write-Host "$($script:Palette.Value)$('{0,-10}' -f $project.MinecraftVersion)$($script:Palette.Reset) " -NoNewline
        Write-Host "$($script:Palette.Heading)$($project.Loader)$($script:Palette.Reset)"
    }
    Write-Host ''
    Write-Host "$($script:Palette.Secondary)$($Projects.Count) proyecto(s)$($script:Palette.Reset)"
}

function Format-ByteSize {
    param([long]$Bytes)
    if ($Bytes -ge 1GB) { return '{0:N2} GB' -f ($Bytes / 1GB) }
    if ($Bytes -ge 1MB) { return '{0:N2} MB' -f ($Bytes / 1MB) }
    if ($Bytes -ge 1KB) { return '{0:N2} KB' -f ($Bytes / 1KB) }
    return "$Bytes B"
}

function Write-BuildSummary {
    param([Parameter(Mandatory)]$Build)
    $mods = $Build.Inventory.Mods
    Write-MpBanner 'BUILD COMPLETADO'
    Write-MpSuccess 'Estado: correcto'
    foreach ($row in ([ordered]@{
        Archivo          = [System.IO.Path]::GetFileName($Build.Path)
        Tamaño           = Format-ByteSize $Build.Size
        Duración         = ('{0:N2} s' -f $Build.Duration.TotalSeconds)
        Mods             = $mods.Count
        'Solo cliente'   = @($mods | Where-Object Side -eq client).Count
        'Solo host'      = @($mods | Where-Object Side -eq server).Count
        Ambos            = @($mods | Where-Object Side -eq both).Count
        Desconocidos     = @($mods | Where-Object Side -notin @('client', 'server', 'both')).Count
        'Resources activos' = $Build.Inventory.ActiveResources.Count
        Shaders          = $Build.Inventory.Shaders.Count
        Ruta             = $Build.Path
    }).GetEnumerator()) {
        Write-Host ('{0,-20} {1}' -f $row.Key, $row.Value)
    }
}
