function Get-ModrinthSearchCachePath {
    Join-Path (Get-ModpackToolsConfigDirectory) 'last-search.json'
}

function Get-ModrinthVersionCachePath {
    Join-Path (Get-ModpackToolsConfigDirectory) 'last-versions.json'
}

function Invoke-ModrinthApiRequest {
    param([Parameter(Mandatory)][string]$PathAndQuery, [string]$FailureLabel = 'request')
    $uri = "https://api.modrinth.com/v2/$PathAndQuery"
    $headers = @{ 'User-Agent' = "R3Neer-ModpackTools/$script:ModuleVersion" }
    try { return Invoke-RestMethod -Method Get -Uri $uri -Headers $headers -ErrorAction Stop }
    catch {
        Throw-MpError -Message "Modrinth $FailureLabel could not be completed" -Details $_.Exception.Message -Hint 'check the network connection and retry' -ErrorId 'Versions.RequestFailed' -Category ConnectionError
    }
}

function Get-ModrinthProjectIdFromItem {
    param([Parameter(Mandatory)]$Item)
    if (-not ([string]$Item.Id).StartsWith('modrinth:', [System.StringComparison]::OrdinalIgnoreCase)) {
        Throw-MpError -Message "Content '$($Item.Name)' does not use Modrinth version metadata" -Hint 'version selection currently requires Modrinth-managed content' -ErrorId 'Versions.UnsupportedProvider' -Category NotImplemented -TargetObject $Item.Id
    }
    return ([string]$Item.Id).Substring(9)
}

function Get-ModrinthCompatibleVersions {
    param([Parameter(Mandatory)]$Project, [Parameter(Mandatory)]$Item)

    $projectId = Get-ModrinthProjectIdFromItem -Item $Item
    $query = [System.Collections.Generic.List[string]]::new()
    $gameVersions = ConvertTo-Json -Compress -InputObject @([string]$Project.MinecraftVersion)
    $query.Add('game_versions=' + [System.Uri]::EscapeDataString($gameVersions))
    if ($Item.Kind -eq 'mod' -and $Project.Loader) {
        $loaders = ConvertTo-Json -Compress -InputObject @(([string]$Project.Loader).ToLowerInvariant())
        $query.Add('loaders=' + [System.Uri]::EscapeDataString($loaders))
    }
    elseif ($Item.Kind -eq 'resourcepack') {
        $loaders = ConvertTo-Json -Compress -InputObject @('minecraft')
        $query.Add('loaders=' + [System.Uri]::EscapeDataString($loaders))
    }
    $query.Add('include_changelog=false')
    $response = @(Invoke-ModrinthApiRequest -PathAndQuery ("project/$projectId/version?" + ($query -join '&')) -FailureLabel 'version lookup')
    $versions = @(
        $index = 0
        foreach ($version in $response) {
            $index++
            $primary = @($version.files | Where-Object primary | Select-Object -First 1)
            if (-not $primary.Count) { $primary = @($version.files | Select-Object -First 1) }
            [pscustomobject]@{
                Index         = $index
                Id            = [string]$version.id
                ProjectId     = [string]$version.project_id
                Name          = [string]$version.name
                VersionNumber = [string]$version.version_number
                VersionType   = [string]$version.version_type
                Published     = [string]$version.date_published
                Filename      = $(if ($primary.Count) { [string]$primary[0].filename } else { $null })
                Loaders       = @($version.loaders | ForEach-Object { [string]$_ })
                GameVersions  = @($version.game_versions | ForEach-Object { [string]$_ })
                Dependencies  = @($version.dependencies)
                Installed     = ([string]$version.id).Equals([string]$Item.VersionId, [System.StringComparison]::OrdinalIgnoreCase)
            }
        }
    )
    $view = [pscustomobject]@{
        SchemaVersion = 1; CreatedUtc = [datetime]::UtcNow.ToString('o'); ProjectId = $Project.Id
        MinecraftVersion = $Project.MinecraftVersion; Loader = $Project.Loader; ItemId = $Item.Id
        ItemName = $Item.Name; ItemKind = $Item.Kind; InstalledVersionId = $Item.VersionId; Versions = $versions
    }
    $path = Get-ModrinthVersionCachePath
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($path)) | Out-Null
    Write-Utf8TextFileAtomic -Path $path -Text ($view | ConvertTo-Json -Depth 10)
    return $view
}

function Read-ModrinthVersionCache {
    $path = Get-ModrinthVersionCachePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    try { return Get-Content -Raw -LiteralPath $path -Encoding UTF8 | ConvertFrom-Json }
    catch { Throw-MpError -Message "Version cache '$path' is invalid" -Hint 'modpack versions <content>' -ErrorId 'Versions.InvalidCache' -Category InvalidData -TargetObject $path }
}

function Resolve-ModrinthVersionChoice {
    param(
        [Parameter(Mandatory)][string]$Selector,
        [Parameter(Mandatory)]$Project,
        [Parameter(Mandatory)]$Item,
        $VersionView
    )
    $view = if ($VersionView) { $VersionView } else { Read-ModrinthVersionCache }
    if (-not $view) { Throw-MpError -Message "Version '$Selector' cannot be resolved because no version list has been saved" -Hint "modpack versions $($Item.Id)" -ErrorId 'Versions.CacheNotFound' -Category ObjectNotFound -TargetObject $Selector }
    if (-not (Test-MpCacheTimestamp -CreatedUtc $view.CreatedUtc)) { Throw-MpError -Message 'The saved version list has expired' -Hint "modpack versions $($Item.Id)" -ErrorId 'Versions.CacheExpired' -Category InvalidData }
    if (-not ([string]$view.ProjectId).Equals($Project.Id, [System.StringComparison]::OrdinalIgnoreCase) -or -not ([string]$view.ItemId).Equals($Item.Id, [System.StringComparison]::OrdinalIgnoreCase)) {
        Throw-MpError -Message "The saved version list belongs to '$($view.ItemName)' in project '$($view.ProjectId)'" -Hint "modpack versions $($Item.Id) --project $($Project.Id)" -ErrorId 'Versions.CacheContextMismatch' -Category InvalidData -TargetObject $Selector
    }
    $matches = if ($Selector -match '^[1-9][0-9]*$') {
        @($view.Versions | Where-Object { [int]$_.Index -eq [int]$Selector })
    }
    else {
        @($view.Versions | Where-Object { ([string]$_.Id).Equals($Selector, [System.StringComparison]::OrdinalIgnoreCase) -or ([string]$_.VersionNumber).Equals($Selector, [System.StringComparison]::OrdinalIgnoreCase) })
    }
    if ($matches.Count -eq 0) { Throw-MpError -Message "Compatible version '$Selector' was not found for '$($Item.Name)'" -Hint "modpack versions $($Item.Id)" -ErrorId 'Versions.NotFound' -Category ObjectNotFound -TargetObject $Selector }
    if ($matches.Count -gt 1) { Throw-MpError -Message "Version number '$Selector' identifies more than one Modrinth release" -Details (@($matches | ForEach-Object Id) -join ', ') -Hint 'use the exact version ID or a numbered result' -ErrorId 'Versions.Ambiguous' -Category InvalidArgument -TargetObject $Selector }
    return $matches[0]
}

function ConvertTo-ModrinthProjectType {
    param([string]$Type = 'all')
    $normalized = Resolve-InventoryType -Type $Type
    if ($normalized -eq 'shaderpack') { return 'shader' }
    return $normalized
}

function Invoke-ModrinthSearchRequest {
    param(
        [Parameter(Mandatory)][string]$Query,
        [Parameter(Mandatory)]$Project,
        [string]$Type = 'all',
        [ValidateRange(1, 50)][int]$Limit = 10
    )

    $apiType = ConvertTo-ModrinthProjectType -Type $Type
    $facetGroups = [System.Collections.Generic.List[object]]::new()
    $facetGroups.Add([string[]]@("versions:$($Project.MinecraftVersion)"))
    if ($apiType -eq 'all') {
        $facetGroups.Add([string[]]@('project_type:mod', 'project_type:resourcepack', 'project_type:shader'))
    }
    else {
        $facetGroups.Add([string[]]@("project_type:$apiType"))
    }
    if ($apiType -eq 'mod' -and $Project.Loader) {
        $facetGroups.Add([string[]]@("categories:$($Project.Loader.ToLowerInvariant())"))
    }

    $facets = ConvertTo-Json -InputObject $facetGroups.ToArray() -Compress -Depth 5
    $uri = 'https://api.modrinth.com/v2/search?query={0}&facets={1}&index=relevance&limit={2}' -f @(
        [System.Uri]::EscapeDataString($Query),
        [System.Uri]::EscapeDataString($facets),
        $Limit
    )
    $headers = @{ 'User-Agent' = "R3Neer-ModpackTools/$script:ModuleVersion" }
    try {
        return Invoke-RestMethod -Method Get -Uri $uri -Headers $headers -ErrorAction Stop
    }
    catch {
        Throw-MpError -Message 'Modrinth search could not be completed' -Details $_.Exception.Message -Hint 'check the network connection and retry' -ErrorId 'Search.RequestFailed' -Category ConnectionError
    }
}

function Write-ModrinthSearchCache {
    param([Parameter(Mandatory)]$Search)
    $path = Get-ModrinthSearchCachePath
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($path)) | Out-Null
    $json = $Search | ConvertTo-Json -Depth 6
    Write-Utf8TextFileAtomic -Path $path -Text $json
    return $path
}

function Read-ModrinthSearchCache {
    $path = Get-ModrinthSearchCachePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    try {
        return Get-Content -Raw -LiteralPath $path -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        Throw-MpError -Message "Search cache '$path' is invalid" -Hint 'modpack search <query>' -ErrorId 'Search.InvalidCache' -Category InvalidData -TargetObject $path
    }
}

function Search-ModrinthContent {
    param(
        [Parameter(Mandatory)]$Project,
        [Parameter(Mandatory)][string]$Query,
        [string]$Type = 'all',
        [ValidateRange(1, 50)][int]$Limit = 10
    )

    $normalizedType = Resolve-InventoryType -Type $Type
    $response = Invoke-ModrinthSearchRequest -Query $Query -Project $Project -Type $normalizedType -Limit $Limit
    $results = @(
        $index = 0
        foreach ($hit in @($response.hits)) {
            $index++
            $kind = if ([string]$hit.project_type -eq 'shader') { 'shaderpack' } else { [string]$hit.project_type }
            [pscustomobject]@{
                Index       = $index
                ProjectId   = [string]$hit.project_id
                Slug        = [string]$hit.slug
                Type        = $kind
                Title       = [string]$hit.title
                Author      = [string]$hit.author
                Description = [string]$hit.description
                Downloads   = [long]$hit.downloads
            }
        }
    )
    $search = [pscustomobject]@{
        SchemaVersion    = 1
        CreatedUtc       = [datetime]::UtcNow.ToString('o')
        Query            = $Query
        ProjectId        = $Project.Id
        MinecraftVersion = $Project.MinecraftVersion
        Loader           = $Project.Loader
        Type             = $normalizedType
        TotalHits        = [long]$response.total_hits
        Results          = $results
    }
    [void](Write-ModrinthSearchCache -Search $search)
    return $search
}

function Resolve-ModrinthSearchNumber {
    param(
        [Parameter(Mandatory)][string]$Selector,
        [Parameter(Mandatory)]$Project
    )

    if ($Selector -notmatch '^[1-9][0-9]*$') { return $null }
    $cache = Read-ModrinthSearchCache
    if (-not $cache) { Throw-MpError -Message "Search result number '$Selector' cannot be resolved because there is no saved search" -Hint 'modpack search <query>' -ErrorId 'Search.CacheNotFound' -Category ObjectNotFound -TargetObject $Selector }
    if (-not (Test-MpCacheTimestamp -CreatedUtc $cache.CreatedUtc)) {
        Throw-MpError -Message 'The saved search has expired' -Hint 'modpack search <query>' -ErrorId 'Search.CacheExpired' -Category InvalidData
    }
    if (-not ([string]$cache.ProjectId).Equals($Project.Id, [System.StringComparison]::OrdinalIgnoreCase)) {
        Throw-MpError -Message "The saved search belongs to project '$($cache.ProjectId)', not '$($Project.Id)'" -Hint "modpack search <query> --project $($Project.Id)" -ErrorId 'Search.ProjectMismatch' -Category InvalidData -TargetObject $Selector
    }
    if (-not ([string]$cache.MinecraftVersion).Equals([string]$Project.MinecraftVersion, [System.StringComparison]::OrdinalIgnoreCase) -or
        -not ([string]$cache.Loader).Equals([string]$Project.Loader, [System.StringComparison]::OrdinalIgnoreCase)) {
        Throw-MpError -Message 'The project compatibility settings changed after the saved search' -Hint 'modpack search <query>' -ErrorId 'Search.CompatibilityChanged' -Category InvalidData
    }
    $result = @($cache.Results | Where-Object { [int]$_.Index -eq [int]$Selector })
    if ($result.Count -ne 1) {
        $maximum = @($cache.Results).Count
        Throw-MpError -Message "Search result number '$Selector' does not exist; available range: 1-$maximum" -Hint 'choose a number shown by the latest modpack search' -ErrorId 'Search.ResultOutOfRange' -Category InvalidArgument -TargetObject $Selector
    }
    return $result[0]
}
