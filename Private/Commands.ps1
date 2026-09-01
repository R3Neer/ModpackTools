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
            if ($null -ne $inlineValue) { throw "La opción '--$name' no acepta valor." }
            $options[$name] = $true
            continue
        }
        if ($ValueOptions -notcontains $name) { throw "Opción desconocida '--$name'." }
        if ($null -eq $inlineValue) {
            $i++
            if ($i -ge $Arguments.Count -or ([string]$Arguments[$i]).StartsWith('--')) { throw "Falta el valor de '--$name'." }
            $inlineValue = [string]$Arguments[$i]
        }
        $options[$name] = $inlineValue
    }
    [pscustomobject]@{ Options = $options; Positionals = @($positionals) }
}

function Assert-PositionalCount {
    param([array]$Values = @(), [int]$Minimum, [int]$Maximum, [string]$Usage)
    $count = @($Values).Count
    if ($count -lt $Minimum -or $count -gt $Maximum) { throw "Uso: $Usage" }
}

function Invoke-MpHelp {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    $topic = if ($Arguments.Count) { [string]$Arguments[0] } else { '' }
    $help = @{
        '' = @'
ModpackTools 0.2

  modpack list
  modpack use [id]
  modpack status [id] [--full]
  modpack inventory [id] [filtros]
  modpack build [id] [--no-refresh] [--keep-old] [--open] [--raw-log]
  modpack add mod <slug> [--project <id>] [--category <id>]
  modpack new <id> --name <nombre> --minecraft <versión> --loader fabric [opciones]
  modpack config get root
  modpack config set root <directorio>
  modpack help [comando]
'@
        build = 'Uso: modpack build [id] [--no-refresh] [--keep-old] [--open] [--raw-log]'
        status = 'Uso: modpack status [id] [--full]'
        inventory = @'
Uso: modpack inventory [id] [filtros]

  --type <all|mod|resourcepack|shaderpack>
  --category <id|unclassified>
  --side <client|host|both|unknown>
  --source <packwiz|local|builtin|missing>
  --state <all|active|inactive>
  --search <texto>
  --unclassified
'@
        add = 'Uso: modpack add mod <slug> [--project <id>] [--category <id>]'
        new = 'Uso: modpack new <id> --name <nombre> --minecraft <versión> --loader fabric [--path <carpeta>] [--loader-version <versión>] [--pack-version <versión>] [--display-version <versión>]'
        config = 'Uso: modpack config get root | modpack config set root <directorio>'
        use = 'Uso: modpack use [id]'
        list = 'Uso: modpack list'
    }
    if (-not $help.ContainsKey($topic)) { throw "No hay ayuda para '$topic'." }
    Write-Host $help[$topic]
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
        if ($script:ActiveProjectId) { Write-Host $script:ActiveProjectId }
        else { Write-Host 'No hay proyecto activo.' }
        return
    }
    $project = Set-ActiveModpackProject -Id ([string]$Arguments[0])
    Write-MpSuccess "Proyecto activo: $($project.Id) ($($project.DisplayName))"
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
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 0 -Maximum 1 -Usage 'modpack inventory [id] [filtros]'
    if ($parsed.Options.ContainsKey('unclassified') -and $parsed.Options.ContainsKey('category')) {
        throw 'Usa --unclassified o --category, no ambos.'
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
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 0 -Maximum 1 -Usage 'modpack build [id] [opciones]'
    $id = if ($parsed.Positionals.Count) { $parsed.Positionals[0] } else { $null }
    $project = Resolve-ModpackProject -Id $id
    Write-MpStep "Construyendo $($project.DisplayName)..."
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
    if ($parsed.Positionals[0] -ne 'mod') { throw "v0.1 solo implementa 'modpack add mod'." }
    $projectId = if ($parsed.Options.ContainsKey('project')) { $parsed.Options.project } else { $null }
    $project = Resolve-ModpackProject -Id $projectId
    $category = if ($parsed.Options.ContainsKey('category')) { $parsed.Options.category } else { $null }
    Write-MpStep "Añadiendo '$($parsed.Positionals[1])' a $($project.Id)..."
    $result = Add-ModpackMod -Project $project -Slug $parsed.Positionals[1] -Category $category
    Write-MpSuccess "$($result.Item.Name) añadido como '$($result.Item.Id)'."
    if ($category) { Write-Host "Categoría: $category" }
    else { Write-Host 'Categoría: SIN CLASIFICAR' }
}

function Invoke-MpNew {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Invoke-MpHelp new; return }
    $parsed = ConvertFrom-MpOptions -Arguments $Arguments -ValueOptions @('name', 'minecraft', 'loader', 'path', 'loader-version', 'pack-version', 'display-version')
    Assert-PositionalCount -Values $parsed.Positionals -Minimum 1 -Maximum 1 -Usage 'modpack new <id> --name <nombre> --minecraft <versión> --loader fabric'
    foreach ($required in @('name', 'minecraft', 'loader')) {
        if (-not $parsed.Options.ContainsKey($required)) { throw "Falta la opción obligatoria '--$required'." }
    }
    $parameters = @{
        Id = $parsed.Positionals[0]; Name = $parsed.Options.name; MinecraftVersion = $parsed.Options.minecraft; Loader = $parsed.Options.loader
    }
    if ($parsed.Options.ContainsKey('path')) { $parameters.DirectoryName = $parsed.Options.path }
    if ($parsed.Options.ContainsKey('loader-version')) { $parameters.LoaderVersion = $parsed.Options['loader-version'] }
    if ($parsed.Options.ContainsKey('pack-version')) { $parameters.PackVersion = $parsed.Options['pack-version'] }
    if ($parsed.Options.ContainsKey('display-version')) { $parameters.DisplayVersion = $parsed.Options['display-version'] }
    Write-MpStep "Creando el proyecto '$($parsed.Positionals[0])'..."
    $project = New-ModpackProject @parameters
    Write-MpSuccess "Proyecto creado en $($project.Root)"
}

function Invoke-MpConfig {
    param([Parameter(ValueFromRemainingArguments)][object[]]$Arguments = @())
    if ($Arguments -contains '--help') { Invoke-MpHelp config; return }
    Assert-PositionalCount -Values $Arguments -Minimum 2 -Maximum 3 -Usage 'modpack config get root | modpack config set root <directorio>'
    $verb = [string]$Arguments[0]
    $name = ([string]$Arguments[1]).ToLowerInvariant()
    if ($name -ne 'root') { throw "Configuración desconocida '$name'." }
    switch ($verb.ToLowerInvariant()) {
        'get' {
            if ($Arguments.Count -ne 2) { throw 'Uso: modpack config get root' }
            Write-Host (Get-ModpackRoot)
        }
        'set' {
            if ($Arguments.Count -ne 3) { throw 'Uso: modpack config set root <directorio>' }
            $value = Set-ModpackToolsConfigValue -Name root -Value ([string]$Arguments[2])
            Write-MpSuccess "root = $value"
        }
        default { throw "Operación de configuración desconocida '$verb'." }
    }
}
