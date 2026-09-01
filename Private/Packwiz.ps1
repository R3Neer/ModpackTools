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

## Creating or adopting projects

`modpack new` creates Fabric, Quilt, Forge, or NeoForge Packwiz projects. `modpack init` adopts an existing Packwiz project without changing its technical metadata or installed content:

```powershell
modpack new <id> --name <name> --minecraft <version> --loader <fabric|quilt|forge|neoforge>
modpack init <id> --path <existing-packwiz-directory>
```

An initialized project must be a direct child of the configured root. Existing README and `.gitignore` files are preserved.

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
modpack side set <mod|inventory-number> <client|host|both>
modpack versions <name|id|filename>
modpack update <name|id|filename> --to <version|number>
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

Every displayed inventory item has one global reference number for that filtered view. Use it with `modpack resource`, `modpack side`, `modpack versions`, as the mod argument of `modpack classify set`, or with `modpack update`. References are bound to this project and expire after 24 hours.

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

Search, inventory, version, and category numbers have separate contexts. `modpack add <number>` uses the latest search; `resource`, `side`, `versions`, update selectors, and the mod argument of `classify set` use the latest inventory; `update --to <number>` uses the latest compatible-version list; the category argument of `classify set` and `classify remove` use the latest `classify list`. The argument position makes the intended list unambiguous.

`modpack classify` defines, lists, removes, and assigns editorial categories. Its final `unclassified` list row can be assigned but never removed. Removing a defined category that is in use requires `--unclassify`. Category definitions and assignments live only in `.modpack/metadata.psd1`; numbered lists are temporary references. Resource pack activation and ordering require Default Options; `modpack resource move` reorders an enabled pack and `disable` removes it from the enabled order without uninstalling it.

`modpack versions` lists compatible Modrinth releases and saves numbered choices. `modpack update` updates Packwiz-managed mods, resource packs, and shaders; `--to` selects an exact release, including an older one. Every update checks the declared dependency graph first: newly introduced known conflicts block the operation, while `--strict` also requires complete metadata and a clean resulting graph. Multiple selectors form one transaction, and every Packwiz metadata change is rolled back if one fails. Use `--type mod|resourcepack|shaderpack` to narrow the operation. Local files are never updated. Review the result with `modpack diff` before building.

## Sources of truth

- `pack.toml`, its configured index, and `.pw.toml` files own technical Packwiz data.
- `.modpack/project.psd1` owns the short ID and display/build identity.
- `.modpack/metadata.psd1` owns editorial categories, display-name overrides, and explicit side overrides for local JARs.
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

function Write-ModpackRegistrationFiles {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][System.Collections.IDictionary]$Descriptor
    )

    [System.IO.Directory]::CreateDirectory((Join-Path $Root '.modpack')) | Out-Null
    Write-PowerShellDataFileAtomic -Path (Join-Path $Root '.modpack/project.psd1') -Data $Descriptor
    Write-PowerShellDataFileAtomic -Path (Join-Path $Root '.modpack/metadata.psd1') -Data ([ordered]@{
        Categories    = [ordered]@{}
        Mods          = [ordered]@{}
        ResourcePacks = [ordered]@{}
    })
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
    Write-ModpackRegistrationFiles -Root $Root -Descriptor ([ordered]@{
        SchemaVersion  = 1
        Id             = $Id
        DisplayName    = $DisplayName
        DisplayVersion = $DisplayVersion
        OutputName     = $outputName
    })

    $project = Read-ModpackProject -ProjectRoot $Root
    [void](Write-ModpackProjectReadme -Project $project)
    [System.IO.File]::WriteAllText((Join-Path $Root '.gitignore'), "dist/`n", [System.Text.UTF8Encoding]::new($false))
}

function Initialize-ExistingModpackProject {
    param(
        [Parameter(Mandatory)][string]$Id,
        [string]$Path = (Get-Location).Path,
        [string]$DisplayName,
        [string]$DisplayVersion,
        [string]$OutputName
    )

    if ($Id -notmatch '^[a-z][a-z0-9-]*$') {
        Throw-MpError -Message "Project ID '$Id' is invalid; allowed characters: lowercase letters, numbers, hyphens" -Hint 'choose an ID such as my-pack' -ErrorId 'Project.InvalidId' -Category InvalidArgument -TargetObject $Id
    }
    if ([string]::IsNullOrWhiteSpace($Path)) {
        Throw-MpError -Message 'Packwiz project path cannot be empty' -Hint 'modpack init <id> --path <existing-directory>' -ErrorId 'Project.InvalidPath' -Category InvalidArgument -TargetObject $Path
    }
    $root = Get-ModpackRoot
    $projectRoot = [System.IO.Path]::GetFullPath($Path)
    if (-not (Test-Path -LiteralPath $projectRoot -PathType Container)) {
        Throw-MpError -Message "Packwiz project directory '$projectRoot' does not exist" -Hint 'modpack init <id> --path <existing-directory>' -ErrorId 'Project.PathNotFound' -Category ObjectNotFound -TargetObject $projectRoot
    }
    $parent = [System.IO.Path]::GetFullPath((Split-Path -Parent $projectRoot)).TrimEnd('\', '/')
    $normalizedRoot = $root.TrimEnd('\', '/')
    if (-not $parent.Equals($normalizedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        Throw-MpError -Message "Packwiz project '$projectRoot' is not a direct child of configured root '$root'" -Hint 'move the project directly below the configured root or configure its parent as the root' -ErrorId 'Project.OutsideRoot' -Category InvalidArgument -TargetObject $projectRoot
    }

    $modpackDirectory = Join-Path $projectRoot '.modpack'
    $descriptorPath = Join-Path $modpackDirectory 'project.psd1'
    $metadataPath = Join-Path $modpackDirectory 'metadata.psd1'
    if ((Test-Path -LiteralPath $modpackDirectory) -and -not (Test-Path -LiteralPath $modpackDirectory -PathType Container)) {
        Throw-MpError -Message "Path '$modpackDirectory' exists but is not a directory" -Hint 'move the conflicting path, then retry' -ErrorId 'Project.InitializationConflict' -Category ResourceExists -TargetObject $modpackDirectory
    }
    if (Test-Path -LiteralPath $descriptorPath -PathType Leaf) {
        Throw-MpError -Message "Packwiz project '$projectRoot' is already initialized for ModpackTools" -Hint 'modpack list' -ErrorId 'Project.AlreadyInitialized' -Category ResourceExists -TargetObject $descriptorPath
    }
    if (Test-Path -LiteralPath $modpackDirectory) {
        $conflicts = @(Get-ChildItem -LiteralPath $modpackDirectory -Force -ErrorAction Stop)
        if ($conflicts.Count -gt 0) {
            Throw-MpError -Message "Directory '$modpackDirectory' contains files that ModpackTools will not overwrite" -Details (($conflicts.Name | Sort-Object) -join ', ') -Hint 'review or move the existing .modpack contents, then retry' -ErrorId 'Project.InitializationConflict' -Category ResourceExists -TargetObject $modpackDirectory
        }
    }

    $packPath = Join-Path $projectRoot 'pack.toml'
    if (-not (Test-Path -LiteralPath $packPath -PathType Leaf)) {
        Throw-MpError -Message "Directory '$projectRoot' is not a Packwiz project because 'pack.toml' is missing" -Hint 'run this command from an existing Packwiz project' -ErrorId 'Project.ManifestNotFound' -Category ObjectNotFound -TargetObject $packPath
    }
    $pack = Get-PackTomlData -Path $packPath
    $indexPath = Resolve-PackwizIndexPath -ProjectRoot $projectRoot -IndexFile $pack.IndexFile
    if (-not (Test-Path -LiteralPath $indexPath -PathType Leaf)) {
        Throw-MpError -Message "Packwiz project '$projectRoot' is incomplete because index '$($pack.IndexFile)' is missing" -Hint 'restore the Packwiz index before initializing ModpackTools' -ErrorId 'Project.IndexNotFound' -Category ObjectNotFound -TargetObject $indexPath
    }
    $missingFields = @(
        if ([string]::IsNullOrWhiteSpace([string]$pack.Name)) { 'name' }
        if ([string]::IsNullOrWhiteSpace([string]$pack.Version)) { 'version' }
        if ([string]::IsNullOrWhiteSpace([string]$pack.MinecraftVersion)) { 'versions.minecraft' }
        if ([string]::IsNullOrWhiteSpace([string]$pack.Loader)) { 'versions.<loader>' }
    )
    if ($missingFields.Count -gt 0) {
        Throw-MpError -Message "Packwiz manifest '$packPath' is missing required data" -Details ($missingFields -join ', ') -Hint 'repair pack.toml before initializing ModpackTools' -ErrorId 'Project.InvalidManifest' -Category InvalidData -TargetObject $packPath
    }
    if (@(Get-ModpackProjects | Where-Object Id -eq $Id).Count -gt 0) {
        Throw-MpError -Message "Project ID '$Id' is already registered" -Hint 'choose a different project ID or run modpack use <id>' -ErrorId 'Project.AlreadyExists' -Category ResourceExists -TargetObject $Id
    }
    if ($PSBoundParameters.ContainsKey('DisplayName') -and [string]::IsNullOrWhiteSpace($DisplayName)) {
        Throw-MpError -Message "Display name cannot be empty" -Hint '--display-name <name>' -ErrorId 'Project.InvalidDisplayName' -Category InvalidArgument -TargetObject $DisplayName
    }
    if ($PSBoundParameters.ContainsKey('DisplayVersion') -and [string]::IsNullOrWhiteSpace($DisplayVersion)) {
        Throw-MpError -Message "Display version cannot be empty" -Hint '--display-version <version>' -ErrorId 'Project.InvalidDisplayVersion' -Category InvalidArgument -TargetObject $DisplayVersion
    }
    if ($PSBoundParameters.ContainsKey('OutputName')) {
        $invalidFileName = [System.IO.Path]::GetInvalidFileNameChars() | Where-Object { $OutputName.Contains([string]$_) } | Select-Object -First 1
        if ([string]::IsNullOrWhiteSpace($OutputName) -or $invalidFileName -or [System.IO.Path]::GetFileName($OutputName) -ne $OutputName -or -not $OutputName.EndsWith('.mrpack', [System.StringComparison]::OrdinalIgnoreCase)) {
            Throw-MpError -Message "Output name '$OutputName' must be a valid .mrpack filename" -Hint '--output-name <filename.mrpack>' -ErrorId 'Project.InvalidOutputName' -Category InvalidArgument -TargetObject $OutputName
        }
    }

    $descriptor = [ordered]@{ SchemaVersion = 1; Id = $Id }
    if ($PSBoundParameters.ContainsKey('DisplayName')) { $descriptor.DisplayName = $DisplayName }
    if ($PSBoundParameters.ContainsKey('DisplayVersion')) { $descriptor.DisplayVersion = $DisplayVersion }
    if ($PSBoundParameters.ContainsKey('OutputName')) { $descriptor.OutputName = $OutputName }

    $createdModpackDirectory = -not (Test-Path -LiteralPath $modpackDirectory)
    $createdFiles = [System.Collections.Generic.List[string]]::new()
    try {
        Write-ModpackRegistrationFiles -Root $projectRoot -Descriptor $descriptor
        $createdFiles.Add($descriptorPath)
        $createdFiles.Add($metadataPath)
        $project = Read-ModpackProject -ProjectRoot $projectRoot
        Assert-ModpackStructure -Project $project

        $readmePath = Join-Path $projectRoot 'README.md'
        if (-not (Test-Path -LiteralPath $readmePath)) {
            [void](Write-ModpackProjectReadme -Project $project)
            $createdFiles.Add($readmePath)
        }
        $gitIgnorePath = Join-Path $projectRoot '.gitignore'
        if (-not (Test-Path -LiteralPath $gitIgnorePath)) {
            [System.IO.File]::WriteAllText($gitIgnorePath, "dist/`n", [System.Text.UTF8Encoding]::new($false))
            $createdFiles.Add($gitIgnorePath)
        }
        return [pscustomobject]@{ Project = $project; CreatedFiles = @($createdFiles) }
    }
    catch {
        foreach ($createdFile in @($createdFiles | Select-Object -Unique)) {
            if (Test-Path -LiteralPath $createdFile -PathType Leaf) { Remove-Item -LiteralPath $createdFile -Force }
        }
        if (Test-Path -LiteralPath $metadataPath -PathType Leaf) { Remove-Item -LiteralPath $metadataPath -Force }
        if (Test-Path -LiteralPath $descriptorPath -PathType Leaf) { Remove-Item -LiteralPath $descriptorPath -Force }
        if ($createdModpackDirectory -and (Test-Path -LiteralPath $modpackDirectory -PathType Container) -and @(Get-ChildItem -LiteralPath $modpackDirectory -Force).Count -eq 0) {
            Remove-Item -LiteralPath $modpackDirectory -Force
        }
        throw
    }
}

function Get-PackwizInitArguments {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$MinecraftVersion,
        [Parameter(Mandatory)][string]$PackVersion,
        [Parameter(Mandatory)][string]$Loader,
        [string]$LoaderVersion
    )

    $normalizedLoader = $Loader.ToLowerInvariant()
    if ($normalizedLoader -notin @('fabric', 'quilt', 'forge', 'neoforge')) {
        Throw-MpError -Message "Loader '$Loader' is not supported by project creation; allowed values: fabric, quilt, forge, neoforge" -Hint '--loader <fabric|quilt|forge|neoforge>' -ErrorId 'Project.UnsupportedLoader' -Category InvalidArgument -TargetObject $Loader
    }
    $arguments = @('init', '--yes', '--name', $Name, '--mc-version', $MinecraftVersion, '--version', $PackVersion, '--modloader', $normalizedLoader)
    if ($LoaderVersion) { $arguments += @("--$normalizedLoader-version", $LoaderVersion) }
    else { $arguments += "--$normalizedLoader-latest" }
    return $arguments
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
    $initArguments = @(Get-PackwizInitArguments -Name $Name -MinecraftVersion $MinecraftVersion -PackVersion $PackVersion -Loader $Loader -LoaderVersion $LoaderVersion)
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
        [void](Invoke-Packwiz -Arguments $initArguments -WorkingDirectory $temporary)
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
    foreach ($name in @('pack.toml', $Project.IndexFile)) {
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

function Set-ModpackContentExactVersion {
    param(
        [Parameter(Mandatory)]$Project,
        [Parameter(Mandatory)]$Target,
        [Parameter(Mandatory)]$Version
    )
    $projectId = Get-ModrinthProjectIdFromItem -Item $Target
    $originalPath = $Target.MetadataPath
    $originalSide = $Target.Side
    $log = @(Invoke-Packwiz -Arguments @('modrinth', 'add', '--project-id', $projectId, '--version-id', ([string]$Version.Id), '--yes') -WorkingDirectory $Project.Root)
    $matches = @(
        Get-ChildItem -LiteralPath $Project.Root -Filter '*.pw.toml' -File -Recurse -ErrorAction SilentlyContinue | Where-Object {
            $text = Get-Content -Raw -LiteralPath $_.FullName -Encoding UTF8
            (Get-TomlString -Text $text -Section 'update.modrinth' -Key 'mod-id') -eq $projectId
        }
    )
    $selected = @($matches | Where-Object {
        $text = Get-Content -Raw -LiteralPath $_.FullName -Encoding UTF8
        (Get-TomlString -Text $text -Section 'update.modrinth' -Key version) -eq [string]$Version.Id
    })
    if ($selected.Count -ne 1) { Throw-MpError -Message "Packwiz did not produce exactly one metadata file for version '$($Version.Id)'" -Hint 'the complete update was rolled back' -ErrorId 'Versions.NormalizationFailed' -Category InvalidResult -TargetObject $Version.Id }
    foreach ($duplicate in $matches) {
        if (-not $duplicate.FullName.Equals($selected[0].FullName, [System.StringComparison]::OrdinalIgnoreCase)) { Remove-Item -LiteralPath $duplicate.FullName -Force }
    }
    $text = Get-Content -Raw -LiteralPath $selected[0].FullName -Encoding UTF8
    if ($originalSide -in @('client', 'server', 'both') -and (Get-TomlString -Text $text -Key side) -ne $originalSide) {
        $text = Set-TomlString -Text $text -Key side -Value $originalSide
        Write-Utf8TextFileAtomic -Path $selected[0].FullName -Text $text
    }
    $log += @(Invoke-Packwiz -Arguments @('refresh') -WorkingDirectory $Project.Root)
    return [pscustomobject]@{ MetadataPath = $selected[0].FullName; Log = $log; OriginalPath = $originalPath }
}

function Update-ModpackContent {
    param(
        [Parameter(Mandatory)]$Project,
        [string[]]$Selectors = @(),
        [switch]$All,
        [string]$Type = 'all',
        [string]$To,
        [switch]$Strict
    )

    Assert-ModpackStructure -Project $Project
    if ($All -and $Selectors.Count) { Throw-MpError -Message "Content selectors and '--all' cannot be combined" -Hint 'remove the selectors or --all' -ErrorId 'Option.ForbiddenCombination' -Category InvalidArgument }
    if ($To -and ($All -or $Selectors.Count -ne 1)) { Throw-MpError -Message "Option '--to' requires exactly one content selector" -Hint 'modpack update <selector> --to <version>' -ErrorId 'Option.VersionTargetConflict' -Category InvalidArgument -TargetObject $To }
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

    $chosenVersion = $null
    if ($To) {
        $versionView = Get-ModrinthCompatibleVersions -Project $Project -Item $targets[0]
        $chosenVersion = Resolve-ModrinthVersionChoice -Selector $To -Project $Project -Item $targets[0] -VersionView $versionView
    }
    $preflight = Test-ModpackUpdatePreflight -Project $Project -Targets $targets -ExactVersion $chosenVersion -Strict:$Strict
    $snapshot = Get-PackwizStateSnapshot -Project $Project
    $log = [System.Collections.Generic.List[string]]::new()
    try {
        if ($To) {
            $exact = Set-ModpackContentExactVersion -Project $Project -Target $targets[0] -Version $chosenVersion
            foreach ($line in $exact.Log) { $log.Add($line) }
        }
        elseif ($All -and $normalizedType -eq 'all') {
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
                $updated = if ($To) { $afterItems | Where-Object Id -eq $target.Id | Select-Object -First 1 } else { $afterItems | Where-Object MetadataPath -eq $target.MetadataPath | Select-Object -First 1 }
                if (-not $updated) { Throw-MpError -Message "Updated metadata '$($target.MetadataPath)' could not be normalized" -Hint 'inspect the .pw.toml file before retrying' -ErrorId 'Content.NormalizationFailed' -Category InvalidResult -TargetObject $target.MetadataPath }
                $relative = [System.IO.Path]::GetRelativePath($Project.Root, $updated.MetadataPath)
                $changed = if (-not $snapshot.ContainsKey($relative)) { $true } else {
                    -not [System.Linq.Enumerable]::SequenceEqual([byte[]]$snapshot[$relative], [byte[]][System.IO.File]::ReadAllBytes($updated.MetadataPath))
                }
                [pscustomobject]@{
                    Id             = $updated.Id
                    Kind           = $updated.Kind
                    Name           = $updated.Name
                    PreviousFile   = $target.Filename
                    Filename       = $updated.Filename
                    Changed        = $changed
                    MetadataPath   = $updated.MetadataPath
                    VersionId      = $updated.VersionId
                    VersionNumber  = $(if ($chosenVersion) { $chosenVersion.VersionNumber } else { $null })
                }
            }
        )
        return [pscustomobject]@{ Project = $Project; Items = $results; Log = @($log); Preflight = $preflight }
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
