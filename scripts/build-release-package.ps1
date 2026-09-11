[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputPath,
    [string]$ExpectedVersion
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $repositoryRoot 'ModpackTools.psd1'
$manifest = Import-PowerShellDataFile -LiteralPath $manifestPath
$version = [string]$manifest.ModuleVersion

if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "ModuleVersion '$version' is not a stable semantic version."
}
if ($ExpectedVersion -and [version]$version -ne [version]$ExpectedVersion) {
    throw "Manifest version $version does not match expected version $ExpectedVersion."
}

$output = [IO.Path]::GetFullPath($OutputPath)
$stage = Join-Path ([IO.Path]::GetTempPath()) ('ModpackTools-release-' + [guid]::NewGuid().ToString('N'))
$verify = Join-Path ([IO.Path]::GetTempPath()) ('ModpackTools-release-verify-' + [guid]::NewGuid().ToString('N'))

$distribution = @(
    'docs',
    'Private',
    'Public',
    'Nushell',
    'Install-ModpackTools.ps1',
    'install-modpack-tools.nu',
    'ModpackTools.psd1',
    'ModpackTools.psm1',
    'README.md',
    'LICENSE',
    'theme.toml',
    'dependencies.psd1'
)

try {
    New-Item -ItemType Directory -Path $stage | Out-Null
    foreach ($item in $distribution) {
        $source = Join-Path $repositoryRoot $item
        if (-not (Test-Path -LiteralPath $source)) {
            throw "Required distribution item is missing: $item"
        }
        Copy-Item -LiteralPath $source -Destination $stage -Recurse -Force
    }

    $outputDirectory = Split-Path -Parent $output
    if ($outputDirectory) { New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null }
    Remove-Item -LiteralPath $output -Force -ErrorAction SilentlyContinue
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $output -CompressionLevel Optimal

    Expand-Archive -LiteralPath $output -DestinationPath $verify -Force
    $packagedManifest = Test-ModuleManifest -Path (Join-Path $verify 'ModpackTools.psd1')
    if ([version]$packagedManifest.Version -ne [version]$version) {
        throw "Packaged version $($packagedManifest.Version) does not match $version."
    }

    $installers = @(Get-ChildItem -LiteralPath $verify -Filter 'Install-ModpackTools.ps1' -Recurse -File)
    if ($installers.Count -ne 1) {
        throw 'Release archive must contain exactly one Install-ModpackTools.ps1.'
    }

    $nuInstaller = Join-Path $verify 'install-modpack-tools.nu'
    if (-not (Test-Path -LiteralPath $nuInstaller -PathType Leaf)) {
        throw 'Release archive is missing install-modpack-tools.nu.'
    }

    Write-Output $output
}
finally {
    Remove-Item -LiteralPath $stage,$verify -Recurse -Force -ErrorAction SilentlyContinue
}
