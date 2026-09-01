function Read-MpThemeColors {
    param([string]$Path = (Join-Path $script:ModuleRoot 'theme.toml'))

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Theme file does not exist: $Path"
    }
    $text = Get-Content -Raw -LiteralPath $Path -Encoding UTF8
    $colors = [ordered]@{}
    foreach ($name in @('client', 'host', 'success', 'error', 'process', 'secondary', 'heading', 'local', 'accent', 'value')) {
        $value = Get-TomlString -Text $text -Section colors -Key $name
        if (-not $value) { throw "Required theme color '$name' is missing from '$Path'." }
        if ($value -notmatch '^#[0-9A-Fa-f]{6}$') {
            throw "Theme color '$name' must use the #RRGGBB format; found '$value'."
        }
        $colors[$name] = $value.ToUpperInvariant()
    }
    return $colors
}

function ConvertTo-MpAnsiColor {
    param([Parameter(Mandatory)][string]$Hex)
    return $PSStyle.Foreground.FromRgb(
        [Convert]::ToInt32($Hex.Substring(1, 2), 16),
        [Convert]::ToInt32($Hex.Substring(3, 2), 16),
        [Convert]::ToInt32($Hex.Substring(5, 2), 16)
    )
}

$themeColors = Read-MpThemeColors
$script:Palette = @{
    Client    = ConvertTo-MpAnsiColor $themeColors.client
    Host      = ConvertTo-MpAnsiColor $themeColors.host
    Success   = ConvertTo-MpAnsiColor $themeColors.success
    Error     = ConvertTo-MpAnsiColor $themeColors.error
    Process   = ConvertTo-MpAnsiColor $themeColors.process
    Secondary = ConvertTo-MpAnsiColor $themeColors.secondary
    Heading   = ConvertTo-MpAnsiColor $themeColors.heading
    Local     = ConvertTo-MpAnsiColor $themeColors.local
    Accent    = ConvertTo-MpAnsiColor $themeColors.accent
    Value     = ConvertTo-MpAnsiColor $themeColors.value
    Reset     = $PSStyle.Reset
}
Remove-Variable themeColors

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

function Write-MpInfo {
    param([Parameter(Mandatory)][string]$Text)
    Write-Host "$($script:Palette.Heading)•$($script:Palette.Reset) $($script:Palette.Value)$Text$($script:Palette.Reset)"
}

function Write-MpKeyValue {
    param([Parameter(Mandatory)][string]$Key, [AllowNull()][object]$Value, [int]$Width = 16)
    Write-Host "$($script:Palette.Secondary)$($Key.PadRight($Width))$($script:Palette.Reset) " -NoNewline
    Write-Host "$($script:Palette.Value)$Value$($script:Palette.Reset)"
}

function Write-MpCommandLine {
    param([Parameter(Mandatory)][string]$Command, [string]$Description)
    Write-Host "  $($script:Palette.Accent)$Command$($script:Palette.Reset)" -NoNewline
    if ($Description) { Write-Host "  $($script:Palette.Secondary)$Description$($script:Palette.Reset)" }
    else { Write-Host '' }
}

function Write-MpUsage {
    param([Parameter(Mandatory)][string]$Text)
    Write-Host "$($script:Palette.Secondary)Usage:$($script:Palette.Reset) $($script:Palette.Accent)$Text$($script:Palette.Reset)"
}

function Write-MpSideLegend {
    Write-Host ''
    Write-Host '  ' -NoNewline
    Write-Host "$($script:Palette.Client)[C]$($script:Palette.Reset)" -NoNewline
    Write-Host "$($script:Palette.Secondary) Client    $($script:Palette.Reset)" -NoNewline
    Write-Host "$($script:Palette.Host)[H]$($script:Palette.Reset)" -NoNewline
    Write-Host "$($script:Palette.Secondary) Host    $($script:Palette.Reset)" -NoNewline
    Write-Host "$($script:Palette.Client)[C]$($script:Palette.Reset)$($script:Palette.Host)[H]$($script:Palette.Reset)" -NoNewline
    Write-Host "$($script:Palette.Secondary) Both$($script:Palette.Reset)"
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
        Write-Host "$($script:Palette.Secondary)  · None$($script:Palette.Reset)"
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
        Write-MpSection 'MODS · UNCLASSIFIED' $unclassified.Count
        foreach ($item in $unclassified) {
            if ($item.InvalidCategory) {
                Write-MpWarning "'$($item.Name)' references the missing category '$($item.InvalidCategory)' and is shown as unclassified."
            }
            Write-MpSideEntry $item
        }
    }
}

function Write-ResourcePackInventory {
    param([Parameter(Mandatory)]$Inventory, [switch]$HideEmptySections)

    if (-not $HideEmptySections -or $Inventory.ActiveResources.Count -gt 0 -or $Inventory.InactiveResources.Count -eq 0) {
        Write-MpSection 'RESOURCE PACKS · ACTUAL PRIORITY' $Inventory.ActiveResources.Count
        if ($Inventory.ActiveResources.Count -eq 0) {
            Write-Host "$($script:Palette.Secondary)  · None$($script:Palette.Reset)"
        }
    }
    foreach ($item in $Inventory.ActiveResources) {
        $source = switch ($item.Source) {
            'builtin' { 'built-in' }
            'local'   { $item.Filename }
            'missing' { 'file not found' }
            default   { $item.Filename }
        }
        Write-Host "$($script:Palette.Secondary)$('{0,3}. ' -f $item.Priority)$($script:Palette.Reset)" -NoNewline
        Write-Host "$($script:Palette.Success)✓$($script:Palette.Reset) $($PSStyle.Bold)$($item.Name)$($PSStyle.Reset)" -NoNewline
        if ($item.Source -eq 'local') { Write-Host "  $($script:Palette.Local)LOCAL$($script:Palette.Reset)" -NoNewline }
        if ($source) { Write-Host "  $($script:Palette.Secondary)$source$($script:Palette.Reset)" }
        else { Write-Host '' }
    }

    if ($Inventory.InactiveResources.Count -gt 0) {
        Write-MpSection 'RESOURCE PACKS · PRESENT BUT DISABLED' $Inventory.InactiveResources.Count
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
        Write-Host "$($script:Palette.Secondary)  · None$($script:Palette.Reset)"
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
        $description = if ($View.Filters.Count) { $View.Filters -join ' · ' } else { 'none' }
        Write-Host ''
        Write-Host "$($script:Palette.Accent)FILTERS$($script:Palette.Reset)  $($script:Palette.Secondary)$description$($script:Palette.Reset)"
        Write-Host "$($script:Palette.Accent)MATCHES$($script:Palette.Reset)  $($View.TotalMatches)"
    }
    if ($View.TotalMatches -eq 0) {
        Write-Host ''
        Write-Host "$($script:Palette.Process)No items match the filters.$($script:Palette.Reset)"
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
        Resources = "$($Inventory.ActiveResources.Count) enabled"
        Shaders   = $Inventory.Shaders.Count
    }
    foreach ($key in $rows.Keys) {
        Write-Host "$($script:Palette.Secondary)$('{0,-11}' -f $key)$($script:Palette.Reset) " -NoNewline
        Write-Host "$($script:Palette.Value)$($rows[$key])$($script:Palette.Reset)"
    }
}

function Write-ModpackList {
    param([Parameter(Mandatory)][array]$Projects, [Parameter(Mandatory)][string]$Root)
    Write-MpBanner 'REGISTERED MODPACKS'
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
    Write-Host "$($script:Palette.Secondary)$($Projects.Count) project(s)$($script:Palette.Reset)"
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
    Write-MpBanner 'BUILD COMPLETE'
    Write-MpSuccess 'Status: successful'
    foreach ($row in ([ordered]@{
        File             = [System.IO.Path]::GetFileName($Build.Path)
        Size             = Format-ByteSize $Build.Size
        Duration         = ('{0:N2} s' -f $Build.Duration.TotalSeconds)
        Mods             = $mods.Count
        'Client only'    = @($mods | Where-Object Side -eq client).Count
        'Host only'      = @($mods | Where-Object Side -eq server).Count
        Both             = @($mods | Where-Object Side -eq both).Count
        Unknown          = @($mods | Where-Object Side -notin @('client', 'server', 'both')).Count
        'Enabled resources' = $Build.Inventory.ActiveResources.Count
        Shaders          = $Build.Inventory.Shaders.Count
        Path             = $Build.Path
    }).GetEnumerator()) {
        Write-MpKeyValue -Key $row.Key -Value $row.Value -Width 20
    }
}

function Write-MpDiffItems {
    param([Parameter(Mandatory)][AllowEmptyCollection()][array]$Items, [Parameter(Mandatory)][ValidateSet('Added', 'Changed', 'Removed')][string]$Status)
    if ($Items.Count -eq 0) { return }
    $settings = switch ($Status) {
        'Added'   { @{ Symbol = '+'; Color = $script:Palette.Success } }
        'Changed' { @{ Symbol = '~'; Color = $script:Palette.Process } }
        'Removed' { @{ Symbol = '-'; Color = $script:Palette.Error } }
    }
    Write-MpSection ("DIFF · " + $Status.ToUpperInvariant()) $Items.Count
    foreach ($item in $Items) {
        Write-Host "  $($settings.Color)$($settings.Symbol)$($script:Palette.Reset) " -NoNewline
        Write-Host "$($script:Palette.Accent)$('{0,-10}' -f "[$($item.Kind)]")$($script:Palette.Reset) " -NoNewline
        Write-Host "$($script:Palette.Value)$($item.Path)$($script:Palette.Reset)"
    }
}

function Write-ModpackDiff {
    param([Parameter(Mandatory)]$Diff)
    Write-MpBanner "DIFF · $($Diff.Project.DisplayName)"
    Write-MpKeyValue 'Baseline' ([System.IO.Path]::GetFileName($Diff.BaselinePath))
    Write-MpKeyValue 'Built' $Diff.BaselineTime
    if ($Diff.Total -eq 0) {
        Write-Host ''
        Write-MpSuccess 'No differences from the latest build.'
        return
    }
    Write-MpDiffItems -Items $Diff.Added -Status Added
    Write-MpDiffItems -Items $Diff.Changed -Status Changed
    Write-MpDiffItems -Items $Diff.Removed -Status Removed
    Write-Host ''
    Write-MpKeyValue 'Differences' $Diff.Total
}
