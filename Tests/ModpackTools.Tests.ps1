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
            Categories = @{ performance = @{ Name = 'PERFORMANCE'; Order = 10 } }; Mods = @{}; ResourcePacks = @{}
        }
        return $projectRoot
    }

    function New-TestMrpack {
        param([string]$Path, [string]$ManifestJson, [hashtable]$Overrides = @{})
        $archive = [System.IO.Compression.ZipFile]::Open($Path, [System.IO.Compression.ZipArchiveMode]::Create)
        try {
            $manifestEntry = $archive.CreateEntry('modrinth.index.json')
            $writer = [System.IO.StreamWriter]::new($manifestEntry.Open(), [System.Text.UTF8Encoding]::new($false))
            try { $writer.Write($ManifestJson) } finally { $writer.Dispose() }
            foreach ($relative in $Overrides.Keys) {
                $entry = $archive.CreateEntry("overrides/$relative")
                $entryWriter = [System.IO.StreamWriter]::new($entry.Open(), [System.Text.UTF8Encoding]::new($false))
                try { $entryWriter.Write([string]$Overrides[$relative]) } finally { $entryWriter.Dispose() }
            }
        }
        finally { $archive.Dispose() }
    }

    Describe 'Theme configuration' {
        It 'loads every color from the source theme file' {
            $colors = Read-MpThemeColors
            $colors.Count | Should Be 10
            $colors.client | Should Be '#748FFC'
            $colors.host | Should Be '#BE70FF'
        }

        It 'rejects missing and malformed colors' {
            $missing = Join-Path $TestDrive 'missing-theme.toml'
            [System.IO.File]::WriteAllText($missing, "[colors]`nclient = `"#748FFC`"")
            { Read-MpThemeColors -Path $missing } | Should Throw "Required theme color 'host'"

            $invalid = Join-Path $TestDrive 'invalid-theme.toml'
            $theme = Get-Content -Raw -LiteralPath (Join-Path $script:ModuleRoot 'theme.toml')
            [System.IO.File]::WriteAllText($invalid, $theme.Replace('#748FFC', 'blue'))
            { Read-MpThemeColors -Path $invalid } | Should Throw '#RRGGBB'
        }
    }

    Describe 'Packwiz dependency management' {
        BeforeEach {
            $script:ConfigHomeOverride = Join-Path $TestDrive 'dependency-config'
            $script:DependencyManifestOverride = $null
        }

        AfterEach { $script:DependencyManifestOverride = $null }

        It 'uses and clears an explicit Packwiz executable override' {
            $executable = Join-Path $TestDrive 'custom-packwiz.exe'
            [System.IO.File]::WriteAllText($executable, 'fixture')
            Set-ModpackToolsConfigValue -Name packwiz -Value $executable | Should Be ([System.IO.Path]::GetFullPath($executable))
            $resolved = Resolve-MpPackwiz
            $resolved.Available | Should Be $true
            $resolved.Source | Should Be 'configured'
            $resolved.Path | Should Be ([System.IO.Path]::GetFullPath($executable))

            Set-ModpackToolsConfigValue -Name packwiz -Value auto | Should Be 'auto'
            (Get-ModpackToolsConfig).ContainsKey('PackwizPath') | Should Be $false
        }

        It 'installs a managed Packwiz archive only after verifying both hashes' {
            $contents = Join-Path $TestDrive 'packwiz-contents'
            [System.IO.Directory]::CreateDirectory($contents) | Out-Null
            $executable = Join-Path $contents 'packwiz.exe'
            [System.IO.File]::WriteAllText($executable, 'verified executable')
            [System.IO.File]::WriteAllText((Join-Path $contents 'LICENSE.packwiz.txt'), 'MIT fixture')
            $archive = Join-Path $TestDrive 'packwiz.zip'
            [System.IO.Compression.ZipFile]::CreateFromDirectory($contents, $archive)
            $script:DependencyManifestOverride = @{
                Packwiz = @{ WindowsX64 = @{
                    Commit = 'fixture'; DisplayVersion = 'fixture'; Uri = 'https://example.invalid/packwiz.zip'
                    ArchiveSha256 = (Get-FileHash $archive -Algorithm SHA256).Hash
                    Executable = 'packwiz.exe'; ExecutableSha256 = (Get-FileHash $executable -Algorithm SHA256).Hash
                    License = 'LICENSE.packwiz.txt'
                } }
            }

            $installed = Install-MpManagedPackwiz -ArchivePath $archive
            $installed.Installed | Should Be $true
            Test-Path -LiteralPath $installed.Path -PathType Leaf | Should Be $true
            (Get-Content -Raw -LiteralPath $installed.Path) | Should Be 'verified executable'
            Test-Path -LiteralPath (Join-Path (Split-Path -Parent $installed.Path) 'LICENSE.packwiz.txt') | Should Be $true
        }

        It 'rejects a managed archive whose declared hash does not match' {
            $archive = Join-Path $TestDrive 'invalid-packwiz.zip'
            [System.IO.File]::WriteAllText($archive, 'not a zip')
            $script:DependencyManifestOverride = @{
                Packwiz = @{ WindowsX64 = @{
                    Commit = 'fixture'; DisplayVersion = 'fixture'; Uri = 'https://example.invalid/packwiz.zip'
                    ArchiveSha256 = ('0' * 64); Executable = 'packwiz.exe'; ExecutableSha256 = ('0' * 64)
                    License = 'LICENSE.packwiz.txt'
                } }
            }
            { Install-MpManagedPackwiz -ArchivePath $archive } | Should Throw 'failed SHA-256 verification'
        }
    }

    Describe 'Packwiz project creation' {
        It 'builds init arguments for every supported loader using the compatible latest version' {
            $fabric = @(Get-PackwizInitArguments -Name 'Fabric Pack' -MinecraftVersion '1.21.1' -PackVersion '0.1.0' -Loader FABRIC)
            $fabric | Should Be @('init', '--yes', '--name', 'Fabric Pack', '--mc-version', '1.21.1', '--version', '0.1.0', '--modloader', 'fabric', '--fabric-latest')

            $quilt = @(Get-PackwizInitArguments -Name 'Quilt Pack' -MinecraftVersion '1.21.1' -PackVersion '0.1.0' -Loader Quilt)
            $quilt | Should Be @('init', '--yes', '--name', 'Quilt Pack', '--mc-version', '1.21.1', '--version', '0.1.0', '--modloader', 'quilt', '--quilt-latest')

            $forge = @(Get-PackwizInitArguments -Name 'Forge Pack' -MinecraftVersion '1.20.1' -PackVersion '0.1.0' -Loader Forge)
            $forge | Should Be @('init', '--yes', '--name', 'Forge Pack', '--mc-version', '1.20.1', '--version', '0.1.0', '--modloader', 'forge', '--forge-latest')

            $neoForge = @(Get-PackwizInitArguments -Name 'NeoForge Pack' -MinecraftVersion '1.21.1' -PackVersion '0.1.0' -Loader NeoForge)
            $neoForge | Should Be @('init', '--yes', '--name', 'NeoForge Pack', '--mc-version', '1.21.1', '--version', '0.1.0', '--modloader', 'neoforge', '--neoforge-latest')
        }

        It 'passes an explicit NeoForge version instead of requesting the latest one' {
            $arguments = @(Get-PackwizInitArguments -Name 'NeoForge Pack' -MinecraftVersion '1.21.1' -PackVersion '0.1.0' -Loader neoforge -LoaderVersion '21.1.200')
            ($arguments -contains '--neoforge-version') | Should Be $true
            ($arguments -contains '21.1.200') | Should Be $true
            ($arguments -contains '--neoforge-latest') | Should Be $false
        }

        It 'rejects loaders that project creation does not support' {
            { Get-PackwizInitArguments -Name 'Unsupported' -MinecraftVersion '1.21.1' -PackVersion '0.1.0' -Loader liteloader } | Should Throw 'allowed values: fabric, quilt, forge, neoforge'
        }

        It 'creates and discovers a NeoForge project through Packwiz' {
            $root = Join-Path $TestDrive 'neoforge-root'
            [System.IO.Directory]::CreateDirectory($root) | Out-Null
            $script:ConfigHomeOverride = Join-Path $TestDrive 'neoforge-config'
            Set-ModpackToolsConfigValue -Name root -Value $root | Out-Null
            Mock Invoke-Packwiz {
                param($Arguments, $WorkingDirectory)
                $script:CapturedInitArguments = @($Arguments)
                [System.IO.File]::WriteAllText((Join-Path $WorkingDirectory 'pack.toml'), @'
name = "NeoForge Test"
version = "0.1.0"
pack-format = "packwiz:1.1.0"

[versions]
minecraft = "1.21.1"
neoforge = "21.1.200"
'@)
                [System.IO.File]::WriteAllText((Join-Path $WorkingDirectory 'index.toml'), 'hash-format = "sha256"')
            }

            $project = New-ModpackProject -Id 'neo-test' -Name 'NeoForge Test' -MinecraftVersion '1.21.1' -Loader neoforge -LoaderVersion '21.1.200'

            $project.Id | Should Be 'neo-test'
            $project.Loader | Should Be 'neoforge'
            $project.LoaderVersion | Should Be '21.1.200'
            ($script:CapturedInitArguments -contains '--modloader') | Should Be $true
            ($script:CapturedInitArguments -contains 'neoforge') | Should Be $true
            ($script:CapturedInitArguments -contains '--neoforge-version') | Should Be $true
            Test-Path -LiteralPath (Join-Path $project.Root '.modpack/project.psd1') | Should Be $true
        }
    }

    Describe 'Existing Packwiz project initialization' {
        BeforeEach {
            $script:ConfigHomeOverride = Join-Path $TestDrive ('init-config-' + [guid]::NewGuid().ToString('N'))
            $script:InitRoot = Join-Path $TestDrive ('init-root-' + [guid]::NewGuid().ToString('N'))
            [System.IO.Directory]::CreateDirectory($script:InitRoot) | Out-Null
            Set-ModpackToolsConfigValue -Name root -Value $script:InitRoot | Out-Null
        }

        function New-ConventionalPackwizProject {
            param([string]$Name, [string]$Loader = 'fabric', [switch]$ExistingDocumentation)
            $path = Join-Path $script:InitRoot $Name
            [System.IO.Directory]::CreateDirectory($path) | Out-Null
            [System.IO.File]::WriteAllText((Join-Path $path 'pack.toml'), @"
name = "$Name"
version = "0.4.0"
pack-format = "packwiz:1.1.0"

[index]
file = "index.toml"
hash-format = "sha256"
hash = "fixture"

[versions]
minecraft = "1.21.1"
$Loader = "loader-version"
"@)
            [System.IO.File]::WriteAllText((Join-Path $path 'index.toml'), 'hash-format = "sha256"')
            [System.IO.Directory]::CreateDirectory((Join-Path $path 'mods')) | Out-Null
            [System.IO.File]::WriteAllText((Join-Path $path 'mods/existing.pw.toml'), 'name = "Existing"')
            if ($ExistingDocumentation) {
                [System.IO.File]::WriteAllText((Join-Path $path 'README.md'), 'original readme')
                [System.IO.File]::WriteAllText((Join-Path $path '.gitignore'), 'custom-cache/')
            }
            return $path
        }

        It 'adopts a conventional project with a minimal descriptor and preserves existing files' {
            $path = New-ConventionalPackwizProject -Name 'Existing Forge' -Loader forge -ExistingDocumentation
            $packBefore = Get-Content -Raw -LiteralPath (Join-Path $path 'pack.toml')
            $indexBefore = Get-Content -Raw -LiteralPath (Join-Path $path 'index.toml')
            $modBefore = Get-Content -Raw -LiteralPath (Join-Path $path 'mods/existing.pw.toml')

            $result = Initialize-ExistingModpackProject -Id existing-forge -Path $path

            $result.Project.Id | Should Be 'existing-forge'
            $result.Project.Loader | Should Be 'forge'
            $result.Project.DisplayName | Should Be 'Existing Forge'
            $result.Project.DisplayVersion | Should Be '0.4.0'
            $descriptor = Import-PowerShellDataFile (Join-Path $path '.modpack/project.psd1')
            @($descriptor.Keys | Sort-Object) | Should Be @('Id', 'SchemaVersion')
            $metadata = Import-PowerShellDataFile (Join-Path $path '.modpack/metadata.psd1')
            @($metadata.Categories.Keys).Count | Should Be 0
            @($metadata.Mods.Keys).Count | Should Be 0
            @($metadata.ResourcePacks.Keys).Count | Should Be 0
            (Get-Content -Raw -LiteralPath (Join-Path $path 'pack.toml')) | Should Be $packBefore
            (Get-Content -Raw -LiteralPath (Join-Path $path 'index.toml')) | Should Be $indexBefore
            (Get-Content -Raw -LiteralPath (Join-Path $path 'mods/existing.pw.toml')) | Should Be $modBefore
            (Get-Content -Raw -LiteralPath (Join-Path $path 'README.md')) | Should Be 'original readme'
            (Get-Content -Raw -LiteralPath (Join-Path $path '.gitignore')) | Should Be 'custom-cache/'
            @(Get-ModpackProjects | Where-Object Id -eq 'existing-forge').Count | Should Be 1
        }

        It 'adopts every recognized loader and creates only missing convenience files' {
            $index = 0
            foreach ($loader in @('fabric', 'quilt', 'forge', 'neoforge')) {
                $index++
                $path = New-ConventionalPackwizProject -Name "Pack $index" -Loader $loader
                $result = Initialize-ExistingModpackProject -Id "pack-$index" -Path $path
                $result.Project.Loader | Should Be $loader
                Test-Path -LiteralPath (Join-Path $path 'README.md') | Should Be $true
                (Get-Content -Raw -LiteralPath (Join-Path $path '.gitignore')) | Should Be "dist/`n"
            }
        }

        It 'uses the current directory when path is omitted and accepts editorial overrides' {
            $path = New-ConventionalPackwizProject -Name 'Current Pack' -Loader quilt
            Push-Location $path
            try {
                $result = Initialize-ExistingModpackProject -Id current-pack -DisplayName 'Current Display' -DisplayVersion 'Release' -OutputName 'Current.mrpack'
            }
            finally { Pop-Location }
            $result.Project.DisplayName | Should Be 'Current Display'
            $result.Project.DisplayVersion | Should Be 'Release'
            $result.Project.OutputName | Should Be 'Current.mrpack'
        }

        It 'supports a custom relative Packwiz index and includes it in rollback snapshots' {
            $path = New-ConventionalPackwizProject -Name 'Custom Index' -Loader fabric
            $packPath = Join-Path $path 'pack.toml'
            $packText = (Get-Content -Raw -LiteralPath $packPath).Replace('file = "index.toml"', 'file = "meta/custom-index.toml"')
            [System.IO.File]::WriteAllText($packPath, $packText)
            [System.IO.Directory]::CreateDirectory((Join-Path $path 'meta')) | Out-Null
            [System.IO.File]::WriteAllText((Join-Path $path 'meta/custom-index.toml'), 'hash-format = "sha256"')

            $result = Initialize-ExistingModpackProject -Id custom-index -Path $path
            $result.Project.IndexFile | Should Be 'meta/custom-index.toml'
            $result.Project.IndexPath | Should Be (Join-Path $path 'meta/custom-index.toml')
            Assert-ModpackStructure -Project $result.Project
            $snapshot = Get-PackwizStateSnapshot -Project $result.Project
            $snapshot.ContainsKey('meta\custom-index.toml') | Should Be $true
        }

        It 'rejects projects outside the configured root and duplicate IDs' {
            $outside = Join-Path $TestDrive ('outside-' + [guid]::NewGuid().ToString('N'))
            [System.IO.Directory]::CreateDirectory($outside) | Out-Null
            { Initialize-ExistingModpackProject -Id outside -Path $outside } | Should Throw 'not a direct child'

            $first = New-ConventionalPackwizProject -Name 'First'
            $second = New-ConventionalPackwizProject -Name 'Second'
            Initialize-ExistingModpackProject -Id duplicate -Path $first | Out-Null
            { Initialize-ExistingModpackProject -Id duplicate -Path $second } | Should Throw 'already registered'
            { Initialize-ExistingModpackProject -Id duplicate -Path $first } | Should Throw 'already initialized'
        }

        It 'rejects missing Packwiz files, invalid manifests, and conflicting ModpackTools state' {
            $missing = Join-Path $script:InitRoot 'Missing'
            [System.IO.Directory]::CreateDirectory($missing) | Out-Null
            { Initialize-ExistingModpackProject -Id missing -Path $missing } | Should Throw 'pack.toml'

            $invalid = Join-Path $script:InitRoot 'Invalid'
            [System.IO.Directory]::CreateDirectory($invalid) | Out-Null
            [System.IO.File]::WriteAllText((Join-Path $invalid 'pack.toml'), 'name = "Invalid"')
            [System.IO.File]::WriteAllText((Join-Path $invalid 'index.toml'), 'hash-format = "sha256"')
            { Initialize-ExistingModpackProject -Id invalid -Path $invalid } | Should Throw 'missing required data'

            $escaping = New-ConventionalPackwizProject -Name 'Escaping Index'
            $escapingPack = Join-Path $escaping 'pack.toml'
            $escapingText = (Get-Content -Raw -LiteralPath $escapingPack).Replace('file = "index.toml"', 'file = "../outside.toml"')
            [System.IO.File]::WriteAllText($escapingPack, $escapingText)
            { Initialize-ExistingModpackProject -Id escaping -Path $escaping } | Should Throw 'escapes project root'

            $partial = New-ConventionalPackwizProject -Name 'Partial'
            [System.IO.Directory]::CreateDirectory((Join-Path $partial '.modpack')) | Out-Null
            [System.IO.File]::WriteAllText((Join-Path $partial '.modpack/foreign.txt'), 'preserve')
            { Initialize-ExistingModpackProject -Id partial -Path $partial } | Should Throw 'will not overwrite'
            (Get-Content -Raw -LiteralPath (Join-Path $partial '.modpack/foreign.txt')) | Should Be 'preserve'

            $fileConflict = New-ConventionalPackwizProject -Name 'File Conflict'
            [System.IO.File]::WriteAllText((Join-Path $fileConflict '.modpack'), 'foreign file')
            { Initialize-ExistingModpackProject -Id file-conflict -Path $fileConflict } | Should Throw 'is not a directory'

            $invalidOutput = New-ConventionalPackwizProject -Name 'Invalid Output'
            { Initialize-ExistingModpackProject -Id invalid-output -Path $invalidOutput -OutputName '..\outside.mrpack' } | Should Throw 'valid .mrpack filename'
        }

        It 'is exposed through the public command with the same output contract' {
            $path = New-ConventionalPackwizProject -Name 'Public Init' -Loader neoforge
            $output = (modpack init public-init --path $path 6>&1 | Out-String)
            $output | Should Match 'Existing Packwiz project initialized for ModpackTools'
            $output | Should Match 'Next: modpack use public-init'
            $output | Should Match "(`r?`n){2}$"
            (Read-ModpackProject -ProjectRoot $path).Id | Should Be 'public-init'
        }

        It 'rolls back every created file when initialization fails' {
            $path = New-ConventionalPackwizProject -Name 'Rollback'
            Mock Write-ModpackProjectReadme { throw 'injected README failure' }

            { Initialize-ExistingModpackProject -Id rollback -Path $path } | Should Throw 'injected README failure'

            Test-Path -LiteralPath (Join-Path $path '.modpack/project.psd1') | Should Be $false
            Test-Path -LiteralPath (Join-Path $path '.modpack/metadata.psd1') | Should Be $false
            Test-Path -LiteralPath (Join-Path $path '.modpack') | Should Be $false
            Test-Path -LiteralPath (Join-Path $path 'README.md') | Should Be $false
            Test-Path -LiteralPath (Join-Path $path '.gitignore') | Should Be $false
            Test-Path -LiteralPath (Join-Path $path 'pack.toml') | Should Be $true
        }
    }

    Describe 'MRPack diff' {
        It 'reads manifest entries and uncompressed overrides semantically' {
            $path = Join-Path $TestDrive 'sample.mrpack'
            New-TestMrpack -Path $path -ManifestJson @'
{"name":"Sample","versionId":"1.0","dependencies":{"minecraft":"1.21.1"},"files":[{"path":"mods/sample.jar","hashes":{"sha512":"abc"},"downloads":["https://example.invalid/sample.jar"],"fileSize":12,"env":{"client":"required","server":"required"}}]}
'@ -Overrides @{ 'config/sample.json' = '{"enabled":true}' }

            $snapshot = @(Get-MrpackSnapshot $path)
            $snapshot.Count | Should Be 5
            ($snapshot | Where-Object Path -eq 'mods/sample.jar').Kind | Should Be 'MOD'
            ($snapshot | Where-Object Path -eq 'config/sample.json').Kind | Should Be 'CONFIG'
        }

        It 'classifies added changed and removed records' {
            $baseline = @(
                [pscustomobject]@{ Key='manifest:mods/old.jar'; Kind='MOD'; Path='mods/old.jar'; Fingerprint='old' }
                [pscustomobject]@{ Key='override:config/shared.json'; Kind='CONFIG'; Path='config/shared.json'; Fingerprint='before' }
            )
            $current = @(
                [pscustomobject]@{ Key='override:config/shared.json'; Kind='CONFIG'; Path='config/shared.json'; Fingerprint='after' }
                [pscustomobject]@{ Key='manifest:mods/new.jar'; Kind='MOD'; Path='mods/new.jar'; Fingerprint='new' }
            )
            $diff = Compare-MrpackSnapshots -Baseline $baseline -Current $current
            $diff.Total | Should Be 3
            $diff.Added[0].Path | Should Be 'mods/new.jar'
            $diff.Changed[0].Path | Should Be 'config/shared.json'
            $diff.Removed[0].Path | Should Be 'mods/old.jar'
        }

        It 'renders a partial diff when other sections are empty' {
            $diff = [pscustomobject]@{
                Project = [pscustomobject]@{ DisplayName = 'Sample' }
                BaselinePath = 'C:\builds\sample.mrpack'
                BaselineTime = [datetime]'2026-01-01'
                Added = @()
                Changed = @([pscustomobject]@{ Kind='OVERRIDE'; Path='README.md' })
                Removed = @()
                Total = 1
            }
            { Write-ModpackDiff -Diff $diff } | Should Not Throw
        }

        It 'rejects the old add mod syntax' {
            { Invoke-MpAdd @('mod') } | Should Throw "Argument 'mod' is not valid"
            { Invoke-MpAdd @('mod', 'sodium') } | Should Throw 'The command arguments do not match'
        }
    }

    Describe 'Generated project README' {
        It 'documents the current CLI and sources of truth' {
            $fixtureRoot = Join-Path $TestDrive 'readme-packs'
            [System.IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
            $projectPath = New-TestModpack $fixtureRoot 'Readme Pack' 'readme'
            $text = Get-ModpackProjectReadmeText -Project (Read-ModpackProject $projectPath)
            $text | Should Match 'modpack add <slug>'
            $text | Should Match 'modpack search <query>'
            $text | Should Match 'modpack add <search-number>'
            $text | Should Match 'modpack update --all'
            $text | Should Match 'resource packs, and shaders'
            $text | Should Match 'one transaction'
            $text | Should Match 'modpack diff'
            $text | Should Match 'modpack diff --project readme'
            $text | Should Match 'Every command that operates on an existing project'
            $text | Should Match 'modpack resource enable'
            $text | Should Match 'modpack resource move'
            $text | Should Match 'Resource pack activation and ordering require Default Options'
            $text | Should Match 'modpack update <inventory-number>'
            $text | Should Match 'modpack classify list'
            $text | Should Match 'modpack classify create <id>'
            $text | Should Match 'modpack classify set <name\|id\|filename>'
            $text | Should Match 'modpack classify remove <category\|number>'
            $text | Should Match 'Search, inventory, and category numbers have separate contexts'
            $text | Should Match 'defaultoptions-common.toml'
            $text | Should Match 'modpack --help'
            $text | Should Match 'modpack inventory --help'
            $text | Should Match 'modpack --version'
            $text | Should Match 'modpack doctor'
            $text | Should Match 'modpack new <id>.*fabric\|quilt\|forge\|neoforge'
            $text | Should Match 'modpack init <id> --path'
            $text | Should Not Match 'modpack add mod'
        }
    }

    Describe 'CLI help' {
        It 'uses one catalog for every executable command' {
            $catalog = Get-MpCommandCatalog
            @($catalog.Keys).Count | Should Be 16
            @($catalog.Keys) | Should Be @('list', 'use', 'status', 'new', 'init', 'inventory', 'search', 'add', 'classify', 'resource', 'side', 'update', 'build', 'diff', 'doctor', 'config')
            foreach ($name in $catalog.Keys) {
                $catalog[$name].Summary | Should Not BeNullOrEmpty
                $catalog[$name].Description | Should Not BeNullOrEmpty
                $catalog[$name].Handler | Should Match '^Invoke-Mp[A-Z]'
                (Get-Command $catalog[$name].Handler -ErrorAction Stop).CommandType | Should Be 'Function'
                @($catalog[$name].Usage).Count | Should BeGreaterThan 0
            }
        }

        It 'shows the same overview for no arguments and global --help' {
            $bare = (modpack 6>&1 | Out-String)
            $explicit = (modpack --help 6>&1 | Out-String)
            $bare | Should Be $explicit
            $explicit | Should Match 'PROJECTS'
            $explicit | Should Match 'CONTENT'
            $explicit | Should Match 'BUILD AND CONFIGURATION'
            $explicit | Should Match 'modpack <command> --help'
            $explicit | Should Not Match 'modpack help'
        }

        It 'renders detailed help for every command without resolving a project' {
            foreach ($name in (Get-MpCommandCatalog).Keys) {
                $output = (& modpack $name '--help' 6>&1 | Out-String)
                $output | Should Match '(?m)^USAGE\r?$'
                $output | Should Match ([regex]::Escape("modpack $name"))
            }
        }

        It 'rejects help as a command and points to global --help' {
            $record = $null
            try { modpack help } catch { $record = $_ }
            $record | Should Not BeNullOrEmpty
            $record.FullyQualifiedErrorId | Should Match '^ModpackTools\.Command\.Unknown'
            $record.Exception.Message | Should Match "Command 'help' is not recognized"
            $record.Exception.Message | Should Match '(?m)^Try: modpack --help$'
        }

        It 'prints the module version through the global version option' {
            (modpack --version 6>&1 | Out-String).Trim() | Should Be 'ModpackTools 2.0.0'
            { modpack version } | Should Throw "Command 'version' is not recognized"
        }

        It 'terminates every successful public command output with a blank line' {
            Mock Write-MpOutputEnd {}

            modpack --version
            Assert-MockCalled Write-MpOutputEnd -Times 1 -Exactly

            modpack --help
            Assert-MockCalled Write-MpOutputEnd -Times 2 -Exactly
        }
    }

    Describe 'Environment doctor' {
        BeforeEach {
            $fixtureRoot = Join-Path $TestDrive 'doctor-packs'
            [System.IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
            $script:ConfigHomeOverride = Join-Path $TestDrive 'doctor-config'
            Write-PowerShellDataFileAtomic -Path (Get-ModpackToolsConfigPath) -Data @{ Root = $fixtureRoot }
            $doctorProjectPath = New-TestModpack $fixtureRoot 'Doctor Pack' 'doctor'
            Mock Resolve-MpPackwiz { [pscustomobject]@{ Available=$true; Path='C:\Tools\packwiz.exe'; Source='configured'; Version='fixture' } }
            Mock Test-MpPackwizInvocation { $true }
            Mock Get-MpMinecraftJavaCheck { New-MpDoctorCheck -Section OPTIONAL -Status warn -Label 'Minecraft Java' -Value 'Not detected' }
        }

        It 'separates required health from optional warnings' {
            $report = Get-MpDoctorReport
            $report.Failures | Should Be 0
            $report.Warnings | Should BeGreaterThan 0
            ($report.Checks | Where-Object { $_.Section -eq 'PACKWIZ' -and $_.Label -eq 'Invocation' }).Status | Should Be 'pass'
            ($report.Checks | Where-Object { $_.Section -eq 'PROJECT ROOT' -and $_.Label -eq 'Discovery' }).Value | Should Be '1 project discovered'
            ($report.Checks | Where-Object { $_.Label -eq 'Active' }).Status | Should Be 'info'
            ($report.Checks | Where-Object { $_.Label -eq 'Default Options' }).Status | Should Be 'warn'
        }

        It 'reports Default Options as an optional ready project integration' {
            [System.IO.File]::WriteAllText((Join-Path $doctorProjectPath 'mods/default-options.pw.toml'), @'
name = "Default Options"
filename = "defaultoptions-fabric.jar"
[update.modrinth]
mod-id = "WEg59z5b"
'@)
            [System.IO.File]::WriteAllText((Join-Path $doctorProjectPath 'config/defaultoptions-common.toml'), 'defaultResourcePacks = []')

            $check = (Get-MpDoctorReport).Checks | Where-Object { $_.Label -eq 'Default Options' }
            $check.Status | Should Be 'pass'
            $check.Value | Should Be '1/1 projects ready'
        }

        It 'requires --fix when --yes is used' {
            { Invoke-MpDoctor @('--yes') } | Should Throw "Option '--yes' requires '--fix'"
        }

        It 'reports an empty configuration as a missing required root' {
            $script:ConfigHomeOverride = Join-Path $TestDrive 'empty-doctor-config'
            $report = Get-MpDoctorReport
            $report.Failures | Should BeGreaterThan 0
            ($report.Checks | Where-Object { $_.Section -eq 'PROJECT ROOT' -and $_.Label -eq 'Root' }).Value | Should Be 'Not configured'
        }
    }

    Describe 'Project discovery and resolution' {
        BeforeEach {
            $fixtureRoot = Join-Path $TestDrive 'root with spaces'
            [System.IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
            $script:ConfigHomeOverride = Join-Path $TestDrive 'config'
            Write-PowerShellDataFileAtomic -Path (Get-ModpackToolsConfigPath) -Data @{ Root = $fixtureRoot }
            $script:ActiveProjectId = $null
        }

        It 'discovers projects and reads project.psd1 and pack.toml' {
            New-TestModpack $fixtureRoot 'Pack Sample' 'sample' | Out-Null
            $projects = @(Get-ModpackProjects)
            $projects.Count | Should Be 1
            $projects[0].Id | Should Be 'sample'
            $projects[0].DisplayName | Should Be 'Display sample'
            $projects[0].MinecraftVersion | Should Be '1.21.1'
            $projects[0].Loader | Should Be 'fabric'
            $projects[0].Root | Should Match 'root with spaces'
        }

        It 'detects duplicate IDs' {
            New-TestModpack $fixtureRoot 'Pack A' 'same' | Out-Null
            New-TestModpack $fixtureRoot 'Pack B' 'same' | Out-Null
            { Get-ModpackProjects } | Should Throw 'Multiple registered projects use the same ID'
        }

        It 'gives an explicit ID priority over the active session' {
            New-TestModpack $fixtureRoot 'Pack A' 'one' | Out-Null
            New-TestModpack $fixtureRoot 'Pack B' 'two' | Out-Null
            $script:ActiveProjectId = 'one'
            (Resolve-ModpackProject).Id | Should Be 'one'
            (Resolve-ModpackProject -Id 'two').Id | Should Be 'two'
        }

        It 'resolves project commands from --project, a positional ID, or the active session' {
            New-TestModpack $fixtureRoot 'Pack A' 'one' | Out-Null
            New-TestModpack $fixtureRoot 'Pack B' 'two' | Out-Null
            $script:ActiveProjectId = 'one'
            (Resolve-MpCommandProject -Options @{}).Id | Should Be 'one'
            (Resolve-MpCommandProject -Options @{ project = 'two' }).Id | Should Be 'two'
            (Resolve-MpCommandProject -Options @{} -PositionalId 'two').Id | Should Be 'two'
        }

        It 'rejects simultaneous positional and --project IDs' {
            New-TestModpack $fixtureRoot 'Pack A' 'one' | Out-Null
            New-TestModpack $fixtureRoot 'Pack B' 'two' | Out-Null
            { Resolve-MpCommandProject -Options @{ project = 'one' } -PositionalId 'two' } | Should Throw 'both positionally'
        }

        It 'accepts --project in read-only project commands' {
            New-TestModpack $fixtureRoot 'Pack Public' 'public' | Out-Null
            { Invoke-MpStatus @('--project', 'public') } | Should Not Throw
            { Invoke-MpInventory @('--project', 'public', '--type', 'mod') } | Should Not Throw
        }

        It 'accepts public commands without additional arguments' {
            New-TestModpack $fixtureRoot 'Pack Public' 'public' | Out-Null
            { modpack list } | Should Not Throw
        }
    }

    Describe 'CLI option diagnostics' {
        It 'suggests the prefixed inventory filter when -- is omitted' {
            { Invoke-MpInventory @('search', 'Taverns') } | Should Throw 'Try: --search'
        }

        It 'uses the same diagnostic for options in other commands' {
            { Invoke-MpResource @('enable', 'Pack', 'position', '1') } | Should Throw 'Try: --position'
            { Invoke-MpAdd @('sodium', 'category', 'performance') } | Should Throw 'Try: --category'
        }
    }

    Describe 'Error message contract' {
        It 'formats expected errors with a stable ID and actionable hint' {
            $record = $null
            try { modpack definitely-not-a-command } catch { $record = $_ }
            $record | Should Not BeNullOrEmpty
            $record.FullyQualifiedErrorId | Should Match '^ModpackTools\.Command\.Unknown'
            $record.Exception.Message | Should Match "^Command 'definitely-not-a-command' is not recognized\."
            $record.Exception.Message | Should Match '(?m)^Try: modpack --help$'
            $record.ScriptStackTrace | Should Not Match '\\Private\\'
        }

        It 'keeps expected CLI implementation errors on the shared helper' {
            $implementationFiles = @(
                Get-ChildItem -LiteralPath (Join-Path $script:ModuleRoot 'Private') -Filter '*.ps1' -File
                Get-ChildItem -LiteralPath (Join-Path $script:ModuleRoot 'Public') -Filter '*.ps1' -File
            ) | Where-Object Name -ne 'Errors.ps1'
            $adHocThrows = @(
                foreach ($file in $implementationFiles) {
                    Select-String -LiteralPath $file.FullName -Pattern '\bthrow\s+[''"]' -AllMatches
                }
            )
            $adHocThrows.Count | Should Be 0
        }

        It 'uses namespaced error IDs at every expected error site' {
            $implementationFiles = @(
                Get-ChildItem -LiteralPath (Join-Path $script:ModuleRoot 'Private') -Filter '*.ps1' -File
                Get-ChildItem -LiteralPath (Join-Path $script:ModuleRoot 'Public') -Filter '*.ps1' -File
            )
            $invalidIds = @()
            foreach ($file in $implementationFiles) {
                $text = Get-Content -Raw -LiteralPath $file.FullName
                foreach ($match in [regex]::Matches($text, "-ErrorId\s+'([^']+)'")) {
                    if ($match.Groups[1].Value -notmatch '^[A-Z][A-Za-z]+\.[A-Z][A-Za-z]+$') { $invalidIds += $match.Groups[1].Value }
                }
            }
            $invalidIds.Count | Should Be 0
        }
    }

    Describe 'Metadata and normalized inventory' {
        BeforeEach {
            $fixtureRoot = Join-Path $TestDrive ('packs-' + [guid]::NewGuid().ToString('N'))
            [System.IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
            $projectPath = New-TestModpack $fixtureRoot 'Pack' 'pack'
            $project = Read-ModpackProject $projectPath
        }

        It 'reads editorial categories' {
            $metadata = Get-ModpackMetadata $project
            $metadata.Categories.performance.Name | Should Be 'PERFORMANCE'
            $metadata.Categories.performance.Order | Should Be 10
        }

        It 'places a mod without a category in unclassified' {
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

        It 'normalizes client server and both sides' {
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

        It 'sets a Packwiz side in its technical metadata' {
            $path = Join-Path $projectPath 'mods/managed.pw.toml'
            [System.IO.File]::WriteAllText($path, @'
name = "Managed"
filename = "managed.jar"
side = "both"
[update.modrinth]
mod-id = "managed-id"
version = "v1"
'@)
            $result = Set-ModpackModSide -Project $project -Selector managed -Side host
            $result.PreviousSide | Should Be 'both'
            $result.Side | Should Be 'server'
            (Get-TomlString -Text (Get-Content -Raw -LiteralPath $path) -Key side) | Should Be 'server'
            (Get-ModpackInventory $project).Mods[0].Side | Should Be 'server'
        }

        It 'stores an explicit side override for a local mod' {
            [System.IO.File]::WriteAllBytes((Join-Path $projectPath 'mods/local.jar'), [byte[]](1, 2, 3))
            $result = Set-ModpackModSide -Project $project -Selector local.jar -Side client
            $result.Side | Should Be 'client'
            (Get-ModpackMetadata $project).Mods[$result.Item.Id].Side | Should Be 'client'
            (Get-ModpackInventory $project).Mods[0].Side | Should Be 'client'
        }

        It 'rejects invalid sides without changing a managed file' {
            $path = Join-Path $projectPath 'mods/managed.pw.toml'
            [System.IO.File]::WriteAllText($path, "name = `"Managed`"`nfilename = `"managed.jar`"`nside = `"both`"")
            $before = [System.IO.File]::ReadAllBytes($path)
            { Set-ModpackModSide -Project $project -Selector managed -Side unknown } | Should Throw 'allowed values: client, host, both'
            [System.Linq.Enumerable]::SequenceEqual([byte[]]$before, [byte[]][System.IO.File]::ReadAllBytes($path)) | Should Be $true
        }

        It 'applies a valid category using the stable ID' {
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

    Describe 'Modrinth search' {
        BeforeEach {
            $fixtureRoot = Join-Path $TestDrive 'search-packs'
            [System.IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
            $projectPath = New-TestModpack $fixtureRoot 'Search Pack' 'search'
            $project = Read-ModpackProject $projectPath
            $script:ConfigHomeOverride = Join-Path $TestDrive 'search-config'
            Write-PowerShellDataFileAtomic -Path (Get-ModpackToolsConfigPath) -Data @{ Root = $fixtureRoot }
            Mock Invoke-RestMethod {
                [pscustomobject]@{
                    total_hits = 2
                    hits = @(
                        [pscustomobject]@{ project_id='AANobbMI'; slug='sodium'; project_type='mod'; title='Sodium'; author='jellysquid3'; description='Renderer optimization'; downloads=12000000 }
                        [pscustomobject]@{ project_id='fresh-id'; slug='fresh-animations'; project_type='resourcepack'; title='Fresh Animations'; author='FreshLX'; description='Animated entities'; downloads=3400000 }
                    )
                }
            }
        }

        It 'queries compatible Modrinth projects and normalizes numbered results' {
            $search = Search-ModrinthContent -Project $project -Query 'sodium' -Limit 10
            $search.Results.Count | Should Be 2
            $search.Results[0].Index | Should Be 1
            $search.Results[1].Type | Should Be 'resourcepack'
            Test-Path -LiteralPath (Get-ModrinthSearchCachePath) | Should Be $true
            Assert-MockCalled Invoke-RestMethod -Times 1 -ParameterFilter {
                $Uri -match 'api\.modrinth\.com/v2/search' -and
                [System.Uri]::UnescapeDataString($Uri) -match 'versions:1\.21\.1' -and
                $Headers.'User-Agent' -eq 'R3Neer-ModpackTools/2.0.0'
            }
        }

        It 'adds the loader facet to a mod-only search' {
            [void](Search-ModrinthContent -Project $project -Query 'performance' -Type mod)
            Assert-MockCalled Invoke-RestMethod -Times 1 -ParameterFilter {
                [System.Uri]::UnescapeDataString($Uri) -match 'categories:fabric'
            }
        }

        It 'resolves a saved number to its stable project ID' {
            [void](Search-ModrinthContent -Project $project -Query 'sodium')
            $result = Resolve-ModrinthSearchNumber -Selector '2' -Project $project
            $result.ProjectId | Should Be 'fresh-id'
            $result.Type | Should Be 'resourcepack'
        }

        It 'rejects a saved number for a different project' {
            [void](Search-ModrinthContent -Project $project -Query 'sodium')
            $otherPath = New-TestModpack $fixtureRoot 'Other Pack' 'other'
            $other = Read-ModpackProject $otherPath
            { Resolve-ModrinthSearchNumber -Selector '1' -Project $other } | Should Throw "belongs to project 'search'"
        }

        It 'rejects numbers after the project compatibility changes' {
            [void](Search-ModrinthContent -Project $project -Query 'sodium')
            $project.MinecraftVersion = '1.22'
            { Resolve-ModrinthSearchNumber -Selector '1' -Project $project } | Should Throw 'compatibility settings changed'
        }

        It 'renders IDs and numbered choices' {
            $search = Search-ModrinthContent -Project $project -Query 'sodium'
            { Write-ModrinthSearchResults -Search $search -Project $project } | Should Not Throw
        }

        It 'keeps search and inventory number scopes isolated by consuming command' {
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'mods/inventory-only.pw.toml'), @'
name = "Inventory Only"
filename = "inventory-only.jar"
side = "both"
[update.modrinth]
mod-id = "inventory-id"
'@)
            [void](Search-ModrinthContent -Project $project -Query 'sodium')
            $view = Select-ModpackInventory -Inventory (Get-ModpackInventory -Project $project)
            [void](Set-ModpackInventoryReferences -View $view)

            (Resolve-ModrinthSearchNumber -Selector '1' -Project $project).ProjectId | Should Be 'AANobbMI'
            (Resolve-ModpackInventoryNumber -Selector '1' -Project $project).Id | Should Be 'modrinth:inventory-id'

            Mock Add-ModpackContent {
                [pscustomobject]@{ Item = [pscustomobject]@{ Name='Sodium'; Id='modrinth:AANobbMI'; Kind='mod' }; Log=@(); RelatedItems=@() }
            }
            Mock Update-ModpackContent {
                [pscustomobject]@{ Project=$project; Items=@(); Log=@() }
            }

            Invoke-MpAdd @('1', '--project', 'search')
            Assert-MockCalled Add-ModpackContent -Times 1 -ParameterFilter { $ModrinthProjectId -eq 'AANobbMI' }
            Invoke-MpUpdate @('1', '--project', 'search')
            Assert-MockCalled Update-ModpackContent -Times 1 -ParameterFilter { @($Selectors).Count -eq 1 -and $Selectors[0] -eq 'modrinth:inventory-id' }
        }
    }

    Describe 'Mod classification' {
        BeforeEach {
            $fixtureRoot = Join-Path $TestDrive 'classification-packs'
            [System.IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
            $projectId = 'pack-' + [guid]::NewGuid().ToString('N')
            $projectPath = New-TestModpack $fixtureRoot ('Pack-' + [guid]::NewGuid().ToString('N')) $projectId
            $project = Read-ModpackProject $projectPath
            $script:ConfigHomeOverride = Join-Path $TestDrive 'classification-config'
            Write-PowerShellDataFileAtomic -Path (Get-ModpackToolsConfigPath) -Data @{ Root = $fixtureRoot }
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'mods/sodium.pw.toml'), @'
name = "Sodium"
filename = "sodium.jar"
side = "client"
[update.modrinth]
mod-id = "AANobbMI"
'@)
        }

        It 'classifies and reclassifies an existing mod by stable selectors' {
            $classified = Set-ModpackModClassification -Project $project -Selector 'Sodium' -Category PERFORMANCE
            $classified.Category | Should Be 'performance'
            $classified.Item.Category | Should Be 'performance'

            $unclassified = Set-ModpackModClassification -Project $project -Selector 'modrinth:AANobbMI' -Category unclassified
            $unclassified.PreviousCategory | Should Be 'performance'
            $unclassified.Item.Category | Should Be 'unclassified'
            (Get-ModpackMetadata $project).Mods.ContainsKey('modrinth:AANobbMI') | Should Be $false
        }

        It 'preserves other editorial metadata when removing a category' {
            $metadata = Get-ModpackMetadata $project
            $metadata.Mods['modrinth:AANobbMI'] = @{ Name = 'Fast Renderer'; Category = 'performance' }
            Write-PowerShellDataFileAtomic -Path (Join-Path $projectPath '.modpack/metadata.psd1') -Data $metadata

            [void](Set-ModpackModClassification -Project $project -Selector 'Fast Renderer' -Category unclassified)
            $updated = Get-ModpackMetadata $project
            $updated.Mods['modrinth:AANobbMI'].Name | Should Be 'Fast Renderer'
            $updated.Mods['modrinth:AANobbMI'].ContainsKey('Category') | Should Be $false
        }

        It 'classifies a mod by its latest inventory number' {
            $view = Select-ModpackInventory -Inventory (Get-ModpackInventory -Project $project)
            [void](Set-ModpackInventoryReferences -View $view)

            Invoke-MpClassify @('set', '1', 'performance', '--project', $project.Id)

            (Get-ModpackMetadata -Project $project).Mods['modrinth:AANobbMI'].Category | Should Be 'performance'
        }

        It 'creates a category with explicit presentation metadata' {
            Invoke-MpClassify @('create', 'world-generation', '--name', 'WORLD GENERATION', '--order', '25', '--project', $project.Id)

            $metadata = Get-ModpackMetadata -Project $project
            $metadata.Categories['world-generation'].Name | Should Be 'WORLD GENERATION'
            $metadata.Categories['world-generation'].Order | Should Be 25
        }

        It 'appends a category when no order is provided' {
            Invoke-MpClassify @('create', 'visuals', '--project', $project.Id)

            $category = (Get-ModpackMetadata -Project $project).Categories['visuals']
            $category.Name | Should Be 'VISUALS'
            $category.Order | Should Be 20
        }

        It 'reserves unclassified for clearing assignments' {
            { Invoke-MpClassify @('create', 'unclassified', '--project', $project.Id) } | Should Throw "Category ID 'unclassified' is reserved"
        }

        It 'uses inventory numbers for mods and category-list numbers for categories' {
            [void](New-ModpackCategory -Project $project -Id 'visuals' -Name 'VISUALS' -Order 5)
            $inventory = Select-ModpackInventory -Inventory (Get-ModpackInventory -Project $project)
            [void](Set-ModpackInventoryReferences -View $inventory)
            Invoke-MpClassify @('list', '--project', $project.Id)

            Invoke-MpClassify @('set', '1', '1', '--project', $project.Id)

            (Get-ModpackMetadata -Project $project).Mods['modrinth:AANobbMI'].Category | Should Be 'visuals'
        }

        It 'uses category-list numbers in the inventory category filter' {
            [void](New-ModpackCategory -Project $project -Id 'visuals' -Name 'VISUALS' -Order 5)
            [void](Set-ModpackModClassification -Project $project -Selector 'Sodium' -Category 'visuals')
            Invoke-MpClassify @('list', '--project', $project.Id)

            { Invoke-MpInventory @('--category', '1', '--project', $project.Id) } | Should Not Throw
            $references = Read-ModpackInventoryReferenceCache
            @($references.Results).Count | Should Be 1
            $references.Results[0].Selector | Should Be 'modrinth:AANobbMI'
        }

        It 'lists categories through the public command with only the operation argument' {
            Set-ActiveModpackProject -Id $project.Id | Out-Null

            { modpack classify list } | Should Not Throw
            $cache = Read-ModpackCategoryCache
            $cache.ProjectId | Should Be $project.Id
            @($cache.Categories).Count | Should Be 2
            @($cache.Categories | Where-Object Id -eq 'unclassified')[0].Index | Should Be 2
        }

        It 'resolves the numbered unclassified row for set but never for remove' {
            Set-ActiveModpackProject -Id $project.Id | Out-Null
            modpack classify list

            (Resolve-ModpackCategoryId -Project $project -Selector '2' -AllowUnclassified) | Should Be 'unclassified'
            { Invoke-MpClassify @('remove', '2', '--project', $project.Id) } | Should Throw "The 'unclassified' classification cannot be removed"
        }

        It 'removes an unused category by its latest category number' {
            Invoke-MpClassify @('create', 'visuals', '--project', $project.Id)

            Invoke-MpClassify @('remove', '2', '--project', $project.Id)

            (Get-ModpackMetadata -Project $project).Categories.ContainsKey('visuals') | Should Be $false
        }

        It 'refuses to remove a category in use unless unclassification is explicit' {
            $metadataPath = Join-Path $projectPath '.modpack/metadata.psd1'
            $metadata = Get-ModpackMetadata -Project $project
            $metadata.Mods['modrinth:AANobbMI'] = @{ Name = 'Fast Renderer'; Category = 'performance' }
            Write-PowerShellDataFileAtomic -Path $metadataPath -Data $metadata

            { Invoke-MpClassify @('remove', 'performance', '--project', $project.Id) } | Should Throw 'cannot be removed safely'
            (Get-ModpackMetadata -Project $project).Categories.ContainsKey('performance') | Should Be $true

            Invoke-MpClassify @('remove', 'performance', '--unclassify', '--project', $project.Id)
            $updated = Get-ModpackMetadata -Project $project
            $updated.Categories.ContainsKey('performance') | Should Be $false
            $updated.Mods['modrinth:AANobbMI'].Name | Should Be 'Fast Renderer'
            $updated.Mods['modrinth:AANobbMI'].ContainsKey('Category') | Should Be $false
        }

        It 'rejects the former classify syntax instead of treating it as an alias' {
            { Invoke-MpClassify @('sodium', 'performance', '--project', $project.Id) } | Should Throw "Classify operation 'sodium' is not recognized"
        }

        It 'rejects an unknown category without modifying metadata' {
            $path = Join-Path $projectPath '.modpack/metadata.psd1'
            $before = [System.IO.File]::ReadAllBytes($path)
            { Set-ModpackModClassification -Project $project -Selector 'sodium.jar' -Category nonexistent } | Should Throw "Category 'nonexistent' is not defined"
            [System.Linq.Enumerable]::SequenceEqual([byte[]]$before, [byte[]][System.IO.File]::ReadAllBytes($path)) | Should Be $true
        }
    }

    Describe 'Content updates' {
        BeforeEach {
            $fixtureRoot = Join-Path $TestDrive 'update-packs'
            [System.IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
            $projectPath = New-TestModpack $fixtureRoot 'Pack' 'pack'
            $project = Read-ModpackProject $projectPath
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'mods/sodium.pw.toml'), @'
name = "Sodium"
filename = "sodium-old.jar"
side = "client"
[update.modrinth]
mod-id = "AANobbMI"
version = "old"
'@)
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'mods/lithium.pw.toml'), @'
name = "Lithium"
filename = "lithium-old.jar"
side = "both"
[update.modrinth]
mod-id = "gvQqBUqZ"
version = "old"
'@)
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'resourcepacks/fresh.pw.toml'), @'
name = "Fresh Resources"
filename = "fresh-old.zip"
side = "client"
[update.modrinth]
mod-id = "fresh-id"
version = "old"
'@)
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'shaderpacks/vivid.pw.toml'), @'
name = "Vivid Shader"
filename = "vivid-old.zip"
side = "client"
[update.modrinth]
mod-id = "vivid-id"
version = "old"
'@)
        }

        It 'resolves every managed content type by name ID filename and metadata stem' {
            (Resolve-ModpackUpdateSelectors -Project $project -Selectors @('Sodium'))[0].Id | Should Be 'modrinth:AANobbMI'
            (Resolve-ModpackUpdateSelectors -Project $project -Selectors @('modrinth:gvQqBUqZ'))[0].Name | Should Be 'Lithium'
            (Resolve-ModpackUpdateSelectors -Project $project -Selectors @('fresh-old.zip'))[0].Kind | Should Be 'resourcepack'
            (Resolve-ModpackUpdateSelectors -Project $project -Selectors @('vivid'))[0].Kind | Should Be 'shaderpack'
        }

        It 'rejects an explicitly selected local JAR' {
            [System.IO.File]::WriteAllBytes((Join-Path $projectPath 'mods/local.jar'), [byte[]](1, 2, 3))
            { Resolve-ModpackUpdateSelectors -Project $project -Selectors @('local.jar') } | Should Throw 'cannot be updated by Packwiz'
        }

        It 'restores the whole Packwiz state when a grouped update fails' {
            $sodiumPath = Join-Path $projectPath 'mods/sodium.pw.toml'
            $lithiumPath = Join-Path $projectPath 'mods/lithium.pw.toml'
            $indexPath = Join-Path $projectPath 'index.toml'
            $beforeSodium = [System.IO.File]::ReadAllBytes($sodiumPath)
            $beforeLithium = [System.IO.File]::ReadAllBytes($lithiumPath)
            $beforeIndex = [System.IO.File]::ReadAllBytes($indexPath)
            $script:updateCalls = 0
            Mock Invoke-Packwiz {
                $script:updateCalls++
                if ($script:updateCalls -eq 1) {
                    [System.IO.File]::WriteAllText($sodiumPath, 'changed')
                    return @('updated sodium')
                }
                [System.IO.File]::WriteAllText($lithiumPath, 'changed')
                [System.IO.File]::WriteAllText($indexPath, 'changed')
                throw 'simulated Packwiz failure'
            }

            { Update-ModpackContent -Project $project -Selectors @('sodium', 'lithium') } | Should Throw 'changes were restored'
            [System.Linq.Enumerable]::SequenceEqual([byte[]]$beforeSodium, [byte[]][System.IO.File]::ReadAllBytes($sodiumPath)) | Should Be $true
            [System.Linq.Enumerable]::SequenceEqual([byte[]]$beforeLithium, [byte[]][System.IO.File]::ReadAllBytes($lithiumPath)) | Should Be $true
            [System.Linq.Enumerable]::SequenceEqual([byte[]]$beforeIndex, [byte[]][System.IO.File]::ReadAllBytes($indexPath)) | Should Be $true
            Assert-MockCalled Invoke-Packwiz -Times 2
        }

        It 'delegates unrestricted --all to Packwiz for every managed content type' {
            Mock Invoke-Packwiz { return @('already current') }
            $result = Update-ModpackContent -Project $project -All
            $result.Items.Count | Should Be 4
            @($result.Items | ForEach-Object Kind | Sort-Object -Unique) | Should Be @('mod', 'resourcepack', 'shaderpack')
            Assert-MockCalled Invoke-Packwiz -Times 1 -ParameterFilter { $Arguments -contains '--all' }
        }

        It 'updates only the requested type when --all is narrowed' {
            Mock Invoke-Packwiz { return @('already current') }
            $result = Update-ModpackContent -Project $project -All -Type resourcepack
            $result.Items.Count | Should Be 1
            $result.Items[0].Kind | Should Be 'resourcepack'
            Assert-MockCalled Invoke-Packwiz -Times 1 -ParameterFilter { $Arguments -contains 'fresh' -and $Arguments -notcontains '--all' }
        }

        It 'also rolls back when updated metadata cannot be normalized' {
            $sodiumPath = Join-Path $projectPath 'mods/sodium.pw.toml'
            $before = [System.IO.File]::ReadAllBytes($sodiumPath)
            Mock Invoke-Packwiz {
                Remove-Item -LiteralPath $sodiumPath -Force
                return @('metadata removed')
            }

            { Update-ModpackContent -Project $project -Selectors @('sodium') } | Should Throw 'changes were restored'
            Test-Path -LiteralPath $sodiumPath | Should Be $true
            [System.Linq.Enumerable]::SequenceEqual([byte[]]$before, [byte[]][System.IO.File]::ReadAllBytes($sodiumPath)) | Should Be $true
        }
    }

    Describe 'Adding searched content' {
        BeforeEach {
            $fixtureRoot = Join-Path $TestDrive 'add-packs'
            [System.IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
            $projectPath = New-TestModpack $fixtureRoot ('Pack-' + [guid]::NewGuid().ToString('N')) 'pack'
            $project = Read-ModpackProject $projectPath
        }

        It 'installs a resource pack by stable cached project ID' {
            Mock Invoke-Packwiz {
                [System.IO.File]::WriteAllText((Join-Path $projectPath 'resourcepacks/fresh.pw.toml'), @'
name = "Fresh Animations"
filename = "fresh.zip"
side = "client"
[update.modrinth]
mod-id = "fresh-id"
version = "v1"
'@)
                return @('installed')
            }
            $result = Add-ModpackContent -Project $project -Selector 2 -ModrinthProjectId 'fresh-id'
            $result.Item.Kind | Should Be 'resourcepack'
            $result.Item.Id | Should Be 'modrinth:fresh-id'
            Assert-MockCalled Invoke-Packwiz -Times 1 -ParameterFilter { $Arguments -contains '--project-id' -and $Arguments -contains 'fresh-id' }
        }

        It 'rolls back when a category is applied to non-mod content' {
            $indexPath = Join-Path $projectPath 'index.toml'
            $beforeIndex = [System.IO.File]::ReadAllBytes($indexPath)
            $resourcePath = Join-Path $projectPath 'resourcepacks/fresh.pw.toml'
            Mock Invoke-Packwiz {
                [System.IO.File]::WriteAllText($resourcePath, @'
name = "Fresh Animations"
filename = "fresh.zip"
side = "client"
[update.modrinth]
mod-id = "fresh-id"
'@)
                [System.IO.File]::WriteAllText($indexPath, 'changed')
                return @('installed')
            }
            { Add-ModpackContent -Project $project -Selector 'fresh-animations' -Category performance } | Should Throw 'changes were restored'
            Test-Path -LiteralPath $resourcePath | Should Be $false
            [System.Linq.Enumerable]::SequenceEqual([byte[]]$beforeIndex, [byte[]][System.IO.File]::ReadAllBytes($indexPath)) | Should Be $true
        }
    }

    Describe 'Default Options and resource packs' {
        BeforeEach {
            $fixtureRoot = Join-Path $TestDrive 'packs'
            [System.IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
            $projectId = 'pack-' + [guid]::NewGuid().ToString('N')
            $projectPath = New-TestModpack $fixtureRoot ('Pack-' + [guid]::NewGuid().ToString('N')) $projectId
            $project = Read-ModpackProject $projectPath
            $script:ConfigHomeOverride = Join-Path $TestDrive 'resource-config'
            Write-PowerShellDataFileAtomic -Path (Get-ModpackToolsConfigPath) -Data @{ Root = $fixtureRoot }
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'mods/default-options.pw.toml'), @'
name = "Default Options"
filename = "defaultoptions-fabric.jar"
[update.modrinth]
mod-id = "WEg59z5b"
'@)
        }

        It 'reads brackets inside strings without closing the array' {
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

        It 'correctly reverses Default Options priority' {
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'config/defaultoptions-common.toml'), 'defaultResourcePacks = ["low", "middle", "high"]')
            $order = @(Get-DefaultResourcePackOrder $project)
            $order | Should Be @('high', 'middle', 'low')
        }

        It 'shows the ID of a built-in pack without a friendly name' {
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'config/defaultoptions-common.toml'), 'defaultResourcePacks = ["unknown:builtin"]')
            $resource = (Get-ModpackInventory $project).ActiveResources[0]
            $resource.Name | Should Be 'unknown:builtin'
            $resource.Source | Should Be 'builtin'
        }

        It 'resolves the actual name of a physical resource pack' {
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'resourcepacks/animated.pw.toml'), @'
name = "Animated Wind"
filename = "Animated 3D Wind Charge [1.1].zip"
side = "client"
'@)
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'config/defaultoptions-common.toml'), 'defaultResourcePacks = ["file/Animated 3D Wind Charge [1.1].zip"]')
            (Get-ModpackInventory $project).ActiveResources[0].Name | Should Be 'Animated Wind'
        }

        It 'enables a physical pack at the requested visible priority' {
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'resourcepacks/active.pw.toml'), "name = `"Active Pack`"`nfilename = `"active.zip`"")
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'resourcepacks/new.pw.toml'), "name = `"New Pack`"`nfilename = `"new.zip`"")
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'config/defaultoptions-common.toml'), 'defaultResourcePacks = ["vanilla", "file/active.zip"]')

            $result = Enable-ModpackResourcePack -Project $project -Selector 'New Pack' -Position 2

            $result.WasActive | Should Be $false
            $result.Item.Priority | Should Be 2
            @(Get-DefaultResourcePackOrder $project) | Should Be @('file/active.zip', 'file/new.zip', 'vanilla')
        }

        It 'repositions an enabled pack without duplicating it' {
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'resourcepacks/active.pw.toml'), "name = `"Active Pack`"`nfilename = `"active.zip`"")
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'config/defaultoptions-common.toml'), 'defaultResourcePacks = ["vanilla", "file/active.zip"]')

            $result = Enable-ModpackResourcePack -Project $project -Selector 'active.zip' -Position 2

            $result.WasActive | Should Be $true
            @(Get-DefaultResourcePackOrder $project) | Should Be @('vanilla', 'file/active.zip')
        }

        It 'moves an enabled pack with the explicit move operation' {
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'resourcepacks/active.pw.toml'), "name = `"Active Pack`"`nfilename = `"active.zip`"")
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'config/defaultoptions-common.toml'), 'defaultResourcePacks = ["vanilla", "file/active.zip"]')

            Invoke-MpResource @('move', 'Active Pack', '--position', '2', '--project', $project.Id)

            @(Get-DefaultResourcePackOrder $project) | Should Be @('vanilla', 'file/active.zip')
        }

        It 'rejects moving a disabled pack' {
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'resourcepacks/inactive.pw.toml'), "name = `"Inactive Pack`"`nfilename = `"inactive.zip`"")
            $path = Join-Path $projectPath 'config/defaultoptions-common.toml'
            [System.IO.File]::WriteAllText($path, 'defaultResourcePacks = ["vanilla"]')
            $before = Get-Content -Raw -LiteralPath $path

            { Move-ModpackResourcePack -Project $project -Selector 'Inactive Pack' -Position 1 } | Should Throw 'is disabled and cannot be moved'
            (Get-Content -Raw -LiteralPath $path) | Should Be $before
        }

        It 'enables a resource pack by its latest inventory number' {
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'resourcepacks/active.pw.toml'), "name = `"Active Pack`"`nfilename = `"active.zip`"")
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'resourcepacks/new.pw.toml'), "name = `"New Pack`"`nfilename = `"new.zip`"")
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'config/defaultoptions-common.toml'), 'defaultResourcePacks = ["file/active.zip"]')
            $view = Select-ModpackInventory -Inventory (Get-ModpackInventory -Project $project) -Type resourcepack
            [void](Set-ModpackInventoryReferences -View $view)

            Invoke-MpResource @('enable', '2', '--position', '1', '--project', $project.Id)

            @(Get-DefaultResourcePackOrder -Project $project) | Should Be @('file/new.zip', 'file/active.zip')
        }

        It 'requires Default Options before changing resource pack state or order' {
            [System.IO.File]::Delete((Join-Path $projectPath 'mods/default-options.pw.toml'))
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'resourcepacks/new.pw.toml'), "name = `"New Pack`"`nfilename = `"new.zip`"")
            $path = Join-Path $projectPath 'config/defaultoptions-common.toml'
            [System.IO.File]::WriteAllText($path, 'defaultResourcePacks = ["vanilla"]')
            $before = Get-Content -Raw -LiteralPath $path

            { Enable-ModpackResourcePack -Project $project -Selector 'New Pack' -Position 1 } | Should Throw "Default Options is not installed"
            (Get-Content -Raw -LiteralPath $path) | Should Be $before
        }

        It 'disables an enabled pack without deleting it and preserves remaining order' {
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'resourcepacks/active.pw.toml'), "name = `"Active Pack`"`nfilename = `"active.zip`"")
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'config/defaultoptions-common.toml'), 'defaultResourcePacks = ["vanilla", "file/active.zip"]')

            $result = Disable-ModpackResourcePack -Project $project -Selector 'Active Pack'

            $result.WasActive | Should Be $true
            @(Get-DefaultResourcePackOrder $project) | Should Be @('vanilla')
            Test-Path -LiteralPath (Join-Path $projectPath 'resourcepacks/active.pw.toml') | Should Be $true
            $result.Inventory.InactiveResources[0].Name | Should Be 'Active Pack'
        }

        It 'does not rewrite Default Options when the pack is already disabled' {
            [System.IO.File]::WriteAllText((Join-Path $projectPath 'resourcepacks/inactive.pw.toml'), "name = `"Inactive Pack`"`nfilename = `"inactive.zip`"")
            $path = Join-Path $projectPath 'config/defaultoptions-common.toml'
            [System.IO.File]::WriteAllText($path, 'defaultResourcePacks = ["vanilla"]')
            $before = [System.IO.File]::ReadAllBytes($path)

            $result = Disable-ModpackResourcePack -Project $project -Selector 'inactive.zip'

            $result.WasActive | Should Be $false
            [System.Linq.Enumerable]::SequenceEqual([byte[]]$before, [byte[]][System.IO.File]::ReadAllBytes($path)) | Should Be $true
        }

        It 'rejects out-of-range positions without modifying the file' {
            $path = Join-Path $projectPath 'config/defaultoptions-common.toml'
            [System.IO.File]::WriteAllText($path, 'defaultResourcePacks = ["vanilla"]')
            $before = Get-Content -Raw -LiteralPath $path

            { Enable-ModpackResourcePack -Project $project -Selector 'vanilla' -Position 3 } | Should Throw 'allowed range 1-1'
            (Get-Content -Raw -LiteralPath $path) | Should Be $before
        }
    }

    Describe 'Inventory filters' {
        BeforeEach {
            $script:ConfigHomeOverride = Join-Path $TestDrive 'inventory-config'
            $filterInventory = [pscustomobject]@{
                Project = [pscustomobject]@{ Id = 'filter' }
                Metadata = @{ Categories = @{ performance = @{ Name = 'PERFORMANCE'; Order = 10 } } }
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

        It 'filters mods by category' {
            $view = Select-ModpackInventory -Inventory $filterInventory -Category performance
            $view.IncludedTypes | Should Be @('mod')
            $view.Mods.Count | Should Be 1
            $view.Mods[0].Name | Should Be 'Sodium'
        }

        It 'accepts host as a server-side alias and combines source' {
            $view = Select-ModpackInventory -Inventory $filterInventory -Side host -Source local
            $view.Mods.Count | Should Be 1
            $view.Mods[0].Name | Should Be 'Host Tool'
        }

        It 'filters resource packs by state and text' {
            $view = Select-ModpackInventory -Inventory $filterInventory -Type resourcepack -State active -Search builtin
            $view.ActiveResources.Count | Should Be 1
            $view.ActiveResources[0].Source | Should Be 'builtin'
            $view.InactiveResources.Count | Should Be 0
        }

        It 'filters local items of every type' {
            $view = Select-ModpackInventory -Inventory $filterInventory -Source local
            $view.TotalMatches | Should Be 2
            $view.Mods.Count | Should Be 1
            $view.InactiveResources.Count | Should Be 1
        }

        It 'rejects contradictory filter combinations' {
            { Select-ModpackInventory -Inventory $filterInventory -Type shaderpack -Side client } | Should Throw 'apply only to mods'
            { Select-ModpackInventory -Inventory $filterInventory -Category performance -State active } | Should Throw 'cannot be combined'
        }

        It 'numbers every displayed item globally in rendering order' {
            $view = Select-ModpackInventory -Inventory $filterInventory
            [void](Set-ModpackInventoryReferences -View $view)
            $items = @(Get-ModpackInventoryReferenceItems -View $view)

            $items.Count | Should Be 7
            @($items | ForEach-Object ReferenceNumber) | Should Be @(1, 2, 3, 4, 5, 6, 7)
            @($items | ForEach-Object Name) | Should Be @('Sodium', 'Host Tool', 'Shared Mod', 'Active Pack', 'Builtin Pack', 'Old Pack', 'Vivid')
            (Resolve-ModpackInventoryNumber -Selector '1' -Project $filterInventory.Project).Id | Should Be 'modrinth:a'
            (Resolve-ModpackInventoryNumber -Selector '4' -Project $filterInventory.Project -AllowedKinds resourcepack).Selector | Should Be 'active.zip'
            { Resolve-ModpackInventoryNumber -Selector '4' -Project $filterInventory.Project -AllowedKinds mod } | Should Throw 'not accepted by this command'
            { Resolve-ModpackInventoryNumber -Selector '2' -Project $filterInventory.Project -RequirePackwiz } | Should Throw 'Packwiz cannot update'
        }

        It 'numbers only the filtered view and restarts at one' {
            $view = Select-ModpackInventory -Inventory $filterInventory -Type resourcepack -State inactive
            [void](Set-ModpackInventoryReferences -View $view)
            $items = @(Get-ModpackInventoryReferenceItems -View $view)

            $items.Count | Should Be 1
            $items[0].ReferenceNumber | Should Be 1
            $items[0].Name | Should Be 'Old Pack'
        }

        It 'pads numbered references to the shared list width' {
            (Format-MpReferenceLabel -Reference 9 -Width 2) | Should Be '[ 9]'
            (Format-MpReferenceLabel -Reference 10 -Width 2) | Should Be '[10]'
            (Get-MpReferenceWidth -Items @(
                [pscustomobject]@{ ReferenceNumber = 9 }
                [pscustomobject]@{ ReferenceNumber = 10 }
            )) | Should Be 2
        }
    }

    Describe 'Native processes' {
        It 'turns a nonzero exit code into a clear error' {
            { Invoke-NativeCommandChecked -FilePath 'pwsh' -Arguments @('-NoProfile', '-Command', 'exit 7') -WorkingDirectory $TestDrive } | Should Throw 'code 7'
        }
    }
}
