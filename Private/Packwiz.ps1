function Invoke-NativeCommandChecked {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$WorkingDirectory
    )

    $command = Get-Command $FilePath -ErrorAction Stop
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $command.Source
    $startInfo.WorkingDirectory = [System.IO.Path]::GetFullPath($WorkingDirectory)
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $startInfo.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    foreach ($argument in $Arguments) { [void]$startInfo.ArgumentList.Add($argument) }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) { throw "Could not start '$FilePath'." }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        $lines = @(
            @($stdout -split "`r?`n" | Where-Object { $_ -ne '' })
            @($stderr -split "`r?`n" | Where-Object { $_ -ne '' })
        )
        if ($process.ExitCode -ne 0) {
            $details = if ($lines.Count) { [Environment]::NewLine + ($lines -join [Environment]::NewLine) } else { '' }
            throw "'$FilePath $($Arguments -join ' ')' exited with code $($process.ExitCode).$details"
        }
        return $lines
    }
    finally { $process.Dispose() }
}

function Invoke-Packwiz {
    param(
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$WorkingDirectory
    )
    return @(Invoke-NativeCommandChecked -FilePath 'packwiz' -Arguments $Arguments -WorkingDirectory $WorkingDirectory)
}

function New-ModpackProjectFiles {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Id,
        [Parameter(Mandatory)][string]$DisplayName,
        [Parameter(Mandatory)][string]$DisplayVersion
    )

    foreach ($directory in @('.modpack', 'mods', 'config', 'resourcepacks', 'shaderpacks', 'dist')) {
        [System.IO.Directory]::CreateDirectory((Join-Path $Root $directory)) | Out-Null
    }
    $outputName = (($DisplayName -replace '[\\/:*?"<>|]', '-').Trim() + "-$DisplayVersion.mrpack")
    Write-PowerShellDataFileAtomic -Path (Join-Path $Root '.modpack/project.psd1') -Data ([ordered]@{
        SchemaVersion  = 1
        Id             = $Id
        DisplayName    = $DisplayName
        DisplayVersion = $DisplayVersion
        OutputName     = $outputName
    })
    Write-PowerShellDataFileAtomic -Path (Join-Path $Root '.modpack/metadata.psd1') -Data ([ordered]@{
        Categories    = [ordered]@{}
        Mods          = [ordered]@{}
        ResourcePacks = [ordered]@{}
    })

    $readme = @"
# $DisplayName

Packwiz project managed with ModpackTools.

Common commands:

```powershell
modpack use $Id
modpack status
modpack add <slug> --category <category>
modpack build
```

Technical data belongs to Packwiz. Editorial decisions are stored in `.modpack`.
"@
    [System.IO.File]::WriteAllText((Join-Path $Root 'README.md'), $readme, [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText((Join-Path $Root '.gitignore'), "dist/`n", [System.Text.UTF8Encoding]::new($false))
}

function New-ModpackProject {
    param(
        [Parameter(Mandatory)][string]$Id,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$MinecraftVersion,
        [Parameter(Mandatory)][string]$Loader,
        [string]$LoaderVersion,
        [string]$DirectoryName,
        [string]$PackVersion = '0.1.0',
        [string]$DisplayVersion = $MinecraftVersion
    )

    if ($Id -notmatch '^[a-z][a-z0-9-]*$') { throw "Invalid Id '$Id'. Use lowercase letters, numbers, and hyphens." }
    if ($Loader.ToLowerInvariant() -ne 'fabric') { throw "modpack new currently automates only the 'fabric' loader." }
    $root = Get-ModpackRoot
    if ((Get-ModpackProjects | Where-Object Id -eq $Id)) { throw "A project with Id '$Id' already exists." }
    if (-not $DirectoryName) { $DirectoryName = (($Name -replace '[\\/:*?"<>|]', '-').Trim() + "-$MinecraftVersion") }
    if ([System.IO.Path]::IsPathRooted($DirectoryName)) {
        $target = [System.IO.Path]::GetFullPath($DirectoryName)
    } else {
        $target = [System.IO.Path]::GetFullPath((Join-Path $root $DirectoryName))
    }
    if ((Split-Path -Parent $target) -ne $root.TrimEnd('\')) {
        throw "The project must be a direct child of the configured root: $root"
    }
    if (Test-Path -LiteralPath $target) { throw "The destination already exists and will not be overwritten: $target" }

    $temporary = Join-Path $root ('.modpacktools-new-' + [guid]::NewGuid().ToString('N'))
    [System.IO.Directory]::CreateDirectory($temporary) | Out-Null
    try {
        $arguments = @('init', '--yes', '--name', $Name, '--mc-version', $MinecraftVersion, '--version', $PackVersion, '--modloader', 'fabric')
        if ($LoaderVersion) { $arguments += @('--fabric-version', $LoaderVersion) }
        else { $arguments += '--fabric-latest' }
        [void](Invoke-Packwiz -Arguments $arguments -WorkingDirectory $temporary)
        New-ModpackProjectFiles -Root $temporary -Id $Id -DisplayName $Name -DisplayVersion $DisplayVersion
        Move-Item -LiteralPath $temporary -Destination $target
    }
    catch {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
        throw
    }
    return (Read-ModpackProject -ProjectRoot $target)
}

function Add-ModpackMod {
    param(
        [Parameter(Mandatory)]$Project,
        [Parameter(Mandatory)][string]$Slug,
        [string]$Category
    )

    if ($Category) {
        $metadata = Get-ModpackMetadata -Project $Project
        if (-not $metadata.Categories.ContainsKey($Category)) {
            throw "Category '$Category' does not exist in '$($Project.Id)'."
        }
    }
    $before = @{}
    $modsPath = Join-Path $Project.Root 'mods'
    if (Test-Path -LiteralPath $modsPath) {
        foreach ($file in Get-ChildItem -LiteralPath $modsPath -Filter '*.pw.toml' -File) {
            $before[$file.FullName] = "$($file.Length):$($file.LastWriteTimeUtc.Ticks)"
        }
    }
    $log = @(Invoke-Packwiz -Arguments @('modrinth', 'add', $Slug, '--yes') -WorkingDirectory $Project.Root)
    $candidates = @(
        Get-ChildItem -LiteralPath $modsPath -Filter '*.pw.toml' -File |
            Where-Object { -not $before.ContainsKey($_.FullName) -or $before[$_.FullName] -ne "$($_.Length):$($_.LastWriteTimeUtc.Ticks)" } |
            Sort-Object LastWriteTimeUtc -Descending
    )
    if ($candidates.Count -eq 0) { throw "Packwiz completed successfully, but the added or updated .pw.toml file could not be identified." }
    $items = @(Get-PackwizItems -Project $Project -Directory mods -Kind mod)
    $item = $items | Where-Object MetadataPath -eq $candidates[0].FullName | Select-Object -First 1
    if (-not $item) { throw "Could not normalize '$($candidates[0].FullName)'." }
    if ($Category) { Set-ModMetadataCategory -Project $Project -ModId $item.Id -Category $Category }
    return [pscustomobject]@{ Item = $item; Log = $log }
}

function Build-ModpackProject {
    param(
        [Parameter(Mandatory)]$Project,
        [switch]$NoRefresh,
        [switch]$KeepOld,
        [switch]$RawLog
    )

    Assert-ModpackStructure -Project $Project
    $started = [datetime]::UtcNow
    $allLog = [System.Collections.Generic.List[string]]::new()
    if (-not $NoRefresh) {
        foreach ($line in @(Invoke-Packwiz -Arguments @('refresh') -WorkingDirectory $Project.Root)) { $allLog.Add($line) }
    }
    $dist = Join-Path $Project.Root 'dist'
    [System.IO.Directory]::CreateDirectory($dist) | Out-Null
    $finalName = $Project.OutputName
    if ($KeepOld -and (Test-Path -LiteralPath (Join-Path $dist $finalName))) {
        $base = [System.IO.Path]::GetFileNameWithoutExtension($finalName)
        $finalName = '{0}-{1}.mrpack' -f $base, (Get-Date -Format 'yyyyMMdd-HHmmss')
    }
    $finalPath = Join-Path $dist $finalName
    $temporary = Join-Path $dist ('.modpacktools-' + [guid]::NewGuid().ToString('N') + '.mrpack')
    try {
        foreach ($line in @(Invoke-Packwiz -Arguments @('modrinth', 'export', '--output', $temporary) -WorkingDirectory $Project.Root)) { $allLog.Add($line) }
        if (-not (Test-Path -LiteralPath $temporary -PathType Leaf)) { throw 'Packwiz did not generate the expected artifact.' }
        Move-Item -LiteralPath $temporary -Destination $finalPath -Force
    }
    finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }

    $visibleLog = if ($RawLog) { @($allLog) } else { @($allLog | Where-Object { $_ -notmatch '\sadded to manifest\s*$' }) }
    [pscustomobject]@{
        Path       = $finalPath
        Size       = (Get-Item -LiteralPath $finalPath).Length
        Duration   = [datetime]::UtcNow - $started
        Log        = $visibleLog
        RawLog     = @($allLog)
        Inventory  = Get-ModpackInventory -Project $Project
    }
}
