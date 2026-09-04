function Get-MpValidatedGraphReport {
    param($Project, $State, [switch]$Check)
    $report = Get-MpGraphReport $Project $State.Nodes
    $context = @{ Project = $Project; Info = @{}; Domains = @{}; DomainKnown = @{}; ProviderAvailability = @{}; Check = [bool]$Check }
    return Resolve-MpProviderAvailabilityReport $context $State.Nodes $report
}

function Get-MpProjectHealth {
    param($Project, [switch]$Check)
    $tree = Get-MpTreeState $Project.Root
    $fingerprint = Get-MpHash ((@($tree.Keys | Where-Object { $_ -notlike 'dist/*' } | Sort-Object | ForEach-Object { "$_=$($tree[$_])" }) -join "`n") + '|validator=3')
    $path = Join-Path (Get-ModpackToolsConfigDirectory) ('health/' + (Get-MpHash $Project.Root.ToLowerInvariant()) + '.json')
    if (-not $Check -and [IO.File]::Exists($path)) {
        try {
            $cached = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
            if ($cached.Fingerprint -eq $fingerprint -and [datetime]$cached.CreatedUtc -gt [datetime]::UtcNow.AddHours(-24)) { return $cached.Report }
        } catch { Write-Verbose 'Ignoring unreadable health cache' }
    }
    $state = Get-MpProjectState $Project -Check:$Check
    $report = Get-MpValidatedGraphReport $Project $state -Check:$Check
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $path))
    Write-Utf8TextFileAtomic $path (@{ Fingerprint = $fingerprint; CreatedUtc = [datetime]::UtcNow.ToString('o'); Report = $report } | ConvertTo-Json -Depth 60)
    return $report
}

function Write-MpHealth {
    param($Report)
    $label = if ($Report.Errors.Count) { "$($Report.Errors.Count) dependency issue(s)" } elseif ($Report.Unknown.Count) { 'Incomplete verification' } elseif ($Report.Warnings.Count) { "Verified with $($Report.Warnings.Count) warning(s)" } else { 'Healthy' }
    Write-R3KeyValue (Get-MpConsole) 'Health' $label
    foreach ($item in @(Get-MpHealthDisplayItems $Report -Compact)) { Write-MpDoctorItem -Status $item.Status -Text $item.Text }
}

function Get-MpHealthIssueCode {
    param($Issue)
    $code = [string](Get-MpPropertyValue $Issue 'Code')
    if ($code) { return $code }
    $message = [string](Get-MpPropertyValue $Issue 'Message')
    if ($message -match '^Multiple bundled versions') { return 'verification.bundled-selection' }
    if ($message -match 'required java ') { return 'environment.java-undeclared' }
    if ($message -match 'provider version ') { return 'provider.version-differs' }
    if ($message -match 'provider metadata exposes no compatible candidate') { return 'provider.no-compatible-candidate' }
    if ($message -match 'Provider dependency metadata is unavailable') { return 'provider.metadata-unavailable' }
    if ((Get-MpPropertyValue $Issue 'Severity') -eq 'warning') { return 'recommendation.optional' }
    return 'verification.requirement'
}

function Merge-MpHealthIssues {
    param([AllowEmptyCollection()][object[]]$Issues = @())
    $groups = [ordered]@{}
    foreach ($issue in @($Issues)) {
        $code = Get-MpHealthIssueCode $issue
        $owner = [string](Get-MpPropertyValue $issue 'Owner')
        $requirement = Get-MpPropertyValue $issue 'Requirement'
        if ($null -ne $requirement -and (Get-MpPropertyValue $requirement 'Target')) {
            $target = [string](Get-MpPropertyValue $requirement 'Target')
            $range = @((Get-MpPropertyValue $requirement 'Range')) -join ' OR '
            $kind = [string](Get-MpPropertyValue $requirement 'Kind')
            $scope = [string](Get-MpPropertyValue $requirement 'Scope')
            $suggested = [string](Get-MpPropertyValue $requirement 'SuggestedVersionId')
            $requirementKey = "$target|$range|$kind|$scope|$suggested"
        }
        else { $requirementKey = [string](Get-MpPropertyValue $issue 'Message') -replace ' \[(client|server)\](?=;|$)', '' }
        $identityOwner = if ($code -eq 'verification.bundled-selection') { '' } else { $owner }
        $key = "$code|$identityOwner|$requirementKey"
        if (-not $groups.Contains($key)) {
            $groups[$key] = [pscustomobject]@{
                Code = $code
                Issue = $issue
                Sides = [Collections.Generic.List[string]]::new()
            }
        }
        $side = [string](Get-MpPropertyValue $issue 'Side')
        if ($side -and -not $groups[$key].Sides.Contains($side)) { $groups[$key].Sides.Add($side) }
    }
    return @($groups.Values)
}

function Get-MpMergedIssueSideText {
    param($Merged)
    $sides = @($Merged.Sides | Where-Object { $_ -in @('client','server') } | Sort-Object -Unique)
    if ($sides.Count -eq 2) { return 'client/server' }
    if ($sides.Count -eq 1) { return $sides[0] }
    return 'both'
}

function Get-MpMergedIssueText {
    param($Merged)
    $message = [string](Get-MpPropertyValue $Merged.Issue 'Message')
    $message = $message -replace ' \[(client|server)\](?=;|$)', ''
    $side = Get-MpMergedIssueSideText $Merged
    if ($side -ne 'both') { return "$message [$side]" }
    return $message
}

function Get-MpRecommendationDisplayItems {
    param([AllowEmptyCollection()][object[]]$Merged = @())
    $owners = @($Merged | Group-Object {
        $name = [string](Get-MpPropertyValue $_.Issue 'OwnerName')
        if ($name) { $name } else {
            $message = [string](Get-MpPropertyValue $_.Issue 'Message')
            if ($message.Contains(':')) { $message.Substring(0, $message.IndexOf(':')) } else { [string](Get-MpPropertyValue $_.Issue 'Owner') }
        }
    } | Sort-Object Name)
    foreach ($owner in $owners) {
        $requirements = foreach ($entry in @($owner.Group | Sort-Object { [string](Get-MpPropertyValue (Get-MpPropertyValue $_.Issue 'Requirement') 'Target') })) {
            $requirement = Get-MpPropertyValue $entry.Issue 'Requirement'
            if ($null -eq $requirement -or -not (Get-MpPropertyValue $requirement 'Target')) { Get-MpMergedIssueText $entry; continue }
            $target = [string](Get-MpPropertyValue $requirement 'Target')
            $range = @((Get-MpPropertyValue $requirement 'Range')) -join ' OR '
            $constraint = if ($range -and $range -ne '*') { " $range" } else { '' }
            "$target$constraint [$(Get-MpMergedIssueSideText $entry)]"
        }
        [pscustomobject]@{ Status = 'info'; Text = "$($owner.Name): $($requirements -join '; ')" }
    }
}

function Get-MpIncompleteGroupText {
    param([string]$Code, [int]$Count)
    switch ($Code) {
        'verification.bundled-selection' { return "$Count bundled mod ID(s) require runtime selection; the loader decides which included version is active." }
        'environment.java-undeclared' { return "$Count Java requirement(s) cannot be checked because JavaVersion is not declared in .modpack/project.psd1." }
        'version-range.unsupported' { return "$Count version range(s) could not be interpreted completely." }
        'provider.version-differs' { return "$Count provider version pointer(s) differ from the installed compatible project version." }
        'provider.no-compatible-candidate' { return "$Count provider requirement(s) expose no compatible candidate for this project." }
        'provider.metadata-unavailable' { return "$Count item(s) do not expose provider dependency metadata." }
        'verification.runtime-override' { return "$Count runtime override file(s) require manual verification." }
        'verification.metadata' { return "$Count metadata result(s) require manual verification." }
        default { return "$Count requirement(s) could not be verified from the available metadata." }
    }
}

function Get-MpHealthDisplayItems {
    param($Report, [switch]$Details, [switch]$Compact)
    foreach ($issue in @($Report.Errors)) {
        [pscustomobject]@{ Status = 'fail'; Text = $issue.Message }
    }
    $recommendations = @(Merge-MpHealthIssues @($Report.Warnings))
    if ($recommendations.Count) {
        [pscustomobject]@{
            Status = 'warn'
            Text = "$($recommendations.Count) optional dependency recommendation(s)."
        }
        if (-not $Compact) { Get-MpRecommendationDisplayItems $recommendations }
    }
    $unknown = @(Merge-MpHealthIssues @($Report.Unknown))
    if ($unknown.Count) {
        [pscustomobject]@{
            Status = 'warn'
            Text = "$($unknown.Count) requirement(s) could not be verified from the available metadata."
        }
        if (-not $Compact) {
            foreach ($group in @($unknown | Group-Object Code | Sort-Object @{ Expression = 'Count'; Descending = $true },Name)) {
                [pscustomobject]@{ Status = 'info'; Text = Get-MpIncompleteGroupText $group.Name $group.Count }
                if ($Details) {
                    foreach ($entry in @($group.Group | Sort-Object { Get-MpMergedIssueText $_ })) {
                        [pscustomobject]@{ Status = 'info'; Text = "  - $(Get-MpMergedIssueText $entry)" }
                    }
                }
            }
            if (-not $Details) { [pscustomobject]@{ Status = 'info'; Text = 'Run: modpack doctor --details' } }
        }
        [pscustomobject]@{
            Status = 'warn'
            Text = 'Verification is incomplete; Fabric Loader may still reject this pack during startup.'
        }
    }
    elseif (-not $Compact) {
        [pscustomobject]@{
            Status = 'info'
            Text = 'Scope: declared dependency metadata only; game startup, mixins and runtime behaviour are not tested.'
        }
    }
}

function Test-MpProjectIndex {
    param($Project)
    $issues = [Collections.Generic.List[string]]::new()
    try {
        $pack = ConvertFrom-MpToml (Get-Content -LiteralPath (Join-Path $Project.Root 'pack.toml') -Raw)
        $indexPath = Resolve-MpContainedPath $Project.Root $Project.IndexFile
        $index = ConvertFrom-MpToml (Get-Content -LiteralPath $indexPath -Raw)
        $algorithm = [string]$pack['index']['hash-format']
        if ($algorithm -notin @('sha256','sha512','sha1','md5')) { $issues.Add('Unsupported index hash algorithm') }
        elseif ((Get-FileHash -LiteralPath $indexPath -Algorithm $algorithm).Hash -ne [string]$pack['index']['hash']) { $issues.Add('pack.toml index hash is stale') }
        if ($index.ContainsKey('files')) {
            foreach ($entry in $index['files']) {
                $path = Resolve-MpContainedPath $Project.Root ([string]$entry['file'])
                $hashAlgorithm = if ($entry.ContainsKey('hash-format')) { [string]$entry['hash-format'] } else { [string]$index['hash-format'] }
                if (-not [IO.File]::Exists($path)) { $issues.Add("Index references missing file $($entry['file'])") }
                elseif ($hashAlgorithm -notin @('sha256','sha512','sha1','md5')) { $issues.Add("Unsupported hash algorithm for $($entry['file'])") }
                elseif ((Get-FileHash -LiteralPath $path -Algorithm $hashAlgorithm).Hash -ne [string]$entry['hash']) { $issues.Add("Index hash is stale for $($entry['file'])") }
            }
        }
    }
    catch { $issues.Add($_.Exception.Message) }
    return @($issues)
}

function Get-MpBuildArtifactStatus {
    param($Project)
    $artifact = Get-MpLatestBuildFile $Project
    if (-not $artifact) {
        return [pscustomobject]@{ State='missing'; Path=''; BuiltUtc=$null; Total=0; Added=@(); Changed=@(); Removed=@(); Error='' }
    }
    $tree = Get-MpTreeState $Project.Root
    $sourceFingerprint = Get-MpHash (@($tree.Keys | Where-Object { $_ -notlike 'dist/*' } | Sort-Object | ForEach-Object { "$_=$($tree[$_])" }) -join "`n")
    $artifactFingerprint = (Get-FileHash -LiteralPath $artifact.FullName -Algorithm SHA256).Hash
    $fingerprint = Get-MpHash "$sourceFingerprint|$artifactFingerprint|artifact-validator=1"
    $cachePath = Join-Path (Get-ModpackToolsConfigDirectory) ('build-health/' + (Get-MpHash $Project.Root.ToLowerInvariant()) + '.json')
    if ([IO.File]::Exists($cachePath)) {
        try {
            $cached = Get-Content -LiteralPath $cachePath -Raw | ConvertFrom-Json
            if ($cached.Fingerprint -eq $fingerprint) { return $cached.Status }
        } catch { Write-Verbose 'Ignoring unreadable build health cache' }
    }
    try {
        $comparison = Compare-MpBuildArtifactToProject $Project $artifact.FullName
        $status = [pscustomobject]@{
            State = $(if ($comparison.Total) { 'stale' } else { 'current' })
            Path = $artifact.FullName
            BuiltUtc = $artifact.LastWriteTimeUtc.ToString('o')
            Total = $comparison.Total
            Added = @($comparison.Added)
            Changed = @($comparison.Changed)
            Removed = @($comparison.Removed)
            Error = ''
        }
        [void][IO.Directory]::CreateDirectory((Split-Path -Parent $cachePath))
        Write-Utf8TextFileAtomic $cachePath (@{ Fingerprint=$fingerprint; Status=$status } | ConvertTo-Json -Depth 30)
        return $status
    }
    catch {
        return [pscustomobject]@{ State='unknown'; Path=$artifact.FullName; BuiltUtc=$artifact.LastWriteTimeUtc.ToString('o'); Total=0; Added=@(); Changed=@(); Removed=@(); Error=$_.Exception.Message }
    }
}

function Get-MpBuildArtifactDoctorCheck {
    param($Project)
    $status = Get-MpBuildArtifactStatus $Project
    if ($status.State -eq 'missing') {
        return New-MpDoctorCheck -Section 'BUILD ARTIFACT' -Status info -Label 'Latest .mrpack' -Value 'Not built' -Detail "Run: modpack build --project $($Project.Id)"
    }
    if ($status.State -eq 'unknown') {
        return New-MpDoctorCheck -Section 'BUILD ARTIFACT' -Status warn -Label 'Latest .mrpack' -Value 'Could not verify' -Detail $status.Error
    }
    if ($status.State -eq 'current') {
        return New-MpDoctorCheck -Section 'BUILD ARTIFACT' -Status pass -Label 'Latest .mrpack' -Value 'Current' -Detail $status.Path
    }
    $items = [Collections.Generic.List[object]]::new()
    foreach ($entry in @($status.Added | Select-Object -First 4)) { $items.Add([pscustomobject]@{ Status='info'; Text="Project adds: $($entry.Path)" }) }
    foreach ($entry in @($status.Changed | Select-Object -First 4)) { $items.Add([pscustomobject]@{ Status='info'; Text="Project changes: $($entry.Path)" }) }
    foreach ($entry in @($status.Removed | Select-Object -First 4)) { $items.Add([pscustomobject]@{ Status='info'; Text="Artifact still contains: $($entry.Path)" }) }
    if ($status.Total -gt $items.Count) { $items.Add([pscustomobject]@{ Status='info'; Text="$($status.Total - $items.Count) additional difference(s)." }) }
    $items.Add([pscustomobject]@{ Status='warn'; Text="Do not install this artifact. Run: modpack build --project $($Project.Id)" })
    $built = ([datetime]$status.BuiltUtc).ToLocalTime().ToString('yyyy-MM-dd HH:mm:ss')
    return New-MpDoctorCheck -Section 'BUILD ARTIFACT' -Status warn -Label 'Latest .mrpack' -Value 'Stale' -Detail "$([IO.Path]::GetFileName($status.Path)); built $built; $($status.Total) difference(s)." -Items @($items)
}

function Build-ModpackProject {
    param($Project, [switch]$NoRefresh, [switch]$KeepOld, [switch]$RawLog, [switch]$Strict, [switch]$DryRun)
    if ([IO.Path]::GetFileName($Project.OutputName) -cne $Project.OutputName -or -not $Project.OutputName.EndsWith('.mrpack')) { Throw-MpError -Message 'OutputName must be an MRPack filename' -Hint 'repair the project descriptor OutputName' -ErrorId 'Build.InvalidOutputName' -Category InvalidData }
    Assert-ModpackStructure $Project
    $transaction = Invoke-MpProjectTransaction $Project -DryRun:$DryRun -Prepare {
        param($stage)
        $state = Get-MpProjectState $stage -Check
        $report = Get-MpValidatedGraphReport $stage $state -Check
        Assert-MpGraphPolicy $report -Build -Strict:$Strict
        if ($NoRefresh) {
            $indexIssues = @(Test-MpProjectIndex $stage)
            if ($indexIssues.Count) { Throw-MpError -Message 'The index cannot be exported without refresh' -Details ($indexIssues -join '; ') -Hint 'omit --no-refresh' -ErrorId 'Build.StaleIndex' -Category InvalidData }
        }
        $build = Invoke-MpStagedBuild $stage -NoRefresh:$NoRefresh -KeepOld:$KeepOld -RawLog:$RawLog
        $indexIssues = @(Test-MpProjectIndex $stage)
        if ($indexIssues.Count) { Throw-MpError -Message 'Export preparation left an inconsistent index' -Details ($indexIssues -join '; ') -Hint 'inspect the Packwiz refresh result' -ErrorId 'Build.InvalidIndex' -Category InvalidResult }
        $artifactComparison = Compare-MpBuildArtifactToProject $stage $build.Path
        if ($artifactComparison.Total) {
            Throw-MpError -Message 'The generated artifact does not match the prepared project' -Details "$($artifactComparison.Total) manifest or override difference(s) remained after export" -Hint 'inspect the Packwiz export and retry' -ErrorId 'Build.ArtifactMismatch' -Category InvalidResult
        }
        $build | Add-Member -NotePropertyName Health -NotePropertyValue $report
        return $build
    }
    $result = $transaction.Result
    $result.Path = Join-Path (Join-Path $Project.Root 'dist') ([IO.Path]::GetFileName($result.Path))
    $result.Inventory = Get-ModpackInventory $Project
    $result | Add-Member -NotePropertyName DryRun -NotePropertyValue ([bool]$DryRun)
    return $result
}

function Get-MpProjectDoctorChecks {
    param($Project, $HealthReport, [switch]$Details)
    foreach ($relative in @('pack.toml', $Project.IndexFile, '.modpack/project.psd1', '.modpack/metadata.psd1')) {
        $present = Test-Path -LiteralPath (Join-Path $Project.Root $relative) -PathType Leaf
        New-MpDoctorCheck -Section PROJECT -Status $(if ($present) { 'pass' } else { 'fail' }) -Label $relative -Value $(if ($present) { 'Present' } else { 'Missing' })
    }
    try {
        $metadata = Get-ModpackMetadata $Project
        $invalid = @($metadata.Mods.Keys | Where-Object { $metadata.Mods[$_].ContainsKey('Category') -and -not $metadata.Categories.ContainsKey($metadata.Mods[$_].Category) })
        New-MpDoctorCheck PROJECT $(if ($invalid.Count) { 'fail' } else { 'pass' }) 'Editorial metadata' $(if ($invalid.Count) { 'Unknown category assignments: ' + ($invalid -join ', ') } else { 'Valid' })
    } catch { New-MpDoctorCheck PROJECT fail Metadata $_.Exception.Message }
    $index = @(Test-MpProjectIndex $Project)
    New-MpDoctorCheck PROJECT $(if ($index.Count) { 'fail' } else { 'pass' }) Index $(if ($index.Count) { $index -join '; ' } else { 'Hashes match' })
    try {
        $health = if ($HealthReport) { $HealthReport } else { Get-MpProjectHealth $Project }
        $status = if ($health.Errors.Count) { 'fail' } elseif ($health.Unknown.Count -or $health.Warnings.Count) { 'warn' } else { 'pass' }
        $value = if ($health.Errors.Count) { "$($health.Errors.Count) conflict(s)" } elseif ($health.Unknown.Count) { 'Verification incomplete' } elseif ($health.Warnings.Count) { "$($health.Warnings.Count) warning(s)" } else { 'Healthy' }
        New-MpDoctorCheck PROJECT $status Dependencies $value -Items @(Get-MpHealthDisplayItems $health -Details:$Details)
    } catch { New-MpDoctorCheck PROJECT fail Dependencies $_.Exception.Message }
    try { Get-MpBuildArtifactDoctorCheck $Project }
    catch { New-MpDoctorCheck 'BUILD ARTIFACT' warn 'Latest .mrpack' 'Could not verify' $_.Exception.Message }
}

function Invoke-MpDoctor {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Show-MpHelp doctor; return }
    $parsed = ConvertFrom-MpOptions $Arguments -ValueOptions @('project') -SwitchOptions @('fix','yes','dry-run','strict','allow-downgrade','details')
    Assert-PositionalCount $parsed.Positionals -Minimum 0 -Maximum 0 -Usage 'modpack doctor [--project <id>] [--fix] [--yes]'
    if ($parsed.Options.ContainsKey('yes') -and -not $parsed.Options.ContainsKey('fix')) { Throw-MpError -Message "Option '--yes' requires '--fix'" -Hint 'modpack doctor --fix --yes' -ErrorId 'Option.RequiredCombination' -Category InvalidArgument }
    $project = $null; $projectError = $null
    if ($parsed.Options.ContainsKey('project') -or $script:ActiveProjectId) {
        try { $project = Resolve-MpCommandProject $parsed.Options } catch { $projectError = $_.Exception.Message }
    }
    $doctorHealth = $null
    if ($parsed.Options.ContainsKey('fix')) {
        if (-not $parsed.Options.ContainsKey('dry-run')) { Repair-MpDoctorEnvironment -Yes:$parsed.Options.ContainsKey('yes') }
        if ($project) {
            $preview = Invoke-MpContentOperation $project -Operation repair -DryRun -Strict:$parsed.Options.ContainsKey('strict') -AllowDowngrade:$parsed.Options.ContainsKey('allow-downgrade')
            $doctorHealth = $preview.Result.Baseline
            Write-MpContentPlan $preview.Result -SkipHealth
            $hasProjectChanges = @($preview.Changes).Count -gt 0
            if ($parsed.Options.ContainsKey('dry-run')) { Write-MpTransactionSummary $preview -DryRun }
            elseif ($hasProjectChanges) { Write-MpTransactionSummary $preview -Preview }
            else { Write-R3Status (Get-MpConsole) success 'No repairable project changes are required.' }
            if ($hasProjectChanges -and -not $parsed.Options.ContainsKey('dry-run') -and (Confirm-MpDoctorAction -Prompt 'Apply the project repair?' -Yes:$parsed.Options.ContainsKey('yes'))) {
                # Recompute under the project lock; a changed plan must be reviewed again.
                $repair = Invoke-MpContentOperation $project -Operation repair -ExpectedChanges $preview.Changes -Strict:$parsed.Options.ContainsKey('strict') -AllowDowngrade:$parsed.Options.ContainsKey('allow-downgrade')
                Write-MpTransactionSummary $repair
                $doctorHealth = $repair.Result.Report
            }
        }
    }
    $report = Get-MpDoctorReport
    if ($project) { $report.Checks += @(Get-MpProjectDoctorChecks $project $doctorHealth -Details:$parsed.Options.ContainsKey('details')) }
    if ($projectError) { $report.Checks += New-MpDoctorCheck PROJECT fail Project $projectError }
    $report.Failures = @($report.Checks | Where-Object Status -eq fail).Count
    $report.Warnings = @($report.Checks | Where-Object Status -eq warn).Count
    Write-MpDoctorReport $report
}
