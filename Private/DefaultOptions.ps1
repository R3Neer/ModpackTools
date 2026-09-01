function Get-TomlArrayStrings {
    param(
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][string]$Key
    )

    $match = [regex]::Match($Text, ('(?m)^\s*{0}\s*=\s*\[' -f [regex]::Escape($Key)))
    if (-not $match.Success) { return @() }

    $openIndex = $match.Index + $match.Length - 1
    $depth = 0
    $inString = $false
    $escaped = $false
    $closeIndex = -1

    for ($i = $openIndex; $i -lt $Text.Length; $i++) {
        $char = $Text[$i]
        if ($escaped) { $escaped = $false; continue }
        if ($inString -and $char -eq '\') { $escaped = $true; continue }
        if ($char -eq '"') { $inString = -not $inString; continue }
        if (-not $inString) {
            if ($char -eq '[') { $depth++ }
            elseif ($char -eq ']') {
                $depth--
                if ($depth -eq 0) { $closeIndex = $i; break }
            }
        }
    }

    if ($closeIndex -lt 0) { throw "El array TOML '$Key' no tiene cierre." }
    $content = $Text.Substring($openIndex + 1, $closeIndex - $openIndex - 1)
    return @(
        [regex]::Matches($content, '"(?<value>(?:\\.|[^"\\])*)"') |
            ForEach-Object { ConvertFrom-TomlBasicString $_.Groups['value'].Value }
    )
}

function Get-DefaultResourcePackOrder {
    param([Parameter(Mandatory)]$Project)

    $path = Join-Path $Project.Root 'config/defaultoptions-common.toml'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return @() }
    $text = Get-Content -Raw -LiteralPath $path -Encoding UTF8
    $ids = @(Get-TomlArrayStrings -Text $text -Key 'defaultResourcePacks')
    [array]::Reverse($ids)
    return $ids
}
