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
            $env:MODPACKTOOLS_CONFIG_HOME = Join-Path $TestDrive 'config'
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
    }

    Describe 'Procesos nativos' {
        It 'convierte un exit code no cero en error comprensible' {
            { Invoke-NativeCommandChecked -FilePath 'pwsh' -Arguments @('-NoProfile', '-Command', 'exit 7') -WorkingDirectory $TestDrive } | Should Throw 'código 7'
        }
    }
}
