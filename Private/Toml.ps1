function ConvertFrom-TomlBasicString {
    param([Parameter(Mandatory)][string]$Value)

    # This is sufficient for the basic strings emitted by Packwiz.
    return [regex]::Unescape($Value)
}

function Get-TomlSectionText {
    param(
        [Parameter(Mandatory)][string]$Text,
        [AllowEmptyString()][string]$Section = ''
    )

    if ([string]::IsNullOrEmpty($Section)) {
        $match = [regex]::Match($Text, '\A(?<body>.*?)(?=^\s*\[|\z)', 'Multiline,Singleline')
        return $match.Groups['body'].Value
    }

    $escaped = [regex]::Escape($Section)
    $pattern = "(?ms)^\s*\[$escaped\]\s*\r?\n(?<body>.*?)(?=^\s*\[|\z)"
    $match = [regex]::Match($Text, $pattern)
    if (-not $match.Success) { return $null }
    return $match.Groups['body'].Value
}

function Get-TomlString {
    param(
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][string]$Key,
        [AllowEmptyString()][string]$Section = ''
    )

    $body = Get-TomlSectionText -Text $Text -Section $Section
    if ($null -eq $body) { return $null }
    $pattern = '(?m)^\s*{0}\s*=\s*"(?<value>(?:\\.|[^"\\])*)"\s*(?:#.*)?$' -f [regex]::Escape($Key)
    $match = [regex]::Match($body, $pattern)
    if (-not $match.Success) { return $null }
    return (ConvertFrom-TomlBasicString $match.Groups['value'].Value)
}

function Get-PackTomlData {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Throw-MpError -Message "Packwiz manifest '$Path' does not exist" -Hint 'restore pack.toml or recreate the project' -ErrorId 'Project.ManifestNotFound' -Category ObjectNotFound -TargetObject $Path
    }

    $text = Get-Content -Raw -LiteralPath $Path -Encoding UTF8
    $loader = $null
    $loaderVersion = $null
    foreach ($candidate in @('fabric', 'quilt', 'neoforge', 'forge')) {
        $value = Get-TomlString -Text $text -Section 'versions' -Key $candidate
        if ($null -ne $value) {
            $loader = $candidate
            $loaderVersion = $value
            break
        }
    }

    $indexFile = Get-TomlString -Text $text -Section 'index' -Key 'file'
    if ([string]::IsNullOrWhiteSpace($indexFile)) { $indexFile = 'index.toml' }

    [pscustomobject]@{
        Name             = Get-TomlString -Text $text -Key 'name'
        Author           = Get-TomlString -Text $text -Key 'author'
        Version          = Get-TomlString -Text $text -Key 'version'
        IndexFile        = $indexFile
        MinecraftVersion = Get-TomlString -Text $text -Section 'versions' -Key 'minecraft'
        Loader           = $loader
        LoaderVersion    = $loaderVersion
    }
}

function Resolve-PackwizIndexPath {
    param(
        [Parameter(Mandatory)][string]$ProjectRoot,
        [Parameter(Mandatory)][string]$IndexFile
    )

    if ([System.IO.Path]::IsPathRooted($IndexFile)) {
        Throw-MpError -Message "Packwiz index path '$IndexFile' must be relative to the project" -Hint 'repair the [index] file value in pack.toml' -ErrorId 'Project.InvalidIndexPath' -Category InvalidData -TargetObject $IndexFile
    }
    $root = [System.IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\', '/')
    $resolved = [System.IO.Path]::GetFullPath((Join-Path $root $IndexFile))
    $prefix = $root + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        Throw-MpError -Message "Packwiz index path '$IndexFile' escapes project root '$root'" -Hint 'repair the [index] file value in pack.toml' -ErrorId 'Project.InvalidIndexPath' -Category InvalidData -TargetObject $IndexFile
    }
    return $resolved
}
