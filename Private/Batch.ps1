function Resolve-MpBatchSelectors {
    param($Project, [string[]]$Selectors, [string[]]$Kinds = @('mod'), [switch]$RequirePackwiz)
    $seen = @{}
    foreach ($selector in $Selectors) {
        $reference = Resolve-ModpackInventoryNumber -Selector $selector -Project $Project -AllowedKinds $Kinds -RequirePackwiz:$RequirePackwiz
        $value = if ($reference) { [string]$reference.Selector } else { $selector }
        if (-not $seen.ContainsKey($value)) { $seen[$value] = $true; $value }
    }
}

function Set-MpResourceBlock {
    param($Project, [string[]]$Selectors, [ValidateSet('enable','move','disable')][string]$Operation, [int]$Position)
    $inventory = Get-ModpackInventory -Project $Project
    [void](Assert-ModpackDefaultOptionsInstalled -Project $Project -Inventory $inventory)
    $targets = @(); $ids = @()
    foreach ($selector in $Selectors) {
        $target = Resolve-ModpackResourcePack -Inventory $inventory -Selector $selector
        if ($target.DefaultId -notin $ids) { $targets += $target; $ids += $target.DefaultId }
    }
    if ($Operation -eq 'move' -and @($targets | Where-Object { -not $_.Active }).Count) {
        Throw-MpError -Message 'Every resource pack in a move must already be enabled' -Hint 'use resource enable for disabled packs' -ErrorId 'ResourcePack.NotEnabled' -Category InvalidOperation
    }
    $remaining = @($inventory.ActiveResources | Where-Object { $_.Id -notin $ids } | ForEach-Object Id)
    $ordered = [Collections.Generic.List[string]]::new()
    foreach ($id in $remaining) { $ordered.Add($id) }
    if ($Operation -ne 'disable') {
        if ($Position -lt 1 -or $Position -gt $remaining.Count + 1) {
            Throw-MpError -Message "Block position must be between 1 and $($remaining.Count + 1)" -Hint 'choose a position in the list after removing the selected packs' -ErrorId 'ResourcePack.InvalidPosition' -Category InvalidArgument
        }
        $ordered.InsertRange($Position - 1, [string[]]$ids)
    }
    if (($ordered -join "`0") -cne (@($inventory.ActiveResources.Id) -join "`0")) {
        Set-DefaultResourcePackOrder -Project $Project -Ids @($ordered)
    }
}

function Invoke-MpResource {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Show-MpHelp resource; return }
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments -ValueOptions @('project','position') -SwitchOptions @('dry-run')
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 2 -Maximum ([int]::MaxValue) -Usage 'modpack resource enable|move|disable <selector...> [--position n]'
    $operation = $parsed.Positionals[0].ToLowerInvariant()
    if ($operation -notin @('enable','move','disable')) {
        Throw-MpError -Message "Unknown resource operation '$operation'" -Hint 'modpack resource --help' -ErrorId 'ResourcePack.UnknownOperation' -Category InvalidArgument
    }
    $position = 0
    if ($operation -ne 'disable') {
        if (-not $parsed.Options.ContainsKey('position') -or -not [int]::TryParse([string]$parsed.Options.position, [ref]$position) -or $position -lt 1) {
            Throw-MpError -Message 'A positive --position is required' -Hint '--position <n>' -ErrorId 'Option.InvalidPosition' -Category InvalidArgument
        }
    }
    elseif ($parsed.Options.ContainsKey('position')) {
        Throw-MpError -Message 'Disable does not accept --position' -Hint 'remove --position' -ErrorId 'Option.ForbiddenCombination' -Category InvalidArgument
    }
    $project = Resolve-MpCommandProject -Options $parsed.Options
    $selectors = @(Resolve-MpBatchSelectors -Project $project -Selectors $parsed.Positionals[1..($parsed.Positionals.Count - 1)] -Kinds @('resourcepack'))
    $transaction = Invoke-MpProjectTransaction -Project $project -DryRun:$parsed.Options.ContainsKey('dry-run') -Prepare {
        param($stage)
        Set-MpResourceBlock -Project $stage -Selectors $selectors -Operation $operation -Position $position
    }
    Write-MpTransactionSummary $transaction -DryRun:$parsed.Options.ContainsKey('dry-run')
    if (-not $parsed.Options.ContainsKey('dry-run')) { Write-ResourcePackInventory -Inventory (Get-ModpackInventory $project) -HideEmptySections }
}

function Invoke-MpSide {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Show-MpHelp side; return }
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments -ValueOptions @('project') -SwitchOptions @('dry-run')
    Assert-PositionalCount $parsed.Positionals -Minimum 3 -Maximum ([int]::MaxValue) -Usage 'modpack side set <mod...> <client|host|both>'
    if ($parsed.Positionals[0] -ne 'set') {
        Throw-MpError -Message 'Only side set is supported' -Hint 'modpack side --help' -ErrorId 'Content.UnknownSideOperation' -Category InvalidArgument
    }
    $project = Resolve-MpCommandProject $parsed.Options
    $selectors = @(Resolve-MpBatchSelectors $project $parsed.Positionals[1..($parsed.Positionals.Count - 2)])
    $side = $parsed.Positionals[-1]
    $transaction = Invoke-MpProjectTransaction $project -DryRun:$parsed.Options.ContainsKey('dry-run') -Prepare {
        param($stage)
        $before = Get-MpProjectState $stage
        $baseline = Get-MpGraphReport $stage $before.Nodes
        $targets = @($selectors | ForEach-Object { Resolve-ModpackModForClassification $stage $_ } | Sort-Object Id -Unique)
        foreach ($target in $targets) { Set-ModpackModSide $stage $target.Id $side | Out-Null }
        $after = Get-MpProjectState $stage
        Assert-MpGraphPolicy (Get-MpGraphReport $stage $after.Nodes) -Baseline $baseline
        if (@($targets | Where-Object Source -eq packwiz).Count) { Invoke-Packwiz -Arguments @('refresh') -WorkingDirectory $stage.Root | Out-Null }
    }
    Write-MpTransactionSummary $transaction -DryRun:$parsed.Options.ContainsKey('dry-run')
}

function Invoke-MpClassifyBatch {
    param([string]$Operation, [object[]]$Arguments)
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments -ValueOptions @('project') -SwitchOptions @('dry-run','unclassify')
    $minimum = if ($Operation -eq 'set') { 2 } else { 1 }
    Assert-PositionalCount $parsed.Positionals -Minimum $minimum -Maximum ([int]::MaxValue) -Usage "modpack classify $Operation <selectors...>"
    if ($Operation -eq 'set' -and $parsed.Options.ContainsKey('unclassify')) {
        Throw-MpError -Message '--unclassify is only valid for classify remove' -Hint 'use category unclassified to clear assignments' -ErrorId 'Option.ForbiddenCombination' -Category InvalidArgument
    }
    $project = Resolve-MpCommandProject $parsed.Options
    if ($Operation -eq 'set') {
        $category = Resolve-ModpackCategoryId $project $parsed.Positionals[-1] -AllowUnclassified
        $selectors = @(Resolve-MpBatchSelectors $project $parsed.Positionals[0..($parsed.Positionals.Count - 2)])
    }
    else { $selectors = @($parsed.Positionals | ForEach-Object { Resolve-ModpackCategoryId $project $_ } | Select-Object -Unique) }
    $transaction = Invoke-MpProjectTransaction $project -DryRun:$parsed.Options.ContainsKey('dry-run') -Prepare {
        param($stage)
        if ($Operation -eq 'set') {
            $targets = @($selectors | ForEach-Object { Resolve-ModpackModForClassification $stage $_ } | Sort-Object Id -Unique)
            foreach ($target in $targets) { Set-ModpackModClassification $stage $target.Id $category | Out-Null }
        }
        else { foreach ($selector in $selectors) { Remove-ModpackCategory $stage $selector -Unclassify:$parsed.Options.ContainsKey('unclassify') | Out-Null } }
    }
    Write-MpTransactionSummary $transaction -DryRun:$parsed.Options.ContainsKey('dry-run')
    if (-not $parsed.Options.ContainsKey('dry-run')) {
        $view = Get-ModpackCategoryView $project
        Write-ModpackCategoryCache $view
    }
}

function Invoke-MpPinOperation {
    param([object[]]$Arguments, [bool]$Pinned)
    $command = if ($Pinned) { 'pin' } else { 'unpin' }
    if ($Arguments -contains '--help') { Show-MpHelp $command; return }
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments -ValueOptions @('project') -SwitchOptions @('dry-run')
    Assert-PositionalCount $parsed.Positionals -Minimum 1 -Maximum ([int]::MaxValue) -Usage "modpack $command <selector...>"
    $project = Resolve-MpCommandProject $parsed.Options
    $selectors = @(Resolve-MpBatchSelectors $project $parsed.Positionals -Kinds @('mod','resourcepack','shaderpack') -RequirePackwiz)
    $transaction = Invoke-MpProjectTransaction $project -DryRun:$parsed.Options.ContainsKey('dry-run') -Prepare {
        param($stage)
        $targets = @(Resolve-ModpackUpdateSelectors $stage $selectors)
        foreach ($target in $targets) {
            $text = Get-Content -LiteralPath $target.MetadataPath -Raw
            $text = Set-MpTomlLiteral -Text $text -Key 'pin' -Value $Pinned.ToString().ToLowerInvariant()
            Write-Utf8TextFileAtomic $target.MetadataPath $text
        }
        Invoke-Packwiz -Arguments @('refresh') -WorkingDirectory $stage.Root | Out-Null
    }
    Write-MpTransactionSummary $transaction -DryRun:$parsed.Options.ContainsKey('dry-run')
}

function Invoke-MpPin { param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @()); Invoke-MpPinOperation -Arguments $Arguments -Pinned $true }
function Invoke-MpUnpin { param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @()); Invoke-MpPinOperation -Arguments $Arguments -Pinned $false }

function Set-MpTomlLiteral {
    param([string]$Text, [string]$Key, [string]$Value)
    $newline = if ($Text.Contains("`r`n")) { "`r`n" } else { "`n" }
    $header = [regex]::Match($Text, '(?m)^\s*\[')
    $end = if ($header.Success) { $header.Index } else { $Text.Length }
    $prefix = $Text.Substring(0, $end)
    $pattern = '(?m)^' + [regex]::Escape($Key) + '[ \t]*=[^\r\n]*'
    if ([regex]::IsMatch($prefix, $pattern)) { $prefix = [regex]::Replace($prefix, $pattern, "$Key = $Value") }
    else { $prefix = "$Key = $Value$newline" + $prefix }
    return $prefix + $Text.Substring($end)
}
