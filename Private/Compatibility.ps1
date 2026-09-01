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

function Get-ModrinthVersionById {
    param([Parameter(Mandatory)][string]$VersionId)
    return Invoke-ModrinthApiRequest -PathAndQuery ("version/" + [System.Uri]::EscapeDataString($VersionId)) -FailureLabel 'dependency lookup'
}

function Test-ModpackUpdatePreflight {
    param(
        [Parameter(Mandatory)]$Project,
        [Parameter(Mandatory)][array]$Targets,
        $ExactVersion,
        [switch]$Strict
    )

    $warnings = [System.Collections.Generic.List[string]]::new()
    $conflicts = [System.Collections.Generic.List[string]]::new()
    $finalVersions = @{}
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
                $conflicts.Add("$($target.Name): no compatible version exists for Minecraft $($Project.MinecraftVersion) and $($Project.Loader)")
                continue
            }
            $finalVersions[$projectId] = $candidate
        }
        catch {
            $warnings.Add("$($target.Name): candidate dependency data could not be verified ($($_.Exception.Message -split "`r?`n")[0])")
        }
    }

    foreach ($item in $installedItems) {
        if (-not ([string]$item.Id).StartsWith('modrinth:', [System.StringComparison]::OrdinalIgnoreCase)) {
            if ($targetIds -contains $item.Id) { continue }
            $warnings.Add("$($item.Name): installed provider is not covered by the Modrinth dependency check")
            continue
        }
        $projectId = Get-ModrinthProjectIdFromItem -Item $item
        if ($finalVersions.ContainsKey($projectId)) { continue }
        if ([string]::IsNullOrWhiteSpace([string]$item.VersionId)) {
            $warnings.Add("$($item.Name): installed Modrinth version ID is missing")
            continue
        }
        try { $finalVersions[$projectId] = Get-ModrinthVersionById -VersionId $item.VersionId }
        catch { $warnings.Add("$($item.Name): installed dependency data could not be verified ($($_.Exception.Message -split "`r?`n")[0])") }
    }

    foreach ($ownerProjectId in @($finalVersions.Keys)) {
        $owner = $finalVersions[$ownerProjectId]
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
            $present = $finalVersions.ContainsKey($dependencyProjectId)
            $actualVersionId = if ($present) { [string](Get-MpPropertyValue -InputObject $finalVersions[$dependencyProjectId] -Name id) } else { $null }
            if ($type -eq 'required') {
                if (-not $present) { $conflicts.Add("$ownerName requires missing Modrinth project $dependencyProjectId") }
                elseif ($dependencyVersionId -and -not $actualVersionId.Equals($dependencyVersionId, [System.StringComparison]::OrdinalIgnoreCase)) {
                    $conflicts.Add("$ownerName requires version $dependencyVersionId of $dependencyProjectId, but the resulting pack would use $actualVersionId")
                }
            }
            elseif ($present -and (-not $dependencyVersionId -or $actualVersionId.Equals($dependencyVersionId, [System.StringComparison]::OrdinalIgnoreCase))) {
                $scope = if ($dependencyVersionId) { "version $dependencyVersionId of $dependencyProjectId" } else { "project $dependencyProjectId" }
                $conflicts.Add("$ownerName declares $scope incompatible")
            }
        }
    }

    $uniqueWarnings = @($warnings | Sort-Object -Unique)
    $uniqueConflicts = @($conflicts | Sort-Object -Unique)
    if ($uniqueConflicts.Count -or ($Strict -and $uniqueWarnings.Count)) {
        $details = @($uniqueConflicts; $(if ($Strict) { $uniqueWarnings | ForEach-Object { "Unverified: $_" } })) -join '; '
        $message = if ($uniqueConflicts.Count) { 'The update would create a known dependency conflict' } else { 'Strict dependency checking could not verify the complete resulting pack' }
        Throw-MpError -Message $message -Details $details -Hint 'choose another compatible version or rerun without --strict when only incomplete metadata is reported' -ErrorId 'Compatibility.UpdateBlocked' -Category InvalidOperation -TargetObject $Project.Id
    }
    return [pscustomobject]@{ Checked = $finalVersions.Count; Warnings = $uniqueWarnings; Complete = ($uniqueWarnings.Count -eq 0); Candidates = $finalVersions }
}
