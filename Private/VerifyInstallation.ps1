param([Parameter(Mandatory)][string]$ModulePath, [Parameter(Mandatory)][string]$ExpectedVersion)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Errors.ps1')
$module = Import-Module -Name $ModulePath -Force -PassThru
if ($module.Version -ne [version]$ExpectedVersion -or $module.ModuleBase -ne (Split-Path -Parent ([IO.Path]::GetFullPath($ModulePath)))) { Throw-MpError -Message 'Installed version or path does not match the update target' -Hint 'restore the previous installation' -ErrorId 'Installation.VerificationFailed' }
& $module {
    Assert-MpPresentation
    [void](Get-MpConsole)
    foreach ($command in (Get-MpCommandCatalog).Keys) { modpack $command --help --ascii --colour never 6>$null }
}
Write-Output "Verified ModpackTools $ExpectedVersion at $ModulePath"
