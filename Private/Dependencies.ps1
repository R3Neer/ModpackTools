function Get-MpDependencyManifest {
    if ($script:DependencyManifestOverride) { return $script:DependencyManifestOverride }
    $path = Join-Path $script:ModuleRoot 'dependencies.psd1'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        Throw-MpError -Message "Dependency manifest '$path' does not exist" -Hint 'reinstall ModpackTools' -ErrorId 'Dependency.ManifestNotFound' -Category ObjectNotFound -TargetObject $path
    }
    try { return Import-PowerShellDataFile -LiteralPath $path }
    catch {
        Throw-MpError -Message "Dependency manifest '$path' is invalid" -Details $_.Exception.Message -Hint 'reinstall ModpackTools' -ErrorId 'Dependency.ManifestInvalid' -Category InvalidData -TargetObject $path
    }
}

function Get-MpPackwizDependency {
    if (-not $IsWindows -or [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [System.Runtime.InteropServices.Architecture]::X64) {
        Throw-MpError -Message 'Managed Packwiz installation is currently available only for Windows x64' -Hint 'install Packwiz manually and run modpack config set packwiz <executable>' -ErrorId 'Dependency.PlatformUnsupported' -Category NotImplemented
    }
    $manifest = Get-MpDependencyManifest
    return $manifest.Packwiz.WindowsX64
}

function Get-MpManagedPackwizDirectory {
    param($Dependency = (Get-MpPackwizDependency))
    return Join-Path (Get-ModpackToolsConfigDirectory) "tools\packwiz\$($Dependency.DisplayVersion)"
}

function Get-MpManagedPackwizPath {
    param($Dependency = (Get-MpPackwizDependency))
    return Join-Path (Get-MpManagedPackwizDirectory -Dependency $Dependency) $Dependency.Executable
}

function Resolve-MpPackwiz {
    $config = Get-ModpackToolsConfig
    if ($config.ContainsKey('PackwizPath') -and -not [string]::IsNullOrWhiteSpace([string]$config.PackwizPath)) {
        $configured = [System.IO.Path]::GetFullPath([string]$config.PackwizPath)
        return [pscustomobject]@{
            Available = Test-Path -LiteralPath $configured -PathType Leaf
            Path = $configured
            Source = 'configured'
            Version = $null
        }
    }

    $command = Get-Command packwiz -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) {
        return [pscustomobject]@{ Available = $true; Path = $command.Source; Source = 'PATH'; Version = $null }
    }

    try {
        $dependency = Get-MpPackwizDependency
        $managed = Get-MpManagedPackwizPath -Dependency $dependency
        if (Test-Path -LiteralPath $managed -PathType Leaf) {
            return [pscustomobject]@{ Available = $true; Path = $managed; Source = 'managed'; Version = $dependency.DisplayVersion }
        }
    }
    catch {
        if (Test-MpExpectedError -Exception $_.Exception) {
            return [pscustomobject]@{ Available = $false; Path = $null; Source = 'missing'; Version = $null }
        }
        throw
    }
    return [pscustomobject]@{ Available = $false; Path = $null; Source = 'missing'; Version = $null }
}

function Get-MpPackwizExecutable {
    $resolved = Resolve-MpPackwiz
    if (-not $resolved.Available) {
        $message = if ($resolved.Source -eq 'configured') { "Configured Packwiz executable '$($resolved.Path)' does not exist" } else { 'Required command Packwiz is not available' }
        Throw-MpError -Message $message -Hint 'modpack doctor --fix' -ErrorId 'Dependency.PackwizNotFound' -Category ObjectNotFound -TargetObject $resolved.Path
    }
    return $resolved.Path
}

function Install-MpManagedPackwiz {
    [CmdletBinding()]
    param([string]$ArchivePath)

    $dependency = Get-MpPackwizDependency
    $targetDirectory = Get-MpManagedPackwizDirectory -Dependency $dependency
    $targetExecutable = Join-Path $targetDirectory $dependency.Executable
    if (Test-Path -LiteralPath $targetExecutable -PathType Leaf) {
        $existingHash = (Get-FileHash -LiteralPath $targetExecutable -Algorithm SHA256).Hash
        if ($existingHash -eq $dependency.ExecutableSha256) {
            return [pscustomobject]@{ Path = $targetExecutable; Version = $dependency.DisplayVersion; Installed = $false }
        }
    }

    $temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('ModpackTools-packwiz-' + [guid]::NewGuid().ToString('N'))
    $download = Join-Path $temporaryRoot 'packwiz.zip'
    $expanded = Join-Path $temporaryRoot 'expanded'
    $staging = "$targetDirectory.install-$([guid]::NewGuid().ToString('N'))"
    [System.IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
    try {
        if ($ArchivePath) { Copy-Item -LiteralPath $ArchivePath -Destination $download -Force }
        else {
            try { Invoke-WebRequest -Uri $dependency.Uri -OutFile $download -UseBasicParsing }
            catch {
                Throw-MpError -Message 'The verified Packwiz archive could not be downloaded' -Details $_.Exception.Message -Hint 'check the network connection and retry modpack doctor --fix' -ErrorId 'Dependency.DownloadFailed' -Category ConnectionError -TargetObject $dependency.Uri
            }
        }

        $archiveHash = (Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash
        if ($archiveHash -ne $dependency.ArchiveSha256) {
            Throw-MpError -Message 'Downloaded Packwiz archive failed SHA-256 verification' -Details "Expected $($dependency.ArchiveSha256); received $archiveHash" -Hint 'retry later or install Packwiz manually' -ErrorId 'Dependency.ArchiveHashMismatch' -Category SecurityError -TargetObject $download
        }
        try { Expand-Archive -LiteralPath $download -DestinationPath $expanded -Force }
        catch {
            Throw-MpError -Message 'The verified Packwiz archive could not be extracted' -Details $_.Exception.Message -Hint 'retry modpack doctor --fix' -ErrorId 'Dependency.ArchiveInvalid' -Category InvalidData -TargetObject $download
        }
        $sourceExecutable = Join-Path $expanded $dependency.Executable
        $sourceLicense = Join-Path $expanded $dependency.License
        if (-not (Test-Path -LiteralPath $sourceExecutable -PathType Leaf) -or -not (Test-Path -LiteralPath $sourceLicense -PathType Leaf)) {
            Throw-MpError -Message 'Verified Packwiz archive does not contain the expected executable and license' -Hint 'retry later or install Packwiz manually' -ErrorId 'Dependency.ArchiveIncomplete' -Category InvalidData -TargetObject $download
        }
        $executableHash = (Get-FileHash -LiteralPath $sourceExecutable -Algorithm SHA256).Hash
        if ($executableHash -ne $dependency.ExecutableSha256) {
            Throw-MpError -Message 'Packwiz executable failed SHA-256 verification' -Details "Expected $($dependency.ExecutableSha256); received $executableHash" -Hint 'retry later or install Packwiz manually' -ErrorId 'Dependency.ExecutableHashMismatch' -Category SecurityError -TargetObject $sourceExecutable
        }

        [System.IO.Directory]::CreateDirectory($staging) | Out-Null
        Copy-Item -LiteralPath $sourceExecutable,$sourceLicense -Destination $staging -Force
        $parent = Split-Path -Parent $targetDirectory
        [System.IO.Directory]::CreateDirectory($parent) | Out-Null
        $backup = $null
        if (Test-Path -LiteralPath $targetDirectory) {
            $backup = "$targetDirectory.backup-$([guid]::NewGuid().ToString('N'))"
            Move-Item -LiteralPath $targetDirectory -Destination $backup
        }
        try { Move-Item -LiteralPath $staging -Destination $targetDirectory }
        catch {
            if ($backup -and -not (Test-Path -LiteralPath $targetDirectory)) { Move-Item -LiteralPath $backup -Destination $targetDirectory }
            throw
        }
        if ($backup) { Remove-Item -LiteralPath $backup -Recurse -Force }
        return [pscustomobject]@{ Path = $targetExecutable; Version = $dependency.DisplayVersion; Installed = $true }
    }
    finally {
        if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
        if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
    }
}

function Add-MpDirectoryToUserPath {
    param([Parameter(Mandatory)][string]$Directory)
    $resolved = [System.IO.Path]::GetFullPath($Directory).TrimEnd('\')
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $entries = @($userPath -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if (-not ($entries | Where-Object { ([System.IO.Path]::GetFullPath($_)).TrimEnd('\') -eq $resolved })) {
        $updated = (@($entries) + $resolved) -join ';'
        [Environment]::SetEnvironmentVariable('Path', $updated, 'User')
    }
    $processEntries = @($env:Path -split ';')
    if (-not ($processEntries | Where-Object { $_.TrimEnd('\') -eq $resolved })) { $env:Path = "$env:Path;$resolved" }
    return $resolved
}
