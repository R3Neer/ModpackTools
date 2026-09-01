function Get-ModpackToolsConfigDirectory {
    if ($script:ConfigHomeOverride) {
        return [System.IO.Path]::GetFullPath($script:ConfigHomeOverride)
    }

    $localApplicationData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    if ([string]::IsNullOrWhiteSpace($localApplicationData)) {
        throw 'No se puede determinar LOCALAPPDATA para guardar la configuración.'
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
        throw "La configuración '$path' no es un PSD1 válido: $($_.Exception.Message)"
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
        throw "Configuración desconocida '$Name'. En v0.1 solo existe 'root'."
    }

    $resolved = [System.IO.Path]::GetFullPath($Value)
    if (-not (Test-Path -LiteralPath $resolved -PathType Container)) {
        throw "El root de modpacks no existe o no es un directorio: $resolved"
    }

    $config = Get-ModpackToolsConfig
    $config['Root'] = $resolved
    Write-PowerShellDataFileAtomic -Data $config -Path (Get-ModpackToolsConfigPath)
    return $resolved
}

function Get-ModpackRoot {
    $config = Get-ModpackToolsConfig
    if (-not $config.ContainsKey('Root') -or [string]::IsNullOrWhiteSpace([string]$config.Root)) {
        throw "ModpackTools no está configurado. Ejecuta: modpack config set root '<directorio>'."
    }

    $root = [System.IO.Path]::GetFullPath([string]$config.Root)
    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
        throw "El root configurado no existe: $root"
    }
    return $root
}
