[CmdletBinding()]
param(
    [switch]$Force,
    [switch]$NonInteractive,
    [switch]$SkipDoctor
)

$ErrorActionPreference = 'Stop'

function Read-InstallerConfirmation {
    param([string]$Prompt, [bool]$Default = $true)
    $suffix = if ($Default) { '[Y/n]' } else { '[y/N]' }
    $answer = Read-Host "$Prompt $suffix"
    if ([string]::IsNullOrWhiteSpace($answer)) { return $Default }
    return $answer.Trim().ToLowerInvariant() -in @('y', 'yes')
}

if ($PSVersionTable.PSVersion.Major -lt 7) {
    Write-Host ''
    Write-Host 'MODPACKTOOLS SETUP'
    Write-Host "PowerShell $($PSVersionTable.PSVersion) is running. ModpackTools requires PowerShell 7 or newer."
    if ($NonInteractive) { throw 'PowerShell 7 is required. Install it and run the installer again.' }
    $winget = Get-Command winget.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $winget) {
        throw 'PowerShell 7 could not be installed automatically because WinGet is unavailable. See https://learn.microsoft.com/powershell/scripting/install/install-powershell-on-windows'
    }
    if (-not (Read-InstallerConfirmation -Prompt 'Install the latest stable PowerShell 7 with WinGet?' -Default $true)) {
        throw 'PowerShell 7 is required. Install it and run the installer again.'
    }
    & $winget.Source install --id Microsoft.PowerShell --source winget --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) { throw "WinGet could not install PowerShell 7 (exit code $LASTEXITCODE)." }

    $pwshCommand = Get-Command pwsh.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    $pwshPath = if ($pwshCommand) { $pwshCommand.Source } else { $null }
    if (-not $pwshPath) {
        foreach ($candidate in @(
            (Join-Path $env:ProgramFiles 'PowerShell\7\pwsh.exe'),
            (Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\pwsh.exe')
        )) {
            if (Test-Path -LiteralPath $candidate -PathType Leaf) { $pwshPath = $candidate; break }
        }
    }
    if (-not $pwshPath) { throw 'PowerShell 7 was installed, but pwsh.exe is not visible yet. Open PowerShell 7 and run this installer again.' }

    $forward = @('-NoProfile', '-File', $PSCommandPath)
    if ($Force) { $forward += '-Force' }
    if ($NonInteractive) { $forward += '-NonInteractive' }
    if ($SkipDoctor) { $forward += '-SkipDoctor' }
    & $pwshPath @forward
    exit $LASTEXITCODE
}

$manifest = Import-PowerShellDataFile -LiteralPath (Join-Path $PSScriptRoot 'ModpackTools.psd1')
$version = [string]$manifest.ModuleVersion
$modulePaths = @($env:PSModulePath -split ';' | Where-Object { $_ } | ForEach-Object { [System.IO.Path]::GetFullPath($_) })
$preferredRoot = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'PowerShell/Modules'
$userModuleRoot = $modulePaths | Where-Object { $_ -eq $preferredRoot } | Select-Object -First 1
if (-not $userModuleRoot) {
    $userModuleRoot = $modulePaths | Where-Object {
        $_.StartsWith([System.IO.Path]::GetFullPath($HOME), [System.StringComparison]::OrdinalIgnoreCase)
    } | Select-Object -First 1
}
if (-not $userModuleRoot) { throw 'No user module directory was found in PSModulePath.' }

$moduleBase = Join-Path $userModuleRoot 'ModpackTools'
$destination = $moduleBase
if ((Test-Path -LiteralPath $destination) -and -not $Force) {
    throw "ModpackTools is already installed at '$destination'. Use -Force to update it."
}
$temporary = Join-Path $userModuleRoot ('.ModpackTools.install-' + [guid]::NewGuid().ToString('N'))
$backup = Join-Path $userModuleRoot ('.ModpackTools.backup-' + [guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($temporary) | Out-Null
try {
    foreach ($name in @('docs', 'Private', 'Public', 'ModpackTools.psd1', 'ModpackTools.psm1', 'README.md', 'LICENSE', 'theme.toml', 'dependencies.psd1')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $temporary -Recurse -Force
    }
    if (Test-Path -LiteralPath $destination) { Move-Item -LiteralPath $destination -Destination $backup }
    Move-Item -LiteralPath $temporary -Destination $destination
    if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Recurse -Force }
}
catch {
    if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
    if ((Test-Path -LiteralPath $backup) -and -not (Test-Path -LiteralPath $destination)) {
        Move-Item -LiteralPath $backup -Destination $destination
    }
    throw
}
Write-Host "ModpackTools $version installed at $destination"
Import-Module (Join-Path $destination 'ModpackTools.psd1') -Force
if (-not $SkipDoctor) {
    if ($NonInteractive) { modpack doctor }
    else { modpack doctor --fix }
}
Write-Host ''
Write-Host 'ModpackTools is ready. Open a new session or run: Import-Module ModpackTools -Force'
