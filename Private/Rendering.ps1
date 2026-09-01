$script:Palette = @{
    Client    = $PSStyle.Foreground.FromRgb(116, 143, 252)
    Host      = $PSStyle.Foreground.FromRgb(190, 112, 255)
    Success   = $PSStyle.Foreground.FromRgb(80, 200, 120)
    Error     = $PSStyle.Foreground.FromRgb(245, 90, 90)
    Process   = $PSStyle.Foreground.FromRgb(245, 200, 80)
    Secondary = $PSStyle.Foreground.FromRgb(145, 150, 160)
    Heading   = $PSStyle.Foreground.FromRgb(80, 205, 220)
    Local     = $PSStyle.Foreground.FromRgb(255, 145, 205)
    Reset     = $PSStyle.Reset
}

function Write-MpTitle {
    param([Parameter(Mandatory)][string]$Text)
    Write-Host ''
    Write-Host "$($script:Palette.Heading)$Text$($script:Palette.Reset)"
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

function Write-MpSideEntry {
    param([Parameter(Mandatory)]$Item)

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
    Write-Host "$($script:Palette.Success)✓$($script:Palette.Reset) $($Item.Name)" -NoNewline
    if ($Item.Source -eq 'local') {
        Write-Host "  $($script:Palette.Local)LOCAL$($script:Palette.Reset)" -NoNewline
    }
    Write-Host ''
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

    foreach ($category in $orderedCategories) {
        $items = @($Inventory.Mods | Where-Object Category -eq $category.Key | Sort-Object Name)
        if ($items.Count -eq 0) { continue }
        Write-MpTitle "MODS · $($category.Name) ($($items.Count))"
        foreach ($item in $items) { Write-MpSideEntry $item }
    }

    $unclassified = @($Inventory.Mods | Where-Object Category -eq 'unclassified' | Sort-Object Name)
    if ($unclassified.Count -gt 0) {
        Write-MpTitle "MODS · SIN CLASIFICAR ($($unclassified.Count))"
        foreach ($item in $unclassified) {
            if ($item.InvalidCategory) {
                Write-MpWarning "'$($item.Name)' referencia la categoría inexistente '$($item.InvalidCategory)'; se muestra sin clasificar."
            }
            Write-MpSideEntry $item
        }
    }
}

function Write-ResourcePackInventory {
    param([Parameter(Mandatory)]$Inventory)

    Write-MpTitle "RESOURCE PACKS · PRIORIDAD REAL ($($Inventory.ActiveResources.Count))"
    foreach ($item in $Inventory.ActiveResources) {
        $source = switch ($item.Source) {
            'builtin' { 'integrado' }
            'local'   { 'LOCAL' }
            'missing' { 'archivo no encontrado' }
            default   { $item.Filename }
        }
        Write-Host ('{0,3}. ' -f $item.Priority) -NoNewline
        Write-Host "$($script:Palette.Success)✓$($script:Palette.Reset) $($item.Name)" -NoNewline
        if ($source) { Write-Host "  $($script:Palette.Secondary)$source$($script:Palette.Reset)" }
        else { Write-Host '' }
    }

    if ($Inventory.InactiveResources.Count -gt 0) {
        Write-MpTitle "RESOURCE PACKS · PRESENTES PERO INACTIVOS ($($Inventory.InactiveResources.Count))"
        foreach ($item in $Inventory.InactiveResources) {
            Write-Host "$($script:Palette.Secondary)○$($script:Palette.Reset) $($item.Name)"
        }
    }
}

function Write-ShaderInventory {
    param([Parameter(Mandatory)]$Inventory)
    Write-MpTitle "SHADER PACKS ($($Inventory.Shaders.Count))"
    foreach ($item in $Inventory.Shaders) {
        Write-Host "$($script:Palette.Success)✓$($script:Palette.Reset) $($item.Name)"
    }
}

function Write-ModpackHeader {
    param([Parameter(Mandatory)]$Project, [Parameter(Mandatory)]$Inventory)

    Write-MpTitle "$($Project.DisplayName) $($Project.DisplayVersion)"
    $rows = [ordered]@{
        ID        = $Project.Id
        Minecraft = $Project.MinecraftVersion
        Loader    = $(if ($Project.Loader) { (Get-Culture).TextInfo.ToTitleCase($Project.Loader) } else { '?' })
        Root      = $Project.Root
        Mods      = $Inventory.Mods.Count
        Resources = "$($Inventory.ActiveResources.Count) activos"
        Shaders   = $Inventory.Shaders.Count
    }
    foreach ($key in $rows.Keys) { Write-Host ('{0,-11} {1}' -f $key, $rows[$key]) }
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
    Write-MpTitle 'BUILD COMPLETADO'
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
