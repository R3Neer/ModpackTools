function Read-ModpackProject {
    param([Parameter(Mandatory)][string]$ProjectRoot)

    $root = [System.IO.Path]::GetFullPath($ProjectRoot)
    $projectPath = Join-Path $root '.modpack/project.psd1'
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        Throw-MpError -Message "Project descriptor '$projectPath' does not exist" -Hint "restore '.modpack/project.psd1' or register a valid project" -ErrorId 'Project.DescriptorNotFound' -Category ObjectNotFound -TargetObject $projectPath
    }

    try {
        $descriptor = Import-PowerShellDataFile -LiteralPath $projectPath
    }
    catch {
        Throw-MpError -Message "Project descriptor '$projectPath' is not a valid PSD1 file" -Details $_.Exception.Message -Hint 'repair the project descriptor' -ErrorId 'Project.InvalidDescriptor' -Category InvalidData -TargetObject $projectPath
    }

    foreach ($required in @('SchemaVersion', 'Id')) {
        if (-not $descriptor.ContainsKey($required)) {
            Throw-MpError -Message "Required field '$required' is missing from project descriptor '$projectPath'" -Hint 'repair the project descriptor' -ErrorId 'Project.MissingField' -Category InvalidData -TargetObject $required
        }
    }
    if ([int]$descriptor.SchemaVersion -ne 1) {
        Throw-MpError -Message "Schema version '$($descriptor.SchemaVersion)' in project descriptor '$projectPath' is not supported" -Hint 'upgrade ModpackTools or migrate the project descriptor' -ErrorId 'Project.UnsupportedSchema' -Category InvalidData -TargetObject $descriptor.SchemaVersion
    }
    if ([string]$descriptor.Id -notmatch '^[a-z][a-z0-9-]*$') {
        Throw-MpError -Message "Project ID '$($descriptor.Id)' in '$projectPath' is invalid; allowed characters: lowercase letters, numbers, hyphens" -Hint 'edit the Id field in .modpack/project.psd1' -ErrorId 'Project.InvalidId' -Category InvalidData -TargetObject $descriptor.Id
    }

    $pack = Get-PackTomlData -Path (Join-Path $root 'pack.toml')
    $indexPath = Resolve-PackwizIndexPath -ProjectRoot $root -IndexFile $pack.IndexFile
    $displayName = if ($descriptor.ContainsKey('DisplayName')) { [string]$descriptor.DisplayName } else { $pack.Name }
    $displayVersion = if ($descriptor.ContainsKey('DisplayVersion')) { [string]$descriptor.DisplayVersion } else { $pack.Version }
    $defaultOutputBase = ($displayName -replace '[\\/:*?"<>|]', '-').Trim()
    if ($displayVersion) { $defaultOutputBase = "$defaultOutputBase-$displayVersion" }
    $outputName = if ($descriptor.ContainsKey('OutputName')) { [string]$descriptor.OutputName } else { "$defaultOutputBase.mrpack" }

    [pscustomobject]@{
        Id               = [string]$descriptor.Id
        Root             = $root
        DescriptorPath   = $projectPath
        DisplayName      = $displayName
        DisplayVersion   = $displayVersion
        OutputName       = $outputName
        TechnicalName    = $pack.Name
        PackVersion      = $pack.Version
        IndexFile        = $pack.IndexFile
        IndexPath        = $indexPath
        MinecraftVersion = $pack.MinecraftVersion
        Loader           = $pack.Loader
        LoaderVersion    = $pack.LoaderVersion
    }
}

function Get-ModpackProjects {
    param([string]$Root = (Get-ModpackRoot))

    $resolvedRoot = [System.IO.Path]::GetFullPath($Root)
    $projects = @(
        Get-ChildItem -LiteralPath $resolvedRoot -Directory -ErrorAction Stop |
            Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName '.modpack/project.psd1') -PathType Leaf } |
            ForEach-Object { Read-ModpackProject -ProjectRoot $_.FullName }
    )

    $duplicates = @($projects | Group-Object Id | Where-Object Count -gt 1)
    if ($duplicates.Count -gt 0) {
        $details = foreach ($duplicate in $duplicates) {
            "'$($duplicate.Name)': " + (($duplicate.Group.Root) -join ', ')
        }
        Throw-MpError -Message 'Multiple registered projects use the same ID' -Details ($details -join '; ') -Hint 'assign a unique Id in each .modpack/project.psd1 file' -ErrorId 'Project.DuplicateId' -Category InvalidData -TargetObject $details
    }

    return @($projects | Sort-Object Id)
}

function Resolve-ModpackProject {
    param([AllowNull()][AllowEmptyString()][string]$Id)

    $effectiveId = if (-not [string]::IsNullOrWhiteSpace($Id)) { $Id } else { $script:ActiveProjectId }
    if ([string]::IsNullOrWhiteSpace($effectiveId)) {
        Throw-MpError -Message 'No active project is selected' -Hint 'modpack use <id>, or add --project <id> to this command' -ErrorId 'Project.NotSelected' -Category ObjectNotFound
    }

    $matches = @(Get-ModpackProjects | Where-Object Id -eq $effectiveId)
    if ($matches.Count -eq 0) {
        Throw-MpError -Message "Project '$effectiveId' is not registered" -Hint 'modpack list' -ErrorId 'Project.NotFound' -Category ObjectNotFound -TargetObject $effectiveId
    }
    return $matches[0]
}

function Assert-ModpackStructure {
    param([Parameter(Mandatory)]$Project)

    foreach ($relative in @('pack.toml', $Project.IndexFile, '.modpack/project.psd1', '.modpack/metadata.psd1')) {
        $path = Join-Path $Project.Root $relative
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            Throw-MpError -Message "Project '$($Project.Id)' is incomplete because '$relative' is missing" -Details "Project root: '$($Project.Root)'" -Hint 'restore the missing file or recreate the project' -ErrorId 'Project.Incomplete' -Category InvalidData -TargetObject (Join-Path $Project.Root $relative)
        }
    }
}

function Set-ActiveModpackProject {
    param([Parameter(Mandatory)][string]$Id)
    $project = Resolve-ModpackProject -Id $Id
    $script:ActiveProjectId = $project.Id
    return $project
}
