function Get-MpProjectCandidates {
    param($Context, [string]$Id)
    if ($Context.Domains.ContainsKey($Id)) { return @($Context.Domains[$Id]) }
    if (-not $Id.StartsWith('modrinth:')) { return @() }
    $projectId = $Id.Substring(9)
    $check = (Get-MpPropertyValue $Context 'Check') -eq $true
    $projectEndpoint = "project/$projectId"
    $info = if ($Context.Info.ContainsKey($Id)) { $Context.Info[$Id] } else { Get-MpMetadataCache $projectEndpoint -Check:$check }
    if (-not $info) { $Context.DomainKnown[$Id] = $false; $Context.Domains[$Id] = @(); return @() }
    $kind = [string]$info.project_type
    if ($kind -notin @('mod','resourcepack','shaderpack')) {
        Throw-MpError -Message "Unsupported content kind '$kind'" -Hint 'select a mod, resource pack or shader pack' -ErrorId 'Content.UnsupportedKind' -Category InvalidArgument
    }
    $game = [Uri]::EscapeDataString((ConvertTo-Json -InputObject @($Context.Project.MinecraftVersion) -Compress))
    $loaders = @(if ($kind -eq 'mod') { $Context.Project.Loader } elseif ($kind -eq 'resourcepack') { 'minecraft' })
    if ($Context.Project.Loader -eq 'quilt' -and $kind -eq 'mod') { $loaders = @('quilt','fabric') }
    $query = "project/$projectId/version?game_versions=$game"
    if ($loaders.Count) { $query += '&loaders=' + [Uri]::EscapeDataString((ConvertTo-Json -InputObject $loaders -Compress)) }
    $cachePath = Get-MpMetadataCachePath $query
    $known = $check -or ([IO.File]::Exists($cachePath) -and [IO.File]::GetLastWriteTimeUtc($cachePath) -gt [datetime]::UtcNow.AddHours(-24))
    $versions = @(Get-MpMetadataCache $query -Check:$check | ForEach-Object { $_ } | Sort-Object @{ Expression = { [datetime]$_.date_published }; Descending = $true },id)
    $Context.Info[$Id] = $info
    $Context.Domains[$Id] = $versions
    $Context.DomainKnown[$Id] = $known
    return $versions
}

function Get-MpCandidateNode {
    param($Context, [string]$Id, $Version)
    $key = "$Id/$($Version.id)"
    if ($Context.Candidates.ContainsKey($key)) { return $Context.Candidates[$key] }
    if ($Context.Before.ContainsKey($Id)) { $item = $Context.Before[$Id].Item; $intent = $Context.Before[$Id].Intent }
    else {
        if (-not $Context.Info.ContainsKey($Id)) { [void](Get-MpProjectCandidates $Context $Id) }
        $info = $Context.Info[$Id]
        $side = if ($info.server_side -eq 'unsupported') { 'client' } elseif ($info.client_side -eq 'unsupported') { 'server' } else { 'both' }
        $item = [pscustomobject]@{ Id = $Id; Name = $info.title; Kind = $info.project_type; Source = 'packwiz'; MetadataPath = $null; Filename = ''; VersionId = ''; Side = $side; Category = 'unclassified' }
        $intent = 'transitive'
    }
    $node = ConvertTo-MpNode -Project $Context.Project -Item $item -Version $Version -Check -Intent $intent
    if (-not $node.File) { Throw-MpError -Message "Version '$($Version.id)' has no installable file" -Hint 'choose another version' -ErrorId 'Versions.MissingFile' -Category InvalidData }
    $cachePath = Join-Path (Get-ModpackToolsConfigDirectory) ('metadata/' + (Get-MpHash ('version/' + $Version.id)) + '.json')
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $cachePath))
    Write-Utf8TextFileAtomic $cachePath ($Version | ConvertTo-Json -Depth 60 -Compress)
    $Context.Candidates[$key] = $node
    return $node
}

function Get-MpRequirementTargets {
    param($Requirement)
    if ($null -eq $Requirement -or $Requirement -is [string]) { return }
    if ($Requirement.Target) { $Requirement }
    foreach ($child in @($Requirement.Any) + @($Requirement.All)) { Get-MpRequirementTargets $child }
    if ($Requirement.Unless) { Get-MpRequirementTargets $Requirement.Unless }
}

function Test-MpDowngrade {
    param($Before, $After)
    $compared = $false
    foreach ($old in $Before.Mods) {
        $new = @($After.Mods | Where-Object Id -eq $old.Id | Select-Object -First 1)
        if (-not $new.Count) { continue }
        $comparison = Compare-MpVersion $new[0].Version $old.Version -Maven:($Before.RawVersion -and @($Before.RawVersion.loaders | Where-Object { $_ -in @('forge','neoforge') }).Count -gt 0)
        if ($null -ne $comparison) { $compared = $true; if ($comparison -lt 0) { return $true } }
    }
    if ($compared) { return $false }
    return $Before.Published -and $After.Published -and [datetime]$After.Published -lt [datetime]$Before.Published
}

function Get-MpSolutionCost {
    param($Context, [hashtable]$Nodes)
    $changed = 0; $explicit = 0; $added = 0; [decimal]$freshness = 0
    foreach ($id in $Nodes.Keys) {
        if ($Nodes[$id].Published) { $freshness += ([datetime]$Nodes[$id].Published).Ticks }
        if (-not $Context.Before.ContainsKey($id)) { $added++; continue }
        if ($Nodes[$id].VersionId -cne $Context.Before[$id].VersionId) { $changed++; if ($Context.Before[$id].Intent -eq 'explicit') { $explicit++ } }
    }
    return @($changed,$explicit,$added,(-$freshness))
}

function Resolve-MpProviderAvailabilityReport {
    param($Context, [hashtable]$Nodes, $Report)
    foreach ($issue in @($Report.Errors)) {
        $requirement = $issue.Requirement
        if (-not $requirement -or $requirement.Scope -ne 'project' -or (Get-MpPropertyValue $requirement 'Source') -ne 'provider') { continue }
        if ($Nodes.ContainsKey($requirement.Target)) { continue }
        if (-not $Context.ProviderAvailability.ContainsKey($requirement.Target)) {
            $versions = @(Get-MpProjectCandidates $Context $requirement.Target)
            if (-not $Context.DomainKnown[$requirement.Target]) { continue }
            $Context.ProviderAvailability[$requirement.Target] = $versions.Count -gt 0
        }
        if (-not $Context.ProviderAvailability[$requirement.Target]) {
            $issue.Severity = 'unknown'
            $issue.Code = 'provider.no-compatible-candidate'
            $issue.Message += '; provider metadata exposes no compatible candidate'
        }
    }
    $all = @($Report.Issues)
    return [pscustomobject]@{
        Issues = $all
        Errors = @($all | Where-Object Severity -eq error)
        Unknown = @($all | Where-Object Severity -eq unknown)
        Warnings = @($all | Where-Object Severity -eq warning)
        Complete = @($all | Where-Object Severity -eq unknown).Count -eq 0
    }
}

function Search-MpSolution {
    param($Context, [hashtable]$Nodes)
    $key = (@($Nodes.Keys | Sort-Object | ForEach-Object { "$_=$($Nodes[$_].VersionId)" }) -join '|')
    if ($Context.Visited.ContainsKey($key)) { return }
    $Context.Visited[$key] = $true
    if ($Context.Visited.Count -gt 10000) {
        Throw-MpError -Message 'Dependency search exceeded 10000 states; no changes were applied' -Hint 'narrow the requested batch or pin a known compatible dependency' -ErrorId 'Compatibility.SearchLimit' -Category LimitsExceeded
    }
    $report = Resolve-MpProviderAvailabilityReport $Context $Nodes (Get-MpGraphReport $Context.Project $Nodes)
    $errors = @($report.Errors | Where-Object { $Context.Strict -or $_.Key -notin $Context.BaselineKeys })
    if (-not $errors.Count) {
        if ($Context.Strict -and $report.Unknown.Count) { $Context.LastReport = $report; return }
        $cost = Get-MpSolutionCost $Context $Nodes
        $better = $null -eq $Context.Best
        if (-not $better) {
            for ($i = 0; $i -lt 4; $i++) {
                if ($cost[$i] -lt $Context.Cost[$i]) { $better = $true; break }
                if ($cost[$i] -gt $Context.Cost[$i]) { break }
            }
        }
        if ($better) { $Context.Best = $Nodes.Clone(); $Context.Cost = $cost; $Context.Report = $report }
        return
    }
    $Context.LastReport = $report
    $issue = $errors[0]
    $ids = @($issue.Owner)
    foreach ($requirement in @(Get-MpRequirementTargets $issue.Requirement)) {
        if ($requirement.Scope -eq 'project') { $ids += $requirement.Target }
        else {
            $directProviders = @()
            foreach ($node in $Nodes.Values) {
                if (@($node.Mods | Where-Object { $_.Id -eq $requirement.Target -or $_.Provides.ContainsKey($requirement.Target) }).Count) { $directProviders += $node.Id }
            }
            $ids = @($directProviders) + @($ids)
            # Only relationships declared by the failing owner may introduce a project for a missing internal mod ID.
            if (-not $directProviders.Count -and $Nodes.ContainsKey($issue.Owner)) {
                foreach ($dep in $Nodes[$issue.Owner].Requirements) {
                    if ($dep.Scope -eq 'project' -and $dep.Kind -eq 'required') { $ids += $dep.Target }
                }
            }
        }
    }
    $orderedIds = @(); foreach ($id in $ids) { if ($id -and $id -notin $orderedIds) { $orderedIds += $id } }
    foreach ($id in $orderedIds) {
        if ($Context.Roots.ContainsKey($id)) { continue }
        if ($Context.Before.ContainsKey($id) -and ($Context.Before[$id].Pinned -or $Context.Before[$id].Item.Source -eq 'local')) { continue }
        $versions = @(Get-MpProjectCandidates $Context $id)
        foreach ($version in $versions) {
            if ($Nodes.ContainsKey($id) -and $Nodes[$id].VersionId -ceq $version.id) { continue }
            $candidate = Get-MpCandidateNode $Context $id $version
            if (-not $Context.AllowDowngrade -and $Context.Before.ContainsKey($id) -and (Test-MpDowngrade $Context.Before[$id] $candidate)) { continue }
            $next = $Nodes.Clone(); $next[$id] = $candidate
            Search-MpSolution $Context $next
        }
    }
}

function New-MpContentPlan {
    param($Project, [string[]]$Selectors = @(), [ValidateSet('add','update','repair')][string]$Operation,
        [switch]$All, [string]$Type = 'all', [string]$To, [switch]$Strict, [switch]$AllowDowngrade, $State)
    $state = if ($State) { $State } else { Get-MpProjectState $Project -Check }
    $baseline = Get-MpGraphReport $Project $state.Nodes
    $context = @{
        Project = $Project; Before = $state.Nodes; Info = @{}; Domains = @{}; DomainKnown = @{}; Candidates = @{}; Roots = @{}; Visited = @{}; ProviderAvailability = @{}; Check = $true
        BaselineKeys = @(); Strict = [bool]$Strict
        AllowDowngrade = [bool]$AllowDowngrade; Best = $null; Cost = $null; Report = $null; LastReport = $baseline
    }
    $baseline = Resolve-MpProviderAvailabilityReport $context $state.Nodes $baseline
    if ($Operation -ne 'repair') { $context.BaselineKeys = @($baseline.Errors | ForEach-Object Key) }
    $nodes = $state.Nodes.Clone(); $requested = @()
    if ($Operation -eq 'add') {
        foreach ($selector in $Selectors) {
            $cached = Resolve-ModrinthSearchNumber $selector $Project
            $identity = if ($cached) { $cached.ProjectId } else { $selector -replace '^modrinth:', '' }
            if ($identity -match '^https?://(?:www\.)?modrinth\.com/(?:mod|resourcepack|shader)/([^/]+)(?:/version/([^/]+))?/?$') {
                $identity = $Matches[1]; $urlVersion = $Matches[2]
            } else { $urlVersion = $null }
            $info = Get-MpMetadataCache ('project/' + [Uri]::EscapeDataString($identity)) -Check
            $id = 'modrinth:' + $info.id; $context.Info[$id] = $info
            if ($id -in $requested) { continue }; $requested += $id
            $versions = @(Get-MpProjectCandidates $context $id)
            if (-not $versions.Count) { Throw-MpError -Message "No compatible version of '$identity' exists" -Hint 'check Minecraft and loader compatibility' -ErrorId 'Versions.NoCompatibleVersion' -Category ObjectNotFound }
            $chosen = if ($urlVersion) { @($versions | Where-Object { $_.id -ceq $urlVersion -or $_.version_number -ceq $urlVersion } | Select-Object -First 1) } else { @($versions[0]) }
            if (-not $chosen.Count) { Throw-MpError -Message 'The requested URL version is incompatible' -Hint 'modpack versions <selector>' -ErrorId 'Versions.Incompatible' -Category InvalidArgument }
            if ($nodes.ContainsKey($id) -and $nodes[$id].Pinned -and $nodes[$id].VersionId -cne $chosen[0].id) { Throw-MpError -Message "'$identity' is pinned" -Hint "modpack unpin $id" -ErrorId 'Compatibility.Pinned' -Category InvalidOperation }
            $nodes[$id] = Get-MpCandidateNode $context $id $chosen[0]; $context.Roots[$id] = $true
        }
    }
    elseif ($Operation -eq 'update') {
        $targets = if ($All) { @(Get-ModpackUpdateItems $state.Inventory -Type $Type | Where-Object { $_.Source -eq 'packwiz' -and -not $nodes[$_.Id].Pinned }) } else { @(Resolve-ModpackUpdateSelectors $Project $Selectors -Type $Type) }
        foreach ($target in $targets) {
            if ($nodes[$target.Id].Pinned) { Throw-MpError -Message "'$($target.Name)' is pinned" -Hint "modpack unpin $($target.Id)" -ErrorId 'Compatibility.Pinned' -Category InvalidOperation }
            if (-not $target.Id.StartsWith('modrinth:')) { continue }
            $versions = @(Get-MpProjectCandidates $context $target.Id)
            if (-not $versions.Count) { Throw-MpError -Message "No compatible version for '$($target.Name)'" -Hint 'check project compatibility' -ErrorId 'Versions.NoCompatibleVersion' -Category ObjectNotFound }
            $chosen = $versions[0]
            if ($To) {
                if ($To -match '^[1-9][0-9]*$') {
                    $choice = Resolve-ModrinthVersionChoice $To $Project $target
                    $matches = @($versions | Where-Object id -CEQ $choice.Id)
                }
                else { $matches = @($versions | Where-Object { $_.id -ceq $To -or $_.version_number -ceq $To }) }
                if ($matches.Count -ne 1) { Throw-MpError -Message "Version '$To' is unavailable, incompatible or ambiguous" -Hint 'refresh modpack versions and select an exact version ID' -ErrorId 'Versions.NotFound' -Category InvalidArgument }
                $chosen = $matches[0]
            }
            $nodes[$target.Id] = Get-MpCandidateNode $context $target.Id $chosen; $context.Roots[$target.Id] = $true; $requested += $target.Id
        }
    }
    foreach ($id in $context.Roots.Keys) {
        if (-not $AllowDowngrade -and -not $To -and $state.Nodes.ContainsKey($id) -and (Test-MpDowngrade $state.Nodes[$id] $nodes[$id])) {
            Throw-MpError -Message "The latest publication of '$id' would downgrade its installed version" -Hint 'use --allow-downgrade or select a version explicitly with update --to' -ErrorId 'Compatibility.DowngradeBlocked' -Category InvalidOperation
        }
    }
    Search-MpSolution $context $nodes
    if ($null -eq $context.Best) {
        $pins = @($state.Nodes.Values | Where-Object Pinned | ForEach-Object { "$($_.Item.Name) is pinned" })
        $details = @($context.LastReport.Errors | ForEach-Object Message) + @($context.LastReport.Unknown | ForEach-Object Message) + $pins
        Throw-MpError -Message 'No verified dependency solution satisfies this request' -Details ($details -join '; ') -Hint 'adjust the requested versions, unpin a dependency, or use --allow-downgrade when a downgrade is required' -ErrorId 'Compatibility.Unresolvable' -Category InvalidOperation
    }
    $changes = @()
    foreach ($id in @($context.Best.Keys | Sort-Object)) {
        if (-not $state.Nodes.ContainsKey($id) -or $state.Nodes[$id].VersionId -cne $context.Best[$id].VersionId) {
            $changes += [pscustomobject]@{ Id = $id; Before = $(if ($state.Nodes.ContainsKey($id)) { $state.Nodes[$id] } else { $null }); After = $context.Best[$id]; Reason = $(if ($id -in $requested) { 'requested' } else { 'dependency' }) }
        }
    }
    return [pscustomobject]@{ Nodes = $context.Best; Changes = $changes; Requested = $requested; Baseline = $baseline; Report = $context.Report; Operation = $Operation }
}

function Set-MpPlannedContent {
    param($Project, $Plan)
    $metadata = Get-ModpackMetadata $Project
    if (-not $metadata.ContainsKey('Content')) { $metadata.Content = @{} }
    $metadata.ContentSchemaVersion = 1
    foreach ($change in $Plan.Changes) {
        $node = $change.After; $file = $node.File
        if (-not $file) { continue }
        if ($change.Before -and $change.Before.Item.MetadataPath) {
            # Preserve the installed metadata location, including custom subdirectories.
            $path = $change.Before.Item.MetadataPath
            $text = Get-Content -LiteralPath $path -Raw
        }
        else {
            $folder = switch ($node.Item.Kind) { 'mod' { 'mods' }; 'resourcepack' { 'resourcepacks' }; default { 'shaderpacks' } }
            $path = Resolve-MpContainedPath $Project.Root ("$folder/" + ($change.Id -replace ':','-') + '.pw.toml')
            if ([IO.File]::Exists($path)) { Throw-MpError -Message "Planned metadata collides with '$path'" -Hint 'rename the conflicting metadata file' -ErrorId 'Content.PathCollision' -Category InvalidOperation }
            [void][IO.Directory]::CreateDirectory((Split-Path -Parent $path))
            $text = ''
        }
        [void](Resolve-MpContainedPath -Root $Project.Root -Relative ([IO.Path]::GetRelativePath($Project.Root, $path)))
        [void](Resolve-MpContainedPath -Root (Split-Path -Parent $path) -Relative $file.filename)
        $text = Set-MpTomlValue -Text $text -Key name -Value $node.Item.Name
        $text = Set-MpTomlValue -Text $text -Key filename -Value $file.filename
        $text = Set-MpTomlValue -Text $text -Key side -Value $(if ($node.Item.Side -in @('client','server','both')) { $node.Item.Side } else { 'both' })
        $algorithm = if (Get-MpPropertyValue $file.hashes 'sha512') { 'sha512' } else { 'sha1' }
        foreach ($pair in @(@('url',$file.url), @('hash-format',$algorithm), @('hash',(Get-MpPropertyValue $file.hashes $algorithm)))) {
            $text = Set-MpTomlValue -Text $text -Section download -Key $pair[0] -Value $pair[1]
        }
        $text = Set-MpTomlValue -Text $text -Section 'update.modrinth' -Key 'mod-id' -Value $change.Id.Substring(9)
        $text = Set-MpTomlValue -Text $text -Section 'update.modrinth' -Key version -Value $node.VersionId
        Write-Utf8TextFileAtomic $path $text
        if (-not $metadata.Content.ContainsKey($change.Id)) { $metadata.Content[$change.Id] = @{ Intent = $node.Intent } }
    }
    foreach ($id in $Plan.Requested) {
        if ($Plan.Operation -eq 'add') { $metadata.Content[$id] = @{ Intent = 'explicit' } }
    }
    if ($Plan.Changes.Count -or ($Plan.Operation -eq 'add' -and $Plan.Requested.Count)) {
        Write-PowerShellDataFileAtomic -Data $metadata -Path (Join-Path $Project.Root '.modpack/metadata.psd1')
    }
}
