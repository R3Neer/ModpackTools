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
        throw "Metadata file '$path' is invalid: $($_.Exception.Message)"
    }

    foreach ($section in @('Categories', 'Mods', 'ResourcePacks')) {
        if (-not $metadata.ContainsKey($section)) { $metadata[$section] = @{} }
        if ($metadata[$section] -isnot [System.Collections.IDictionary]) {
            throw "Section '$section' in '$path' must be a table."
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
    if (-not $metadata.Categories.ContainsKey($Category)) {
        throw "Category '$Category' does not exist in the metadata for '$($Project.Id)'."
    }
    if (-not $metadata.Mods.ContainsKey($ModId)) { $metadata.Mods[$ModId] = @{} }
    $metadata.Mods[$ModId]['Category'] = $Category
    Write-PowerShellDataFileAtomic -Data $metadata -Path (Join-Path $Project.Root '.modpack/metadata.psd1')
}
