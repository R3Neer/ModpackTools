function Get-MpReferenceWidth {
    param([Parameter(Mandatory)][AllowEmptyCollection()][array]$Items, [string]$PropertyName = 'ReferenceNumber')
    $references = @($Items | Where-Object { $_.PSObject.Properties[$PropertyName] } | ForEach-Object { [string]$_.PSObject.Properties[$PropertyName].Value })
    if (-not $references.Count) { return 0 }
    return @($references | ForEach-Object Length | Measure-Object -Maximum).Maximum
}

function Format-MpReferenceLabel {
    param([Parameter(Mandatory)][int]$Reference, [Parameter(Mandatory)][int]$Width)
    $format = '{0,' + $Width + '}'
    return '[' + ($format -f $Reference) + ']'
}

function Write-MpSideEntry {
    param([Parameter(Mandatory)]$Item, [int]$ReferenceWidth = 0)

    if ($Item.PSObject.Properties['ReferenceNumber']) {
        $label = Format-MpReferenceLabel -Reference $Item.ReferenceNumber -Width $ReferenceWidth
        Write-R3Line (Get-MpConsole) @(@{Text="  "}, @{Text="$label";Role='accent'}, @{Text=" "}) -NoNewline
    }
    else { Write-R3Line (Get-MpConsole) @(@{Text='  '}) -NoNewline }
    switch ($Item.Side) {
        'client' {
            Write-R3Line (Get-MpConsole) @(@{Text="[C]";Role='client'}, @{Text="    "}) -NoNewline
        }
        'server' {
            Write-R3Line (Get-MpConsole) @(@{Text="[H]";Role='host'}, @{Text="    "}) -NoNewline
        }
        'both' {
            Write-R3Line (Get-MpConsole) @(@{Text="[C]";Role='client'}, @{Text="[H]";Role='host'}, @{Text=" "}) -NoNewline
        }
        default {
            Write-R3Line (Get-MpConsole) @(@{Text="[?]";Role='secondary'}, @{Text="    "}) -NoNewline
        }
    }
    Write-R3Line (Get-MpConsole) @(@{Text="$(Get-R3Symbol (Get-MpConsole) success)";Role='success'}, @{Text=" "}, @{Text="$($Item.Name)";Bold=$true}) -NoNewline
    if ((Get-MpPropertyValue $Item 'Pinned') -eq $true) { Write-R3Line (Get-MpConsole) @(@{Text=' [pinned]'}) -NoNewline }
    if ($Item.Source -eq 'local') {
        Write-R3Line (Get-MpConsole) @(@{Text="  "}, @{Text="LOCAL";Role='local'}) -NoNewline
    }
    if ($Item.Filename) { Write-R3Line (Get-MpConsole) @(@{Text="  "}, @{Text="$($Item.Filename)";Role='secondary'}) }
    else { Write-R3Line (Get-MpConsole) @(@{Text=''}) }
}

function Write-ModInventory {
    param([Parameter(Mandatory)]$Inventory, [int]$ReferenceWidth = 0)

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
        Write-R3Section (Get-MpConsole) 'MODS' 0
        Write-R3Line (Get-MpConsole) @(@{Text="  · None";Role='secondary'})
        return
    }
    Write-MpSideLegend
    foreach ($category in $orderedCategories) {
        $items = @($Inventory.Mods | Where-Object Category -eq $category.Key | Sort-Object Name)
        if ($items.Count -eq 0) { continue }
        Write-R3Section (Get-MpConsole) "MODS · $($category.Name)" $items.Count
        foreach ($item in $items) { Write-MpSideEntry -Item $item -ReferenceWidth $ReferenceWidth }
    }

    $unclassified = @($Inventory.Mods | Where-Object Category -eq 'unclassified' | Sort-Object Name)
    if ($unclassified.Count -gt 0) {
        Write-R3Section (Get-MpConsole) 'MODS · UNCLASSIFIED' $unclassified.Count
        foreach ($item in $unclassified) {
            if ($item.InvalidCategory) {
                Write-R3Status (Get-MpConsole) warning "'$($item.Name)' references the missing category '$($item.InvalidCategory)' and is shown as unclassified."
            }
            Write-MpSideEntry -Item $item -ReferenceWidth $ReferenceWidth
        }
    }
}

function Write-ResourcePackInventory {
    param([Parameter(Mandatory)]$Inventory, [switch]$HideEmptySections, [int]$ReferenceWidth = 0)

    if (-not $HideEmptySections -or $Inventory.ActiveResources.Count -gt 0 -or $Inventory.InactiveResources.Count -eq 0) {
        Write-R3Section (Get-MpConsole) 'RESOURCE PACKS · ACTUAL PRIORITY' $Inventory.ActiveResources.Count
        if ($Inventory.ActiveResources.Count -eq 0) {
            Write-R3Line (Get-MpConsole) @(@{Text="  · None";Role='secondary'})
        }
    }
    foreach ($item in $Inventory.ActiveResources) {
        $source = switch ($item.Source) {
            'builtin' { 'built-in' }
            'local'   { $item.Filename }
            'missing' { 'file not found' }
            default   { $item.Filename }
        }
        if ($item.PSObject.Properties['ReferenceNumber']) {
            $label = Format-MpReferenceLabel -Reference $item.ReferenceNumber -Width $ReferenceWidth
            Write-R3Line (Get-MpConsole) @(@{Text="  "}, @{Text="$label";Role='accent'}, @{Text=" "}) -NoNewline
        }
        else {
            Write-R3Line (Get-MpConsole) @(@{Text="$('{0,3}. ' -f $item.Priority)";Role='secondary'}) -NoNewline
        }
        Write-R3Line (Get-MpConsole) @(@{Text="$(Get-R3Symbol (Get-MpConsole) success)";Role='success'}, @{Text=" "}, @{Text="$($item.Name)";Bold=$true}) -NoNewline
        if ($item.PSObject.Properties['ReferenceNumber']) { Write-R3Line (Get-MpConsole) @(@{Text="  "}, @{Text="priority $($item.Priority)";Role='accent'}) -NoNewline }
        if ($item.Source -eq 'local') { Write-R3Line (Get-MpConsole) @(@{Text="  "}, @{Text="LOCAL";Role='local'}) -NoNewline }
        if ($source) { Write-R3Line (Get-MpConsole) @(@{Text="  "}, @{Text="$source";Role='secondary'}) }
        else { Write-R3Line (Get-MpConsole) @(@{Text=''}) }
    }

    if ($Inventory.InactiveResources.Count -gt 0) {
        Write-R3Section (Get-MpConsole) 'RESOURCE PACKS · PRESENT BUT DISABLED' $Inventory.InactiveResources.Count
        foreach ($item in $Inventory.InactiveResources) {
            if ($item.PSObject.Properties['ReferenceNumber']) {
                $label = Format-MpReferenceLabel -Reference $item.ReferenceNumber -Width $ReferenceWidth
                Write-R3Line (Get-MpConsole) @(@{Text="  "}, @{Text="$label";Role='accent'}, @{Text=" "}) -NoNewline
            }
            else { Write-R3Line (Get-MpConsole) @(@{Text='  '}) -NoNewline }
            Write-R3Line (Get-MpConsole) @(@{Text="$(Get-R3Symbol (Get-MpConsole) inactive)";Role='secondary'}, @{Text=" "}, @{Text="$($item.Name)";Bold=$true}) -NoNewline
            if ($item.Source -eq 'local') { Write-R3Line (Get-MpConsole) @(@{Text="  "}, @{Text="LOCAL";Role='local'}) -NoNewline }
            if ($item.Filename) { Write-R3Line (Get-MpConsole) @(@{Text="  "}, @{Text="$($item.Filename)";Role='secondary'}) }
            else { Write-R3Line (Get-MpConsole) @(@{Text=''}) }
        }
    }
}

function Write-ShaderInventory {
    param([Parameter(Mandatory)]$Inventory, [int]$ReferenceWidth = 0)
    Write-R3Section (Get-MpConsole) 'SHADER PACKS' $Inventory.Shaders.Count
    if ($Inventory.Shaders.Count -eq 0) {
        Write-R3Line (Get-MpConsole) @(@{Text="  · None";Role='secondary'})
        return
    }
    foreach ($item in $Inventory.Shaders) {
        $shaderEntry = [pscustomobject]@{ Side = 'client'; Name = $item.Name; Source = $item.Source; Filename = $item.Filename }
        if ($item.PSObject.Properties['ReferenceNumber']) {
            $shaderEntry | Add-Member -NotePropertyName ReferenceNumber -NotePropertyValue $item.ReferenceNumber
        }
        Write-MpSideEntry -Item $shaderEntry -ReferenceWidth $ReferenceWidth
    }
}

function Write-InventoryView {
    param([Parameter(Mandatory)]$View, [switch]$ShowFilters)
    if ($ShowFilters) {
        $description = if ($View.Filters.Count) { $View.Filters -join ' · ' } else { 'none' }
        Write-R3Line (Get-MpConsole) @(@{Text=''})
        Write-R3Line (Get-MpConsole) @(@{Text="FILTERS";Role='accent'}, @{Text="  "}, @{Text="$description";Role='secondary'})
        Write-R3Line (Get-MpConsole) @(@{Text="MATCHES";Role='accent'}, @{Text="  $($View.TotalMatches)"})
    }
    if ($View.TotalMatches -eq 0) {
        Write-R3Line (Get-MpConsole) @(@{Text=''})
        Write-R3Line (Get-MpConsole) @(@{Text="No items match the filters.";Role='process'})
        return
    }
    $hideEmpty = $View.Filters.Count -gt 0
    $referenceWidth = Get-MpReferenceWidth -Items @(Get-ModpackInventoryReferenceItems -View $View)
    if (($View.IncludedTypes -contains 'mod') -and (-not $hideEmpty -or $View.Mods.Count -gt 0)) {
        Write-ModInventory -Inventory $View -ReferenceWidth $referenceWidth
    }
    $resourceCount = $View.ActiveResources.Count + $View.InactiveResources.Count
    if (($View.IncludedTypes -contains 'resourcepack') -and (-not $hideEmpty -or $resourceCount -gt 0)) {
        Write-ResourcePackInventory -Inventory $View -HideEmptySections:$hideEmpty -ReferenceWidth $referenceWidth
    }
    if (($View.IncludedTypes -contains 'shaderpack') -and (-not $hideEmpty -or $View.Shaders.Count -gt 0)) {
        Write-ShaderInventory -Inventory $View -ReferenceWidth $referenceWidth
    }
    $hasReferences = @(Get-ModpackInventoryReferenceItems -View $View | Where-Object { $_.PSObject.Properties['ReferenceNumber'] }).Count -gt 0
    if ($hasReferences) {
        Write-R3Status (Get-MpConsole) info 'Use these numbers with resource, classify, or update. The add command uses search result numbers.'
    }
}

function Write-ModpackHeader {
    param([Parameter(Mandatory)]$Project, [Parameter(Mandatory)]$Inventory)

    Write-R3Banner (Get-MpConsole) "$($Project.DisplayName) $($Project.DisplayVersion)"
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
        Write-R3Line (Get-MpConsole) @(@{Text="$('{0,-11}' -f $key)";Role='secondary'}, @{Text=" "}) -NoNewline
        Write-R3Line (Get-MpConsole) @(@{Text="$($rows[$key])";Role='value'})
    }
}

function Write-ModpackList {
    param([Parameter(Mandatory)][array]$Projects, [Parameter(Mandatory)][string]$Root)
    Write-R3Banner (Get-MpConsole) 'REGISTERED MODPACKS'
    Write-R3Line (Get-MpConsole) @(@{Text="Root: $Root";Role='secondary'})
    Write-R3Line (Get-MpConsole) @(@{Text=''})
    $rows = @(foreach ($project in $Projects) { ,@($project.Id, $project.DisplayName, $project.MinecraftVersion, $project.Loader) })
    Write-R3Table (Get-MpConsole) -Headers @('ID','NAME','MC','LOADER') -Rows $rows
    Write-R3Line (Get-MpConsole) @(@{Text="$($Projects.Count) project(s)";Role='secondary'})
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
    Write-R3Banner (Get-MpConsole) 'BUILD COMPLETE'
    Write-R3Status (Get-MpConsole) success 'Status: successful'
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
        Write-R3KeyValue (Get-MpConsole) -Key $row.Key -Value $row.Value -Width 20
    }
}

function Write-MpDiffItems {
    param([Parameter(Mandatory)][AllowEmptyCollection()][array]$Items, [Parameter(Mandatory)][ValidateSet('Added', 'Changed', 'Removed')][string]$Status)
    if ($Items.Count -eq 0) { return }
    $settings = switch ($Status) {
        'Added'   { @{ Symbol = '+'; Role = 'success' } }
        'Changed' { @{ Symbol = '~'; Role = 'process' } }
        'Removed' { @{ Symbol = '-'; Role = 'error' } }
    }
    Write-R3Section (Get-MpConsole) ("DIFF · " + $Status.ToUpperInvariant()) $Items.Count
    foreach ($item in $Items) {
        Write-R3Line (Get-MpConsole) @(@{Text="  "}, @{Text="$($settings.Symbol)";Role=$settings.Role}, @{Text=" "}) -NoNewline
        Write-R3Line (Get-MpConsole) @(@{Text="$('{0,-10}' -f "[$($item.Kind)]")";Role='accent'}, @{Text=" "}) -NoNewline
        Write-R3Line (Get-MpConsole) @(@{Text="$($item.Path)";Role='value'})
    }
}

function Write-ModpackDiff {
    param([Parameter(Mandatory)]$Diff)
    Write-R3Banner (Get-MpConsole) "DIFF · $($Diff.Project.DisplayName)"
    Write-R3KeyValue (Get-MpConsole) 'Baseline' ([System.IO.Path]::GetFileName($Diff.BaselinePath))
    Write-R3KeyValue (Get-MpConsole) 'Built' $Diff.BaselineTime
    if ($Diff.Total -eq 0) {
        Write-R3Line (Get-MpConsole) @(@{Text=''})
        Write-R3Status (Get-MpConsole) success 'No differences from the latest build.'
        return
    }
    Write-MpDiffItems -Items $Diff.Added -Status Added
    Write-MpDiffItems -Items $Diff.Changed -Status Changed
    Write-MpDiffItems -Items $Diff.Removed -Status Removed
    Write-R3Line (Get-MpConsole) @(@{Text=''})
    Write-R3KeyValue (Get-MpConsole) 'Differences' $Diff.Total
}

function Write-ModUpdateSummary {
    param([Parameter(Mandatory)]$Update)

    Write-R3Banner (Get-MpConsole) "UPDATE · $($Update.Project.DisplayName)"
    if ($Update.PSObject.Properties['Preflight']) {
        Write-R3KeyValue (Get-MpConsole) 'Dependency check' "$($Update.Preflight.Checked) version(s) inspected"
        foreach ($warning in @($Update.Preflight.Warnings)) { Write-R3Status (Get-MpConsole) warning $warning }
        if (@($Update.Preflight.Warnings).Count) { Write-R3Line (Get-MpConsole) @(@{Text=''}) }
    }
    foreach ($item in $Update.Items) {
        $status = if ($item.Changed) { 'UPDATED' } else { 'CURRENT' }
        $role = if ($item.Changed) { 'success' } else { 'secondary' }
        Write-R3Line (Get-MpConsole) @(@{Text="  "}, @{Text="$('{0,-9}' -f $status)";Role=$role}, @{Text=" "}) -NoNewline
        Write-R3Line (Get-MpConsole) @(@{Text="$('{0,-14}' -f "[$($item.Kind)]")";Role='accent'}, @{Text=" "}) -NoNewline
        Write-R3Line (Get-MpConsole) @(@{Text="$($item.Name)";Role='value'}) -NoNewline
        if ($item.PreviousFile -ne $item.Filename) {
            Write-R3Line (Get-MpConsole) @(@{Text=" "}, @{Text="$($item.PreviousFile) -> $($item.Filename)";Role='secondary'})
        }
        else { Write-R3Line (Get-MpConsole) @(@{Text=''}) }
    }
    Write-R3Line (Get-MpConsole) @(@{Text=''})
    Write-R3KeyValue (Get-MpConsole) 'Checked' $Update.Items.Count
    Write-R3KeyValue (Get-MpConsole) 'Updated' @($Update.Items | Where-Object Changed).Count
    Write-R3Status (Get-MpConsole) info 'No build was generated. Run modpack diff, then modpack build when ready.'
}

function Format-MpCompactNumber {
    param([long]$Value)
    if ($Value -ge 1000000) { return ('{0:N1}M' -f ($Value / 1000000)) }
    if ($Value -ge 1000) { return ('{0:N1}K' -f ($Value / 1000)) }
    return [string]$Value
}

function Write-ModrinthSearchResults {
    param(
        [Parameter(Mandatory)]$Search,
        [Parameter(Mandatory)]$Project
    )

    Write-R3Banner (Get-MpConsole) 'SEARCH · MODRINTH'
    Write-R3KeyValue (Get-MpConsole) 'Query' $Search.Query
    Write-R3KeyValue (Get-MpConsole) 'Project' "$($Project.Id) · Minecraft $($Project.MinecraftVersion) · $($Project.Loader)"
    Write-R3KeyValue (Get-MpConsole) 'Type' $Search.Type
    Write-R3KeyValue (Get-MpConsole) 'Found' "$(@($Search.Results).Count) shown · $($Search.TotalHits) total"
    if (@($Search.Results).Count -eq 0) {
        Write-R3Line (Get-MpConsole) @(@{Text=''})
        Write-R3Status (Get-MpConsole) info 'No compatible results were found.'
        return
    }

    Write-R3Section (Get-MpConsole) 'RESULTS' @($Search.Results).Count
    $referenceWidth = Get-MpReferenceWidth -Items @($Search.Results) -PropertyName Index
    foreach ($item in $Search.Results) {
        $typeLabel = switch ($item.Type) {
            'mod'          { 'MOD' }
            'resourcepack' { 'RESOURCE' }
            'shaderpack'   { 'SHADER' }
            default        { ([string]$item.Type).ToUpperInvariant() }
        }
        $label = Format-MpReferenceLabel -Reference $item.Index -Width $referenceWidth
        Write-R3Line (Get-MpConsole) @(@{Text="  "}, @{Text="$label";Role='process'}, @{Text=" "}) -NoNewline
        Write-R3Line (Get-MpConsole) @(@{Text="$('{0,-10}' -f "[$typeLabel]")";Role='accent'}, @{Text=" "}) -NoNewline
        Write-R3Line (Get-MpConsole) @(@{Text="$($item.Title)";Role='value'})
        Write-R3Line (Get-MpConsole) @(@{Text="      "}, @{Text="ID";Role='secondary'}, @{Text=" "}, @{Text="$($item.ProjectId)";Role='value'}, @{Text="  "}) -NoNewline
        Write-R3Line (Get-MpConsole) @(@{Text="slug";Role='secondary'}, @{Text=" $($item.Slug)  "}) -NoNewline
        Write-R3Line (Get-MpConsole) @(@{Text="by";Role='secondary'}, @{Text=" $($item.Author)  "}) -NoNewline
        Write-R3Line (Get-MpConsole) @(@{Text="downloads";Role='secondary'}, @{Text=" $(Format-MpCompactNumber $item.Downloads)"})
        if ($item.Description) { Write-R3Line (Get-MpConsole) @(@{Text="      "}, @{Text="$($item.Description)";Role='secondary'}) }
    }
    Write-R3Line (Get-MpConsole) @(@{Text=''})
    Write-R3Status (Get-MpConsole) info 'Install a result with modpack add <number>. You can also use its ID or slug.'
}

function Write-ModrinthVersionResults {
    param([Parameter(Mandatory)]$View, [Parameter(Mandatory)]$Project)
    Write-R3Banner (Get-MpConsole) "VERSIONS · $($View.ItemName)"
    Write-R3KeyValue (Get-MpConsole) 'Project' "$($Project.Id) · Minecraft $($Project.MinecraftVersion) · $($Project.Loader)"
    Write-R3KeyValue (Get-MpConsole) 'Type' $View.ItemKind
    Write-R3KeyValue (Get-MpConsole) 'Compatible' @($View.Versions).Count
    if (@($View.Versions).Count -eq 0) { Write-R3Line (Get-MpConsole) @(@{Text=''}); Write-R3Status (Get-MpConsole) info 'No compatible versions were found.'; return }
    Write-R3Section (Get-MpConsole) 'AVAILABLE VERSIONS' @($View.Versions).Count
    $referenceWidth = Get-MpReferenceWidth -Items @($View.Versions) -PropertyName Index
    foreach ($version in $View.Versions) {
        $label = Format-MpReferenceLabel -Reference $version.Index -Width $referenceWidth
        $marker = if ($version.Installed) { 'INSTALLED' } elseif ([int]$version.Index -eq 1) { 'LATEST' } else { '' }
        Write-R3Line (Get-MpConsole) @(@{Text="  "}, @{Text="$label";Role='process'}, @{Text=" "}) -NoNewline
        Write-R3Line (Get-MpConsole) @(@{Text="$($version.VersionNumber)";Role='value';Bold=$true}, @{Text=" "}) -NoNewline
        if ($marker) { Write-R3Line (Get-MpConsole) @(@{Text="$marker";Role='success'}, @{Text=" "}) -NoNewline }
        Write-R3Line (Get-MpConsole) @(@{Text="$($version.VersionType) · $($version.Id)";Role='secondary'})
        $details = @($version.Filename, $version.Published) | Where-Object { $_ }
        if ($details.Count) { Write-R3Line (Get-MpConsole) @(@{Text="      "}, @{Text="$($details -join ' · ')";Role='secondary'}) }
    }
    Write-R3Line (Get-MpConsole) @(@{Text=''})
    Write-R3Status (Get-MpConsole) info 'Select one with modpack update <content> --to <number>. Exact version IDs also work.'
}

function Write-ModpackCategoryList {
    param([Parameter(Mandatory)]$View)
    Write-R3Banner (Get-MpConsole) "CATEGORIES · $($View.Project.Id)"
    Write-R3Section (Get-MpConsole) 'CLASSIFICATIONS' @($View.Categories).Count
    $referenceWidth = Get-MpReferenceWidth -Items @($View.Categories)
    foreach ($category in $View.Categories) {
        $mods = if ($category.ModCount -eq 1) { '1 mod' } else { "$($category.ModCount) mods" }
        $order = if ($category.PSObject.Properties['IsUnclassified'] -and $category.IsUnclassified) { 'not assigned' } else { "order $($category.Order)" }
        $label = Format-MpReferenceLabel -Reference $category.ReferenceNumber -Width $referenceWidth
        Write-R3Line (Get-MpConsole) @(@{Text="  "}, @{Text="$label";Role='accent'}, @{Text=" "}) -NoNewline
        Write-R3Line (Get-MpConsole) @(@{Text="$($category.Name)";Role='value';Bold=$true}, @{Text=" "}) -NoNewline
        Write-R3Line (Get-MpConsole) @(@{Text="$($category.Id) · $mods · $order";Role='secondary'})
    }
    Write-R3Line (Get-MpConsole) @(@{Text=''})
    Write-R3Status (Get-MpConsole) info 'Use a classification ID or number with classify set. Only defined categories can be removed.'
}
