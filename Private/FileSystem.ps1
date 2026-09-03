function Test-MpFileSystemLink {
    param([Parameter(Mandatory)]$Item)
    if (-not ($Item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { return $false }
    # Cloud placeholders also carry ReparsePoint. PowerShell exposes LinkType
    # and Target for symbolic links and junctions, but not for OneDrive entries.
    $linkType = $Item.PSObject.Properties['LinkType']
    $target = $Item.PSObject.Properties['Target']
    return [bool](($linkType -and $linkType.Value) -or ($target -and $target.Value))
}
