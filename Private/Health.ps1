function Get-MpProjectHealth {
    param($Project, [switch]$Check)
    $tree = Get-MpTreeState $Project.Root
    $fingerprint = Get-MpHash ((@($tree.Keys | Where-Object { $_ -notlike 'dist/*' } | Sort-Object | ForEach-Object { "$_=$($tree[$_])" }) -join "`n") + '|validator=1')
    $path = Join-Path (Get-ModpackToolsConfigDirectory) ('health/' + (Get-MpHash $Project.Root.ToLowerInvariant()) + '.json')
    if (-not $Check -and [IO.File]::Exists($path)) {
        try {
            $cached = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
            if ($cached.Fingerprint -eq $fingerprint -and [datetime]$cached.CreatedUtc -gt [datetime]::UtcNow.AddHours(-24)) { return $cached.Report }
        } catch { Write-Verbose 'Ignoring unreadable health cache' }
    }
    $state = Get-MpProjectState $Project -Check:$Check
    $report = Get-MpGraphReport $Project $state.Nodes
    if ($Check) {
        [void][IO.Directory]::CreateDirectory((Split-Path -Parent $path))
        Write-Utf8TextFileAtomic $path (@{ Fingerprint = $fingerprint; CreatedUtc = [datetime]::UtcNow.ToString('o'); Report = $report } | ConvertTo-Json -Depth 60)
    }
    return $report
}

function Write-MpHealth {
    param($Report)
    $label = if ($Report.Errors.Count) { "$($Report.Errors.Count) dependency issue(s)" } elseif ($Report.Unknown.Count) { 'Incomplete verification' } elseif ($Report.Warnings.Count) { "Verified with $($Report.Warnings.Count) warning(s)" } else { 'Healthy' }
    Write-R3KeyValue (Get-MpConsole) 'Health' $label
    foreach ($item in @(Get-MpHealthDisplayItems $Report)) { Write-MpDoctorItem -Status $item.Status -Text $item.Text }
}

function Get-MpHealthDisplayItems {
    param($Report)
    foreach ($issue in @($Report.Errors)) {
        [pscustomobject]@{ Status = 'fail'; Text = $issue.Message }
    }
    if ($Report.Warnings.Count) {
        [pscustomobject]@{
            Status = 'warn'
            Text = "$($Report.Warnings.Count) optional dependency recommendation(s)."
        }
    }
    if ($Report.Unknown.Count) {
        [pscustomobject]@{
            Status = 'warn'
            Text = "$($Report.Unknown.Count) requirement(s) could not be verified from the available metadata."
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

function Build-ModpackProject {
    param($Project, [switch]$NoRefresh, [switch]$KeepOld, [switch]$RawLog, [switch]$Strict, [switch]$DryRun)
    if ([IO.Path]::GetFileName($Project.OutputName) -cne $Project.OutputName -or -not $Project.OutputName.EndsWith('.mrpack')) { Throw-MpError -Message 'OutputName must be an MRPack filename' -Hint 'repair the project descriptor OutputName' -ErrorId 'Build.InvalidOutputName' -Category InvalidData }
    Assert-ModpackStructure $Project
    $transaction = Invoke-MpProjectTransaction $Project -DryRun:$DryRun -Prepare {
        param($stage)
        $state = Get-MpProjectState $stage -Check
        $report = Get-MpGraphReport $stage $state.Nodes
        Assert-MpGraphPolicy $report -Build -Strict:$Strict
        if ($NoRefresh) {
            $indexIssues = @(Test-MpProjectIndex $stage)
            if ($indexIssues.Count) { Throw-MpError -Message 'The index cannot be exported without refresh' -Details ($indexIssues -join '; ') -Hint 'omit --no-refresh' -ErrorId 'Build.StaleIndex' -Category InvalidData }
        }
        $build = Invoke-MpStagedBuild $stage -NoRefresh:$NoRefresh -KeepOld:$KeepOld -RawLog:$RawLog
        $indexIssues = @(Test-MpProjectIndex $stage)
        if ($indexIssues.Count) { Throw-MpError -Message 'Export preparation left an inconsistent index' -Details ($indexIssues -join '; ') -Hint 'inspect the Packwiz refresh result' -ErrorId 'Build.InvalidIndex' -Category InvalidResult }
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
    param($Project)
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
        $health = Get-MpProjectHealth $Project -Check
        $status = if ($health.Errors.Count) { 'fail' } elseif ($health.Unknown.Count -or $health.Warnings.Count) { 'warn' } else { 'pass' }
        $value = if ($health.Errors.Count) { "$($health.Errors.Count) conflict(s)" } elseif ($health.Unknown.Count) { 'Verification incomplete' } elseif ($health.Warnings.Count) { "$($health.Warnings.Count) warning(s)" } else { 'Healthy' }
        New-MpDoctorCheck PROJECT $status Dependencies $value -Items @(Get-MpHealthDisplayItems $health)
    } catch { New-MpDoctorCheck PROJECT fail Dependencies $_.Exception.Message }
}

function Invoke-MpDoctor {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Show-MpHelp doctor; return }
    $parsed = ConvertFrom-MpOptions $Arguments -ValueOptions @('project') -SwitchOptions @('fix','yes','dry-run','strict','allow-downgrade')
    Assert-PositionalCount $parsed.Positionals -Minimum 0 -Maximum 0 -Usage 'modpack doctor [--project <id>] [--fix] [--yes]'
    if ($parsed.Options.ContainsKey('yes') -and -not $parsed.Options.ContainsKey('fix')) { Throw-MpError -Message "Option '--yes' requires '--fix'" -Hint 'modpack doctor --fix --yes' -ErrorId 'Option.RequiredCombination' -Category InvalidArgument }
    $project = $null; $projectError = $null
    if ($parsed.Options.ContainsKey('project') -or $script:ActiveProjectId) {
        try { $project = Resolve-MpCommandProject $parsed.Options } catch { $projectError = $_.Exception.Message }
    }
    if ($parsed.Options.ContainsKey('fix')) {
        if (-not $parsed.Options.ContainsKey('dry-run')) { Repair-MpDoctorEnvironment -Yes:$parsed.Options.ContainsKey('yes') }
        if ($project) {
            $preview = Invoke-MpContentOperation $project -Operation repair -DryRun -Strict:$parsed.Options.ContainsKey('strict') -AllowDowngrade:$parsed.Options.ContainsKey('allow-downgrade')
            Write-MpContentPlan $preview.Result
            Write-MpTransactionSummary $preview -DryRun
            if (-not $parsed.Options.ContainsKey('dry-run') -and (Confirm-MpDoctorAction -Prompt 'Apply the project repair?' -Yes:$parsed.Options.ContainsKey('yes'))) {
                # Recompute under the project lock; a changed plan must be reviewed again.
                $repair = Invoke-MpContentOperation $project -Operation repair -ExpectedChanges $preview.Changes -Strict:$parsed.Options.ContainsKey('strict') -AllowDowngrade:$parsed.Options.ContainsKey('allow-downgrade')
                Write-MpTransactionSummary $repair
            }
        }
    }
    $report = Get-MpDoctorReport
    if ($project) { $report.Checks += @(Get-MpProjectDoctorChecks $project) }
    if ($projectError) { $report.Checks += New-MpDoctorCheck PROJECT fail Project $projectError }
    $report.Failures = @($report.Checks | Where-Object Status -eq fail).Count
    $report.Warnings = @($report.Checks | Where-Object Status -eq warn).Count
    Write-MpDoctorReport $report
}
