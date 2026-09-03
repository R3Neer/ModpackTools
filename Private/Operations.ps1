function Set-MpTomlValue {
    param([AllowEmptyString()][string]$Text, [string]$Key, [string]$Value, [string]$Section = '')
    $encoded = '"' + (ConvertTo-TomlBasicString $Value) + '"'
    if (-not $Section) { return Set-MpTomlLiteral $Text $Key $encoded }
    $newline = if ($Text.Contains("`r`n")) { "`r`n" } else { "`n" }
    $header = [regex]::Match($Text, '(?m)^[ \t]*\[' + [regex]::Escape($Section) + '\][^\r\n]*(?:\r?\n|$)')
    if (-not $header.Success) { return $Text.TrimEnd() + "$newline$newline[$Section]$newline$Key = $encoded$newline" }
    $start = $header.Index + $header.Length
    $next = [regex]::Match($Text.Substring($start), '(?m)^[ \t]*\[')
    $end = if ($next.Success) { $start + $next.Index } else { $Text.Length }
    $body = $Text.Substring($start, $end - $start)
    $pattern = '(?m)^[ \t]*' + [regex]::Escape($Key) + '[ \t]*=[^\r\n]*'
    $match = [regex]::Match($body, $pattern)
    if ($match.Success) { $body = $body.Substring(0,$match.Index) + "$Key = $encoded" + $body.Substring($match.Index + $match.Length) }
    else { $body = "$Key = $encoded$newline" + $body }
    return $Text.Substring(0,$start) + $body + $Text.Substring($end)
}

function Invoke-MpContentOperation {
    param($Project, [string[]]$Selectors = @(), [string]$Operation, [switch]$All, [string]$Type = 'all',
        [string]$To, [switch]$Strict, [switch]$AllowDowngrade, [switch]$DryRun, [string]$Category,
        [switch]$Enable, [int]$Position = 0, [AllowNull()][array]$ExpectedChanges = $null)
    $transaction = Invoke-MpProjectTransaction $Project -DryRun:$DryRun -ExpectedChanges $ExpectedChanges -Prepare {
        param($stage)
        $targets = @(); $providerChanged = $false
        $initial = Get-MpProjectState $stage -Check
        $baseline = Get-MpGraphReport $stage $initial.Nodes
        if ($Operation -eq 'update') {
            $targets = if ($All) { @(Get-ModpackUpdateItems $initial.Inventory -Type $Type | Where-Object { $_.Source -eq 'packwiz' -and -not $initial.Nodes[$_.Id].Pinned }) } else { @(Resolve-ModpackUpdateSelectors $stage $Selectors -Type $Type) }
            foreach ($target in $targets) {
                if ($initial.Nodes[$target.Id].Pinned) { Throw-MpError -Message "'$($target.Name)' is pinned" -Hint "modpack unpin $($target.Id)" -ErrorId 'Compatibility.Pinned' -Category InvalidOperation }
                if (-not $target.Id.StartsWith('modrinth:')) {
                    if ($To) { Throw-MpError -Message 'Exact version selection requires Modrinth' -Hint 'omit --to for this provider' -ErrorId 'Versions.UnsupportedProvider' -Category InvalidArgument }
                    $stem = [IO.Path]::GetFileName($target.MetadataPath) -replace '\.pw\.toml$',''
                    $providerChanged = $true
                    Invoke-Packwiz -Arguments @('update',$stem,'--yes') -WorkingDirectory $stage.Root | Out-Null
                }
            }
        }
        $planningState = if ($providerChanged) { Get-MpProjectState $stage -Check } else { $initial }
        $plan = New-MpContentPlan $stage -State $planningState -Selectors $Selectors -Operation $Operation -All:$All -Type $Type -To $To -Strict:$Strict -AllowDowngrade:$AllowDowngrade
        if ($Category) {
            $categoryId = Resolve-ModpackCategoryId $stage $Category -AllowUnclassified
            if (@($plan.Requested | Where-Object { $plan.Nodes[$_].Item.Kind -ne 'mod' }).Count) {
                Throw-MpError -Message '--category applies only to requested mods' -Hint 'remove --category from mixed content batches' -ErrorId 'Option.CategoryRequiresMod' -Category InvalidArgument
            }
        }
        if ($Enable -and @($plan.Requested | Where-Object { $plan.Nodes[$_].Item.Kind -ne 'resourcepack' }).Count) {
            Throw-MpError -Message '--enable requires a resource pack batch' -Hint 'add mods and shaders separately' -ErrorId 'Option.EnableRequiresResource' -Category InvalidArgument
        }
        Set-MpPlannedContent $stage $plan
        if ($Category) { foreach ($id in $plan.Requested) { Set-ModpackModClassification $stage $id $categoryId | Out-Null } }
        if ($Enable) {
            $inventory = Get-ModpackInventory $stage
            $resources = @($plan.Requested | ForEach-Object { $id = $_; ($inventory.ResourcePacks | Where-Object Id -eq $id).Filename })
            Set-MpResourceBlock $stage $resources enable $Position
        }
        $after = Get-MpProjectState $stage
        foreach ($change in $plan.Changes) {
            if (-not $after.Nodes.ContainsKey($change.Id) -or $after.Nodes[$change.Id].VersionId -cne $change.After.VersionId) {
                Throw-MpError -Message 'Materialized content differs from the frozen plan' -Hint 'inspect the generated metadata' -ErrorId 'Content.PlanMismatch' -Category InvalidResult
            }
        }
        $report = Get-MpGraphReport $stage $after.Nodes
        Assert-MpGraphPolicy $report -Baseline $(if ($Operation -eq 'repair') { $null } else { $baseline }) -Strict:$Strict
        if ($Category -or ($Operation -eq 'add' -and $plan.Requested.Count) -or $plan.Changes.Count -or $Enable -or $Operation -eq 'repair' -or @($targets).Count) {
            Invoke-Packwiz -Arguments @('refresh') -WorkingDirectory $stage.Root | Out-Null
        }
        return $plan
    }
    return $transaction
}

function Invoke-MpAdd {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Show-MpHelp add; return }
    $parsed = ConvertFrom-MpOptions $Arguments -ValueOptions @('project','category','position') -SwitchOptions @('dry-run','strict','allow-downgrade','enable')
    Assert-PositionalCount $parsed.Positionals -Minimum 1 -Maximum ([int]::MaxValue) -Usage 'modpack add <selector...> [options]'
    if ($parsed.Positionals[0] -eq 'mod') { Throw-MpError -Message "Argument 'mod' is not valid after 'modpack add'" -Hint 'modpack add <selector...>' -ErrorId 'Command.LegacyAddSyntax' -Category InvalidArgument }
    if ($parsed.Positionals -contains 'category') { Throw-MpError -Message "Option 'category' must start with '--'" -Hint '--category' -ErrorId 'Option.MissingPrefix' -Category InvalidArgument }
    $position = 0
    if ($parsed.Options.ContainsKey('enable') -ne $parsed.Options.ContainsKey('position')) { Throw-MpError -Message '--enable and --position must be used together' -Hint 'add --enable --position <n>' -ErrorId 'Option.RequiredCombination' -Category InvalidArgument }
    if ($parsed.Options.ContainsKey('position') -and (-not [int]::TryParse([string]$parsed.Options.position, [ref]$position) -or $position -lt 1)) { Throw-MpError -Message 'Position must be a positive integer' -Hint '--position <n>' -ErrorId 'Option.InvalidPosition' -Category InvalidArgument }
    $project = Resolve-MpCommandProject $parsed.Options
    $category = if ($parsed.Options.ContainsKey('category')) { [string]$parsed.Options.category } else { $null }
    $selectors = @($parsed.Positionals | ForEach-Object { $reference = Resolve-ModrinthSearchNumber $_ $project; if ($reference) { 'modrinth:' + $reference.ProjectId } else { $_ } })
    $result = Invoke-MpContentOperation $project -Operation add -Selectors $selectors -Category $category -Enable:$parsed.Options.ContainsKey('enable') -Position $position -Strict:$parsed.Options.ContainsKey('strict') -AllowDowngrade:$parsed.Options.ContainsKey('allow-downgrade') -DryRun:$parsed.Options.ContainsKey('dry-run')
    Write-MpContentPlan $result.Result
    Write-MpTransactionSummary $result -DryRun:$parsed.Options.ContainsKey('dry-run')
}

function Invoke-MpUpdate {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Show-MpHelp update; return }
    $parsed = ConvertFrom-MpOptions $Arguments -ValueOptions @('project','type','to') -SwitchOptions @('all','dry-run','strict','allow-downgrade')
    if (($parsed.Options.ContainsKey('all') -and $parsed.Positionals.Count) -or (-not $parsed.Options.ContainsKey('all') -and -not $parsed.Positionals.Count)) { Throw-MpError -Message 'Specify selectors or --all' -Hint 'modpack update --help' -ErrorId 'Option.ForbiddenCombination' -Category InvalidArgument }
    if ($parsed.Options.ContainsKey('to') -and ($parsed.Options.ContainsKey('all') -or $parsed.Positionals.Count -ne 1)) { Throw-MpError -Message '--to requires exactly one selector' -Hint 'modpack update <selector> --to <version>' -ErrorId 'Option.VersionTargetConflict' -Category InvalidArgument }
    $project = Resolve-MpCommandProject $parsed.Options
    $type = if ($parsed.Options.ContainsKey('type')) { Resolve-InventoryType $parsed.Options.type } else { 'all' }
    $kinds = if ($type -eq 'all') { @('mod','resourcepack','shaderpack') } else { @($type) }
    $selectors = @(Resolve-MpBatchSelectors $project $parsed.Positionals -Kinds $kinds -RequirePackwiz)
    $to = if ($parsed.Options.ContainsKey('to')) { [string]$parsed.Options.to } else { $null }
    $result = Invoke-MpContentOperation $project -Operation update -Selectors $selectors -All:$parsed.Options.ContainsKey('all') -Type $type -To $to -Strict:$parsed.Options.ContainsKey('strict') -AllowDowngrade:$parsed.Options.ContainsKey('allow-downgrade') -DryRun:$parsed.Options.ContainsKey('dry-run')
    Write-MpContentPlan $result.Result
    Write-MpTransactionSummary $result -DryRun:$parsed.Options.ContainsKey('dry-run')
}

function Write-MpContentPlan {
    param($Plan)
    foreach ($change in $Plan.Changes) {
        $previous = if ($change.Before) { $change.Before.VersionId } else { 'not installed' }
        Write-R3Status (Get-MpConsole) info "$($change.After.Item.Name): $previous -> $($change.After.VersionId) ($($change.Reason))"
    }
    foreach ($issue in $Plan.Report.Issues) { Write-R3Status (Get-MpConsole) info "$($issue.Severity): $($issue.Message)" }
}
