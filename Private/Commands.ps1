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

    $optionId = if ($Options.ContainsKey('project')) { [string]$Options.project } else { $null }
    if ($optionId -and $PositionalId) {
        Throw-MpError -Message "The project was specified both positionally and with '--project'" -Hint "remove one of the two project IDs" -ErrorId 'Option.ProjectConflict' -Category InvalidArgument -TargetObject $optionId
    }
    $id = if ($optionId) { $optionId } else { $PositionalId }
    return Resolve-ModpackProject -Id $id
}

function Invoke-MpList {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Show-MpHelp list; return }
    Assert-PositionalCount -Values $Arguments -Minimum 0 -Maximum 0 -Usage 'modpack list'
    $root = Get-ModpackRoot
    $projects = @(Get-ModpackProjects)
    Write-ModpackList -Projects $projects -Root $root
}

function Invoke-MpUse {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Show-MpHelp use; return }
    Assert-PositionalCount -Values $Arguments -Minimum 0 -Maximum 1 -Usage 'modpack use [id]'
    if ($Arguments.Count -eq 0) {
        Write-R3Banner (Get-MpConsole) 'ACTIVE PROJECT'
        if ($script:ActiveProjectId) { Write-R3KeyValue (Get-MpConsole) 'ID' $script:ActiveProjectId }
        else { Write-R3Status (Get-MpConsole) info 'There is no active project in this session.' }
        return
    }
    $project = Set-ActiveModpackProject -Id ([string]$Arguments[0])
    Write-R3Status (Get-MpConsole) success "Active project: $($project.Id) ($($project.DisplayName))"
}

function Invoke-MpClassify {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Show-MpHelp classify; return }
    if (-not $Arguments.Count) {
        Throw-MpError -Message 'The classify command requires an operation; allowed values: list, create, remove, set' -Hint 'modpack classify --help' -ErrorId 'Command.MissingOperation' -Category InvalidArgument
    }
    $operation = ([string]$Arguments[0]).ToLowerInvariant()
    $remaining = @($Arguments | Select-Object -Skip 1)
    switch ($operation) {
        'list' {
            $parsed = ConvertFrom-MpOptions -Arguments $remaining -ValueOptions @('project')
            Assert-PositionalCount -Values $parsed.Positionals -Minimum 0 -Maximum 0 -Usage 'modpack classify list [--project <id>]' -OptionNames @('project')
            $project = Resolve-MpCommandProject -Options $parsed.Options
            $view = Get-ModpackCategoryView -Project $project
            Write-ModpackCategoryCache -View $view
            Write-ModpackCategoryList -View $view
        }
        'create' {
            $parsed = ConvertFrom-MpOptions -Arguments $remaining -ValueOptions @('project', 'name', 'order')
            Assert-PositionalCount -Values $parsed.Positionals -Minimum 1 -Maximum 1 -Usage 'modpack classify create <id> [--name <name>] [--order <n>] [--project <id>]' -OptionNames @('project', 'name', 'order')
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
        'remove' { Invoke-MpClassifyBatch -Operation remove -Arguments $remaining }
        'set' { Invoke-MpClassifyBatch -Operation set -Arguments $remaining }
        default {
            Throw-MpError -Message "Classify operation '$($Arguments[0])' is not recognized; allowed values: list, create, remove, set" -Hint 'modpack classify --help' -ErrorId 'Metadata.UnknownClassificationOperation' -Category InvalidArgument -TargetObject $Arguments[0]
        }
    }
}

function Invoke-MpStatus {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Show-MpHelp status; return }
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments -ValueOptions @('project') -SwitchOptions @('full')
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 0 -Maximum 1 -Usage 'modpack status [id] [--project <id>] [--full]' -OptionNames @('project', 'full')
    $id = if ($parsed.Positionals.Count) { $parsed.Positionals[0] } else { $null }
    $project = Resolve-MpCommandProject -Options $parsed.Options -PositionalId $id
    Assert-ModpackStructure -Project $project
    $inventory = Get-ModpackInventory -Project $project
    Write-ModpackHeader -Project $project -Inventory $inventory
    if ($parsed.Options.ContainsKey('full')) {
        $view = Select-ModpackInventory -Inventory $inventory
        [void](Set-ModpackInventoryReferences -View $view)
        Write-InventoryView -View $view
    }
}

function Invoke-MpInventory {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Show-MpHelp inventory; return }
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments `
        -ValueOptions @('project', 'type', 'category', 'side', 'source', 'state', 'search') `
        -SwitchOptions @('unclassified', 'check')
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 0 -Maximum 1 -Usage 'modpack inventory [id] [--project <id>] [filters]' -OptionNames @('project', 'type', 'category', 'side', 'source', 'state', 'search', 'unclassified')
    if ($parsed.Options.ContainsKey('unclassified') -and $parsed.Options.ContainsKey('category')) {
        Throw-MpError -Message "Options '--unclassified' and '--category' cannot be combined" -Hint 'remove one of the two options' -ErrorId 'Option.ForbiddenCombination' -Category InvalidArgument
    }

    $id = if ($parsed.Positionals.Count) { $parsed.Positionals[0] } else { $null }
    $project = Resolve-MpCommandProject -Options $parsed.Options -PositionalId $id
    Assert-ModpackStructure -Project $project
    $inventory = Get-ModpackInventory -Project $project
    $parameters = @{ Inventory = $inventory }
    foreach ($name in @('type', 'category', 'side', 'source', 'state', 'search')) {
        if ($parsed.Options.ContainsKey($name)) { $parameters[$name.Substring(0,1).ToUpperInvariant() + $name.Substring(1)] = $parsed.Options[$name] }
    }
    if ($parsed.Options.ContainsKey('category')) {
        $parameters.Category = Resolve-ModpackCategoryId -Project $project -Selector ([string]$parsed.Options.category) -AllowUnclassified
    }
    if ($parsed.Options.ContainsKey('unclassified')) { $parameters.Category = 'unclassified' }
    $view = Select-ModpackInventory @parameters
    [void](Set-ModpackInventoryReferences -View $view)

    Write-ModpackHeader -Project $project -Inventory $inventory
    Write-MpHealth (Get-MpProjectHealth $project -Check:$parsed.Options.ContainsKey('check'))
    Write-InventoryView -View $view -ShowFilters
}

function Invoke-MpBuild {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Show-MpHelp build; return }
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments -ValueOptions @('project') -SwitchOptions @('no-refresh', 'keep-old', 'open', 'raw-log', 'strict', 'dry-run')
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 0 -Maximum 1 -Usage 'modpack build [id] [--project <id>] [options]' -OptionNames @('project', 'no-refresh', 'keep-old', 'open', 'raw-log')
    $id = if ($parsed.Positionals.Count) { $parsed.Positionals[0] } else { $null }
    $project = Resolve-MpCommandProject -Options $parsed.Options -PositionalId $id
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
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments -ValueOptions @('project')
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 0 -Maximum 1 -Usage 'modpack diff [id] [--project <id>]' -OptionNames @('project')
    $id = if ($parsed.Positionals.Count) { [string]$parsed.Positionals[0] } else { $null }
    $project = Resolve-MpCommandProject -Options $parsed.Options -PositionalId $id
    Assert-ModpackStructure -Project $project
    Write-R3Status (Get-MpConsole) step "Comparing $($project.DisplayName) with its latest build..."
    $diff = Compare-ModpackBuild -Project $project
    Write-ModpackDiff -Diff $diff
}

function Invoke-MpSearch {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Show-MpHelp search; return }
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments -ValueOptions @('project', 'type', 'limit')
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 1 -Maximum 100 -Usage 'modpack search <query> [--type <type>] [--limit <1-50>] [--project <id>]'
    $project = Resolve-MpCommandProject -Options $parsed.Options
    $type = if ($parsed.Options.ContainsKey('type')) { $parsed.Options.type } else { 'all' }
    $limit = 10
    if ($parsed.Options.ContainsKey('limit') -and (-not [int]::TryParse([string]$parsed.Options.limit, [ref]$limit) -or $limit -lt 1 -or $limit -gt 50)) {
        Throw-MpError -Message "Option '--limit' must be an integer from 1 through 50; received '$($parsed.Options.limit)'" -Hint '--limit <1-50>' -ErrorId 'Option.InvalidLimit' -Category InvalidArgument -TargetObject $parsed.Options.limit
    }
    $query = @($parsed.Positionals) -join ' '
    if ([string]::IsNullOrWhiteSpace($query)) { Throw-MpError -Message 'The search query cannot be empty' -Hint 'modpack search <query>' -ErrorId 'Search.EmptyQuery' -Category InvalidArgument }
    Write-R3Status (Get-MpConsole) step "Searching Modrinth for '$query'..."
    $search = Search-ModrinthContent -Project $project -Query $query -Type $type -Limit $limit
    Write-ModrinthSearchResults -Search $search -Project $project
}

function Invoke-MpVersions {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Show-MpHelp versions; return }
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments -ValueOptions @('project')
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 1 -Maximum 1 -Usage 'modpack versions <selector> [--project <id>]' -OptionNames @('project')
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
    if ($Arguments -contains '--help') { Show-MpHelp new; return }
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments -ValueOptions @('name', 'minecraft', 'loader', 'path', 'loader-version', 'pack-version', 'display-version')
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 1 -Maximum 1 -Usage 'modpack new <id> --name <name> --minecraft <version> --loader <fabric|quilt|forge|neoforge>' -OptionNames @('name', 'minecraft', 'loader', 'path', 'loader-version', 'pack-version', 'display-version')
    foreach ($required in @('name', 'minecraft', 'loader')) {
        if (-not $parsed.Options.ContainsKey($required)) { Throw-MpError -Message "Required option '--$required' is missing" -Hint 'modpack new --help' -ErrorId 'Option.Required' -Category InvalidArgument -TargetObject $required }
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
    if ($Arguments -contains '--help') { Show-MpHelp init; return }
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments -ValueOptions @('path', 'display-name', 'display-version', 'output-name')
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 1 -Maximum 1 -Usage 'modpack init <id> [--path <directory>] [options]' -OptionNames @('path', 'display-name', 'display-version', 'output-name')
    $parameters = @{ Id = $parsed.Positionals[0] }
    if ($parsed.Options.ContainsKey('path')) { $parameters.Path = $parsed.Options.path }
    if ($parsed.Options.ContainsKey('display-name')) { $parameters.DisplayName = $parsed.Options['display-name'] }
    if ($parsed.Options.ContainsKey('display-version')) { $parameters.DisplayVersion = $parsed.Options['display-version'] }
    if ($parsed.Options.ContainsKey('output-name')) { $parameters.OutputName = $parsed.Options['output-name'] }
    $location = if ($parameters.ContainsKey('Path')) { $parameters.Path } else { (Get-Location).Path }
    Write-R3Status (Get-MpConsole) step "Initializing Packwiz project '$location' as '$($parsed.Positionals[0])'..."
    $result = Initialize-ExistingModpackProject @parameters
    $project = $result.Project
    Write-R3Status (Get-MpConsole) success 'Existing Packwiz project initialized for ModpackTools.'
    Write-R3KeyValue (Get-MpConsole) 'ID' $project.Id
    Write-R3KeyValue (Get-MpConsole) 'Minecraft' $project.MinecraftVersion
    Write-R3KeyValue (Get-MpConsole) 'Loader' $(if ($project.LoaderVersion) { "$($project.Loader) $($project.LoaderVersion)" } else { $project.Loader })
    Write-R3KeyValue (Get-MpConsole) 'Root' $project.Root
    Write-R3Status (Get-MpConsole) info "Created $(@($result.CreatedFiles).Count) file(s). Next: modpack use $($project.Id)"
}

function Invoke-MpConfig {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Show-MpHelp config; return }
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
