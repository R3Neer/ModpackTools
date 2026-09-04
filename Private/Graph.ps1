function Get-MpMetadataCachePath {
    param([string]$Endpoint)
    Join-Path (Get-ModpackToolsConfigDirectory) ('metadata/' + (Get-MpHash $Endpoint) + '.json')
}

function Get-MpMetadataCache {
    param([string]$Endpoint, [switch]$Check)
    $path = Get-MpMetadataCachePath $Endpoint
    if ($Check) {
        $value = Invoke-ModrinthApiRequest -PathAndQuery $Endpoint
        [void][IO.Directory]::CreateDirectory((Split-Path -Parent $path))
        Write-Utf8TextFileAtomic $path ($value | ConvertTo-Json -Depth 60 -Compress)
        return $value
    }
    if ([IO.File]::Exists($path) -and [IO.File]::GetLastWriteTimeUtc($path) -gt [datetime]::UtcNow.AddHours(-24)) {
        return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    }
    return $null
}

function Get-MpArtifact {
    param([string]$Url, [string]$Hash, [string]$Algorithm, [switch]$Check, [string]$LocalPath)
    if (-not $Hash -or $Algorithm -notin @('sha1','sha256','sha512','md5')) { return $null }
    if ($LocalPath -and [IO.File]::Exists($LocalPath) -and (Get-FileHash -LiteralPath $LocalPath -Algorithm $Algorithm).Hash -eq $Hash) { return $LocalPath }
    $path = Join-Path (Get-ModpackToolsConfigDirectory) ('artifacts/' + (Get-MpHash "$Algorithm`:$Hash"))
    if ([IO.File]::Exists($path) -and (Get-FileHash -LiteralPath $path -Algorithm $Algorithm).Hash -eq $Hash) { return $path }
    if (-not $Check -or -not $Url) { return $null }
    if ($Url -notmatch '^https?://') { return $null }
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $path))
    $temp = $path + '.' + [guid]::NewGuid().ToString('N')
    try {
        Invoke-WebRequest -Uri $Url -OutFile $temp -TimeoutSec 60
        if ((Get-FileHash -LiteralPath $temp -Algorithm $Algorithm).Hash -ne $Hash) {
            Throw-MpError -Message 'Downloaded artifact hash does not match its metadata' -Details $Url -Hint 'retry after checking the provider metadata' -ErrorId 'Content.HashMismatch' -Category InvalidData
        }
        [IO.File]::Move($temp, $path, $true)
    }
    finally { if ([IO.File]::Exists($temp)) { [IO.File]::Delete($temp) } }
    return $path
}

function ConvertTo-MpNode {
    param($Project, $Item, $Version, [switch]$Check, [string]$Intent = 'explicit')
    $warnings = [Collections.Generic.List[string]]::new(); $mods = @(); $requirements = @()
    $raw = $null; $artifact = $null; $file = $null
    if ($Item.MetadataPath -and [IO.File]::Exists($Item.MetadataPath)) { $raw = ConvertFrom-MpToml (Get-Content -LiteralPath $Item.MetadataPath -Raw) }
    $versionId = [string](Get-MpPropertyValue $Item 'VersionId')
    if ($Version) {
        $versionId = [string]$Version.id
        $files = @((Get-MpPropertyValue $Version 'files'))
        $file = @($files | Where-Object { (Get-MpPropertyValue $_ 'primary') -eq $true } | Select-Object -First 1)
        if (-not $file.Count) { $file = @($files | Select-Object -First 1) }
        $file = if ($file.Count) { $file[0] } else { $null }
        if ($raw -and $versionId -ceq [string](Get-MpPropertyValue $Item 'VersionId')) {
            $matching = @($files | Where-Object { $_.filename -ceq $Item.Filename } | Select-Object -First 1)
            if ($matching.Count) { $file = $matching[0] }
        }
        foreach ($dep in @((Get-MpPropertyValue $Version 'dependencies'))) {
            $kind = [string](Get-MpPropertyValue $dep 'dependency_type')
            if ($kind -notin @('required','incompatible')) { continue }
            $id = [string](Get-MpPropertyValue $dep 'project_id'); $exact = [string](Get-MpPropertyValue $dep 'version_id')
            if (-not $id -and $exact) {
                try { $linked = Get-MpMetadataCache "version/$exact" -Check:$Check; if ($linked) { $id = [string]$linked.project_id } } catch { $warnings.Add($_.Exception.Message) }
            }
            if ($id) { $requirements += New-MpRequirement -Target "modrinth:$id" -Kind $kind -Scope project -Source provider -SuggestedVersionId $exact }
            else { $warnings.Add('A provider dependency has no verifiable project identity') }
        }
    }
    elseif ($Item.Source -eq 'packwiz') { $warnings.Add('Provider dependency metadata is unavailable') }
    if ($Item.Kind -eq 'mod') {
        try {
            if ($Item.Source -eq 'local') { $artifact = Resolve-MpContainedPath $Project.Root ('mods/' + $Item.Filename) }
            elseif ($file) {
                $hashes = Get-MpPropertyValue $file 'hashes'; $algorithm = if (Get-MpPropertyValue $hashes 'sha512') { 'sha512' } else { 'sha1' }
                $artifact = Get-MpArtifact -Url $file.url -Hash (Get-MpPropertyValue $hashes $algorithm) -Algorithm $algorithm -Check:$Check
            }
            elseif ($raw -and $raw['download']) {
                $download = $raw['download']
                $local = Resolve-MpContainedPath (Split-Path -Parent $Item.MetadataPath) $Item.Filename
                $artifact = Get-MpArtifact -Url ([string]$download['url']) -Hash ([string]$download['hash']) -Algorithm ([string]$download['hash-format']) -Check:$Check -LocalPath $local
            }
            if ($artifact) {
                $parsed = Get-MpLoaderMetadata $artifact $Project.Loader
                $mods = $parsed.Mods; foreach ($warning in $parsed.Warnings) { $warnings.Add($warning) }
            }
            else { $warnings.Add('JAR requirements have not been verified; run inventory --check') }
        }
        catch { $warnings.Add($_.Exception.Message) }
    }
    $enabled = $true
    if ($raw -and $raw.ContainsKey('option') -and $raw['option']['optional'] -eq $true) {
        $enabled = $raw['option']['default'] -eq $true
        $warnings.Add('Optional install combinations require separate verification; checking the default selection')
    }
    $pinned = $raw -and $raw.ContainsKey('pin') -and $raw['pin'] -eq $true
    return [pscustomobject]@{
        Id = $Item.Id; Item = $Item; VersionId = $versionId; RawVersion = $Version; File = $file
        Mods = @($mods); Requirements = @($requirements); Warnings = @($warnings); Pinned = [bool]$pinned
        Intent = $Intent; Enabled = $enabled; Published = [string](Get-MpPropertyValue $Version 'date_published')
    }
}

function Get-MpProjectState {
    param($Project, [switch]$Check)
    $inventory = Get-ModpackInventory $Project
    $nodes = @{}
    $intent = Get-MpPropertyValue $inventory.Metadata 'Content'
    $items = @(Get-ModpackUpdateItems $inventory)
    $checkedVersions = @{}; $batchWarning = $null
    if ($Check) {
        $versionIds = @($items | Where-Object { $_.Id.StartsWith('modrinth:') -and (Get-MpPropertyValue $_ 'VersionId') } | ForEach-Object VersionId)
        try {
            foreach ($version in @(Get-ModrinthVersionsByIds $versionIds)) {
                $checkedVersions[[string]$version.id] = $version
                $cachePath = Get-MpMetadataCachePath ('version/' + [string]$version.id)
                [void][IO.Directory]::CreateDirectory((Split-Path -Parent $cachePath))
                Write-Utf8TextFileAtomic $cachePath ($version | ConvertTo-Json -Depth 60 -Compress)
            }
        } catch { $batchWarning = $_.Exception.Message }
    }
    foreach ($item in $items) {
        if ($nodes.ContainsKey($item.Id)) {
            Throw-MpError -Message "Duplicate content ID '$($item.Id)'" -Details "Metadata files: '$($nodes[$item.Id].Item.MetadataPath)' and '$($item.MetadataPath)'" -Hint 'resolve the duplicate metadata before changing or building the pack' -ErrorId 'Content.DuplicateIdentity' -Category InvalidData -TargetObject $item.Id
        }
        $version = $null; $warning = $null
        if ($item.Id.StartsWith('modrinth:') -and (Get-MpPropertyValue $item 'VersionId')) {
            try {
                if ($Check) {
                    if ($checkedVersions.ContainsKey([string]$item.VersionId)) { $version = $checkedVersions[[string]$item.VersionId] }
                    else { $warning = if ($batchWarning) { $batchWarning } else { 'Installed provider version metadata is unavailable' } }
                }
                else { $version = Get-MpMetadataCache ('version/' + $item.VersionId) }
            }
            catch { $warning = $_.Exception.Message }
        }
        $origin = 'explicit'
        if ($intent -and $intent.ContainsKey($item.Id) -and (Get-MpPropertyValue $intent[$item.Id] 'Intent') -eq 'transitive') { $origin = 'transitive' }
        $node = ConvertTo-MpNode $Project $item $version -Check:$Check -Intent $origin
        if ($warning) { $node.Warnings += $warning }
        $nodes[$node.Id] = $node
    }
    return [pscustomobject]@{ Project = $Project; Inventory = $inventory; Nodes = $nodes }
}

function New-MpGraphIssue {
    param($Owner, $Requirement, [string]$Side, [string]$Severity, [string]$Message, [string]$Code = 'verification.requirement')
    $identity = "$($Owner.Id)|$($Owner.VersionId)|$Side|" + ($Requirement | ConvertTo-Json -Depth 30 -Compress)
    [pscustomobject]@{
        Key = Get-MpHash $identity
        Code = $Code
        Owner = $Owner.Id
        OwnerName = $Owner.Item.Name
        Requirement = $Requirement
        Side = $Side
        Severity = $Severity
        Message = $Message
    }
}

function Test-MpRequirement {
    param($Requirement, [hashtable]$Nodes, [hashtable]$Mods)
    if ($Requirement.Unless) {
        $unless = Test-MpRequirement $Requirement.Unless $Nodes $Mods
        if ($unless -eq $true) { return $true }; if ($null -eq $unless) { return $null }
    }
    foreach ($op in @('Any','All')) {
        if ($Requirement.$op.Count) {
            $unknown = $false
            foreach ($child in $Requirement.$op) {
                $r = Test-MpRequirement $child $Nodes $Mods
                if ($op -eq 'Any' -and $r -eq $true) { return $Requirement.Kind -notin @('incompatible','discouraged') }
                if ($op -eq 'All' -and $r -eq $false) { return $Requirement.Kind -in @('incompatible','discouraged') }
                if ($null -eq $r) { $unknown = $true }
            }
            if ($unknown) { return $null }; $result = $op -eq 'All'; if ($Requirement.Kind -in @('incompatible','discouraged')) { return -not $result }; return $result
        }
    }
    if (-not $Requirement.Target) { return $null }
    $present = if ($Requirement.Scope -eq 'project') { $Nodes.ContainsKey($Requirement.Target) } else { $Mods.ContainsKey($Requirement.Target) }
    if (-not $present) {
        if ($Requirement.Target -in @('java','javafml','lowcodefml')) { return $null }
        return $Requirement.Kind -in @('optional','incompatible','discouraged')
    }
    $matches = $false; $unknown = $false
    if ($Requirement.Scope -eq 'project') {
        $matches = $Requirement.Range -eq '*' -or $Nodes[$Requirement.Target].VersionId -ceq $Requirement.Range
        $suggestedVersion = [string](Get-MpPropertyValue $Requirement 'SuggestedVersionId')
        if ($matches -and $suggestedVersion -and $Nodes[$Requirement.Target].VersionId -cne $suggestedVersion) { return $null }
    }
    else {
        foreach ($version in @($Mods[$Requirement.Target])) {
            if (-not $version) { $unknown = $true; continue }
            $r = Test-MpVersionRange -Version $version -Range $Requirement.Range -Maven:$Requirement.Maven
            if ($r -eq $true) { $matches = $true; break }; if ($null -eq $r) { $unknown = $true }
        }
    }
    if (-not $matches -and $unknown) { return $null }
    if ($Requirement.Kind -in @('incompatible','discouraged')) { return -not $matches }
    return $matches
}

function Get-MpGraphReport {
    param($Project, [hashtable]$Nodes)
    $issues = [Collections.Generic.List[object]]::new()
    foreach ($node in $Nodes.Values) {
        foreach ($warning in $node.Warnings) {
            $code = if ($warning -eq 'Provider dependency metadata is unavailable') { 'provider.metadata-unavailable' } else { 'verification.metadata' }
            $issues.Add((New-MpGraphIssue $node $warning 'both' 'unknown' "$($node.Item.Name): $warning" -Code $code))
        }
    }
    foreach ($side in @('client','server')) {
        $active = @{}; $nativeOwners = @{}; $mods = @{ minecraft = @($Project.MinecraftVersion) }
        $loaderId = switch ($Project.Loader) { 'fabric' { 'fabricloader' }; 'quilt' { 'quilt_loader' }; default { $Project.Loader } }
        $mods[$loaderId] = @($Project.LoaderVersion)
        if ($Project.Loader -eq 'forge') { $mods.javafml = @(($Project.LoaderVersion -split '\.')[0]); $mods.lowcodefml = @('1') }
        $descriptor = Import-PowerShellDataFile -LiteralPath $Project.DescriptorPath
        if ($descriptor.ContainsKey('JavaVersion')) { $mods.java = @([string]$descriptor.JavaVersion) }
        foreach ($node in $Nodes.Values) {
            if (-not $node.Enabled) { continue }
            if ($node.Item.Side -in @('client','server') -and $node.Item.Side -ne $side) { continue }
            $active[$node.Id] = $node
            foreach ($mod in $node.Mods) {
                if ($mod.Side -in @('client','server') -and $mod.Side -ne $side) { continue }
                if (-not $nativeOwners.ContainsKey($mod.Id)) { $nativeOwners[$mod.Id] = @() }
                $nativeOwners[$mod.Id] += [pscustomobject]@{ Node = $node; Nested = (Get-MpPropertyValue $mod 'Nested') -eq $true }
                if (-not $mods.ContainsKey($mod.Id)) { $mods[$mod.Id] = @() }; $mods[$mod.Id] += $mod.Version
                foreach ($alias in $mod.Provides.Keys) { if (-not $mods.ContainsKey($alias)) { $mods[$alias] = @() }; $mods[$alias] += $mod.Provides[$alias] }
            }
        }
        foreach ($id in $nativeOwners.Keys) {
            if ($nativeOwners[$id].Count -gt 1) {
                $topLevel = @($nativeOwners[$id] | Where-Object { -not $_.Nested })
                $severity = if ($topLevel.Count -gt 1) { 'error' } else { 'unknown' }
                $message = if ($topLevel.Count -gt 1) { "Duplicate top-level mod ID '$id' [$side]" } else { "Multiple bundled versions of '$id' require runtime selection [$side]" }
                $code = if ($severity -eq 'error') { 'conflict.duplicate-id' } else { 'verification.bundled-selection' }
                $issues.Add((New-MpGraphIssue $nativeOwners[$id][0].Node (New-MpRequirement -Target $id) $side $severity $message -Code $code))
            }
        }
        foreach ($node in $active.Values) {
            $requirements = @($node.Requirements)
            foreach ($mod in $node.Mods) { if ($mod.Side -notin @('client','server') -or $mod.Side -eq $side) { $requirements += $mod.Requirements } }
            foreach ($requirement in $requirements) {
                if ($requirement.Side -in @('client','server') -and $requirement.Side -ne $side) { continue }
                $valid = Test-MpRequirement $requirement $active $mods
                if ($valid -eq $true) { continue }
                if ($valid -eq $false -and $requirement.Scope -eq 'mod' -and $requirement.Target -and -not $mods.ContainsKey($requirement.Target) -and @($active.Values | Where-Object { $_.Item.Kind -eq 'mod' -and $_.Mods.Count -eq 0 }).Count) { $valid = $null }
                $severity = if ($null -eq $valid) { 'unknown' } elseif ($requirement.Kind -in @('recommended','discouraged')) { 'warning' } else { 'error' }
                $code = if ($severity -eq 'error') { 'conflict.required' }
                    elseif ($severity -eq 'warning') { if ($requirement.Kind -eq 'discouraged') { 'recommendation.discouraged' } else { 'recommendation.optional' } }
                    elseif ($requirement.Target -eq 'java' -and -not $mods.ContainsKey('java')) { 'environment.java-undeclared' }
                    elseif ($requirement.Scope -eq 'project' -and (Get-MpPropertyValue $requirement 'SuggestedVersionId')) { 'provider.version-differs' }
                    elseif ($mods.ContainsKey($requirement.Target)) { 'version-range.unsupported' }
                    else { 'verification.requirement' }
                $constraint = $requirement.Range -join ' OR '
                $suggestedVersion = [string](Get-MpPropertyValue $requirement 'SuggestedVersionId')
                if ($suggestedVersion) { $constraint = "provider version $suggestedVersion" }
                $message = "$($node.Item.Name): $($requirement.Kind) $($requirement.Target) $constraint [$side]"
                $issue = New-MpGraphIssue $node $requirement $side $severity $message -Code $code
                $facts = @(foreach ($target in @(Get-MpRequirementTargets $requirement)) {
                    if ($target.Scope -eq 'project' -and $active.ContainsKey($target.Target)) { $active[$target.Target].VersionId }
                    elseif ($mods.ContainsKey($target.Target)) { $mods[$target.Target] }
                })
                $issue.Key = Get-MpHash ($issue.Key + '|' + ($facts -join ','))
                $issues.Add($issue)
            }
        }
    }
    foreach ($relative in @('config/fabric_loader_dependencies.json','config/quilt-loader-overrides.json')) {
        if (Test-Path -LiteralPath (Join-Path $Project.Root $relative)) {
            $issues.Add([pscustomobject]@{ Key = Get-MpHash $relative; Code = 'verification.runtime-override'; Owner = ''; OwnerName = ''; Requirement = $null; Side = 'both'; Severity = 'unknown'; Message = "Runtime overrides in $relative require manual verification" })
        }
    }
    $all = @($issues | Sort-Object Key,Severity -Unique)
    return [pscustomobject]@{ Issues = $all; Errors = @($all | Where-Object Severity -eq error); Unknown = @($all | Where-Object Severity -eq unknown); Warnings = @($all | Where-Object Severity -eq warning); Complete = @($all | Where-Object Severity -eq unknown).Count -eq 0 }
}

function Assert-MpGraphPolicy {
    param($Report, $Baseline, [switch]$Strict, [switch]$Build)
    $errors = @($Report.Errors)
    if ($Baseline -and -not $Strict -and -not $Build) { $errors = @($errors | Where-Object { $_.Key -notin @($Baseline.Errors | ForEach-Object Key) }) }
    if ($errors.Count -or ($Strict -and $Report.Unknown.Count)) {
        $details = @($errors | ForEach-Object Message) + @($(if ($Strict) { $Report.Unknown | ForEach-Object Message }))
        Throw-MpError -Message 'The resulting pack does not satisfy dependency validation' -Details ($details -join '; ') -Hint 'resolve the listed requirements, unpin a blocking version, or inspect incomplete metadata' -ErrorId 'Compatibility.GraphBlocked' -Category InvalidOperation
    }
}
