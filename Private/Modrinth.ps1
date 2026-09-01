function Get-ModrinthSearchCachePath {
    Join-Path (Get-ModpackToolsConfigDirectory) 'last-search.json'
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
        throw "Modrinth search failed: $($_.Exception.Message)"
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
        throw "Search cache '$path' is invalid. Run modpack search again."
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
    if (-not $cache) { throw "There is no saved search. Run modpack search before using result number $Selector." }
    $created = [datetimeoffset]::MinValue
    $validDate = if ($cache.CreatedUtc -is [datetime]) {
        $created = [datetimeoffset]$cache.CreatedUtc
        $true
    }
    else {
        [datetimeoffset]::TryParse(
            [string]$cache.CreatedUtc,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::RoundtripKind,
            [ref]$created
        )
    }
    if (-not $validDate -or ([datetimeoffset]::UtcNow - $created.ToUniversalTime()).TotalHours -gt 24) {
        throw 'The saved search has expired. Run modpack search again.'
    }
    if (-not ([string]$cache.ProjectId).Equals($Project.Id, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "The saved search belongs to project '$($cache.ProjectId)', not '$($Project.Id)'. Run modpack search for this project."
    }
    if (-not ([string]$cache.MinecraftVersion).Equals([string]$Project.MinecraftVersion, [System.StringComparison]::OrdinalIgnoreCase) -or
        -not ([string]$cache.Loader).Equals([string]$Project.Loader, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'The project compatibility settings changed after the saved search. Run modpack search again.'
    }
    $result = @($cache.Results | Where-Object { [int]$_.Index -eq [int]$Selector })
    if ($result.Count -ne 1) {
        $maximum = @($cache.Results).Count
        throw "Search result number $Selector does not exist. The saved search contains $maximum result(s)."
    }
    return $result[0]
}
