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
            $text | Should Match 'defaultoptions-common.toml'
            $text | Should Not Match 'modpack add mod'
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
            $record.Exception.Message | Should Match '(?m)^Try: modpack help$'
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
            $fixtureRoot = Join-Path $TestDrive 'packs'
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
                $Headers.'User-Agent' -eq 'R3Neer-ModpackTools/0.10.0'
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
    }

    Describe 'Mod classification' {
        BeforeEach {
            $fixtureRoot = Join-Path $TestDrive 'classification-packs'
            [System.IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
            $projectPath = New-TestModpack $fixtureRoot ('Pack-' + [guid]::NewGuid().ToString('N')) 'pack'
            $project = Read-ModpackProject $projectPath
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
            $projectPath = New-TestModpack $fixtureRoot 'Pack' 'pack'
            $project = Read-ModpackProject $projectPath
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
    }

    Describe 'Native processes' {
        It 'turns a nonzero exit code into a clear error' {
            { Invoke-NativeCommandChecked -FilePath 'pwsh' -Arguments @('-NoProfile', '-Command', 'exit 7') -WorkingDirectory $TestDrive } | Should Throw 'code 7'
        }
    }
}
