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
            if ($null -ne $inlineValue) { throw "Option '--$name' does not accept a value." }
            $options[$name] = $true
            continue
        }
        if ($ValueOptions -notcontains $name) { throw "Unknown option '--$name'." }
        if ($null -eq $inlineValue) {
            $i++
            if ($i -ge $Arguments.Count -or ([string]$Arguments[$i]).StartsWith('--')) { throw "Option '--$name' requires a value." }
            $inlineValue = [string]$Arguments[$i]
        }
        $options[$name] = $inlineValue
    }
    [pscustomobject]@{ Options = $options; Positionals = @($positionals) }
}

function Assert-PositionalCount {
    param([array]$Values = @(), [int]$Minimum, [int]$Maximum, [string]$Usage)
    $count = @($Values).Count
    if ($count -lt $Minimum -or $count -gt $Maximum) { throw "Usage: $Usage" }
}

function Resolve-MpCommandProject {
    param(
        [Parameter(Mandatory)][hashtable]$Options,
        [AllowNull()][AllowEmptyString()][string]$PositionalId
    )

    $optionId = if ($Options.ContainsKey('project')) { [string]$Options.project } else { $null }
    if ($optionId -and $PositionalId) {
        throw "Specify the project either positionally or with '--project', not both."
    }
    $id = if ($optionId) { $optionId } else { $PositionalId }
    return Resolve-ModpackProject -Id $id
}

function Invoke-MpHelp {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    $topic = if ($Arguments.Count) { [string]$Arguments[0] } else { '' }
    $topics = @('', 'build', 'diff', 'status', 'inventory', 'resource', 'search', 'add', 'classify', 'update', 'new', 'config', 'use', 'list')
    if ($topic -notin $topics) { throw "No help is available for '$topic'." }
    if (-not $topic) {
        Write-MpBanner "MODPACKTOOLS $script:ModuleVersion"
        Write-MpCommandLine 'modpack list' 'Registered projects'
        Write-MpCommandLine 'modpack use [id]' 'Active project for this session'
        Write-MpCommandLine 'modpack status [id] [--project <id>] [--full]' 'Project summary'
        Write-MpCommandLine 'modpack inventory [id] [--project <id>] [filters]' 'Contents and filters'
        Write-MpCommandLine 'modpack resource enable <selector> --position <n> [--project <id>]' 'Enable or reposition a resource pack'
        Write-MpCommandLine 'modpack search <query> [options] [--project <id>]' 'Search compatible Modrinth content'
        Write-MpCommandLine 'modpack add <id|slug|number> [options] [--project <id>]' 'Add content with Packwiz'
        Write-MpCommandLine 'modpack classify <mod> <category> [--project <id>]' 'Classify or reclassify a mod'
        Write-MpCommandLine 'modpack update <selector...> | --all [--project <id>]' 'Update Packwiz-managed content'
        Write-MpCommandLine 'modpack build [id] [options] [--project <id>]' 'Generate the .mrpack in dist/'
        Write-MpCommandLine 'modpack diff [id] [--project <id>]' 'Compare the current project with the latest build'
        Write-MpCommandLine 'modpack new <id> [options]' 'Create a project'
        Write-MpCommandLine 'modpack config get|set root' 'Global configuration'
        Write-MpCommandLine 'modpack help [command]' 'Detailed help'
        Write-MpInfo 'Project commands accept --project <id>; omit it after modpack use <id>.'
        return
    }
    Write-MpBanner "HELP · $($topic.ToUpperInvariant())"
    switch ($topic) {
        'build' { Write-MpUsage 'modpack build [id] [--project <id>] [--no-refresh] [--keep-old] [--open] [--raw-log]' }
        'diff' { Write-MpUsage 'modpack diff [id] [--project <id>]' }
        'status' { Write-MpUsage 'modpack status [id] [--project <id>] [--full]' }
        'inventory' {
            Write-MpUsage 'modpack inventory [id] [--project <id>] [filters]'
            foreach ($option in @('--type <all|mod|resourcepack|shaderpack>', '--category <id|unclassified>', '--side <client|host|both|unknown>', '--source <packwiz|local|builtin|missing>', '--state <all|active|inactive>', '--search <text>', '--unclassified')) { Write-MpCommandLine $option }
        }
        'resource' {
            Write-MpUsage 'modpack resource enable <name|id|filename> --position <n> [--project <id>]'
            Write-MpUsage 'modpack resource disable <name|id|filename> [--project <id>]'
            Write-MpInfo 'Position 1 is the highest priority in the Minecraft GUI.'
            Write-MpInfo 'If the pack is already enabled, it is repositioned.'
        }
        'search' {
            Write-MpUsage 'modpack search <query> [--type <mod|resourcepack|shaderpack>] [--limit <1-50>] [--project <id>]'
            Write-MpInfo 'Results are numbered and saved for 24 hours. Install one with modpack add <number>.'
        }
        'add' { Write-MpUsage 'modpack add <id|slug|search-number> [--project <id>] [--category <id>]' }
        'classify' {
            Write-MpUsage 'modpack classify <name|id|filename> <category|unclassified> [--project <id>]'
            Write-MpInfo 'This changes editorial metadata only; it does not move or reinstall the mod.'
        }
        'update' {
            Write-MpUsage 'modpack update <name|id|filename...> [--type <type>] [--project <id>]'
            Write-MpUsage 'modpack update --all [--type <type>] [--project <id>]'
            Write-MpInfo 'Types: mod, resourcepack, shaderpack. Without --type, every Packwiz-managed content type is included.'
        }
        'new' { Write-MpUsage 'modpack new <id> --name <name> --minecraft <version> --loader fabric [--path <directory>] [--loader-version <version>] [--pack-version <version>] [--display-version <version>]' }
        'config' { Write-MpUsage 'modpack config get root | modpack config set root <directory>' }
        'use' { Write-MpUsage 'modpack use [id]' }
        'list' { Write-MpUsage 'modpack list' }
    }
}

function Invoke-MpList {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments.Count -and $Arguments[0] -eq '--help') { Invoke-MpHelp list; return }
    Assert-PositionalCount -Values $Arguments -Minimum 0 -Maximum 0 -Usage 'modpack list'
    $root = Get-ModpackRoot
    $projects = @(Get-ModpackProjects)
    Write-ModpackList -Projects $projects -Root $root
}

function Invoke-MpUse {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments.Count -and $Arguments[0] -eq '--help') { Invoke-MpHelp use; return }
    Assert-PositionalCount -Values $Arguments -Minimum 0 -Maximum 1 -Usage 'modpack use [id]'
    if ($Arguments.Count -eq 0) {
        Write-MpBanner 'ACTIVE PROJECT'
        if ($script:ActiveProjectId) { Write-MpKeyValue 'ID' $script:ActiveProjectId }
        else { Write-MpInfo 'There is no active project in this session.' }
        return
    }
    $project = Set-ActiveModpackProject -Id ([string]$Arguments[0])
    Write-MpSuccess "Active project: $($project.Id) ($($project.DisplayName))"
}

function Invoke-MpResource {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Invoke-MpHelp resource; return }
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments -ValueOptions @('project', 'position')
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 2 -Maximum 2 -Usage 'modpack resource enable|disable <name|id|filename> [options]'
    $operation = $parsed.Positionals[0].ToLowerInvariant()
    if ($operation -notin @('enable', 'disable')) { throw "Unknown resource pack operation '$($parsed.Positionals[0])'. Use 'enable' or 'disable'." }
    $project = Resolve-MpCommandProject -Options $parsed.Options
    Assert-ModpackStructure -Project $project
    if ($operation -eq 'enable') {
        if (-not $parsed.Options.ContainsKey('position')) { throw "Required option '--position' is missing." }
        $position = 0
        if (-not [int]::TryParse([string]$parsed.Options.position, [ref]$position) -or $position -lt 1) { throw "Position must be an integer greater than or equal to 1." }
        Write-MpStep "Enabling or repositioning '$($parsed.Positionals[1])'..."
        $result = Enable-ModpackResourcePack -Project $project -Selector $parsed.Positionals[1] -Position $position
        $verb = if ($result.WasActive) { 'repositioned' } else { 'enabled' }
        Write-MpSuccess "$($result.Item.Name) was $verb at priority $($result.Item.Priority)."
    }
    else {
        if ($parsed.Options.ContainsKey('position')) { throw "Option '--position' cannot be used with resource disable." }
        Write-MpStep "Disabling '$($parsed.Positionals[1])'..."
        $result = Disable-ModpackResourcePack -Project $project -Selector $parsed.Positionals[1]
        if ($result.WasActive) { Write-MpSuccess "$($result.Item.Name) was disabled." }
        else { Write-MpInfo "$($result.Item.Name) is already disabled." }
    }
    Write-ResourcePackInventory -Inventory $result.Inventory -HideEmptySections
}

function Invoke-MpClassify {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Invoke-MpHelp classify; return }
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments -ValueOptions @('project')
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 2 -Maximum 2 -Usage 'modpack classify <name|id|filename> <category|unclassified> [--project <id>]'
    $project = Resolve-MpCommandProject -Options $parsed.Options
    Write-MpStep "Classifying '$($parsed.Positionals[0])'..."
    $result = Set-ModpackModClassification -Project $project -Selector $parsed.Positionals[0] -Category $parsed.Positionals[1]
    Write-MpSuccess "$($result.Item.Name) is classified as '$($result.Category)'."
    Write-MpKeyValue 'Previous' $result.PreviousCategory
    Write-MpKeyValue 'ID' $result.Item.Id
}

function Invoke-MpStatus {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Invoke-MpHelp status; return }
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments -ValueOptions @('project') -SwitchOptions @('full')
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 0 -Maximum 1 -Usage 'modpack status [id] [--project <id>] [--full]'
    $id = if ($parsed.Positionals.Count) { $parsed.Positionals[0] } else { $null }
    $project = Resolve-MpCommandProject -Options $parsed.Options -PositionalId $id
    Assert-ModpackStructure -Project $project
    $inventory = Get-ModpackInventory -Project $project
    Write-ModpackHeader -Project $project -Inventory $inventory
    if ($parsed.Options.ContainsKey('full')) {
        $view = Select-ModpackInventory -Inventory $inventory
        Write-InventoryView -View $view
    }
}

function Invoke-MpInventory {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Invoke-MpHelp inventory; return }
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments `
        -ValueOptions @('project', 'type', 'category', 'side', 'source', 'state', 'search') `
        -SwitchOptions @('unclassified')
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 0 -Maximum 1 -Usage 'modpack inventory [id] [--project <id>] [filters]'
    if ($parsed.Options.ContainsKey('unclassified') -and $parsed.Options.ContainsKey('category')) {
        throw 'Use either --unclassified or --category, not both.'
    }

    $id = if ($parsed.Positionals.Count) { $parsed.Positionals[0] } else { $null }
    $project = Resolve-MpCommandProject -Options $parsed.Options -PositionalId $id
    Assert-ModpackStructure -Project $project
    $inventory = Get-ModpackInventory -Project $project
    $parameters = @{ Inventory = $inventory }
    foreach ($name in @('type', 'category', 'side', 'source', 'state', 'search')) {
        if ($parsed.Options.ContainsKey($name)) { $parameters[$name.Substring(0,1).ToUpperInvariant() + $name.Substring(1)] = $parsed.Options[$name] }
    }
    if ($parsed.Options.ContainsKey('unclassified')) { $parameters.Category = 'unclassified' }
    $view = Select-ModpackInventory @parameters

    Write-ModpackHeader -Project $project -Inventory $inventory
    Write-InventoryView -View $view -ShowFilters
}

function Invoke-MpBuild {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Invoke-MpHelp build; return }
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments -ValueOptions @('project') -SwitchOptions @('no-refresh', 'keep-old', 'open', 'raw-log')
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 0 -Maximum 1 -Usage 'modpack build [id] [--project <id>] [options]'
    $id = if ($parsed.Positionals.Count) { $parsed.Positionals[0] } else { $null }
    $project = Resolve-MpCommandProject -Options $parsed.Options -PositionalId $id
    Write-MpStep "Building $($project.DisplayName)..."
    $build = Build-ModpackProject -Project $project -NoRefresh:$parsed.Options.ContainsKey('no-refresh') -KeepOld:$parsed.Options.ContainsKey('keep-old') -RawLog:$parsed.Options.ContainsKey('raw-log')
    foreach ($line in $build.Log) { Write-Host "$($script:Palette.Secondary)$line$($script:Palette.Reset)" }
    Write-ModInventory $build.Inventory
    Write-ResourcePackInventory $build.Inventory
    Write-ShaderInventory $build.Inventory
    Write-BuildSummary $build
    if ($parsed.Options.ContainsKey('open')) { Start-Process explorer.exe -ArgumentList "/select,`"$($build.Path)`"" }
}

function Invoke-MpDiff {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Invoke-MpHelp diff; return }
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments -ValueOptions @('project')
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 0 -Maximum 1 -Usage 'modpack diff [id] [--project <id>]'
    $id = if ($parsed.Positionals.Count) { [string]$parsed.Positionals[0] } else { $null }
    $project = Resolve-MpCommandProject -Options $parsed.Options -PositionalId $id
    Assert-ModpackStructure -Project $project
    Write-MpStep "Comparing $($project.DisplayName) with its latest build..."
    $diff = Compare-ModpackBuild -Project $project
    Write-ModpackDiff -Diff $diff
}

function Invoke-MpAdd {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Invoke-MpHelp add; return }
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments -ValueOptions @('project', 'category')
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 1 -Maximum 1 -Usage 'modpack add <id|slug|search-number> [--project <id>] [--category <id>]'
    if ($parsed.Positionals[0].ToLowerInvariant() -eq 'mod') { throw "Invalid syntax. Use: modpack add <id|slug|search-number> [--project <id>] [--category <id>]" }
    $project = Resolve-MpCommandProject -Options $parsed.Options
    $category = if ($parsed.Options.ContainsKey('category')) { $parsed.Options.category } else { $null }
    $selector = [string]$parsed.Positionals[0]
    $cached = Resolve-ModrinthSearchNumber -Selector $selector -Project $project
    if ($cached -and $category -and $cached.Type -ne 'mod') {
        throw "Option '--category' can only be used with mods; search result $selector is a $($cached.Type)."
    }
    $label = if ($cached) { "#$selector $($cached.Title)" } else { $selector }
    Write-MpStep "Adding '$label' to $($project.Id)..."
    $parameters = @{ Project = $project; Selector = $selector; Category = $category }
    if ($cached) { $parameters.ModrinthProjectId = [string]$cached.ProjectId }
    $result = Add-ModpackContent @parameters
    Write-MpSuccess "$($result.Item.Name) added as '$($result.Item.Id)'."
    Write-MpKeyValue 'Type' $result.Item.Kind
    if ($category) { Write-MpKeyValue 'Category' $category }
    elseif ($result.Item.Kind -eq 'mod') { Write-MpKeyValue 'Category' 'UNCLASSIFIED' }
}

function Invoke-MpSearch {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Invoke-MpHelp search; return }
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments -ValueOptions @('project', 'type', 'limit')
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 1 -Maximum 100 -Usage 'modpack search <query> [--type <type>] [--limit <1-50>] [--project <id>]'
    $project = Resolve-MpCommandProject -Options $parsed.Options
    $type = if ($parsed.Options.ContainsKey('type')) { $parsed.Options.type } else { 'all' }
    $limit = 10
    if ($parsed.Options.ContainsKey('limit') -and (-not [int]::TryParse([string]$parsed.Options.limit, [ref]$limit) -or $limit -lt 1 -or $limit -gt 50)) {
        throw "Option '--limit' must be an integer between 1 and 50."
    }
    $query = @($parsed.Positionals) -join ' '
    if ([string]::IsNullOrWhiteSpace($query)) { throw 'The search query cannot be empty.' }
    Write-MpStep "Searching Modrinth for '$query'..."
    $search = Search-ModrinthContent -Project $project -Query $query -Type $type -Limit $limit
    Write-ModrinthSearchResults -Search $search -Project $project
}

function Invoke-MpUpdate {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Invoke-MpHelp update; return }
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments -ValueOptions @('project', 'type') -SwitchOptions @('all')
    if ($parsed.Options.ContainsKey('all') -and $parsed.Positionals.Count) {
        throw "Use either content selectors or '--all', not both."
    }
    if (-not $parsed.Options.ContainsKey('all') -and $parsed.Positionals.Count -eq 0) {
        throw 'Usage: modpack update <name|id|filename...> [--type <type>] [--project <id>] | modpack update --all [--type <type>] [--project <id>]'
    }
    $project = Resolve-MpCommandProject -Options $parsed.Options
    $type = if ($parsed.Options.ContainsKey('type')) { $parsed.Options.type } else { 'all' }
    $label = if ($parsed.Options.ContainsKey('all')) { 'all matching Packwiz-managed content' } else { "$($parsed.Positionals.Count) selected item(s)" }
    Write-MpStep "Updating $label in $($project.Id)..."
    $result = Update-ModpackContent -Project $project -Selectors $parsed.Positionals -All:$parsed.Options.ContainsKey('all') -Type $type
    Write-ModUpdateSummary -Update $result
}

function Invoke-MpNew {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Invoke-MpHelp new; return }
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments -ValueOptions @('name', 'minecraft', 'loader', 'path', 'loader-version', 'pack-version', 'display-version')
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 1 -Maximum 1 -Usage 'modpack new <id> --name <name> --minecraft <version> --loader fabric'
    foreach ($required in @('name', 'minecraft', 'loader')) {
        if (-not $parsed.Options.ContainsKey($required)) { throw "Required option '--$required' is missing." }
    }
    $parameters = @{
        Id = $parsed.Positionals[0]; Name = $parsed.Options.name; MinecraftVersion = $parsed.Options.minecraft; Loader = $parsed.Options.loader
    }
    if ($parsed.Options.ContainsKey('path')) { $parameters.DirectoryName = $parsed.Options.path }
    if ($parsed.Options.ContainsKey('loader-version')) { $parameters.LoaderVersion = $parsed.Options['loader-version'] }
    if ($parsed.Options.ContainsKey('pack-version')) { $parameters.PackVersion = $parsed.Options['pack-version'] }
    if ($parsed.Options.ContainsKey('display-version')) { $parameters.DisplayVersion = $parsed.Options['display-version'] }
    Write-MpStep "Creating project '$($parsed.Positionals[0])'..."
    $project = New-ModpackProject @parameters
    Write-MpSuccess "Project created at $($project.Root)"
}

function Invoke-MpConfig {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Invoke-MpHelp config; return }
    Assert-PositionalCount -Values $Arguments -Minimum 2 -Maximum 3 -Usage 'modpack config get root | modpack config set root <directory>'
    $verb = [string]$Arguments[0]
    $name = ([string]$Arguments[1]).ToLowerInvariant()
    if ($name -ne 'root') { throw "Unknown setting '$name'." }
    switch ($verb.ToLowerInvariant()) {
        'get' {
            if ($Arguments.Count -ne 2) { throw 'Usage: modpack config get root' }
            Write-MpBanner 'CONFIGURATION'
            Write-MpKeyValue 'root' (Get-ModpackRoot)
        }
        'set' {
            if ($Arguments.Count -ne 3) { throw 'Usage: modpack config set root <directory>' }
            $value = Set-ModpackToolsConfigValue -Name root -Value ([string]$Arguments[2])
            Write-MpSuccess "root = $value"
        }
        default { throw "Unknown configuration operation '$verb'." }
    }
}
