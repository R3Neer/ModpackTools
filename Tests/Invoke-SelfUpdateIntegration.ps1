[CmdletBinding()]
param([string]$WorkRoot = [IO.Path]::GetTempPath())
$ErrorActionPreference = 'Stop'
$source = Split-Path -Parent $PSScriptRoot
$run = Join-Path ([IO.Path]::GetFullPath($WorkRoot)) ('self-update-integration-' + [guid]::NewGuid().ToString('N'))
$package = Join-Path $run 'package/ModpackTools'
$modules = Join-Path $run 'Modules'
$target = Join-Path $modules 'ModpackTools'
foreach ($path in @($package,$modules)) { [void][IO.Directory]::CreateDirectory($path) }
foreach ($name in @('docs','Private','Public','ModpackTools.psd1','ModpackTools.psm1','README.md','LICENSE','theme.toml','dependencies.psd1','Install-ModpackTools.ps1')) {
    Copy-Item -LiteralPath (Join-Path $source $name) -Destination $package -Recurse
}
$releaseVersion = [string](Import-PowerShellDataFile (Join-Path $package 'ModpackTools.psd1')).ModuleVersion
$archive = Join-Path $run 'release.zip'
[IO.Compression.ZipFile]::CreateFromDirectory((Split-Path -Parent $package), $archive)
$hash = (Get-FileHash $archive).Hash
$savedModulePath = $env:PSModulePath
try {
    $env:PSModulePath = $modules + [IO.Path]::PathSeparator + (Join-Path $PSHOME 'Modules')
    . (Join-Path $source 'Private/Errors.ps1')
    . (Join-Path $source 'Private/Installation.ps1')
    [void](Invoke-MpInstallProcess @('-File', (Join-Path $package 'Install-ModpackTools.ps1'), '-Force', '-NonInteractive', '-SkipDoctor', '-InstallPath', $target))
    $manifestPath = Join-Path $target 'ModpackTools.psd1'
    $text = [IO.File]::ReadAllText($manifestPath).Replace("'$releaseVersion'", "'1.0.0'")
    [IO.File]::WriteAllText($manifestPath, $text)
    $themePath = Join-Path $target 'theme.toml'
    [IO.File]::WriteAllText($themePath, "[colours]`r`nclient = `"#748FFC`"`r`nhost = `"#BE70FF`"`r`nlocal = `"#FF91CD`"`r`nheading = `"#123456`"`r`n")
    $themeHash = (Get-FileHash $themePath).Hash
    Import-Module $manifestPath -Force
    & (Get-Module ModpackTools) {
        param($Run, $Version, $Archive, $Hash)
        $script:ConfigHomeOverride = Join-Path $Run 'cache'
        $script:FixtureReleaseVersion = $Version; $script:FixtureArchive = $Archive; $script:FixtureHash = $Hash
        # Only the HTTP transport is substituted. ZIP verification, extraction,
        # target selection and the real installer in a fresh process all execute.
        function script:Invoke-RestMethod {
            param($Uri,$Headers,$TimeoutSec)
            $v = $script:FixtureReleaseVersion
            [pscustomobject]@{tag_name="v$v";draft=$false;prerelease=$false;html_url="https://github.com/R3Neer/ModpackTools/releases/tag/v$v";assets=@([pscustomobject]@{name="ModpackTools-$v.zip";browser_download_url="https://github.com/R3Neer/ModpackTools/releases/download/v$v/ModpackTools-$v.zip";digest="sha256:$script:FixtureHash"})}
        }
        function script:Invoke-WebRequest { param($Uri,$OutFile,$TimeoutSec); [IO.File]::Copy($script:FixtureArchive,$OutFile) }
        $notice = modpack --version --ascii --colour never 6>&1 | Out-String
        if ($notice -notmatch 'Update available') { throw 'Old installation did not announce the update.' }
        modpack --update --yes
    } $run $releaseVersion $archive $hash
    if ((Get-FileHash $themePath).Hash -ne $themeHash) { throw 'Self-update changed the custom theme.' }
    [void](Invoke-MpInstallProcess @('-File', (Join-Path $target 'Private/VerifyInstallation.ps1'), '-ModulePath', $manifestPath, '-ExpectedVersion', $releaseVersion))
    $before = @{}
    foreach ($file in Get-ChildItem -LiteralPath $target -File -Recurse) { $before[[IO.Path]::GetRelativePath($target,$file.FullName)] = (Get-FileHash $file.FullName).Hash }
    # Inject a post-replacement failure, beyond the existing preflight tests.
    [IO.File]::WriteAllText((Join-Path $package 'Private/VerifyInstallation.ps1'), "throw 'injected post-replacement validation failure'")
    $failed = $false
    try { [void](Invoke-MpInstallProcess @('-File', (Join-Path $package 'Install-ModpackTools.ps1'), '-Force', '-NonInteractive', '-SkipDoctor', '-InstallPath', $target)) }
    catch { if ($_.Exception.Message -notmatch 'injected post-replacement') { throw }; $failed = $true }
    if (-not $failed) { throw 'Expected post-replacement failure did not occur.' }
    $after = @(Get-ChildItem -LiteralPath $target -File -Recurse)
    if ($after.Count -ne $before.Count) { throw 'Rollback changed file count.' }
    foreach ($file in $after) { if ($before[[IO.Path]::GetRelativePath($target,$file.FullName)] -ne (Get-FileHash $file.FullName).Hash) { throw 'Rollback changed installed bytes.' } }
    [pscustomobject]@{ InstalledModule=$manifestPath; Version=$releaseVersion; UpdateNotice=$true; RealInstaller=$true; ThemePreserved=$true; FreshProcessVerified=$true; PostReplacementRollback=$true }
} finally { $env:PSModulePath = $savedModulePath }
