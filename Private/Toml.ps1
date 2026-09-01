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
        throw "pack.toml does not exist: $Path"
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

    [pscustomobject]@{
        Name             = Get-TomlString -Text $text -Key 'name'
        Author           = Get-TomlString -Text $text -Key 'author'
        Version          = Get-TomlString -Text $text -Key 'version'
        MinecraftVersion = Get-TomlString -Text $text -Section 'versions' -Key 'minecraft'
        Loader           = $loader
        LoaderVersion    = $loaderVersion
    }
}
