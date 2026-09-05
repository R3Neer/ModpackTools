[CmdletBinding()]
param(
    [switch]$Force,
    [switch]$NonInteractive,
    [switch]$SkipDoctor,
    [string]$InstallPath,
    [string]$ExpectedManifestHash
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
    if ($InstallPath) { $forward += @('-InstallPath', $InstallPath) }
    if ($ExpectedManifestHash) { $forward += @('-ExpectedManifestHash', $ExpectedManifestHash) }
    & $pwshPath @forward
    exit $LASTEXITCODE
}

. (Join-Path $PSScriptRoot 'Private/FileSystem.ps1')
. (Join-Path $PSScriptRoot 'Private/Errors.ps1')
. (Join-Path $PSScriptRoot 'Private/Installation.ps1')
$manifest = Import-PowerShellDataFile -LiteralPath (Join-Path $PSScriptRoot 'ModpackTools.psd1')
$version = [string]$manifest.ModuleVersion
$destination = Resolve-MpInstallDestination $InstallPath
$userModuleRoot = Split-Path -Parent $destination
if ((Test-Path -LiteralPath $destination) -and -not $Force) {
    throw "ModpackTools is already installed at '$destination'. Use -Force to update it."
}
$temporary = Join-Path $userModuleRoot ('.ModpackTools.install-' + [guid]::NewGuid().ToString('N'))
$backup = Join-Path $userModuleRoot ('.ModpackTools.backup-' + [guid]::NewGuid().ToString('N'))
$safeModuleRoot = [IO.Path]::GetFullPath($userModuleRoot).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
$lockPath = Join-Path $userModuleRoot '.ModpackTools.install.lock'
foreach ($target in @($destination,$temporary,$backup,$lockPath)) {
    if (-not [IO.Path]::GetFullPath($target).StartsWith($safeModuleRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'Installer target escapes the module directory.' }
    if (Test-Path -LiteralPath $target) {
        $item = Get-Item -LiteralPath $target -Force
        if (Test-MpFileSystemLink $item) {
            throw "Installer target is a linked path: $target"
        }
    }
}
[void][IO.Directory]::CreateDirectory($userModuleRoot)
$installLock = [IO.File]::Open($lockPath, 'OpenOrCreate', 'ReadWrite', 'None')
$replacementPlaced = $false
try {
    if ($ExpectedManifestHash -and (Get-FileHash -LiteralPath (Join-Path $destination 'ModpackTools.psd1')).Hash -ne $ExpectedManifestHash) { throw 'Installed version changed while the update was being prepared.' }
    [System.IO.Directory]::CreateDirectory($temporary) | Out-Null
    foreach ($name in @('docs', 'Private', 'Public', 'ModpackTools.psd1', 'ModpackTools.psm1', 'README.md', 'LICENSE', 'theme.toml', 'dependencies.psd1')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $temporary -Recurse -Force
    }
    . (Join-Path $temporary 'Private/Errors.ps1')
    . (Join-Path $temporary 'Private/VerifyR3CLI.ps1')
    [void](Test-MpR3Package -ModuleRoot $temporary)
    $stagedModule = Import-Module (Join-Path $temporary 'ModpackTools.psd1') -Force -PassThru
    & $stagedModule {
        param($InstalledTheme)
        Assert-MpPresentation
        $incoming = Read-MpThemeExtension
        if (Test-Path -LiteralPath $InstalledTheme -PathType Leaf) {
            $existing = Read-MpThemeExtension -Path $InstalledTheme
            $custom = @($existing.Keys | Where-Object { -not $incoming.ContainsKey($_) -or $incoming[$_] -ne $existing[$_] })
            if ($custom.Count) {
                # Preserve the original file byte-for-byte; the data adapter understands legacy themes.
                [IO.File]::Copy($InstalledTheme, (Join-Path $script:ModuleRoot 'theme.toml'), $true)
            }
        }
        [void](Get-MpConsole)
    } (Join-Path $destination 'theme.toml')
    Remove-Module $stagedModule
    if (Test-Path -LiteralPath $destination) { Move-Item -LiteralPath $destination -Destination $backup }
    Move-Item -LiteralPath $temporary -Destination $destination
    $replacementPlaced = $true
    [void](Invoke-MpInstallProcess @('-File', (Join-Path $destination 'Private/VerifyInstallation.ps1'), '-ModulePath', (Join-Path $destination 'ModpackTools.psd1'), '-ExpectedVersion', $version))
    if (Test-Path -LiteralPath $backup) {
        try {
            Remove-Item -LiteralPath $backup -Recurse -Force
        } catch {
            Write-Warning "ModpackTools was updated, but a process is still using the backup at '$backup'. It can be removed after the previous PowerShell sessions and file locks are closed."
        }
    }
}
catch {
    $installationFailure = $_
    if (Test-Path -LiteralPath $temporary) {
        try { Remove-Item -LiteralPath $temporary -Recurse -Force }
        catch { Write-Warning "A process is still using the rejected package at '$temporary'." }
    }
    if ($replacementPlaced -and (Test-Path -LiteralPath $destination)) {
        # Both locations were validated below safeModuleRoot before replacement.
        Move-Item -LiteralPath $destination -Destination $temporary
    }
    if ((Test-Path -LiteralPath $backup) -and -not (Test-Path -LiteralPath $destination)) {
        Move-Item -LiteralPath $backup -Destination $destination
    }
    if (Test-Path -LiteralPath $temporary) {
        try { Remove-Item -LiteralPath $temporary -Recurse -Force }
        catch { Write-Warning "A process is still using the rejected package at '$temporary'." }
    }
    throw $installationFailure
}
finally { $installLock.Dispose() }
Write-Information "ModpackTools $version installed at $destination" -InformationAction Continue
Import-Module (Join-Path $destination 'ModpackTools.psd1') -Force
if (-not $SkipDoctor) {
    if ($NonInteractive) { modpack doctor }
    else { modpack doctor --fix }
}
& (Get-Module ModpackTools) { Write-R3Status (Get-MpConsole) success 'ModpackTools is ready. Open a new session or run: Import-Module ModpackTools -Force' }
