function Get-MpPropertyValue {
    param($InputObject, [Parameter(Mandatory)][string]$Name)
    if ($null -eq $InputObject) { return $null }
    if ($InputObject -is [System.Collections.IDictionary]) {
        if ($InputObject.Contains($Name)) { return $InputObject[$Name] }
        return $null
    }
    $property = $InputObject.PSObject.Properties[$Name]
    if ($property) { return $property.Value }
    return $null
}

function Get-ModpackDependencyGraphIssues {
    param([Parameter(Mandatory)][hashtable]$Versions)
    $conflicts = [System.Collections.Generic.List[string]]::new()
    $warnings = [System.Collections.Generic.List[string]]::new()
    foreach ($ownerProjectId in @($Versions.Keys)) {
        $owner = $Versions[$ownerProjectId]
        $ownerName = [string](Get-MpPropertyValue -InputObject $owner -Name name)
        if ([string]::IsNullOrWhiteSpace($ownerName)) { $ownerName = $ownerProjectId }
        foreach ($dependency in @((Get-MpPropertyValue -InputObject $owner -Name dependencies))) {
            $type = [string](Get-MpPropertyValue -InputObject $dependency -Name dependency_type)
            if ($type -notin @('required', 'incompatible')) { continue }
            $dependencyProjectId = [string](Get-MpPropertyValue -InputObject $dependency -Name project_id)
            $dependencyVersionId = [string](Get-MpPropertyValue -InputObject $dependency -Name version_id)
            if ([string]::IsNullOrWhiteSpace($dependencyProjectId)) {
                $warnings.Add("${ownerName}: a $type dependency names no project and cannot be verified")
                continue
            }
            $present = $Versions.ContainsKey($dependencyProjectId)
            $actualVersionId = if ($present) { [string](Get-MpPropertyValue -InputObject $Versions[$dependencyProjectId] -Name id) } else { $null }
            if ($type -eq 'required') {
                if (-not $present) { $conflicts.Add("$ownerName requires missing Modrinth project $dependencyProjectId") }
                elseif ($dependencyVersionId -and -not $actualVersionId.Equals($dependencyVersionId, [System.StringComparison]::OrdinalIgnoreCase)) {
                    $conflicts.Add("$ownerName requires version $dependencyVersionId of $dependencyProjectId, but the pack uses $actualVersionId")
                }
            }
            elseif ($present -and (-not $dependencyVersionId -or $actualVersionId.Equals($dependencyVersionId, [System.StringComparison]::OrdinalIgnoreCase))) {
                $scope = if ($dependencyVersionId) { "version $dependencyVersionId of $dependencyProjectId" } else { "project $dependencyProjectId" }
                $conflicts.Add("$ownerName declares $scope incompatible")
            }
        }
    }
    return [pscustomobject]@{ Conflicts = @($conflicts | Sort-Object -Unique); Warnings = @($warnings | Sort-Object -Unique) }
}

function Test-ModpackUpdatePreflight {
    param(
        [Parameter(Mandatory)]$Project,
        [Parameter(Mandatory)][array]$Targets,
        $ExactVersion,
        [switch]$Strict
    )

    $warnings = [System.Collections.Generic.List[string]]::new()
    $candidateVersions = @{}
    $currentVersions = @{}
    $inventory = Get-ModpackInventory -Project $Project
    $installedItems = @(Get-ModpackUpdateItems -Inventory $inventory | Where-Object Source -eq packwiz)
    $targetIds = @($Targets | ForEach-Object Id)

    foreach ($target in $Targets) {
        if (-not ([string]$target.Id).StartsWith('modrinth:', [System.StringComparison]::OrdinalIgnoreCase)) {
            $warnings.Add("$($target.Name): provider does not expose dependency data through this check")
            continue
        }
        $projectId = Get-ModrinthProjectIdFromItem -Item $target
        try {
            $candidate = if ($ExactVersion -and $Targets.Count -eq 1) { $ExactVersion } else {
                $view = Get-ModrinthCompatibleVersions -Project $Project -Item $target
                @($view.Versions | Select-Object -First 1)[0]
            }
            if (-not $candidate) {
                $warnings.Add("$($target.Name): no compatible version exists for Minecraft $($Project.MinecraftVersion) and $($Project.Loader)")
                continue
            }
            $candidateVersions[$projectId] = $candidate
        }
        catch {
            $warnings.Add("$($target.Name): candidate dependency data could not be verified ($($_.Exception.Message -split "`r?`n")[0])")
        }
    }

    $versionsToFetch = @{}
    foreach ($item in $installedItems) {
        if (-not ([string]$item.Id).StartsWith('modrinth:', [System.StringComparison]::OrdinalIgnoreCase)) {
            if ($targetIds -contains $item.Id) { continue }
            $warnings.Add("$($item.Name): installed provider is not covered by the Modrinth dependency check")
            continue
        }
        $projectId = Get-ModrinthProjectIdFromItem -Item $item
        if ([string]::IsNullOrWhiteSpace([string]$item.VersionId)) {
            $warnings.Add("$($item.Name): installed Modrinth version ID is missing")
            continue
        }
        $versionsToFetch[[string]$item.VersionId] = [pscustomobject]@{ Item = $item; ProjectId = $projectId }
    }
    if ($versionsToFetch.Count) {
        try {
            $resolvedVersions = @(Get-ModrinthVersionsByIds -VersionIds @($versionsToFetch.Keys))
            $resolvedById = @{}
            foreach ($version in $resolvedVersions) { $resolvedById[[string](Get-MpPropertyValue -InputObject $version -Name id)] = $version }
            foreach ($versionId in $versionsToFetch.Keys) {
                $pending = $versionsToFetch[$versionId]
                if ($resolvedById.ContainsKey($versionId)) { $currentVersions[$pending.ProjectId] = $resolvedById[$versionId] }
                else { $warnings.Add("$($pending.Item.Name): installed version $versionId was not returned by Modrinth") }
            }
        }
        catch {
            $reason = ($_.Exception.Message -split "`r?`n")[0]
            $warnings.Add("Installed Modrinth dependency data could not be verified ($reason)")
        }
    }

    $resultVersions = @{}
    foreach ($key in $currentVersions.Keys) { $resultVersions[$key] = $currentVersions[$key] }
    foreach ($key in $candidateVersions.Keys) { $resultVersions[$key] = $candidateVersions[$key] }
    $baselineIssues = Get-ModpackDependencyGraphIssues -Versions $currentVersions
    $resultIssues = Get-ModpackDependencyGraphIssues -Versions $resultVersions
    $newConflicts = @($resultIssues.Conflicts | Where-Object { $_ -notin $baselineIssues.Conflicts })
    foreach ($existing in $baselineIssues.Conflicts) { $warnings.Add("Pre-existing dependency issue: $existing") }
    foreach ($warning in @($baselineIssues.Warnings; $resultIssues.Warnings)) { $warnings.Add($warning) }

    $uniqueWarnings = @($warnings | Sort-Object -Unique)
    $blockingConflicts = @(if ($Strict) { $resultIssues.Conflicts } else { $newConflicts })
    if ($blockingConflicts.Count -or ($Strict -and $uniqueWarnings.Count)) {
        $details = @($blockingConflicts; $(if ($Strict) { $uniqueWarnings | ForEach-Object { "Unverified: $_" } })) -join '; '
        $message = if ($blockingConflicts.Count) { 'The update would create or retain a known dependency conflict' } else { 'Strict dependency checking could not verify the complete resulting pack' }
        Throw-MpError -Message $message -Details $details -Hint 'choose another compatible version or rerun without --strict when only incomplete metadata is reported' -ErrorId 'Compatibility.UpdateBlocked' -Category InvalidOperation -TargetObject $Project.Id
    }
    return [pscustomobject]@{ Checked = $resultVersions.Count; Warnings = $uniqueWarnings; Complete = ($uniqueWarnings.Count -eq 0 -and @($resultIssues.Conflicts).Count -eq 0); Candidates = $candidateVersions }
}
