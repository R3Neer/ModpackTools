function Resolve-MpRemovalSelectors {
    param($State, [string[]]$Selectors, [string]$Type = 'all')
    $items = @(Get-ModpackUpdateItems $State.Inventory -Type $Type)
    $seen = @{}
    foreach ($selector in $Selectors) {
        $matches = @($items | Where-Object {
            $stem = if ($_.MetadataPath) { [IO.Path]::GetFileName($_.MetadataPath) -replace '\.pw\.toml$', '' } else { '' }
            $selector -in @($_.Id, $_.Name, $_.Filename, $stem)
        })
        if (-not $matches.Count) { Throw-MpError -Message "Content '$selector' was not found" -Hint 'modpack inventory; select a physical installed item' -ErrorId 'Content.NotFound' -Category ObjectNotFound }
        if ($matches.Count -gt 1) { Throw-MpError -Message "Content selector '$selector' is ambiguous" -Details ($matches.Id -join ', ') -Hint 'use a stable ID or --type <mod|resourcepack|shaderpack>' -ErrorId 'Content.AmbiguousSelector' -Category InvalidArgument }
        $item = $matches[0]
        if ($item.Source -notin @('packwiz','local')) { Throw-MpError -Message "Content '$selector' is not a removable file" -Hint 'select the owning mod for built-in resources' -ErrorId 'Content.NotRemovable' -Category InvalidOperation }
        if (-not $seen.ContainsKey($item.Id)) { $seen[$item.Id] = $true; $item.Id }
    }
}

function Get-MpRemovalEdges {
    param([hashtable]$Nodes)
    # Conservative reachability retains all alternatives, conditional guards and
    # optional references. The shared graph validator decides actual conflicts.
    $providers = @{}
    foreach ($node in $Nodes.Values) {
        foreach ($mod in $node.Mods) {
            foreach ($id in @($mod.Id) + @($mod.Provides.Keys)) {
                if (-not $providers.ContainsKey($id)) { $providers[$id] = @() }
                $providers[$id] += $node.Id
            }
        }
    }
    $edges = @{}
    foreach ($node in $Nodes.Values) {
        $targets = @(foreach ($requirement in @($node.Requirements) + @($node.Mods | ForEach-Object { $_.Requirements })) {
            if ($requirement.Kind -in @('incompatible','discouraged')) { continue }
            foreach ($leaf in @(Get-MpRequirementTargets $requirement)) {
                if ($leaf.Scope -eq 'project') { if ($Nodes.ContainsKey($leaf.Target)) { $leaf.Target } }
                elseif ($providers.ContainsKey($leaf.Target)) { $providers[$leaf.Target] }
            }
        })
        $edges[$node.Id] = @($targets | Sort-Object -Unique)
    }
    return $edges
}

function Get-MpRemovalReachable {
    param([hashtable]$Edges, [string[]]$Roots, [hashtable]$Excluded = @{})
    $seen = @{}; $pending = [Collections.Generic.Stack[string]]::new()
    foreach ($id in $Roots) { $pending.Push($id) }
    while ($pending.Count) {
        $id = $pending.Pop()
        if ($seen.ContainsKey($id) -or $Excluded.ContainsKey($id)) { continue }
        $seen[$id] = $true
        foreach ($target in $Edges[$id]) { $pending.Push($target) }
    }
    return $seen
}

function New-MpRemovalPlan {
    param($Project, $State, [string[]]$Selectors, [string]$Type = 'all', [switch]$Cascade, [switch]$AutoRemove, [switch]$Strict)
    $requested = @(Resolve-MpRemovalSelectors $State $Selectors -Type $Type)
    $baseline = Get-MpGraphReport $Project $State.Nodes
    $remaining = $State.Nodes.Clone(); $removed = [ordered]@{}
    foreach ($id in $requested) { $removed[$id] = 'requested'; $remaining.Remove($id) }
    while ($true) {
        $report = Get-MpGraphReport $Project $remaining
        $newErrors = @($report.Errors | Where-Object { $_.Key -notin @($baseline.Errors | ForEach-Object Key) })
        if (-not $newErrors.Count) { break }
        if (-not $Cascade) {
            Throw-MpError -Message 'Removal would break installed dependents' -Details ($newErrors.Message -join '; ') -Hint 'include the dependents explicitly, or preview remove --cascade --dry-run' -ErrorId 'Remove.Dependents' -Category InvalidOperation
        }
        $owners = @($newErrors | Where-Object { $_.Code -eq 'conflict.required' -and $_.Requirement.Kind -eq 'required' -and $remaining.ContainsKey($_.Owner) } | ForEach-Object Owner | Sort-Object -Unique)
        if (-not $owners.Count) { Assert-MpGraphPolicy $report -Baseline $baseline }
        foreach ($id in $owners) { $removed[$id] = 'dependent'; $remaining.Remove($id) }
    }
    if ($AutoRemove) {
        # Unknown declarations could hide references from any surviving item.
        # Refuse speculative cleanup, while plain remove remains available.
        if ($baseline.Unknown.Count) {
            Throw-MpError -Message 'Automatic dependency removal requires complete verification' -Details ($baseline.Unknown.Message -join '; ') -Hint 'resolve incomplete metadata, or omit --autoremove and remove explicit selectors only' -ErrorId 'Remove.IncompleteVerification' -Category InvalidOperation
        }
        $edges = Get-MpRemovalEdges $State.Nodes
        $affected = Get-MpRemovalReachable $edges @($removed.Keys)
        $roots = @($remaining.Values | Where-Object { $_.Intent -ne 'transitive' -or $_.Pinned -or $_.Item.Source -ne 'packwiz' -or -not $affected.ContainsKey($_.Id) } | ForEach-Object Id)
        $excluded = @{}; foreach ($id in $removed.Keys) { $excluded[$id] = $true }
        $retained = Get-MpRemovalReachable $edges $roots $excluded
        foreach ($id in @($remaining.Keys | Sort-Object)) {
            if (-not $retained.ContainsKey($id)) { $removed[$id] = 'unused dependency'; $remaining.Remove($id) }
        }
    }
    foreach ($id in $removed.Keys) {
        if ($State.Nodes[$id].Pinned) { Throw-MpError -Message "'$($State.Nodes[$id].Item.Name)' is pinned" -Hint "modpack unpin $id" -ErrorId 'Compatibility.Pinned' -Category InvalidOperation }
    }
    $report = Get-MpGraphReport $Project $remaining
    Assert-MpGraphPolicy $report -Baseline $baseline -Strict:$Strict
    return [pscustomobject]@{ Requested = $requested; Nodes = $remaining; Baseline = $baseline; Report = $report; Changes = @(foreach ($id in $removed.Keys) { [pscustomobject]@{ Id = $id; Before = $State.Nodes[$id]; After = $null; Reason = $removed[$id] } }) }
}

function Set-MpRemovedContent {
    param($Project, $Plan)
    $metadata = Get-ModpackMetadata $Project
    $resourceIds = @()
    $protected = @{}
    foreach ($node in $Plan.Nodes.Values) {
        $other = $node.Item
        $folder = switch ($other.Kind) { mod { 'mods' }; resourcepack { 'resourcepacks' }; shaderpack { 'shaderpacks' } }
        $base = Resolve-MpContainedPath $Project.Root $folder
        if ($other.MetadataPath) {
            $protected[[IO.Path]::GetFullPath($other.MetadataPath)] = $other.Name
            $base = Split-Path -Parent $other.MetadataPath
        }
        $protected[(Resolve-MpContainedPath $base $other.Filename)] = $other.Name
    }
    foreach ($change in $Plan.Changes) {
        $item = $change.Before.Item
        $directory = switch ($item.Kind) { mod { 'mods' }; resourcepack { 'resourcepacks' }; shaderpack { 'shaderpacks' } }
        $contentRoot = Resolve-MpContainedPath $Project.Root $directory
        if ($item.Source -eq 'packwiz') {
            $relative = [IO.Path]::GetRelativePath($contentRoot, $item.MetadataPath)
            $path = Resolve-MpContainedPath $contentRoot $relative
            # Packwiz owns the metadata, not a potentially unrelated local file.
            # If an artifact is present, require its declared hash before removal.
            $artifact = Resolve-MpContainedPath (Split-Path -Parent $path) $item.Filename
            if ($protected.ContainsKey($artifact)) { Throw-MpError -Message "Artifact '$($item.Filename)' is shared with retained content" -Details $protected[$artifact] -Hint 'reconcile shared file ownership before removal' -ErrorId 'Remove.SharedArtifact' -Category InvalidData }
            if ([IO.File]::Exists($artifact)) {
                $raw = ConvertFrom-MpToml (Get-Content -LiteralPath $path -Raw)
                $download = $raw['download']; $algorithm = [string]$download['hash-format']
                if ($algorithm -notin @('sha1','sha256','sha512','md5') -or (Get-FileHash -LiteralPath $artifact -Algorithm $algorithm).Hash -ne $download['hash']) {
                    Throw-MpError -Message "Local artifact '$($item.Filename)' differs from Packwiz metadata" -Hint 'preserve or reconcile the local file before removing this item' -ErrorId 'Remove.ArtifactMismatch' -Category InvalidData
                }
                [IO.File]::Delete($artifact)
            }
        }
        else { $path = Resolve-MpContainedPath $contentRoot $item.Filename }
        if ($protected.ContainsKey($path)) { Throw-MpError -Message "Removal path is owned by retained content" -Details $protected[$path] -Hint 'reconcile shared file ownership before removal' -ErrorId 'Remove.SharedArtifact' -Category InvalidData }
        if (-not [IO.File]::Exists($path)) { Throw-MpError -Message "Removal target '$($item.Name)' is missing" -Hint 'refresh inventory and retry' -ErrorId 'Content.NotFound' -Category ObjectNotFound }
        [IO.File]::Delete($path)
        foreach ($table in @('Mods','Content')) { if ($metadata.ContainsKey($table)) { [void]$metadata[$table].Remove($item.Id) } }
        if ($item.Kind -eq 'resourcepack') {
            $resourceIds += 'file/' + $item.Filename
            [void]$metadata.ResourcePacks.Remove(('file/' + $item.Filename))
            [void]$metadata.ResourcePacks.Remove($item.Id)
        }
    }
    $active = @(Get-DefaultResourcePackOrder $Project)
    if (@($active | Where-Object { $_ -in $resourceIds }).Count) { Set-DefaultResourcePackOrder $Project @($active | Where-Object { $_ -notin $resourceIds }) }
    if ($Plan.Changes.Count) {
        Write-PowerShellDataFileAtomic $metadata (Join-Path $Project.Root '.modpack/metadata.psd1')
        Invoke-Packwiz -Arguments @('refresh') -WorkingDirectory $Project.Root | Out-Null
    }
}

function Invoke-MpRemovalOperation {
    param($Project, [string[]]$Selectors, [string]$Type = 'all', [switch]$Cascade, [switch]$AutoRemove, [switch]$Strict, [switch]$DryRun, [AllowNull()][array]$ExpectedChanges = $null)
    Invoke-MpProjectTransaction $Project -DryRun:$DryRun -ExpectedChanges $ExpectedChanges -Prepare {
        param($stage)
        $state = Get-MpProjectState $stage -Check
        $plan = New-MpRemovalPlan $stage $state $Selectors -Type $Type -Cascade:$Cascade -AutoRemove:$AutoRemove -Strict:$Strict
        Set-MpRemovedContent $stage $plan
        $after = Get-MpProjectState $stage
        if ((@($plan.Nodes.Keys | Sort-Object) -join "`0") -cne (@($after.Nodes.Keys | Sort-Object) -join "`0")) {
            Throw-MpError -Message 'Materialized removal differs from the plan' -Hint 'inspect the project metadata' -ErrorId 'Content.PlanMismatch' -Category InvalidResult
        }
        foreach ($id in $plan.Nodes.Keys) {
            if ($plan.Nodes[$id].VersionId -cne $after.Nodes[$id].VersionId) { Throw-MpError -Message 'Removal changed a retained version' -Hint 'inspect the project metadata' -ErrorId 'Content.PlanMismatch' -Category InvalidResult }
        }
        $plan.Report = Get-MpGraphReport $stage $after.Nodes
        Assert-MpGraphPolicy $plan.Report -Baseline $plan.Baseline -Strict:$Strict
        return $plan
    }
}

function Invoke-MpRemove {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Show-MpHelp remove; return }
    $parsed = ConvertFrom-MpOptions $Arguments -ValueOptions @('project','type') -SwitchOptions @('cascade','autoremove','strict','dry-run','yes')
    Assert-PositionalCount $parsed.Positionals -Minimum 1 -Maximum ([int]::MaxValue) -Usage 'modpack remove <selector...> [options]'
    $project = Resolve-MpCommandProject $parsed.Options
    $type = if ($parsed.Options.ContainsKey('type')) { Resolve-InventoryType $parsed.Options.type } else { 'all' }
    $kinds = if ($type -eq 'all') { @('mod','resourcepack','shaderpack') } else { @($type) }
    $selectors = @(Resolve-MpBatchSelectors $project $parsed.Positionals -Kinds $kinds)
    # Freeze names/numbers to identities before preview and confirmation.
    $state = [pscustomobject]@{ Inventory = Get-ModpackInventory $project }
    $selectors = @(Resolve-MpRemovalSelectors $state $selectors -Type $type)
    $options = @{ Project=$project; Selectors=$selectors; Type=$type; Cascade=$parsed.Options.ContainsKey('cascade'); AutoRemove=$parsed.Options.ContainsKey('autoremove'); Strict=$parsed.Options.ContainsKey('strict') }
    $preview = Invoke-MpRemovalOperation @options -DryRun
    Write-MpContentPlan $preview.Result
    Write-MpTransactionSummary $preview -DryRun:$parsed.Options.ContainsKey('dry-run') -Preview:(-not $parsed.Options.ContainsKey('dry-run'))
    if ($parsed.Options.ContainsKey('dry-run') -or -not $preview.Changes.Count) { return }
    if (-not $parsed.Options.ContainsKey('yes') -and -not (Confirm-MpDoctorAction -Prompt 'Apply this removal?' -Default $false)) { Write-R3Status (Get-MpConsole) info 'Removal cancelled; no project changes applied.'; return }
    $result = Invoke-MpRemovalOperation @options -ExpectedChanges $preview.Changes
    Write-MpTransactionSummary $result
}
