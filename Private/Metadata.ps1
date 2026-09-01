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
        Throw-MpError -Message "Category '$Category' is not defined for project '$($Project.Id)'" -Hint 'choose a category shown by modpack inventory --type mod' -ErrorId 'Metadata.UnknownCategory' -Category InvalidArgument -TargetObject $Category
    }
    if (-not $metadata.Mods.ContainsKey($ModId)) { $metadata.Mods[$ModId] = @{} }
    $metadata.Mods[$ModId]['Category'] = [string]$categoryId
    Write-PowerShellDataFileAtomic -Data $metadata -Path (Join-Path $Project.Root '.modpack/metadata.psd1')
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
        $categoryId = $metadata.Categories.Keys | Where-Object { ([string]$_).Equals($Category, [System.StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
        if (-not $categoryId) {
            Throw-MpError -Message "Category '$Category' is not defined for project '$($Project.Id)'" -Hint 'choose a category shown by modpack inventory --type mod' -ErrorId 'Metadata.UnknownCategory' -Category InvalidArgument -TargetObject $Category
        }
        if (-not $metadata.Mods.ContainsKey($item.Id)) { $metadata.Mods[$item.Id] = @{} }
        $metadata.Mods[$item.Id]['Category'] = [string]$categoryId
        $normalizedCategory = [string]$categoryId
    }
    Write-PowerShellDataFileAtomic -Data $metadata -Path (Join-Path $Project.Root '.modpack/metadata.psd1')
    $updated = (Get-ModpackInventory -Project $Project).Mods | Where-Object Id -eq $item.Id | Select-Object -First 1
    return [pscustomobject]@{ Item = $updated; PreviousCategory = $previous; Category = $normalizedCategory }
}
