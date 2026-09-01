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

function Invoke-MpHelp {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    $topic = if ($Arguments.Count) { [string]$Arguments[0] } else { '' }
    $topics = @('', 'build', 'status', 'inventory', 'resource', 'add', 'new', 'config', 'use', 'list')
    if ($topic -notin $topics) { throw "No help is available for '$topic'." }
    if (-not $topic) {
        Write-MpBanner "MODPACKTOOLS $script:ModuleVersion"
        Write-MpCommandLine 'modpack list' 'Registered projects'
        Write-MpCommandLine 'modpack use [id]' 'Active project for this session'
        Write-MpCommandLine 'modpack status [id] [--full]' 'Project summary'
        Write-MpCommandLine 'modpack inventory [id] [filters]' 'Contents and filters'
        Write-MpCommandLine 'modpack resource enable <selector> --position <n>' 'Enable or reposition a resource pack'
        Write-MpCommandLine 'modpack add mod <slug> [options]' 'Add a mod with Packwiz'
        Write-MpCommandLine 'modpack build [id] [options]' 'Generate the .mrpack in dist/'
        Write-MpCommandLine 'modpack new <id> [options]' 'Create a project'
        Write-MpCommandLine 'modpack config get|set root' 'Global configuration'
        Write-MpCommandLine 'modpack help [command]' 'Detailed help'
        return
    }
    Write-MpBanner "HELP · $($topic.ToUpperInvariant())"
    switch ($topic) {
        'build' { Write-MpUsage 'modpack build [id] [--no-refresh] [--keep-old] [--open] [--raw-log]' }
        'status' { Write-MpUsage 'modpack status [id] [--full]' }
        'inventory' {
            Write-MpUsage 'modpack inventory [id] [filters]'
            foreach ($option in @('--type <all|mod|resourcepack|shaderpack>', '--category <id|unclassified>', '--side <client|host|both|unknown>', '--source <packwiz|local|builtin|missing>', '--state <all|active|inactive>', '--search <text>', '--unclassified')) { Write-MpCommandLine $option }
        }
        'resource' {
            Write-MpUsage 'modpack resource enable <name|id|filename> --position <n> [--project <id>]'
            Write-MpInfo 'Position 1 is the highest priority in the Minecraft GUI.'
            Write-MpInfo 'If the pack is already enabled, it is repositioned.'
        }
        'add' { Write-MpUsage 'modpack add mod <slug> [--project <id>] [--category <id>]' }
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
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 2 -Maximum 2 -Usage 'modpack resource enable <name|id|filename> --position <n> [--project <id>]'
    if ($parsed.Positionals[0].ToLowerInvariant() -ne 'enable') { throw "Unknown resource pack operation '$($parsed.Positionals[0])'. Use 'enable'." }
    if (-not $parsed.Options.ContainsKey('position')) { throw "Required option '--position' is missing." }
    $position = 0
    if (-not [int]::TryParse([string]$parsed.Options.position, [ref]$position) -or $position -lt 1) { throw "Position must be an integer greater than or equal to 1." }
    $projectId = if ($parsed.Options.ContainsKey('project')) { $parsed.Options.project } else { $null }
    $project = Resolve-ModpackProject -Id $projectId
    Assert-ModpackStructure -Project $project
    Write-MpStep "Enabling or repositioning '$($parsed.Positionals[1])'..."
    $result = Enable-ModpackResourcePack -Project $project -Selector $parsed.Positionals[1] -Position $position
    $verb = if ($result.WasActive) { 'repositioned' } else { 'enabled' }
    Write-MpSuccess "$($result.Item.Name) was $verb at priority $($result.Item.Priority)."
    Write-ResourcePackInventory -Inventory $result.Inventory -HideEmptySections
}

function Invoke-MpStatus {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Invoke-MpHelp status; return }
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments -SwitchOptions @('full')
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 0 -Maximum 1 -Usage 'modpack status [id] [--full]'
    $id = if ($parsed.Positionals.Count) { $parsed.Positionals[0] } else { $null }
    $project = Resolve-ModpackProject -Id $id
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
        -ValueOptions @('type', 'category', 'side', 'source', 'state', 'search') `
        -SwitchOptions @('unclassified')
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 0 -Maximum 1 -Usage 'modpack inventory [id] [filters]'
    if ($parsed.Options.ContainsKey('unclassified') -and $parsed.Options.ContainsKey('category')) {
        throw 'Use either --unclassified or --category, not both.'
    }

    $id = if ($parsed.Positionals.Count) { $parsed.Positionals[0] } else { $null }
    $project = Resolve-ModpackProject -Id $id
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
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments -SwitchOptions @('no-refresh', 'keep-old', 'open', 'raw-log')
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 0 -Maximum 1 -Usage 'modpack build [id] [options]'
    $id = if ($parsed.Positionals.Count) { $parsed.Positionals[0] } else { $null }
    $project = Resolve-ModpackProject -Id $id
    Write-MpStep "Building $($project.DisplayName)..."
    $build = Build-ModpackProject -Project $project -NoRefresh:$parsed.Options.ContainsKey('no-refresh') -KeepOld:$parsed.Options.ContainsKey('keep-old') -RawLog:$parsed.Options.ContainsKey('raw-log')
    foreach ($line in $build.Log) { Write-Host "$($script:Palette.Secondary)$line$($script:Palette.Reset)" }
    Write-ModInventory $build.Inventory
    Write-ResourcePackInventory $build.Inventory
    Write-ShaderInventory $build.Inventory
    Write-BuildSummary $build
    if ($parsed.Options.ContainsKey('open')) { Start-Process explorer.exe -ArgumentList "/select,`"$($build.Path)`"" }
}

function Invoke-MpAdd {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Invoke-MpHelp add; return }
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments -ValueOptions @('project', 'category')
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 2 -Maximum 2 -Usage 'modpack add mod <slug> [--project <id>] [--category <id>]'
    if ($parsed.Positionals[0] -ne 'mod') { throw "Only 'modpack add mod' is currently implemented." }
    $projectId = if ($parsed.Options.ContainsKey('project')) { $parsed.Options.project } else { $null }
    $project = Resolve-ModpackProject -Id $projectId
    $category = if ($parsed.Options.ContainsKey('category')) { $parsed.Options.category } else { $null }
    Write-MpStep "Adding '$($parsed.Positionals[1])' to $($project.Id)..."
    $result = Add-ModpackMod -Project $project -Slug $parsed.Positionals[1] -Category $category
    Write-MpSuccess "$($result.Item.Name) added as '$($result.Item.Id)'."
    if ($category) { Write-MpKeyValue 'Category' $category }
    else { Write-MpKeyValue 'Category' 'UNCLASSIFIED' }
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
