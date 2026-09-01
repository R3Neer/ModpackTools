function Invoke-NativeCommandChecked {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$WorkingDirectory
    )

    try {
        $command = Get-Command $FilePath -ErrorAction Stop
    } catch {
        Throw-MpError -Message "Required command '$FilePath' is not available" -Details $_.Exception.Message -Hint "install '$FilePath' and ensure it is available in PATH" -ErrorId 'Build.CommandNotFound' -Category ObjectNotFound -TargetObject $FilePath
    }
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
        if (-not $process.Start()) { Throw-MpError -Message "Command '$FilePath' could not be started" -Hint 'verify the executable and working directory' -ErrorId 'Build.ProcessStartFailed' -Category ResourceUnavailable -TargetObject $FilePath }
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
            $details = if ($lines.Count) { $lines -join [Environment]::NewLine } else { $null }
            Throw-MpError -Message "Command '$FilePath $($Arguments -join ' ')' exited with code $($process.ExitCode)" -Details $details -Hint 'review the command output and retry' -ErrorId 'Build.ProcessFailed' -Category OperationStopped -TargetObject $FilePath
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
    return @(Invoke-NativeCommandChecked -FilePath (Get-MpPackwizExecutable) -Arguments $Arguments -WorkingDirectory $WorkingDirectory)
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
modpack --help
modpack --version
modpack doctor
modpack use {2}
modpack status
modpack inventory
modpack diff
modpack build
```

`modpack use` selects this project only for the current PowerShell process. Every command that operates on an existing project also accepts `--project <id>`, for example `modpack diff --project {2}`. `status`, `inventory`, `build`, and `diff` retain their positional ID shorthand; specifying both forms at once is rejected.

Run `modpack <command> --help` for concise command documentation. For example, `modpack inventory --help` explains every filter and the numbered-reference context.

Run `modpack doctor` to check PowerShell, Packwiz, configuration, project discovery, Git, and the standard Minecraft Java installation. Git and Minecraft are optional; Packwiz and a valid project root are required for the complete workflow.

## Managing content

Search or add Modrinth content. Editorial categories apply only to mods:

```powershell
modpack search <query>
modpack search <query> --type mod
modpack add <search-number>
modpack add <slug>
modpack add <slug> --category <category>
modpack classify list
modpack classify create <id> --name <name>
modpack classify set <name|id|filename> <category|number|unclassified>
modpack classify remove <category|number> [--unclassify]
modpack update <name|id|filename...>
modpack update --all
modpack update --all --type mod
```

Inspect or filter the current contents:

```powershell
modpack inventory --type mod
modpack inventory --category <category|number>
modpack inventory --type resourcepack --state inactive
modpack inventory --search <text>
modpack update <inventory-number>
```

Every displayed inventory item has one global reference number for that filtered view. Use it with `modpack resource`, as the mod argument of `modpack classify set`, or with `modpack update`. References are bound to this project and expire after 24 hours.

Enable or reposition a resource pack. Position `1` is the highest priority in the Minecraft GUI:

```powershell
modpack resource enable <name|id|filename> --position <n>
modpack resource move <name|id|filename> --position <n>
modpack resource disable <name|id|filename>
```

## Build workflow

```powershell
modpack diff
modpack build
```

`modpack diff` compares the current project with the newest `.mrpack` in `dist/`. `modpack build` refreshes Packwiz metadata and writes the generated artifact to `dist/`.

`modpack search` queries compatible Modrinth projects and saves the numbered results for 24 hours. `modpack add <number>` installs from that saved list; IDs and slugs remain valid directly. The cache is only a convenience reference and Packwiz remains the technical source of truth.

Search, inventory, and category numbers have separate contexts. `modpack add <number>` uses the latest search; `resource`, `update`, and the mod argument of `classify set` use the latest inventory; the category argument of `classify set` and `classify remove` use the latest `classify list`. The argument position makes the intended list unambiguous.

`modpack classify` defines, lists, removes, and assigns editorial categories. Its final `unclassified` list row can be assigned but never removed. Removing a defined category that is in use requires `--unclassify`. Category definitions and assignments live only in `.modpack/metadata.psd1`; numbered lists are temporary references. Resource pack activation and ordering require Default Options; `modpack resource move` reorders an enabled pack and `disable` removes it from the enabled order without uninstalling it.

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

    if ($Id -notmatch '^[a-z][a-z0-9-]*$') { Throw-MpError -Message "Project ID '$Id' is invalid; allowed characters: lowercase letters, numbers, hyphens" -Hint 'choose an ID such as my-pack' -ErrorId 'Project.InvalidId' -Category InvalidArgument -TargetObject $Id }
    if ($Loader.ToLowerInvariant() -ne 'fabric') { Throw-MpError -Message "Loader '$Loader' is not supported by project creation; allowed value: fabric" -Hint '--loader fabric' -ErrorId 'Project.UnsupportedLoader' -Category InvalidArgument -TargetObject $Loader }
    $root = Get-ModpackRoot
    if ((Get-ModpackProjects | Where-Object Id -eq $Id)) { Throw-MpError -Message "Project ID '$Id' is already registered" -Hint 'choose a different project ID or run modpack use <id>' -ErrorId 'Project.AlreadyExists' -Category ResourceExists -TargetObject $Id }
    if (-not $DirectoryName) { $DirectoryName = (($Name -replace '[\\/:*?"<>|]', '-').Trim() + "-$MinecraftVersion") }
    if ([System.IO.Path]::IsPathRooted($DirectoryName)) {
        $target = [System.IO.Path]::GetFullPath($DirectoryName)
    } else {
        $target = [System.IO.Path]::GetFullPath((Join-Path $root $DirectoryName))
    }
    if ((Split-Path -Parent $target) -ne $root.TrimEnd('\')) {
        Throw-MpError -Message "Project destination '$target' is not a direct child of configured root '$root'" -Hint 'choose a directory directly below the configured root' -ErrorId 'Project.DestinationOutsideRoot' -Category InvalidArgument -TargetObject $target
    }
    if (Test-Path -LiteralPath $target) { Throw-MpError -Message "Project destination '$target' already exists and will not be overwritten" -Hint 'choose a different --path or remove the existing directory deliberately' -ErrorId 'Project.DestinationExists' -Category ResourceExists -TargetObject $target }

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

function Get-PackwizContentItemByMetadataPath {
    param(
        [Parameter(Mandatory)]$Project,
        [Parameter(Mandatory)][string]$MetadataPath
    )

    $relative = [System.IO.Path]::GetRelativePath($Project.Root, $MetadataPath).Replace('\', '/')
    $definition = switch -Regex ($relative) {
        '^mods/'          { @{ Directory = 'mods'; Kind = 'mod' }; break }
        '^resourcepacks/' { @{ Directory = 'resourcepacks'; Kind = 'resourcepack' }; break }
        '^shaderpacks/'   { @{ Directory = 'shaderpacks'; Kind = 'shaderpack' }; break }
        default { Throw-MpError -Message "Packwiz metadata location '$relative' is not supported" -Hint 'place metadata under mods, resourcepacks, or shaderpacks' -ErrorId 'Build.UnsupportedMetadataLocation' -Category InvalidData -TargetObject $relative }
    }
    return Get-PackwizItems -Project $Project -Directory $definition.Directory -Kind $definition.Kind |
        Where-Object MetadataPath -eq $MetadataPath |
        Select-Object -First 1
}

function Add-ModpackContent {
    param(
        [Parameter(Mandatory)]$Project,
        [Parameter(Mandatory)][string]$Selector,
        [string]$ModrinthProjectId,
        [string]$Category
    )

    if ($Category) {
        $metadata = Get-ModpackMetadata -Project $Project
        if (-not $metadata.Categories.ContainsKey($Category)) {
            Throw-MpError -Message "Category '$Category' is not defined for project '$($Project.Id)'" -Hint 'choose a category shown by modpack inventory --type mod' -ErrorId 'Metadata.UnknownCategory' -Category InvalidArgument -TargetObject $Category
        }
    }
    $snapshot = Get-PackwizStateSnapshot -Project $Project
    try {
        $arguments = if ($ModrinthProjectId) {
            @('modrinth', 'add', '--project-id', $ModrinthProjectId, '--yes')
        }
        else {
            @('modrinth', 'add', $Selector, '--yes')
        }
        $log = @(Invoke-Packwiz -Arguments $arguments -WorkingDirectory $Project.Root)
        $candidates = @(
            Get-ChildItem -LiteralPath $Project.Root -Filter '*.pw.toml' -File -Recurse -ErrorAction SilentlyContinue |
                Where-Object {
                    $relative = [System.IO.Path]::GetRelativePath($Project.Root, $_.FullName)
                    if (-not $snapshot.ContainsKey($relative)) { return $true }
                    $current = [System.IO.File]::ReadAllBytes($_.FullName)
                    return -not [System.Linq.Enumerable]::SequenceEqual([byte[]]$snapshot[$relative], [byte[]]$current)
                } |
                Sort-Object LastWriteTimeUtc -Descending
        )
        if ($candidates.Count -eq 0) { Throw-MpError -Message 'Packwiz completed, but the added metadata file could not be identified' -Hint 'inspect the Packwiz metadata before retrying' -ErrorId 'Content.AddedMetadataNotFound' -Category InvalidResult }
        $items = @($candidates | ForEach-Object { Get-PackwizContentItemByMetadataPath -Project $Project -MetadataPath $_.FullName })
        $item = if ($ModrinthProjectId) {
            $items | Where-Object Id -eq "modrinth:$ModrinthProjectId" | Select-Object -First 1
        }
        else { $items | Select-Object -First 1 }
        if (-not $item) { Throw-MpError -Message 'The installed Modrinth project could not be normalized' -Hint 'inspect the generated .pw.toml file before retrying' -ErrorId 'Content.NormalizationFailed' -Category InvalidResult }
        if ($Category -and $item.Kind -ne 'mod') {
            Throw-MpError -Message "Option '--category' applies only to mods; '$($item.Name)' is '$($item.Kind)'" -Hint 'remove --category' -ErrorId 'Option.CategoryRequiresMod' -Category InvalidArgument -TargetObject $item.Id
        }
        if ($Category) { Set-ModMetadataCategory -Project $Project -ModId $item.Id -Category $Category }
        return [pscustomobject]@{ Item = $item; Log = $log; RelatedItems = $items }
    }
    catch {
        Restore-PackwizStateSnapshot -Project $Project -Snapshot $snapshot
        $reason = ($_.Exception.Message -split "`r?`n")[0]
        Throw-MpError -Message 'Content was not added; all Packwiz changes were restored' -Details $reason -Hint 'review the details and retry' -ErrorId 'Content.AddRolledBack' -Category OperationStopped -TargetObject $Selector
    }
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
            Throw-MpError -Message "Content '$selector' was not found in project '$($Project.Id)'" -Hint 'modpack inventory' -ErrorId 'Content.NotFound' -Category ObjectNotFound -TargetObject $selector
        }
        if ($matches.Count -gt 1) {
            $ids = @($matches | ForEach-Object { "$($_.Kind):$($_.Id)" } | Sort-Object -Unique) -join ', '
            Throw-MpError -Message "Content selector '$selector' matches more than one item" -Details "Matching entries: $ids" -Hint 'add --type <mod|resourcepack|shaderpack>' -ErrorId 'Content.AmbiguousSelector' -Category InvalidArgument -TargetObject $selector
        }
        $item = $matches[0]
        if ($item.Source -ne 'packwiz' -or -not $item.MetadataPath) {
            Throw-MpError -Message "Content '$selector' is local and cannot be updated by Packwiz" -Hint 'replace the local file manually or select Packwiz-managed content' -ErrorId 'Content.LocalNotUpdatable' -Category InvalidOperation -TargetObject $selector
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
    if ($All -and $Selectors.Count) { Throw-MpError -Message "Content selectors and '--all' cannot be combined" -Hint 'remove the selectors or --all' -ErrorId 'Option.ForbiddenCombination' -Category InvalidArgument }
    if (-not $All -and $Selectors.Count -eq 0) { Throw-MpError -Message "The update operation requires at least one selector or '--all'" -Hint 'modpack update --help' -ErrorId 'Command.MissingUpdateTarget' -Category InvalidArgument }
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
    if ($targets.Count -eq 0) { Throw-MpError -Message "Project '$($Project.Id)' has no matching Packwiz-managed content to update" -Hint 'modpack inventory' -ErrorId 'Content.NoUpdateTargets' -Category ObjectNotFound -TargetObject $Project.Id }

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
                if (-not $updated) { Throw-MpError -Message "Updated metadata '$($target.MetadataPath)' could not be normalized" -Hint 'inspect the .pw.toml file before retrying' -ErrorId 'Content.NormalizationFailed' -Category InvalidResult -TargetObject $target.MetadataPath }
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
        $reason = ($_.Exception.Message -split "`r?`n")[0]
        Throw-MpError -Message 'Content was not updated; all Packwiz changes were restored' -Details $reason -Hint 'review the details and retry' -ErrorId 'Content.UpdateRolledBack' -Category OperationStopped -TargetObject $Project.Id
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
        if (-not (Test-Path -LiteralPath $temporary -PathType Leaf)) { Throw-MpError -Message 'Packwiz did not generate the expected build artifact' -Hint 'modpack build --raw-log' -ErrorId 'Build.ArtifactMissing' -Category InvalidResult -TargetObject $temporary }
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
