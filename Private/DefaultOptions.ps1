function Get-TomlArraySpan {
    param(
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][string]$Key
    )

    $match = [regex]::Match($Text, ('(?m)^\s*{0}\s*=\s*\[' -f [regex]::Escape($Key)))
    if (-not $match.Success) { return $null }

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

    if ($closeIndex -lt 0) { Throw-MpError -Message "TOML array '$Key' is not closed" -Hint 'repair the Default Options configuration' -ErrorId 'ResourcePack.UnclosedTomlArray' -Category InvalidData -TargetObject $Key }
    return [pscustomobject]@{ OpenIndex = $openIndex; CloseIndex = $closeIndex }
}

function Get-TomlArrayStrings {
    param(
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][string]$Key
    )

    $span = Get-TomlArraySpan -Text $Text -Key $Key
    if (-not $span) { return @() }
    $content = $Text.Substring($span.OpenIndex + 1, $span.CloseIndex - $span.OpenIndex - 1)
    return @(
        [regex]::Matches($content, '"(?<value>(?:\\.|[^"\\])*)"') |
            ForEach-Object { ConvertFrom-TomlBasicString $_.Groups['value'].Value }
    )
}

function ConvertTo-TomlBasicString {
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Value)
    return $Value.Replace('\', '\\').Replace('"', '\"').Replace("`r", '\r').Replace("`n", '\n').Replace("`t", '\t')
}

function Set-TomlArrayStrings {
    param(
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][string]$Key,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Values
    )

    $span = Get-TomlArraySpan -Text $Text -Key $Key
    if (-not $span) { Throw-MpError -Message "TOML array '$Key' does not exist" -Hint 'repair the Default Options configuration' -ErrorId 'ResourcePack.MissingTomlArray' -Category InvalidData -TargetObject $Key }
    $newline = if ($Text.Contains("`r`n")) { "`r`n" } else { "`n" }
    $replacement = if ($Values.Count -eq 0) { '[]' } else {
        '[' + $newline + (($Values | ForEach-Object { '  "' + (ConvertTo-TomlBasicString $_) + '",' }) -join $newline) + $newline + ']'
    }
    return $Text.Substring(0, $span.OpenIndex) + $replacement + $Text.Substring($span.CloseIndex + 1)
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

function Get-DefaultResourcePackOrder {
    param([Parameter(Mandatory)]$Project)

    $path = Join-Path $Project.Root 'config/defaultoptions-common.toml'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return @() }
    $text = Get-Content -Raw -LiteralPath $path -Encoding UTF8
    $ids = @(Get-TomlArrayStrings -Text $text -Key 'defaultResourcePacks')
    [array]::Reverse($ids)
    return $ids
}

function Set-DefaultResourcePackOrder {
    param(
        [Parameter(Mandatory)]$Project,
        [Parameter(Mandatory)][AllowEmptyCollection()][string[]]$Ids
    )

    $path = Join-Path $Project.Root 'config/defaultoptions-common.toml'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        Throw-MpError -Message "Default Options configuration '$path' does not exist, so resource pack order cannot be changed" -Hint 'install or restore config/defaultoptions-common.toml' -ErrorId 'ResourcePack.DefaultOptionsNotFound' -Category ObjectNotFound -TargetObject $path
    }
    $storedIds = @($Ids)
    [array]::Reverse($storedIds)
    $text = Get-Content -Raw -LiteralPath $path -Encoding UTF8
    $updated = Set-TomlArrayStrings -Text $text -Key 'defaultResourcePacks' -Values $storedIds
    Write-Utf8TextFileAtomic -Path $path -Text $updated

    $verified = @(Get-DefaultResourcePackOrder -Project $Project)
    if (($verified -join "`0") -cne (@($Ids) -join "`0")) {
        Throw-MpError -Message 'Default Options verification did not match the requested resource pack order' -Hint 'inspect config/defaultoptions-common.toml before retrying' -ErrorId 'ResourcePack.VerificationFailed' -Category InvalidResult
    }
}
