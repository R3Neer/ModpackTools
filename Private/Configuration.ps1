function Get-ModpackToolsConfigDirectory {
    if ($script:ConfigHomeOverride) {
        return [System.IO.Path]::GetFullPath($script:ConfigHomeOverride)
    }

    $localApplicationData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    if ([string]::IsNullOrWhiteSpace($localApplicationData)) {
        throw 'LOCALAPPDATA could not be determined for storing the configuration.'
    }

    return (Join-Path $localApplicationData 'ModpackTools')
}

function Get-ModpackToolsConfigPath {
    Join-Path (Get-ModpackToolsConfigDirectory) 'config.psd1'
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
        throw "Configuration file '$path' is not a valid PSD1 file: $($_.Exception.Message)"
    }

    if ($null -eq $data) { return @{} }
    return $data
}

function Set-ModpackToolsConfigValue {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Value
    )

    if ($Name -ne 'root') {
        throw "Unknown setting '$Name'. Only 'root' is currently supported."
    }

    $resolved = [System.IO.Path]::GetFullPath($Value)
    if (-not (Test-Path -LiteralPath $resolved -PathType Container)) {
        throw "The modpack root does not exist or is not a directory: $resolved"
    }

    $config = Get-ModpackToolsConfig
    $config['Root'] = $resolved
    Write-PowerShellDataFileAtomic -Data $config -Path (Get-ModpackToolsConfigPath)
    return $resolved
}

function Get-ModpackRoot {
    $config = Get-ModpackToolsConfig
    if (-not $config.ContainsKey('Root') -or [string]::IsNullOrWhiteSpace([string]$config.Root)) {
        throw "ModpackTools is not configured. Run: modpack config set root '<directory>'."
    }

    $root = [System.IO.Path]::GetFullPath([string]$config.Root)
    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
        throw "The configured root does not exist: $root"
    }
    return $root
}
