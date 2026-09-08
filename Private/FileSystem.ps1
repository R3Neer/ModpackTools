function Test-MpFileSystemLink {
    param([Parameter(Mandatory)]$Item)
    if (-not ($Item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { return $false }
    # Cloud placeholders also carry ReparsePoint. PowerShell exposes LinkType
    # and Target for symbolic links and junctions, but not for OneDrive entries.
    $linkType = $Item.PSObject.Properties['LinkType']
    $target = $Item.PSObject.Properties['Target']
    return [bool](($linkType -and $linkType.Value) -or ($target -and $target.Value))
}

function Write-Utf8TextFileAtomic {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][AllowEmptyString()][string]$Text)
    $directory = Split-Path -Parent $Path
    $temporary = Join-Path $directory ('.' + [System.IO.Path]::GetFileName($Path) + '.' + [guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [System.IO.File]::WriteAllText($temporary, $Text, [System.Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $temporary -Destination $Path -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
}
