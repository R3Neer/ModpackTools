function ConvertFrom-MpRelease {
    param($Release)
    $tag = [string](Get-MpPropertyValue $Release 'tag_name')
    if ($tag -cnotmatch '^v([0-9]+\.[0-9]+\.[0-9]+)$' -or (Get-MpPropertyValue $Release 'draft') -ne $false -or (Get-MpPropertyValue $Release 'prerelease') -ne $false) { Throw-MpError -Message 'The update service did not return a stable ModpackTools release' -Hint 'retry the update check' -ErrorId 'SelfUpdate.InvalidRelease' }
    $version = [version]$Matches[1]
    $url = "https://github.com/R3Neer/ModpackTools/releases/tag/$tag"
    if ((Get-MpPropertyValue $Release 'html_url') -cne $url) { Throw-MpError -Message 'Unexpected ModpackTools release location' -Hint 'use the official release' -ErrorId 'SelfUpdate.InvalidRelease' }
    $assets = @((Get-MpPropertyValue $Release 'assets') | Where-Object { $_.name -ceq "ModpackTools-$version.zip" })
    $asset = if ($assets.Count -eq 1) { $assets[0] } else { $null }
    return [pscustomobject]@{ Version=$version.ToString(); Tag=$tag; Url=$url; Asset=$asset }
}

function Get-MpLatestRelease {
    param([switch]$Refresh, [switch]$Quiet)
    $path = Join-Path (Get-ModpackToolsConfigDirectory) 'self-update.json'
    if (-not $Refresh -and [IO.File]::Exists($path)) {
        try {
            $cache = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
            if ($cache.SchemaVersion -eq 1 -and (Test-MpCacheTimestamp $cache.CheckedUtc)) { return ConvertFrom-MpRelease $cache.Release }
        } catch { } # Invalid/stale cache is never treated as a current release.
    }
    try {
        $release = Invoke-RestMethod -Uri 'https://api.github.com/repos/R3Neer/ModpackTools/releases/latest' -Headers @{ Accept='application/vnd.github+json'; 'User-Agent'="ModpackTools/$script:ModuleVersion" } -TimeoutSec 3
        $result = ConvertFrom-MpRelease $release
        try {
            [void][IO.Directory]::CreateDirectory((Split-Path -Parent $path))
            Write-Utf8TextFileAtomic $path (@{SchemaVersion=1; CheckedUtc=[datetime]::UtcNow.ToString('o'); Release=$release} | ConvertTo-Json -Depth 30)
        } catch { } # A read-only cache directory must not break version checks.
        return $result
    } catch {
        if ($Quiet) { return $null }
        Throw-MpError -Message 'Could not check for a ModpackTools update' -Details $_.Exception.Message -Hint 'check the connection and retry modpack --update --check' -ErrorId 'SelfUpdate.CheckFailed' -Category ConnectionError
    }
}

function Invoke-MpVersion {
    param([object[]]$Arguments = @())
    $parsed = ConvertFrom-MpOptions $Arguments -SwitchOptions @('offline')
    if ($parsed.Positionals.Count) { Throw-MpError -Message 'The global version option does not accept positional arguments' -Hint 'modpack --version [--offline]' -ErrorId 'Command.InvalidArguments' -Category InvalidArgument }
    Write-R3Line (Get-MpConsole) @(@{Text="ModpackTools $script:ModuleVersion"})
    if (-not $parsed.Options.ContainsKey('offline')) {
        $release = Get-MpLatestRelease -Quiet
        if ($release -and [version]$release.Version -gt [version]$script:ModuleVersion) {
            Write-R3Status (Get-MpConsole) info "Update available: $($release.Version). Run modpack --update."
        }
    }
}

function Get-MpSelfUpdateTarget {
    $destination = Resolve-MpInstallDestination
    $manifestPath = Join-Path $destination 'ModpackTools.psd1'
    if (-not [IO.File]::Exists($manifestPath)) { Throw-MpError -Message 'No user installation of ModpackTools was found' -Hint 'run Install-ModpackTools.ps1 from a release package first' -ErrorId 'SelfUpdate.NotInstalled' -Category ObjectNotFound }
    # PowerShell may prefer a versioned or machine-wide installation. Never claim
    # to update the effective module when it resolves outside our writable target.
    $available = @(Get-Module -ListAvailable ModpackTools)
    if ($available.Count -and $available[0].ModuleBase -ne $destination) {
        Throw-MpError -Message 'Another installation takes precedence over the update target' -Details $available[0].ModuleBase -Hint 'select a user installation in PSModulePath before running --update' -ErrorId 'SelfUpdate.ShadowedInstallation' -Category InvalidOperation
    }
    $others = @(foreach ($root in @(Get-MpUserModuleRoots)) {
        $copy = Join-Path $root 'ModpackTools'
        if ($copy -ne $destination -and [IO.File]::Exists((Join-Path $copy 'ModpackTools.psd1'))) { $copy }
    })
    return [pscustomobject]@{ Path=$destination; Version=[version](Import-PowerShellDataFile $manifestPath).ModuleVersion; Others=$others }
}

function Expand-MpUpdatePackage {
    param([string]$ArchivePath, [string]$Destination)
    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        $seen = @{}; [long]$size = 0
        foreach ($entry in $archive.Entries) {
            $size += $entry.Length
            if ($size -gt 128MB -or $archive.Entries.Count -gt 10000) { Throw-MpError -Message 'Update archive exceeds package limits' -Hint 'download a valid release package' -ErrorId 'SelfUpdate.InvalidPackage' }
            $relative = $entry.FullName.Replace('\','/')
            if ($relative.Contains(':') -or $relative.StartsWith('/') -or $relative -match '(^|/)\.\.(/|$)' -or (($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) { Throw-MpError -Message 'Update archive contains an unsafe path or link' -Hint 'download a valid release package' -ErrorId 'SelfUpdate.InvalidPackage' }
            if ($relative.EndsWith('/')) { continue }
            $path = Resolve-MpContainedPath $Destination $relative
            if ($seen.ContainsKey($path)) { Throw-MpError -Message 'Update archive contains duplicate paths' -Hint 'download a valid release package' -ErrorId 'SelfUpdate.InvalidPackage' }
            $seen[$path] = $true
        }
    } finally { $archive.Dispose() }
    [IO.Compression.ZipFile]::ExtractToDirectory($ArchivePath, $Destination)
    $installers = @(Get-ChildItem -LiteralPath $Destination -Filter 'Install-ModpackTools.ps1' -Recurse -File)
    if ($installers.Count -ne 1) { Throw-MpError -Message 'Update archive must contain exactly one installer' -Hint 'download a valid release package' -ErrorId 'SelfUpdate.InvalidPackage' }
    return $installers[0].Directory.FullName
}

function Install-MpSelfUpdate {
    param($Release, $Target)
    $asset = $Release.Asset
    if (-not $asset) { Throw-MpError -Message 'The release has no unique installable ZIP' -Hint $Release.Url -ErrorId 'SelfUpdate.MissingAsset' -Category InvalidData }
    $expectedUrl = "https://github.com/R3Neer/ModpackTools/releases/download/$($Release.Tag)/ModpackTools-$($Release.Version).zip"
    $digest = [string](Get-MpPropertyValue $asset 'digest')
    if ($asset.browser_download_url -cne $expectedUrl -or $digest -notmatch '^sha256:([a-fA-F0-9]{64})$') { Throw-MpError -Message 'Release asset URL or SHA256 is invalid' -Hint $Release.Url -ErrorId 'SelfUpdate.InvalidAsset' -Category InvalidData }
    $expectedHash = $digest.Substring(7)
    $directory = Join-Path (Get-ModpackToolsConfigDirectory) ('self-update/' + [guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($directory)
    try {
        $archive = Join-Path $directory 'release.zip'
        Invoke-WebRequest -Uri $expectedUrl -OutFile $archive -TimeoutSec 120
        if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedHash) { Throw-MpError -Message 'Downloaded update failed SHA256 verification' -Hint 'download the release again' -ErrorId 'SelfUpdate.HashMismatch' }
        $package = Expand-MpUpdatePackage $archive (Join-Path $directory 'package')
        $manifest = Import-PowerShellDataFile (Join-Path $package 'ModpackTools.psd1')
        if ([version]$manifest.ModuleVersion -ne [version]$Release.Version -or $manifest.GUID -ne 'dc256dd6-6b3d-4bc5-aed0-14dad616642b') { Throw-MpError -Message 'Release package identity or version differs from the selected release' -Hint 'download a valid release package' -ErrorId 'SelfUpdate.InvalidPackage' }
        [void](Test-MpR3Package $package)
        if ((Get-FileHash -LiteralPath (Join-Path $Target.Path 'ModpackTools.psd1')).Hash -ne $Target.ManifestHash) { Throw-MpError -Message 'Installed version changed while the update was being prepared' -Hint 'review the installation and retry' -ErrorId 'SelfUpdate.ConcurrentChange' }
        $output = Invoke-MpInstallProcess @('-File', (Join-Path $package 'Install-ModpackTools.ps1'), '-Force', '-NonInteractive', '-SkipDoctor', '-InstallPath', $Target.Path, '-ExpectedManifestHash', $Target.ManifestHash)
        foreach ($line in @($output -split '\r?\n' | Where-Object { $_ })) { Write-R3Line (Get-MpConsole) @(@{ Text=$line }) }
        if ([version](Import-PowerShellDataFile (Join-Path $Target.Path 'ModpackTools.psd1')).ModuleVersion -ne [version]$Release.Version) { Throw-MpError -Message 'The installed version does not match the release' -Hint 'inspect the installer result' -ErrorId 'SelfUpdate.VerificationFailed' }
    } catch {
        Throw-MpError -Message 'ModpackTools update failed' -Details $_.Exception.Message -Hint 'inspect the error and retry modpack --update' -ErrorId 'SelfUpdate.InstallFailed' -Category OperationStopped
    } finally {
        $safe = Resolve-MpContainedPath (Join-Path (Get-ModpackToolsConfigDirectory) 'self-update') ([IO.Path]::GetFileName($directory))
        if ([IO.Directory]::Exists($safe)) { Remove-Item -LiteralPath $safe -Recurse -Force }
    }
}

function Invoke-MpSelfUpdate {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Show-MpHelp '--update'; return }
    $parsed = ConvertFrom-MpOptions $Arguments -SwitchOptions @('check','yes')
    if ($parsed.Positionals.Count -or ($parsed.Options.ContainsKey('check') -and $parsed.Options.ContainsKey('yes'))) { Throw-MpError -Message 'Invalid self-update arguments' -Hint 'modpack --update [--check | --yes]' -ErrorId 'Command.InvalidArguments' -Category InvalidArgument }
    $release = Get-MpLatestRelease -Refresh
    if ($parsed.Options.ContainsKey('check')) {
        Write-R3Line (Get-MpConsole) @(@{Text="ModpackTools $script:ModuleVersion"})
        if ([version]$release.Version -gt [version]$script:ModuleVersion) { Write-R3Status (Get-MpConsole) info "Update available: $($release.Version). Run modpack --update." }
        else { Write-R3Status (Get-MpConsole) success 'No newer stable release is available.' }
        Write-R3Line (Get-MpConsole) @(@{Text=$release.Url})
        return
    }
    $target = Get-MpSelfUpdateTarget
    $target | Add-Member -NotePropertyName ManifestHash -NotePropertyValue (Get-FileHash -LiteralPath (Join-Path $target.Path 'ModpackTools.psd1')).Hash
    Write-R3Status (Get-MpConsole) info "Installation: $($target.Path)"
    foreach ($other in $target.Others) { Write-R3Status (Get-MpConsole) warning "Another installation exists at $other; it is not selected by this session." }
    if ([version]$release.Version -le $target.Version) { Write-R3Status (Get-MpConsole) success "ModpackTools $($target.Version): no newer stable release is available."; return }
    Write-R3Status (Get-MpConsole) info "ModpackTools $($target.Version) -> $($release.Version)"
    Write-R3Line (Get-MpConsole) @(@{Text=$release.Url})
    if (-not $parsed.Options.ContainsKey('yes') -and -not (Confirm-MpDoctorAction -Prompt 'Update ModpackTools?' -Default $false)) { Write-R3Status (Get-MpConsole) info 'Update cancelled.'; return }
    Install-MpSelfUpdate $release $target
    Write-R3Status (Get-MpConsole) success "ModpackTools $($release.Version) installed and verified. Open a new PowerShell session to load it."
}
