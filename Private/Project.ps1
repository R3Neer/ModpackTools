function Read-ModpackProject {
    param([Parameter(Mandatory)][string]$ProjectRoot)

    $root = [System.IO.Path]::GetFullPath($ProjectRoot)
    $projectPath = Join-Path $root '.modpack/project.psd1'
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "The project descriptor does not exist: $projectPath"
    }

    try {
        $descriptor = Import-PowerShellDataFile -LiteralPath $projectPath
    }
    catch {
        throw "Project descriptor '$projectPath' is invalid: $($_.Exception.Message)"
    }

    foreach ($required in @('SchemaVersion', 'Id')) {
        if (-not $descriptor.ContainsKey($required)) {
            throw "'$required' is missing from $projectPath"
        }
    }
    if ([int]$descriptor.SchemaVersion -ne 1) {
        throw "Unsupported SchemaVersion in '$projectPath': $($descriptor.SchemaVersion)"
    }
    if ([string]$descriptor.Id -notmatch '^[a-z][a-z0-9-]*$') {
        throw "Invalid Id '$($descriptor.Id)' in '$projectPath'. Use lowercase letters, numbers, and hyphens."
    }

    $pack = Get-PackTomlData -Path (Join-Path $root 'pack.toml')
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
        throw "Duplicate project IDs were found: $($details -join '; ')"
    }

    return @($projects | Sort-Object Id)
}

function Resolve-ModpackProject {
    param([AllowNull()][AllowEmptyString()][string]$Id)

    $effectiveId = if (-not [string]::IsNullOrWhiteSpace($Id)) { $Id } else { $script:ActiveProjectId }
    if ([string]::IsNullOrWhiteSpace($effectiveId)) {
        throw "There is no active project. Run 'modpack use <id>' or provide '--project <id>'."
    }

    $matches = @(Get-ModpackProjects | Where-Object Id -eq $effectiveId)
    if ($matches.Count -eq 0) {
        throw "No modpack with Id '$effectiveId' exists. Run 'modpack list'."
    }
    return $matches[0]
}

function Assert-ModpackStructure {
    param([Parameter(Mandatory)]$Project)

    foreach ($relative in @('pack.toml', 'index.toml', '.modpack/project.psd1', '.modpack/metadata.psd1')) {
        $path = Join-Path $Project.Root $relative
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Incomplete project structure: '$relative' is missing from '$($Project.Root)'."
        }
    }
}

function Set-ActiveModpackProject {
    param([Parameter(Mandatory)][string]$Id)
    $project = Resolve-ModpackProject -Id $Id
    $script:ActiveProjectId = $project.Id
    return $project
}
