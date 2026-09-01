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

function Get-ModpackProjectReadmeText {
    param([Parameter(Mandatory)]$Project)

    $loaderName = if ($Project.Loader) { (Get-Culture).TextInfo.ToTitleCase($Project.Loader) } else { 'Unknown' }
    $loaderDescription = if ($Project.LoaderVersion) { "$loaderName $($Project.LoaderVersion)" } else { $loaderName }
    $template = @'
# {0} {1}

Minecraft Java modpack managed with [Packwiz](https://packwiz.infra.link/) and ModpackTools.

## Project

- **ID:** `{2}`
- **Minecraft:** {3}
- **Loader:** {4}
- **Technical pack version:** {5}
- **Build artifact:** `dist/{6}`

## Quick start

```powershell
modpack use {2}
modpack status
modpack inventory
modpack diff
modpack build
```

`modpack use` selects this project only for the current PowerShell process. Every command that accepts a project ID can also be called explicitly, for example `modpack diff {2}`.

## Managing content

Add a Modrinth mod and optionally assign an existing editorial category:

```powershell
modpack add <slug>
modpack add <slug> --category <category>
modpack update <name|id|filename...>
modpack update --all
modpack update --all --type mod
```

Inspect or filter the current contents:

```powershell
modpack inventory --type mod
modpack inventory --category <category>
modpack inventory --type resourcepack --state inactive
modpack inventory --search <text>
```

Enable or reposition a resource pack. Position `1` is the highest priority in the Minecraft GUI:

```powershell
modpack resource enable <name|id|filename> --position <n>
```

## Build workflow

```powershell
modpack diff
modpack build
```

`modpack diff` compares the current project with the newest `.mrpack` in `dist/`. `modpack build` refreshes Packwiz metadata and writes the generated artifact to `dist/`.

`modpack update` updates mods, resource packs, and shaders managed by Packwiz. Multiple selectors form one transaction: if any update fails, every Packwiz metadata change in the group is rolled back. Use `--type mod|resourcepack|shaderpack` to narrow the operation. Local files are never updated. Review the result with `modpack diff` before building.

## Sources of truth

- `pack.toml`, `index.toml`, and `.pw.toml` files own technical Packwiz data.
- `.modpack/project.psd1` owns the short ID and display/build identity.
- `.modpack/metadata.psd1` owns editorial categories and display-name overrides.
- `config/defaultoptions-common.toml` owns enabled resource packs and their order.
- `dist/` contains generated artifacts and is not a source of truth.
'@
    return $template -f @(
        $Project.DisplayName, $Project.DisplayVersion, $Project.Id, $Project.MinecraftVersion,
        $loaderDescription, $Project.PackVersion, $Project.OutputName
    )
}

function Write-ModpackProjectReadme {
    param([Parameter(Mandatory)]$Project)
    $path = Join-Path $Project.Root 'README.md'
    Write-Utf8TextFileAtomic -Path $path -Text (Get-ModpackProjectReadmeText -Project $Project)
    return $path
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

    $project = Read-ModpackProject -ProjectRoot $Root
    [void](Write-ModpackProjectReadme -Project $project)
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

function Get-ModpackUpdateItems {
    param(
        [Parameter(Mandatory)]$Inventory,
        [string]$Type = 'all'
    )

    $normalizedType = Resolve-InventoryType -Type $Type
    return @(
        if ($normalizedType -in @('all', 'mod')) { $Inventory.Mods }
        if ($normalizedType -in @('all', 'resourcepack')) { $Inventory.ResourcePacks }
        if ($normalizedType -in @('all', 'shaderpack')) { $Inventory.Shaders }
    )
}

function Resolve-ModpackUpdateSelectors {
    param(
        [Parameter(Mandatory)]$Project,
        [Parameter(Mandatory)][string[]]$Selectors,
        [string]$Type = 'all'
    )

    $inventory = Get-ModpackInventory -Project $Project
    $items = @(Get-ModpackUpdateItems -Inventory $inventory -Type $Type)
    $resolved = [System.Collections.Generic.List[object]]::new()
    foreach ($selector in $Selectors) {
        $matches = @(
            $items | Where-Object {
                $stem = if ($_.MetadataPath) { [System.IO.Path]::GetFileName($_.MetadataPath) -replace '\.pw\.toml$', '' } else { $null }
                @($_.Name, $_.Id, $_.Filename, $stem) | Where-Object {
                    $_ -and ([string]$_).Equals($selector, [System.StringComparison]::OrdinalIgnoreCase)
                }
            }
        )
        if ($matches.Count -eq 0) {
            throw "Content '$selector' was not found. Run: modpack inventory"
        }
        if ($matches.Count -gt 1) {
            $ids = @($matches | ForEach-Object { "$($_.Kind):$($_.Id)" } | Sort-Object -Unique) -join ', '
            throw "Content selector '$selector' is ambiguous. Matching entries: $ids. Use --type to narrow it."
        }
        $item = $matches[0]
        if ($item.Source -ne 'packwiz' -or -not $item.MetadataPath) {
            throw "Content '$selector' is local and cannot be updated by Packwiz."
        }
        if (-not ($resolved | Where-Object MetadataPath -eq $item.MetadataPath)) { $resolved.Add($item) }
    }
    return @($resolved)
}

function Get-PackwizStateSnapshot {
    param([Parameter(Mandatory)]$Project)

    $paths = [System.Collections.Generic.List[string]]::new()
    foreach ($name in @('pack.toml', 'index.toml')) {
        $path = Join-Path $Project.Root $name
        if (Test-Path -LiteralPath $path -PathType Leaf) { $paths.Add($path) }
    }
    Get-ChildItem -LiteralPath $Project.Root -Filter '*.pw.toml' -File -Recurse -ErrorAction SilentlyContinue |
        ForEach-Object { $paths.Add($_.FullName) }

    $files = @{}
    foreach ($path in $paths) {
        $relative = [System.IO.Path]::GetRelativePath($Project.Root, $path)
        $files[$relative] = [System.IO.File]::ReadAllBytes($path)
    }
    return $files
}

function Restore-PackwizStateSnapshot {
    param(
        [Parameter(Mandatory)]$Project,
        [Parameter(Mandatory)][hashtable]$Snapshot
    )

    foreach ($file in Get-ChildItem -LiteralPath $Project.Root -Filter '*.pw.toml' -File -Recurse -ErrorAction SilentlyContinue) {
        $relative = [System.IO.Path]::GetRelativePath($Project.Root, $file.FullName)
        if (-not $Snapshot.ContainsKey($relative)) { Remove-Item -LiteralPath $file.FullName -Force }
    }
    foreach ($relative in $Snapshot.Keys) {
        $path = Join-Path $Project.Root $relative
        [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($path)) | Out-Null
        [System.IO.File]::WriteAllBytes($path, $Snapshot[$relative])
    }
}

function Update-ModpackContent {
    param(
        [Parameter(Mandatory)]$Project,
        [string[]]$Selectors = @(),
        [switch]$All,
        [string]$Type = 'all'
    )

    Assert-ModpackStructure -Project $Project
    if ($All -and $Selectors.Count) { throw "Use either content selectors or '--all', not both." }
    if (-not $All -and $Selectors.Count -eq 0) { throw 'At least one content selector or --all is required.' }
    $normalizedType = Resolve-InventoryType -Type $Type

    $beforeInventory = Get-ModpackInventory -Project $Project
    $targets = if ($All) {
        @(Get-ModpackUpdateItems -Inventory $beforeInventory -Type $normalizedType |
            Where-Object { $_.Source -eq 'packwiz' -and $_.MetadataPath } |
            Sort-Object Kind, Name)
    }
    else {
        @(Resolve-ModpackUpdateSelectors -Project $Project -Selectors $Selectors -Type $normalizedType)
    }
    if ($targets.Count -eq 0) { throw "Project '$($Project.Id)' has no matching Packwiz-managed content to update." }

    $snapshot = Get-PackwizStateSnapshot -Project $Project
    $log = [System.Collections.Generic.List[string]]::new()
    try {
        if ($All -and $normalizedType -eq 'all') {
            foreach ($line in @(Invoke-Packwiz -Arguments @('update', '--all', '--yes') -WorkingDirectory $Project.Root)) { $log.Add($line) }
        }
        else {
            foreach ($target in $targets) {
                $stem = [System.IO.Path]::GetFileName($target.MetadataPath) -replace '\.pw\.toml$', ''
                foreach ($line in @(Invoke-Packwiz -Arguments @('update', $stem, '--yes') -WorkingDirectory $Project.Root)) { $log.Add($line) }
            }
        }

        $afterInventory = Get-ModpackInventory -Project $Project
        $afterItems = @(Get-ModpackUpdateItems -Inventory $afterInventory)
        $results = @(
            foreach ($target in $targets) {
                $updated = $afterItems | Where-Object MetadataPath -eq $target.MetadataPath | Select-Object -First 1
                if (-not $updated) { throw "Updated metadata '$($target.MetadataPath)' could not be normalized." }
                $relative = [System.IO.Path]::GetRelativePath($Project.Root, $target.MetadataPath)
                $beforeBytes = $snapshot[$relative]
                $afterBytes = [System.IO.File]::ReadAllBytes($target.MetadataPath)
                $changed = -not [System.Linq.Enumerable]::SequenceEqual([byte[]]$beforeBytes, [byte[]]$afterBytes)
                [pscustomobject]@{
                    Id             = $updated.Id
                    Kind           = $updated.Kind
                    Name           = $updated.Name
                    PreviousFile   = $target.Filename
                    Filename       = $updated.Filename
                    Changed        = $changed
                    MetadataPath   = $updated.MetadataPath
                }
            }
        )
        return [pscustomobject]@{ Project = $Project; Items = $results; Log = @($log) }
    }
    catch {
        Restore-PackwizStateSnapshot -Project $Project -Snapshot $snapshot
        throw "No content was updated because the operation failed and Packwiz state was restored. $($_.Exception.Message)"
    }
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
