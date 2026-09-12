function ConvertFrom-MpOptions {
    param(
        [object[]]$Arguments = @(),
        [string[]]$ValueOptions = @(),
        [string[]]$SwitchOptions = @()
    )

    $options = @{}
    $positionals = [System.Collections.Generic.List[string]]::new()
    for ($i = 0; $i -lt $Arguments.Count; $i++) {
        $token = [string]$Arguments[$i]
        if (-not $token.StartsWith('--')) { $positionals.Add($token); continue }
        $name = $token.Substring(2)
        $inlineValue = $null
        if ($name.Contains('=')) {
            $parts = $name.Split('=', 2)
            $name = $parts[0]
            $inlineValue = $parts[1]
        }
        if ($SwitchOptions -contains $name) {
            if ($null -ne $inlineValue) { Throw-MpError -Message "Option '--$name' does not accept a value" -Hint "use --$name without an attached value" -ErrorId 'Option.UnexpectedValue' -Category InvalidArgument -TargetObject $name }
            $options[$name] = $true
            continue
        }
        if ($ValueOptions -notcontains $name) { Throw-MpError -Message "Option '--$name' is not recognized for this command" -Hint 'run the command with --help' -ErrorId 'Option.Unknown' -Category InvalidArgument -TargetObject $name }
        if ($null -eq $inlineValue) {
            $i++
            if ($i -ge $Arguments.Count -or ([string]$Arguments[$i]).StartsWith('--')) { Throw-MpError -Message "Option '--$name' requires a value" -Hint "--$name <value>" -ErrorId 'Option.MissingValue' -Category InvalidArgument -TargetObject $name }
            $inlineValue = [string]$Arguments[$i]
        }
        $options[$name] = $inlineValue
    }
    [pscustomobject]@{ Options = $options; Positionals = @($positionals) }
}

function Assert-PositionalCount {
    param(
        [array]$Values = @(),
        [int]$Minimum,
        [int]$Maximum,
        [string]$Usage,
        [string[]]$OptionNames = @()
    )
    $count = @($Values).Count
    if ($count -lt $Minimum -or $count -gt $Maximum) {
        $bareOption = @($Values | Where-Object { $OptionNames -contains [string]$_ } | Select-Object -First 1)
        if ($bareOption.Count) {
            Throw-MpError -Message "Option '$($bareOption[0])' must start with '--'" -Hint "--$($bareOption[0])" -ErrorId 'Option.MissingPrefix' -Category InvalidArgument -TargetObject $bareOption[0]
        }
        Throw-MpError -Message 'The command arguments do not match the expected syntax' -Hint $Usage -ErrorId 'Command.InvalidArguments' -Category InvalidArgument -TargetObject $Values
    }
}

function Resolve-MpCommandProject {
    param(
        [Parameter(Mandatory)][hashtable]$Options,
        [AllowNull()][AllowEmptyString()][string]$PositionalId
    )

    $optionId = $script:CommandProjectId
    if ($optionId -and $PositionalId) {
        Throw-MpError -Message "The project was specified both positionally and with '--project'" -Hint "remove one of the two project IDs" -ErrorId 'Option.ProjectConflict' -Category InvalidArgument -TargetObject $optionId
    }
    $id = if ($optionId) { $optionId } else { $PositionalId }
    return Resolve-ModpackProject -Id $id
}

function Resolve-MpSearchProject {
    param([Parameter(Mandatory)][hashtable]$Options)

    if ($script:CommandProjectId) { return Resolve-ModpackProject -Id $script:CommandProjectId }
    if ($script:ActiveProjectId) { return Resolve-ModpackProject -Id $script:ActiveProjectId }
    return $null
}

function Assert-MpNoProjectContext {
    param([string]$Usage)
    if ($script:CommandProjectId) {
        Throw-MpError -Message "Option '--project' is not valid for this operation" -Hint $Usage -ErrorId 'Option.ForbiddenCombination' -Category InvalidArgument -TargetObject $script:CommandProjectId
    }
}

function Get-MpDomainArguments {
    param([string]$Domain, [object[]]$Arguments)
    if (-not $Arguments.Count) {
        Throw-MpError -Message "The '$Domain' command requires an operation" -Hint "modpack $Domain --help" -ErrorId 'Command.MissingOperation' -Category InvalidArgument
    }
    return [pscustomobject]@{ Operation=([string]$Arguments[0]).ToLowerInvariant(); Remaining=@($Arguments | Select-Object -Skip 1) }
}

function Invoke-MpProject {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Show-MpHelp project; return }
    $domain = Get-MpDomainArguments project $Arguments
    switch ($domain.Operation) {
        'list' { Assert-MpNoProjectContext 'modpack project list'; Invoke-MpList -Arguments $domain.Remaining }
        'current' { Assert-MpNoProjectContext 'modpack project current'; Invoke-MpCurrent -Arguments $domain.Remaining }
        'use' { Assert-MpNoProjectContext 'modpack project use <id>'; Invoke-MpUse -Arguments $domain.Remaining }
        'status' { Invoke-MpStatus -Arguments $domain.Remaining }
        'create' { Assert-MpNoProjectContext 'modpack project create --help'; Invoke-MpNew -Arguments $domain.Remaining }
        'register' { Assert-MpNoProjectContext 'modpack project register --help'; Invoke-MpInit -Arguments $domain.Remaining }
        default { Throw-MpError -Message "Project operation '$($domain.Operation)' is not recognized" -Hint 'modpack project --help' -ErrorId 'Project.UnknownOperation' -Category InvalidArgument -TargetObject $domain.Operation }
    }
}

function Invoke-MpContent {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Show-MpHelp content; return }
    $domain = Get-MpDomainArguments content $Arguments
    switch ($domain.Operation) {
        'list' { Invoke-MpInventory -Arguments $domain.Remaining }
        'search' { Invoke-MpSearch -Arguments $domain.Remaining }
        'add' { Invoke-MpAdd -Arguments $domain.Remaining }
        'remove' { Invoke-MpRemove -Arguments $domain.Remaining }
        'versions' { Invoke-MpVersions -Arguments $domain.Remaining }
        'update' { Invoke-MpUpdate -Arguments $domain.Remaining }
        'pin' { Invoke-MpPin -Arguments $domain.Remaining }
        'unpin' { Invoke-MpUnpin -Arguments $domain.Remaining }
        default { Throw-MpError -Message "Content operation '$($domain.Operation)' is not recognized" -Hint 'modpack content --help' -ErrorId 'Content.UnknownOperation' -Category InvalidArgument -TargetObject $domain.Operation }
    }
}

function Invoke-MpList {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    Assert-PositionalCount -Values $Arguments -Minimum 0 -Maximum 0 -Usage 'modpack project list'
    $root = Get-ModpackRoot
    $projects = @(Get-ModpackProjects)
    Write-ModpackList -Projects $projects -Root $root
}

function Invoke-MpCurrent {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    Assert-PositionalCount -Values $Arguments -Minimum 0 -Maximum 0 -Usage 'modpack project current'
    Write-R3Banner (Get-MpConsole) 'ACTIVE PROJECT'
    if ($script:ActiveProjectId) { Write-R3KeyValue (Get-MpConsole) 'ID' $script:ActiveProjectId }
    else { Write-R3Status (Get-MpConsole) info 'There is no active project in this session.' }
}

function Invoke-MpUse {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    Assert-PositionalCount -Values $Arguments -Minimum 1 -Maximum 1 -Usage 'modpack project use <id>'
    $project = Set-ActiveModpackProject -Id ([string]$Arguments[0])
    Write-R3Status (Get-MpConsole) success "Active project: $($project.Id) ($($project.DisplayName))"
}

function Invoke-MpCategory {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Show-MpHelp category; return }
    $domain = Get-MpDomainArguments category $Arguments
    $remaining = $domain.Remaining
    switch ($domain.Operation) {
        'list' {
            $parsed = ConvertFrom-MpOptions -Arguments $remaining
            Assert-PositionalCount -Values $parsed.Positionals -Minimum 0 -Maximum 0 -Usage 'modpack category list'
            $project = Resolve-MpCommandProject -Options $parsed.Options
            $view = Get-ModpackCategoryView -Project $project
            Write-ModpackCategoryCache -View $view
            Write-ModpackCategoryList -View $view
        }
        'create' {
            $parsed = ConvertFrom-MpOptions -Arguments $remaining -ValueOptions @('name', 'order')
            Assert-PositionalCount -Values $parsed.Positionals -Minimum 1 -Maximum 1 -Usage 'modpack category create <id> [--name <name>] [--order <n>]' -OptionNames @('name', 'order')
            $project = Resolve-MpCommandProject -Options $parsed.Options
            $order = 0
            if ($parsed.Options.ContainsKey('order') -and -not [int]::TryParse([string]$parsed.Options.order, [ref]$order)) {
                Throw-MpError -Message "Category order '$($parsed.Options.order)' is not an integer" -Hint '--order <integer>' -ErrorId 'Option.InvalidOrder' -Category InvalidArgument -TargetObject $parsed.Options.order
            }
            $parameters = @{ Project = $project; Id = [string]$parsed.Positionals[0] }
            if ($parsed.Options.ContainsKey('name')) { $parameters.Name = [string]$parsed.Options.name }
            if ($parsed.Options.ContainsKey('order')) { $parameters.Order = $order }
            $created = New-ModpackCategory @parameters
            Write-R3Status (Get-MpConsole) success "Category '$($created.Id)' was created."
            $view = Get-ModpackCategoryView -Project $project
            Write-ModpackCategoryCache -View $view
            Write-ModpackCategoryList -View $view
        }
        'remove' { Invoke-MpCategoryBatch -Operation remove -Arguments $remaining }
        'assign' { Invoke-MpCategoryBatch -Operation assign -Arguments $remaining }
        'clear' { Invoke-MpCategoryBatch -Operation clear -Arguments $remaining }
        default {
            Throw-MpError -Message "Category operation '$($domain.Operation)' is not recognized" -Hint 'modpack category --help' -ErrorId 'Metadata.UnknownCategoryOperation' -Category InvalidArgument -TargetObject $domain.Operation
        }
    }
}

function Invoke-MpStatus {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 0 -Maximum 0 -Usage 'modpack project status'
    $project = Resolve-MpCommandProject -Options $parsed.Options
    Assert-ModpackStructure -Project $project
    $inventory = Get-ModpackInventory -Project $project
    Write-ModpackHeader -Project $project -Inventory $inventory
}

function Invoke-MpInventory {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments `
        -ValueOptions @('type', 'category', 'side', 'source', 'state', 'match') `
        -SwitchOptions @('verify')
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 0 -Maximum 0 -Usage 'modpack content list [filters]' -OptionNames @('type', 'category', 'side', 'source', 'state', 'match', 'verify')
    $project = Resolve-MpCommandProject -Options $parsed.Options
    Assert-ModpackStructure -Project $project
    $inventory = Get-ModpackInventory -Project $project
    $parameters = @{ Inventory = $inventory }
    foreach ($name in @('type', 'category', 'side', 'source', 'state')) {
        if ($parsed.Options.ContainsKey($name)) { $parameters[$name.Substring(0,1).ToUpperInvariant() + $name.Substring(1)] = $parsed.Options[$name] }
    }
    if ($parsed.Options.ContainsKey('match')) { $parameters.Search = $parsed.Options.match }
    if ($parsed.Options.ContainsKey('category')) {
        $parameters.Category = Resolve-ModpackCategoryId -Project $project -Selector ([string]$parsed.Options.category) -AllowUnclassified
    }
    $view = Select-ModpackInventory @parameters
    [void](Set-ModpackInventoryReferences -View $view)

    Write-ModpackHeader -Project $project -Inventory $inventory
    Write-MpHealth (Get-MpProjectHealth $project -Check:$parsed.Options.ContainsKey('verify'))
    Write-InventoryView -View $view -ShowFilters
}

function Invoke-MpBuild {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Show-MpHelp build; return }
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments -SwitchOptions @('no-refresh', 'keep-old', 'open', 'raw-log', 'strict', 'dry-run')
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 0 -Maximum 0 -Usage 'modpack build [options]' -OptionNames @('no-refresh', 'keep-old', 'open', 'raw-log')
    $project = Resolve-MpCommandProject -Options $parsed.Options
    Write-R3Status (Get-MpConsole) step "Building $($project.DisplayName)..."
    $build = Build-ModpackProject -Project $project -NoRefresh:$parsed.Options.ContainsKey('no-refresh') -KeepOld:$parsed.Options.ContainsKey('keep-old') -RawLog:$parsed.Options.ContainsKey('raw-log') -Strict:$parsed.Options.ContainsKey('strict') -DryRun:$parsed.Options.ContainsKey('dry-run')
    Write-MpHealth $build.Health
    if ($build.DryRun) { Write-R3Status (Get-MpConsole) info 'Dry run: export validated without changing the project.'; return }
    foreach ($line in $build.Log) { Write-R3Line (Get-MpConsole) @(@{Text="$line";Role='secondary'}) }
    Write-ModInventory $build.Inventory
    Write-ResourcePackInventory $build.Inventory
    Write-ShaderInventory $build.Inventory
    Write-BuildSummary $build
    if ($parsed.Options.ContainsKey('open')) { Start-Process explorer.exe -ArgumentList "/select,`"$($build.Path)`"" }
}

function Invoke-MpDiff {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Show-MpHelp diff; return }
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 0 -Maximum 0 -Usage 'modpack diff'
    $project = Resolve-MpCommandProject -Options $parsed.Options
    Assert-ModpackStructure -Project $project
    Write-R3Status (Get-MpConsole) step "Comparing $($project.DisplayName) with its latest build..."
    $diff = Compare-ModpackBuild -Project $project
    Write-ModpackDiff -Diff $diff
}

function Invoke-MpSearch {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments -ValueOptions @('type', 'limit')
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 1 -Maximum 100 -Usage 'modpack content search <query> [--type <type>] [--limit <1-50>]'
    $project = Resolve-MpSearchProject -Options $parsed.Options
    $type = if ($parsed.Options.ContainsKey('type')) { $parsed.Options.type } else { 'all' }
    $limit = 10
    if ($parsed.Options.ContainsKey('limit') -and (-not [int]::TryParse([string]$parsed.Options.limit, [ref]$limit) -or $limit -lt 1 -or $limit -gt 50)) {
        Throw-MpError -Message "Option '--limit' must be an integer from 1 through 50; received '$($parsed.Options.limit)'" -Hint '--limit <1-50>' -ErrorId 'Option.InvalidLimit' -Category InvalidArgument -TargetObject $parsed.Options.limit
    }
    $query = @($parsed.Positionals) -join ' '
    if ([string]::IsNullOrWhiteSpace($query)) { Throw-MpError -Message 'The search query cannot be empty' -Hint 'modpack content search <query>' -ErrorId 'Search.EmptyQuery' -Category InvalidArgument }
    Write-R3Status (Get-MpConsole) step "Searching Modrinth for '$query'..."
    $search = Search-ModrinthContent -Project $project -Query $query -Type $type -Limit $limit
    Write-ModrinthSearchResults -Search $search -Project $project
}

function Invoke-MpVersions {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 1 -Maximum 1 -Usage 'modpack content versions <selector>'
    $project = Resolve-MpCommandProject -Options $parsed.Options
    $rawSelector = [string]$parsed.Positionals[0]
    $reference = Resolve-ModpackInventoryNumber -Selector $rawSelector -Project $project -AllowedKinds @('mod', 'resourcepack', 'shaderpack') -RequirePackwiz
    $selector = if ($reference) { [string]$reference.Selector } else { $rawSelector }
    $item = (Resolve-ModpackUpdateSelectors -Project $project -Selectors @($selector))[0]
    Write-R3Status (Get-MpConsole) step "Finding compatible versions for '$($item.Name)'..."
    $view = Get-ModrinthCompatibleVersions -Project $project -Item $item
    Write-ModrinthVersionResults -View $view -Project $project
}

function Invoke-MpNew {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments -ValueOptions @('name', 'minecraft', 'loader', 'path', 'loader-version', 'pack-version', 'display-version')
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 1 -Maximum 1 -Usage 'modpack project create <id> --name <name> --minecraft <version> --loader <fabric|quilt|forge|neoforge>' -OptionNames @('name', 'minecraft', 'loader', 'path', 'loader-version', 'pack-version', 'display-version')
    foreach ($required in @('name', 'minecraft', 'loader')) {
        if (-not $parsed.Options.ContainsKey($required)) { Throw-MpError -Message "Required option '--$required' is missing" -Hint 'modpack project --help' -ErrorId 'Option.Required' -Category InvalidArgument -TargetObject $required }
    }
    $parameters = @{
        Id = $parsed.Positionals[0]; Name = $parsed.Options.name; MinecraftVersion = $parsed.Options.minecraft; Loader = $parsed.Options.loader
    }
    if ($parsed.Options.ContainsKey('path')) { $parameters.DirectoryName = $parsed.Options.path }
    if ($parsed.Options.ContainsKey('loader-version')) { $parameters.LoaderVersion = $parsed.Options['loader-version'] }
    if ($parsed.Options.ContainsKey('pack-version')) { $parameters.PackVersion = $parsed.Options['pack-version'] }
    if ($parsed.Options.ContainsKey('display-version')) { $parameters.DisplayVersion = $parsed.Options['display-version'] }
    Write-R3Status (Get-MpConsole) step "Creating project '$($parsed.Positionals[0])'..."
    $project = New-ModpackProject @parameters
    Write-R3Status (Get-MpConsole) success "Project created at $($project.Root)"
}

function Invoke-MpInit {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments -ValueOptions @('path', 'display-name', 'display-version', 'output-name')
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 1 -Maximum 1 -Usage 'modpack project register <id> [--path <directory>] [options]' -OptionNames @('path', 'display-name', 'display-version', 'output-name')
    $parameters = @{ Id = $parsed.Positionals[0] }
    if ($parsed.Options.ContainsKey('path')) { $parameters.Path = $parsed.Options.path }
    if ($parsed.Options.ContainsKey('display-name')) { $parameters.DisplayName = $parsed.Options['display-name'] }
    if ($parsed.Options.ContainsKey('display-version')) { $parameters.DisplayVersion = $parsed.Options['display-version'] }
    if ($parsed.Options.ContainsKey('output-name')) { $parameters.OutputName = $parsed.Options['output-name'] }
    $location = if ($parameters.ContainsKey('Path')) { $parameters.Path } else { (Get-Location).Path }
    Write-R3Status (Get-MpConsole) step "Registering Packwiz project '$location' as '$($parsed.Positionals[0])'..."
    $result = Initialize-ExistingModpackProject @parameters
    $project = $result.Project
    Write-R3Status (Get-MpConsole) success 'Existing Packwiz project registered with ModpackTools.'
    Write-R3KeyValue (Get-MpConsole) 'ID' $project.Id
    Write-R3KeyValue (Get-MpConsole) 'Minecraft' $project.MinecraftVersion
    Write-R3KeyValue (Get-MpConsole) 'Loader' $(if ($project.LoaderVersion) { "$($project.Loader) $($project.LoaderVersion)" } else { $project.Loader })
    Write-R3KeyValue (Get-MpConsole) 'Root' $project.Root
    Write-R3Status (Get-MpConsole) info "Created $(@($result.CreatedFiles).Count) file(s). Next: modpack project use $($project.Id)"
}

function Invoke-MpConfig {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Show-MpHelp config; return }
    Assert-MpNoProjectContext 'modpack config --help'
    Assert-PositionalCount -Values $Arguments -Minimum 2 -Maximum 3 -Usage 'modpack config get <root|packwiz> | modpack config set <root|packwiz> <value>'
    $verb = [string]$Arguments[0]
    $name = ([string]$Arguments[1]).ToLowerInvariant()
    if ($name -notin @('root', 'packwiz')) { Throw-MpError -Message "Configuration setting '$name' is not recognized; allowed values: root, packwiz" -Hint 'modpack config --help' -ErrorId 'Configuration.UnknownSetting' -Category InvalidArgument -TargetObject $name }
    switch ($verb.ToLowerInvariant()) {
        'get' {
            if ($Arguments.Count -ne 2) { Throw-MpError -Message "The arguments for 'config get' do not match the expected syntax" -Hint 'modpack config --help' -ErrorId 'Command.InvalidArguments' -Category InvalidArgument }
            Write-R3Banner (Get-MpConsole) 'CONFIGURATION'
            if ($name -eq 'root') { Write-R3KeyValue (Get-MpConsole) 'root' (Get-ModpackRoot) }
            else {
                $packwiz = Resolve-MpPackwiz
                Write-R3KeyValue (Get-MpConsole) 'packwiz' $(if ($packwiz.Available) { $packwiz.Path } else { 'Not found' })
                Write-R3KeyValue (Get-MpConsole) 'source' $packwiz.Source
            }
        }
        'set' {
            if ($Arguments.Count -ne 3) { Throw-MpError -Message "The arguments for 'config set' do not match the expected syntax" -Hint 'modpack config --help' -ErrorId 'Command.InvalidArguments' -Category InvalidArgument }
            $value = Set-ModpackToolsConfigValue -Name $name -Value ([string]$Arguments[2])
            Write-R3Status (Get-MpConsole) success "$name = $value"
        }
        default { Throw-MpError -Message "Configuration operation '$verb' is not recognized; allowed values: get, set" -Hint 'modpack config --help' -ErrorId 'Configuration.UnknownOperation' -Category InvalidArgument -TargetObject $verb }
    }
}
