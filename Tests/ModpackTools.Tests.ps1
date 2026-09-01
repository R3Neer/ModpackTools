$modulePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'ModpackTools.psd1'
Import-Module $modulePath -Force

InModuleScope ModpackTools {
    function New-TestModpack {
        param([string]$Root, [string]$Folder, [string]$Id)
        $projectRoot = Join-Path $Root $Folder
        foreach ($directory in @('.modpack', 'mods', 'config', 'resourcepacks', 'shaderpacks')) {
            [System.IO.Directory]::CreateDirectory((Join-Path $projectRoot $directory)) | Out-Null
        }
        [System.IO.File]::WriteAllText((Join-Path $projectRoot 'pack.toml'), @"
name = "Technical $Id"
author = "Test"
version = "0.1.0"
pack-format = "packwiz:1.1.0"

[index]
file = "index.toml"
hash-format = "sha256"
hash = "test"

[versions]
fabric = "0.16.0"
minecraft = "1.21.1"
"@)
        [System.IO.File]::WriteAllText((Join-Path $projectRoot 'index.toml'), 'hash-format = "sha256"')
        Write-PowerShellDataFileAtomic -Path (Join-Path $projectRoot '.modpack/project.psd1') -Data @{
            SchemaVersion = 1; Id = $Id; DisplayName = "Display $Id"; DisplayVersion = '1.0'; OutputName = "$Id.mrpack"
        }
        Write-PowerShellDataFileAtomic -Path (Join-Path $projectRoot '.modpack/metadata.psd1') -Data @{
            Categories = @{ performance = @{ Name = 'RENDIMIENTO'; Order = 10 } }; Mods = @{}; ResourcePacks = @{}
        }
        return $projectRoot
    }

    Describe 'Descubrimiento y resolución de proyectos' {
        BeforeEach {
            $fixtureRoot = Join-Path $TestDrive 'root con espacios'
            [System.IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
            $script:ConfigHomeOverride = Join-Path $TestDrive 'config'
            Write-PowerShellDataFileAtomic -Path (Get-ModpackToolsConfigPath) -Data @{ Root = $fixtureRoot }
            $script:ActiveProjectId = $null
        }

        It 'descubre proyectos y lee project.psd1 y pack.toml' {
            New-TestModpack $fixtureRoot 'Pack Uno' 'uno' | Out-Null
            $projects = @(Get-ModpackProjects)
            $projects.Count | Should Be 1
            $projects[0].Id | Should Be 'uno'
            $projects[0].DisplayName | Should Be 'Display uno'
            $projects[0].MinecraftVersion | Should Be '1.21.1'
            $projects[0].Loader | Should Be 'fabric'
            $projects[0].Root | Should Match 'root con espacios'
        }

        It 'detecta IDs duplicados' {
            New-TestModpack $fixtureRoot 'Pack A' 'same' | Out-Null
            New-TestModpack $fixtureRoot 'Pack B' 'same' | Out-Null
            { Get-ModpackProjects } | Should Throw 'duplicados'
        }

        It 'da prioridad al ID explícito sobre la sesión activa' {
            New-TestModpack $fixtureRoot 'Pack A' 'one' | Out-Null
            New-TestModpack $fixtureRoot 'Pack B' 'two' | Out-Null
            $script:ActiveProjectId = 'one'
            (Resolve-ModpackProject).Id | Should Be 'one'
            (Resolve-ModpackProject -Id 'two').Id | Should Be 'two'
        }

        It 'acepta comandos públicos sin argumentos adicionales' {
            New-TestModpack $fixtureRoot 'Pack Uno' 'uno' | Out-Null
            { modpack list } | Should Not Throw
        }
    }

    Describe 'Metadata e inventario normalizado' {
        BeforeEach {
            $fixtureRoot = Join-Path $TestDrive 'packs'
            [System.IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
            $projectPath = New-TestModpack $fixtureRoot 'Pack' 'pack'
            $project = Read-ModpackProject $projectPath
        }

        It 'lee categorías editoriales' {
            $metadata = Get-ModpackMetadata $project
            $metadata.Categories.performance.Name | Should Be 'RENDIMIENTO'
            $metadata.Categories.performance.Order | Should Be 10
        }

        It 'coloca un mod sin categoría en unclassified' {
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'mods/sample.pw.toml'), @'
name = "Sample"
filename = "sample.jar"
side = "client"
[update]
[update.modrinth]
mod-id = "abc123"
version = "version1"
'@)
            $inventory = Get-ModpackInventory $project
            $inventory.Mods[0].Id | Should Be 'modrinth:abc123'
            $inventory.Mods[0].Category | Should Be 'unclassified'
        }

        It 'normaliza side client server y both' {
            foreach ($side in @('client', 'server', 'both')) {
                [System.IO.File]::WriteAllText((Join-Path $projectPath "mods/$side.pw.toml"), @"
name = "$side"
filename = "$side.jar"
side = "$side"
"@)
            }
            $sides = @((Get-ModpackInventory $project).Mods | Sort-Object Side | ForEach-Object Side)
            $sides | Should Be @('both', 'client', 'server')
        }

        It 'aplica una categoría válida usando el ID estable' {
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'mods/sample.pw.toml'), @'
name = "Sample"
filename = "sample.jar"
side = "both"
[update.modrinth]
mod-id = "abc123"
'@)
            $metadata = Get-ModpackMetadata $project
            $metadata.Mods['modrinth:abc123'] = @{ Category = 'performance' }
            Write-PowerShellDataFileAtomic -Path (Join-Path $projectPath '.modpack/metadata.psd1') -Data $metadata
            (Get-ModpackMetadata $project).Mods['modrinth:abc123'].Category | Should Be 'performance'
            $categorizedInventory = Get-ModpackInventory $project
            $categorized = $categorizedInventory.Mods | Where-Object Id -eq 'modrinth:abc123' | Select-Object -First 1
            $categorized.Id | Should Be 'modrinth:abc123'
            $categorized.Category | Should Be 'performance'
        }
    }

    Describe 'Default Options y resource packs' {
        BeforeEach {
            $fixtureRoot = Join-Path $TestDrive 'packs'
            [System.IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
            $projectPath = New-TestModpack $fixtureRoot 'Pack' 'pack'
            $project = Read-ModpackProject $projectPath
        }

        It 'lee corchetes dentro de strings sin cerrar el array' {
            $text = @'
defaultResourcePacks = [
  "vanilla",
  "file/Animated 3D Wind Charge [1.1].zip",
  "file/§2p1kl's 3D Items§r§0.zip",
]
'@
            $values = @(Get-TomlArrayStrings -Text $text -Key defaultResourcePacks)
            $values.Count | Should Be 3
            $values[1] | Should Be 'file/Animated 3D Wind Charge [1.1].zip'
            $values[2] | Should Be "file/§2p1kl's 3D Items§r§0.zip"
        }

        It 'invierte correctamente la prioridad de Default Options' {
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'config/defaultoptions-common.toml'), 'defaultResourcePacks = ["low", "middle", "high"]')
            $order = @(Get-DefaultResourcePackOrder $project)
            $order | Should Be @('high', 'middle', 'low')
        }

        It 'muestra el ID de un pack integrado sin nombre bonito' {
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'config/defaultoptions-common.toml'), 'defaultResourcePacks = ["unknown:builtin"]')
            $resource = (Get-ModpackInventory $project).ActiveResources[0]
            $resource.Name | Should Be 'unknown:builtin'
            $resource.Source | Should Be 'builtin'
        }

        It 'resuelve el nombre real de un resource pack físico' {
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'resourcepacks/animated.pw.toml'), @'
name = "Animated Wind"
filename = "Animated 3D Wind Charge [1.1].zip"
side = "client"
'@)
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'config/defaultoptions-common.toml'), 'defaultResourcePacks = ["file/Animated 3D Wind Charge [1.1].zip"]')
            (Get-ModpackInventory $project).ActiveResources[0].Name | Should Be 'Animated Wind'
        }

        It 'activa un pack físico en la prioridad visible solicitada' {
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'resourcepacks/active.pw.toml'), "name = `"Active Pack`"`nfilename = `"active.zip`"")
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'resourcepacks/new.pw.toml'), "name = `"New Pack`"`nfilename = `"new.zip`"")
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'config/defaultoptions-common.toml'), 'defaultResourcePacks = ["vanilla", "file/active.zip"]')

            $result = Enable-ModpackResourcePack -Project $project -Selector 'New Pack' -Position 2

            $result.WasActive | Should Be $false
            $result.Item.Priority | Should Be 2
            @(Get-DefaultResourcePackOrder $project) | Should Be @('file/active.zip', 'file/new.zip', 'vanilla')
        }

        It 'recoloca un pack ya activo sin duplicarlo' {
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'resourcepacks/active.pw.toml'), "name = `"Active Pack`"`nfilename = `"active.zip`"")
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'config/defaultoptions-common.toml'), 'defaultResourcePacks = ["vanilla", "file/active.zip"]')

            $result = Enable-ModpackResourcePack -Project $project -Selector 'active.zip' -Position 2

            $result.WasActive | Should Be $true
            @(Get-DefaultResourcePackOrder $project) | Should Be @('vanilla', 'file/active.zip')
        }

        It 'rechaza posiciones fuera del orden posible sin modificar el fichero' {
            $path = Join-Path $projectPath 'config/defaultoptions-common.toml'
            [System.IO.File]::WriteAllText($path, 'defaultResourcePacks = ["vanilla"]')
            $before = Get-Content -Raw -LiteralPath $path

            { Enable-ModpackResourcePack -Project $project -Selector 'vanilla' -Position 3 } | Should Throw 'entre 1 y 1'
            (Get-Content -Raw -LiteralPath $path) | Should Be $before
        }
    }

    Describe 'Filtros de inventario' {
        BeforeEach {
            $filterInventory = [pscustomobject]@{
                Project = [pscustomobject]@{ Id = 'filter' }
                Metadata = @{ Categories = @{ performance = @{ Name = 'RENDIMIENTO'; Order = 10 } } }
                Mods = @(
                    [pscustomobject]@{ Id='modrinth:a'; Kind='mod'; Name='Sodium'; Filename='sodium.jar'; Side='client'; Source='packwiz'; Category='performance' }
                    [pscustomobject]@{ Id='local:mods/b.jar'; Kind='mod'; Name='Host Tool'; Filename='b.jar'; Side='server'; Source='local'; Category='unclassified' }
                    [pscustomobject]@{ Id='modrinth:c'; Kind='mod'; Name='Shared Mod'; Filename='c.jar'; Side='both'; Source='packwiz'; Category='unclassified' }
                )
                ActiveResources = @(
                    [pscustomobject]@{ Id='file/active.zip'; Kind='resourcepack'; Name='Active Pack'; Filename='active.zip'; Source='packwiz'; Enabled=$true; Priority=1 }
                    [pscustomobject]@{ Id='builtin:test'; Kind='resourcepack'; Name='Builtin Pack'; Filename=$null; Source='builtin'; Enabled=$true; Priority=2 }
                )
                InactiveResources = @(
                    [pscustomobject]@{ Id='local:resourcepacks/old.zip'; Kind='resourcepack'; Name='Old Pack'; Filename='old.zip'; Source='local' }
                )
                Shaders = @(
                    [pscustomobject]@{ Id='modrinth:shader'; Kind='shaderpack'; Name='Vivid'; Filename='vivid.zip'; Source='packwiz' }
                )
            }
        }

        It 'filtra mods por categoría' {
            $view = Select-ModpackInventory -Inventory $filterInventory -Category performance
            $view.IncludedTypes | Should Be @('mod')
            $view.Mods.Count | Should Be 1
            $view.Mods[0].Name | Should Be 'Sodium'
        }

        It 'acepta host como alias del side server y combina source' {
            $view = Select-ModpackInventory -Inventory $filterInventory -Side host -Source local
            $view.Mods.Count | Should Be 1
            $view.Mods[0].Name | Should Be 'Host Tool'
        }

        It 'filtra resource packs por estado y texto' {
            $view = Select-ModpackInventory -Inventory $filterInventory -Type resourcepack -State active -Search builtin
            $view.ActiveResources.Count | Should Be 1
            $view.ActiveResources[0].Source | Should Be 'builtin'
            $view.InactiveResources.Count | Should Be 0
        }

        It 'filtra elementos locales de todos los tipos' {
            $view = Select-ModpackInventory -Inventory $filterInventory -Source local
            $view.TotalMatches | Should Be 2
            $view.Mods.Count | Should Be 1
            $view.InactiveResources.Count | Should Be 1
        }

        It 'rechaza combinaciones de filtros contradictorias' {
            { Select-ModpackInventory -Inventory $filterInventory -Type shaderpack -Side client } | Should Throw 'solo se pueden aplicar a mods'
            { Select-ModpackInventory -Inventory $filterInventory -Category performance -State active } | Should Throw 'No se pueden combinar'
        }
    }

    Describe 'Procesos nativos' {
        It 'convierte un exit code no cero en error comprensible' {
            { Invoke-NativeCommandChecked -FilePath 'pwsh' -Arguments @('-NoProfile', '-Command', 'exit 7') -WorkingDirectory $TestDrive } | Should Throw 'código 7'
        }
    }
}
