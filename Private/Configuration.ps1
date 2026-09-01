function Get-ModpackToolsConfigDirectory {
    if ($script:ConfigHomeOverride) {
        return [System.IO.Path]::GetFullPath($script:ConfigHomeOverride)
    }

    $localApplicationData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    if ([string]::IsNullOrWhiteSpace($localApplicationData)) {
        Throw-MpError -Message "Environment variable 'LOCALAPPDATA' is unavailable, so the configuration location cannot be determined" -Hint 'set LOCALAPPDATA and start a new PowerShell session' -ErrorId 'Configuration.LocalAppDataUnavailable' -Category ResourceUnavailable
    }

    return (Join-Path $localApplicationData 'ModpackTools')
}

function Get-ModpackToolsConfigPath {
    Join-Path (Get-ModpackToolsConfigDirectory) 'config.psd1'
}

function Test-MpCacheTimestamp {
    param(
        [AllowNull()]$CreatedUtc,
        [ValidateRange(1, 8760)][int]$MaximumAgeHours = 24
    )

    $created = [datetimeoffset]::MinValue
    $validDate = if ($CreatedUtc -is [datetime]) {
        $created = [datetimeoffset]$CreatedUtc
        $true
    }
    else {
        [datetimeoffset]::TryParse(
            [string]$CreatedUtc,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::RoundtripKind,
            [ref]$created
        )
    }
    return $validDate -and ([datetimeoffset]::UtcNow - $created.ToUniversalTime()).TotalHours -le $MaximumAgeHours
}

function Get-ModpackToolsConfig {
    $path = Get-ModpackToolsConfigPath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        return @{}
    }

    try {
        $data = Import-PowerShellDataFile -LiteralPath $path
    }
    catch {
        Throw-MpError -Message "Configuration file '$path' is not a valid PSD1 file" -Details $_.Exception.Message -Hint "repair or remove '$path', then run modpack config set root <directory>" -ErrorId 'Configuration.InvalidFile' -Category InvalidData -TargetObject $path
    }

    if ($null -eq $data) { return @{} }
    return $data
}

function Set-ModpackToolsConfigValue {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Value
    )

    $config = Get-ModpackToolsConfig
    switch ($Name.ToLowerInvariant()) {
        'root' {
            $resolved = [System.IO.Path]::GetFullPath($Value)
            if (-not (Test-Path -LiteralPath $resolved -PathType Container)) {
                Throw-MpError -Message "Modpack root '$resolved' does not exist or is not a directory" -Hint 'modpack config set root <existing-directory>' -ErrorId 'Configuration.InvalidRoot' -Category ObjectNotFound -TargetObject $resolved
            }
            $config['Root'] = $resolved
        }
        'packwiz' {
            if ($Value.ToLowerInvariant() -eq 'auto') {
                [void]$config.Remove('PackwizPath')
                $resolved = 'auto'
            }
            else {
                $resolved = [System.IO.Path]::GetFullPath($Value)
                if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
                    Throw-MpError -Message "Packwiz executable '$resolved' does not exist" -Hint 'modpack config set packwiz <existing-executable>' -ErrorId 'Configuration.InvalidPackwizPath' -Category ObjectNotFound -TargetObject $resolved
                }
                $config['PackwizPath'] = $resolved
            }
        }
        default {
            Throw-MpError -Message "Configuration setting '$Name' is not recognized; allowed values: root, packwiz" -Hint 'modpack config --help' -ErrorId 'Configuration.UnknownSetting' -Category InvalidArgument -TargetObject $Name
        }
    }
    Write-PowerShellDataFileAtomic -Data $config -Path (Get-ModpackToolsConfigPath)
    return $resolved
}

function Get-ModpackRoot {
    $config = Get-ModpackToolsConfig
    if (-not $config.ContainsKey('Root') -or [string]::IsNullOrWhiteSpace([string]$config.Root)) {
        Throw-MpError -Message 'No modpack root is configured' -Hint 'modpack config set root <directory>' -ErrorId 'Configuration.MissingRoot' -Category ObjectNotFound
    }

    $root = [System.IO.Path]::GetFullPath([string]$config.Root)
    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
        Throw-MpError -Message "Configured modpack root '$root' does not exist" -Hint 'modpack config set root <existing-directory>' -ErrorId 'Configuration.RootNotFound' -Category ObjectNotFound -TargetObject $root
    }
    return $root
}
