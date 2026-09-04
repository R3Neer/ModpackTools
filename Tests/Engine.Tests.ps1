Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'ModpackTools.psd1') -Force
InModuleScope ModpackTools {
    function New-EngineFixture {
        $script:ConfigHomeOverride = Join-Path $TestDrive ('cache-' + [guid]::NewGuid().ToString('N'))
        $root = Join-Path $TestDrive ('pack with spaces ' + [guid]::NewGuid().ToString('N'))
        foreach ($dir in @('.modpack','mods','config','resourcepacks')) { [void][IO.Directory]::CreateDirectory((Join-Path $root $dir)) }
        [IO.File]::WriteAllText((Join-Path $root 'pack.toml'), "name = `"Fixture`"`nversion = `"1`"`npack-format = `"packwiz:1.1.0`"`n[index]`nfile = `"index.toml`"`nhash-format = `"sha256`"`nhash = `"test`"`n[versions]`nminecraft = `"1.21.1`"`nfabric = `"0.16.0`"`n")
        [IO.File]::WriteAllText((Join-Path $root 'index.toml'), 'hash-format = "sha256"')
        Write-PowerShellDataFileAtomic @{ SchemaVersion = 1; Id = 'fixture'; JavaVersion = '21' } (Join-Path $root '.modpack/project.psd1')
        Write-PowerShellDataFileAtomic @{ Categories = @{ performance = @{ Name = 'Performance'; Order = 1 } }; Mods = @{}; ResourcePacks = @{} } (Join-Path $root '.modpack/metadata.psd1')
        $script:FixtureProject = Read-ModpackProject $root
        $script:FixtureApi = @{}; $script:FixtureArtifacts = @{}
    }
    function New-FixtureJar {
        param([string]$Name, [hashtable]$Entries)
        $path = Join-Path $TestDrive ([guid]::NewGuid().ToString('N') + $Name)
        $zip = [IO.Compression.ZipFile]::Open($path, 'Create')
        try { foreach ($key in $Entries.Keys) { $writer = [IO.StreamWriter]::new($zip.CreateEntry($key).Open()); try { $writer.Write($Entries[$key]) } finally { $writer.Dispose() } } }
        finally { $zip.Dispose() }
        return $path
    }
    function Add-FixtureVersion {
        param([string]$Id, [string]$Version, [hashtable]$Depends = @{}, [array]$ProviderDependencies = @(), [switch]$Installed, [string]$Published = '2026-01-01T00:00:00Z', [switch]$Pinned)
        $vid = "$Id-$Version"
        $jar = New-FixtureJar "$vid.jar" @{ 'fabric.mod.json' = (@{ schemaVersion = 1; id = $Id; name = $Id; version = $Version; depends = $Depends } | ConvertTo-Json -Depth 12) }
        $hash = (Get-FileHash $jar -Algorithm SHA512).Hash
        $script:FixtureArtifacts[$hash] = $jar
        $raw = [pscustomobject]@{ id = $vid; project_id = $Id; name = $vid; version_number = $Version; date_published = $Published; version_type = 'release'; game_versions = @('1.21.1'); loaders = @('fabric'); dependencies = $ProviderDependencies; files = @([pscustomobject]@{ filename = "$vid.jar"; primary = $true; url = "https://example.invalid/$vid.jar"; hashes = @{ sha512 = $hash; sha1 = 'unused' } }) }
        $script:FixtureApi["version/$vid"] = $raw
        $script:FixtureApi["project/$Id"] = [pscustomobject]@{ id = $Id; slug = $Id; title = $Id; project_type = 'mod'; server_side = 'required'; client_side = 'required' }
        if (-not $script:FixtureApi.ContainsKey("project/$Id/version")) { $script:FixtureApi["project/$Id/version"] = @() }
        $script:FixtureApi["project/$Id/version"] += $raw
        if ($Installed) {
            $text = "name = `"$Id`"`nfilename = `"$vid.jar`"`nside = `"both`"`npin = $($Pinned.ToString().ToLowerInvariant())`n[download]`nurl = `"https://example.invalid/$vid.jar`"`nhash-format = `"sha512`"`nhash = `"$hash`"`n[update.modrinth]`nmod-id = `"$Id`"`nversion = `"$vid`"`n"
            [IO.File]::WriteAllText((Join-Path $script:FixtureProject.Root "mods/$Id.pw.toml"), $text)
        }
        return $raw
    }
    Describe 'Version predicates and loader manifests' {
        BeforeEach { New-EngineFixture }
        It 'checks Fabric comparisons, alternatives, wildcards and prereleases' {
            (Test-MpVersionRange '3.3' '>=3.3.2') | Should Be $false
            (Test-MpVersionRange '3.3.2' '>=3.3.2') | Should Be $true
            (Test-MpVersionRange '1.5.9' '1.5.x') | Should Be $true
            (Test-MpVersionRange '2.0' '^1.2') | Should Be $false
            (Test-MpVersionRange '0.2.9' '^0.2.1') | Should Be $true
            (Test-MpVersionRange '1.2.0-rc.1' '>=1.2.0') | Should Be $false
            (Test-MpVersionRange '2.0' @('1.x','>=2')) | Should Be $true
            (Test-MpVersionRange 'custom' '>=1') | Should BeNullOrEmpty
            (Test-MpVersionRange 'custom' 'custom') | Should Be $true
        }
        It 'checks Maven inclusive, exclusive, union and qualifier ranges' {
            (Test-MpVersionRange '2' '[1,2)' -Maven) | Should Be $false
            (Test-MpVersionRange '2' '[2]' -Maven) | Should Be $true
            (Test-MpVersionRange '3' '(,1],[3,)' -Maven) | Should Be $true
            (Compare-MpVersion '1.0-rc1' '1.0' -Maven) | Should BeLessThan 0
            (Compare-MpVersion '1.0' '1.0-final' -Maven) | Should Be 0
            (Compare-MpVersion '1.0-sp1' '1.0' -Maven) | Should BeGreaterThan 0
        }
        It 'reads Forge TOML arrays and manifest version interpolation' {
            $jar = New-FixtureJar 'forge.jar' @{ 'META-INF/mods.toml' = "modLoader = 'javafml'`nloaderVersion = '[47,)'`n[[mods]]`nmodId = 'example'`nversion = '`${file.jarVersion}'`n[[dependencies.example]]`nmodId = 'library'`nmandatory = true`nversionRange = '[1,2)'`nside = 'CLIENT'"; 'META-INF/MANIFEST.MF' = "Implementation-Version: 1.2.3`n" }
            $result = Get-MpLoaderMetadata $jar forge
            ($result.Warnings -join '; ') | Should Be ''
            $result.Mods[0].Version | Should Be '1.2.3'
            $result.Mods[0].Requirements[0].Side | Should Be client
        }
        It 'reads NeoForge incompatibilities and multiple mods per artifact' {
            $jar = New-FixtureJar 'neo.jar' @{ 'META-INF/neoforge.mods.toml' = "[[mods]]`nmodId = 'one'`nversion = '1'`n[[mods]]`nmodId = 'two'`nversion = '2'`n[[dependencies.one]]`nmodId = 'bad'`ntype = 'incompatible'`nversionRange = '[1,)'" }
            $result = Get-MpLoaderMetadata $jar neoforge
            ($result.Warnings -join '; ') | Should Be ''
            $result.Mods.Count | Should Be 2
            $result.Mods[0].Requirements[0].Kind | Should Be incompatible
        }
        It 'reads Quilt alternatives and conditional requirements' {
            $json = @{ schema_version = 1; quilt_loader = @{ id = 'q'; version = '1'; depends = @(@{ any = @('a','b'); unless = 'c' }); provides = @(@{ id = 'alias'; version = '2' }) } } | ConvertTo-Json -Depth 20
            $jar = New-FixtureJar 'quilt.jar' @{ 'quilt.mod.json' = $json }
            $result = Get-MpLoaderMetadata $jar quilt
            ($result.Warnings -join '; ') | Should Be ''
            $result.Mods[0].Provides.alias | Should Be '2'
            (Test-MpRequirement $result.Mods[0].Requirements[0] @{} @{ c = @('1') }) | Should Be $true
        }
        It 'reports corrupt manifests as incomplete' {
            $jar = New-FixtureJar 'broken.jar' @{ 'fabric.mod.json' = '{' }
            (Get-MpLoaderMetadata $jar fabric).Warnings.Count | Should BeGreaterThan 0
        }
    }
    Describe 'Cloud project files' {
        BeforeEach {
            New-EngineFixture
            # Model the attributes of hydrated OneDrive files and directories.
            # Hashing, parsing, copying and rollback still use real fixture bytes.
            Mock Get-ChildItem {
                foreach ($entry in ([IO.DirectoryInfo]::new($LiteralPath)).GetFileSystemInfos()) {
                    [pscustomobject]@{
                        FullName = $entry.FullName; Name = $entry.Name
                        Attributes = $entry.Attributes -bor [IO.FileAttributes]::ReparsePoint
                        LinkType = $null; Target = $null
                        PSIsContainer = $entry -is [IO.DirectoryInfo]
                    }
                }
            } -ParameterFilter {
                $LiteralPath -eq $script:FixtureProject.Root -or
                $LiteralPath.StartsWith($script:FixtureProject.Root + [IO.Path]::DirectorySeparatorChar)
            }
        }
        It 'reads project health and hashes metadata in cloud directories' {
            $tree = Get-MpTreeState $script:FixtureProject.Root
            $tree['.modpack/metadata.psd1'] | Should Be (Get-FileHash (Join-Path $script:FixtureProject.Root '.modpack/metadata.psd1')).Hash
            $health = Get-MpProjectHealth $script:FixtureProject
            $health.Errors.Count | Should Be 0
        }
        It 'prepares commits and rolls back changes to cloud project files' {
            $before = Get-MpTreeState $script:FixtureProject.Root
            $prepare = { param($p); [IO.File]::AppendAllText((Join-Path $p.Root '.modpack/metadata.psd1'), "`r`n# edited") }
            $dry = Invoke-MpProjectTransaction $script:FixtureProject -Prepare $prepare -DryRun
            $dry.Changes.Count | Should Be 1
            @(Get-MpTreeChanges $before (Get-MpTreeState $script:FixtureProject.Root)).Count | Should Be 0
            $commit = Invoke-MpProjectTransaction $script:FixtureProject -Prepare $prepare
            $commit.Applied | Should Be $true
            $commit.Changes[0].After | Should Be $dry.Changes[0].After
            $committed = Get-MpTreeState $script:FixtureProject.Root
            Mock Copy-MpFileAtomic { throw 'injected cloud commit failure' } -ParameterFilter { $Destination.EndsWith('z.txt') }
            { Invoke-MpProjectTransaction $script:FixtureProject -Prepare {
                param($p)
                [IO.File]::AppendAllText((Join-Path $p.Root '.modpack/metadata.psd1'), "`r`n# rollback")
                [IO.File]::WriteAllText((Join-Path $p.Root 'z.txt'), 'new')
            } } | Should Throw 'injected cloud commit failure'
            @(Get-MpTreeChanges $committed (Get-MpTreeState $script:FixtureProject.Root)).Count | Should Be 0
        }
    }
    Describe 'Filesystem link protection' {
        BeforeEach { New-EngineFixture }
        It 'rejects real junctions including a linked project root without touching their target' -Skip:(-not $IsWindows) {
            $outside = Join-Path $TestDrive ('outside-' + [guid]::NewGuid().ToString('N'))
            [void][IO.Directory]::CreateDirectory($outside)
            $sentinel = Join-Path $outside 'sentinel.txt'
            [IO.File]::WriteAllText($sentinel, 'keep')
            $junction = Join-Path $script:FixtureProject.Root 'linked'
            New-Item -ItemType Junction -Path $junction -Target $outside | Out-Null
            try {
                { Get-MpTreeState $script:FixtureProject.Root } | Should Throw 'Linked path'
                { Get-MpTreeState $junction } | Should Throw 'Linked path'
                { Invoke-MpProjectTransaction $script:FixtureProject -Prepare { throw 'must not prepare' } } | Should Throw 'Linked path'
                (Get-Content -LiteralPath $sentinel -Raw) | Should Be 'keep'
            } finally {
                # Delete the junction itself, never recurse into its target.
                [IO.Directory]::Delete($junction)
            }
        }
        It 'rejects a dangling symbolic file before attempting to hash it' {
            Mock Get-ChildItem {
                [pscustomobject]@{
                    Name = 'linked.jar'; FullName = (Join-Path $LiteralPath 'linked.jar')
                    Attributes = [IO.FileAttributes]::ReparsePoint
                    LinkType = 'SymbolicLink'; Target = 'missing.jar'; PSIsContainer = $false
                }
            }
            { Get-MpTreeState $script:FixtureProject.Root } | Should Throw 'Linked path'
        }
    }
    Describe 'Project transaction boundary' {
        BeforeEach { New-EngineFixture }
        It 'writes an intentionally empty text file atomically' {
            $path = Join-Path $script:FixtureProject.Root 'empty.txt'
            Write-Utf8TextFileAtomic -Path $path -Text ''
            [IO.File]::ReadAllText($path) | Should Be ''
            (Get-MpHash '') | Should Be 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855'
        }
        It 'keeps the project byte identical when preparation fails after several writes' {
            $before = Get-MpTreeState $script:FixtureProject.Root
            { Invoke-MpProjectTransaction $script:FixtureProject -Prepare { param($p); [IO.File]::WriteAllText((Join-Path $p.Root 'new.txt'),'new'); [IO.File]::WriteAllText((Join-Path $p.Root '.modpack/metadata.psd1'),'broken'); throw 'injected' } } | Should Throw
            @(Get-MpTreeChanges $before (Get-MpTreeState $script:FixtureProject.Root)).Count | Should Be 0
        }
        It 'detects an external edit made during preparation' {
            $live = Join-Path $script:FixtureProject.Root 'external.txt'
            { Invoke-MpProjectTransaction $script:FixtureProject -Prepare { param($p); [IO.File]::WriteAllText($live,'external'); [IO.File]::WriteAllText((Join-Path $p.Root 'planned.txt'),'planned') } } | Should Throw 'changed during preparation'
            (Get-Content $live) | Should Be external
            (Test-Path (Join-Path $script:FixtureProject.Root 'planned.txt')) | Should Be $false
        }
        It 'makes dry runs and successful commits use the same prepared changes' {
            $prepare = { param($p); [IO.File]::WriteAllText((Join-Path $p.Root 'new.txt'),'new') }
            $dry = Invoke-MpProjectTransaction $script:FixtureProject -Prepare $prepare -DryRun
            (Test-Path (Join-Path $script:FixtureProject.Root 'new.txt')) | Should Be $false
            $real = Invoke-MpProjectTransaction $script:FixtureProject -Prepare $prepare
            $real.Changes[0].After | Should Be $dry.Changes[0].After
        }
        It 'restores editorial metadata when commit fails on a later file' {
            $before = Get-MpTreeState $script:FixtureProject.Root
            Mock Copy-MpFileAtomic { throw 'injected commit failure' } -ParameterFilter { $Destination.EndsWith('z.txt') }
            { Invoke-MpProjectTransaction $script:FixtureProject -Prepare { param($p); [IO.File]::WriteAllText((Join-Path $p.Root '.modpack/metadata.psd1'),'changed'); [IO.File]::WriteAllText((Join-Path $p.Root 'z.txt'),'z') } } | Should Throw
            @(Get-MpTreeChanges $before (Get-MpTreeState $script:FixtureProject.Root)).Count | Should Be 0
        }
    }
    Describe 'Resolution and materialization' {
        BeforeEach {
            New-EngineFixture
            Mock Invoke-ModrinthApiRequest { $key = ($PathAndQuery -split '\?')[0]; if (-not $script:FixtureApi.ContainsKey($key)) { throw "Missing fixture $key" }; return $script:FixtureApi[$key] }
            Mock Get-ModrinthVersionsByIds { @($VersionIds | ForEach-Object { $script:FixtureApi["version/$_"] } | Where-Object { $null -ne $_ }) }
            Mock Get-MpArtifact { return $script:FixtureArtifacts[$Hash] }
            Mock Invoke-Packwiz { return @('refreshed') }
        }
        It 'resolves EMF requirements and applies only the necessary update' {
            [void](Add-FixtureVersion emf '3.3' -Installed)
            [void](Add-FixtureVersion emf '3.3.2' -Published '2026-02-01T00:00:00Z')
            [void](Add-FixtureVersion unrelated '1' -Installed)
            [void](Add-FixtureVersion compat '2' -Depends @{ emf = '>=3.3.2' } -ProviderDependencies @(@{ project_id = 'emf'; version_id = $null; dependency_type = 'required' }))
            $result = Invoke-MpContentOperation $script:FixtureProject -Operation add -Selectors @('compat')
            $result.Result.Changes.Count | Should Be 2
            $inventory = Get-ModpackInventory $script:FixtureProject
            ($inventory.Mods | Where-Object Id -eq 'modrinth:emf').VersionId | Should Be 'emf-3.3.2'
            ($inventory.Mods | Where-Object Id -eq 'modrinth:unrelated').VersionId | Should Be 'unrelated-1'
            $inventory.Metadata.Content['modrinth:compat'].Intent | Should Be explicit
        }
        It 'prepares a dependency repair without an empty text binding failure' {
            [void](Add-FixtureVersion root '1' -Installed -Depends @{ lib = '>=2' } -ProviderDependencies @(@{ project_id = 'lib'; version_id = $null; dependency_type = 'required' }))
            [void](Add-FixtureVersion lib '1' -Installed)
            [void](Add-FixtureVersion lib '2' -Published '2026-02-01T00:00:00Z')
            $before = Get-MpTreeState $script:FixtureProject.Root
            $preview = Invoke-MpContentOperation $script:FixtureProject -Operation repair -DryRun
            $preview.Result.Changes.Count | Should Be 1
            $preview.Result.Changes[0].After.VersionId | Should Be 'lib-2'
            @(Get-MpTreeChanges $before (Get-MpTreeState $script:FixtureProject.Root)).Count | Should Be 0
        }
        It 'treats a different installed version from a provider pointer as incomplete verification' {
            [void](Add-FixtureVersion lib '1')
            [void](Add-FixtureVersion lib '2' -Installed -Published '2026-02-01T00:00:00Z')
            [void](Add-FixtureVersion root '1' -Installed -ProviderDependencies @(@{ project_id = 'lib'; version_id = 'lib-1'; dependency_type = 'required' }))
            $report = Get-MpProjectHealth $script:FixtureProject -Check
            $report.Errors.Count | Should Be 0
            $report.Unknown.Count | Should BeGreaterThan 0
            $report.Unknown[0].Message | Should Match 'provider version lib-1'
        }
        It 'repairs solvable requirements when provider metadata has no compatible candidate' {
            [void](Add-FixtureVersion owner '1' -Installed -ProviderDependencies @(@{ project_id = 'ghost'; version_id = $null; dependency_type = 'required' }))
            $script:FixtureApi['project/ghost'] = [pscustomobject]@{ id='ghost'; title='Ghost'; project_type='mod'; server_side='required'; client_side='required' }
            $script:FixtureApi['project/ghost/version'] = @()
            [void](Add-FixtureVersion compat '1' -Installed -Depends @{ lib = '>=2' } -ProviderDependencies @(@{ project_id = 'lib'; version_id = $null; dependency_type = 'required' }))
            [void](Add-FixtureVersion lib '1' -Installed)
            [void](Add-FixtureVersion lib '2' -Published '2026-02-01T00:00:00Z')
            $preview = Invoke-MpContentOperation $script:FixtureProject -Operation repair -DryRun
            $preview.Result.Changes.Count | Should Be 1
            $preview.Result.Changes[0].After.VersionId | Should Be 'lib-2'
            $preview.Result.Report.Errors.Count | Should Be 0
            $preview.Result.Report.Unknown.Count | Should BeGreaterThan 0
            { Invoke-MpContentOperation $script:FixtureProject -Operation repair -DryRun -Strict } | Should Throw 'No verified dependency solution'
        }
        It 'respects pins and leaves no partial add' {
            [void](Add-FixtureVersion emf '3.3' -Installed -Pinned)
            [void](Add-FixtureVersion emf '3.3.2' -Published '2026-02-01T00:00:00Z')
            [void](Add-FixtureVersion compat '2' -Depends @{ emf = '>=3.3.2' })
            $before = Get-MpTreeState $script:FixtureProject.Root
            { Invoke-MpContentOperation $script:FixtureProject -Operation add -Selectors @('compat') } | Should Throw 'pinned'
            @(Get-MpTreeChanges $before (Get-MpTreeState $script:FixtureProject.Root)).Count | Should Be 0
        }
        It 'backtracks from the latest dependency when another installed mod requires an older candidate' {
            [void](Add-FixtureVersion guard '1' -Installed -Depends @{ lib = '<3' })
            [void](Add-FixtureVersion lib '1' -Installed)
            [void](Add-FixtureVersion lib '2' -Published '2026-02-01T00:00:00Z')
            [void](Add-FixtureVersion lib '3' -Published '2026-03-01T00:00:00Z')
            [void](Add-FixtureVersion root '1' -Depends @{ lib = '>=2' })
            $plan = New-MpContentPlan $script:FixtureProject -Operation add -Selectors @('root')
            $plan.Nodes['modrinth:lib'].VersionId | Should Be 'lib-2'
            $plan.Changes.Count | Should Be 2
        }
        It 'requires permission for dependency downgrades' {
            [void](Add-FixtureVersion lib '3' -Installed -Published '2026-03-01T00:00:00Z')
            [void](Add-FixtureVersion lib '2' -Published '2026-02-01T00:00:00Z')
            [void](Add-FixtureVersion root '1' -Depends @{ lib = '<3' })
            { New-MpContentPlan $script:FixtureProject -Operation add -Selectors @('root') } | Should Throw
            (New-MpContentPlan $script:FixtureProject -Operation add -Selectors @('root') -AllowDowngrade).Nodes['modrinth:lib'].VersionId | Should Be 'lib-2'
        }
        It 'keeps unrelated baseline issues but strict checking rejects them' {
            [void](Add-FixtureVersion broken '1' -Installed -Depends @{ absent = '*' })
            [void](Add-FixtureVersion root '1')
            (New-MpContentPlan $script:FixtureProject -Operation add -Selectors @('root')).Changes.Count | Should Be 1
            { New-MpContentPlan $script:FixtureProject -Operation add -Selectors @('root') -Strict } | Should Throw
        }
        It 'rolls back a complete batch when refresh fails' {
            [void](Add-FixtureVersion a '1'); [void](Add-FixtureVersion b '1')
            Mock Invoke-Packwiz { throw 'refresh failure' }
            $before = Get-MpTreeState $script:FixtureProject.Root
            { Invoke-MpContentOperation $script:FixtureProject -Operation add -Selectors @('a','b') -Category performance } | Should Throw
            @(Get-MpTreeChanges $before (Get-MpTreeState $script:FixtureProject.Root)).Count | Should Be 0
        }
    }
    Describe 'Recovery, batches and project health' {
        BeforeEach {
            New-EngineFixture
            Mock Invoke-ModrinthApiRequest { $key = ($PathAndQuery -split '\?')[0]; if (-not $script:FixtureApi.ContainsKey($key)) { throw "Missing fixture $key" }; return $script:FixtureApi[$key] }
            Mock Get-ModrinthVersionsByIds { @($VersionIds | ForEach-Object { $script:FixtureApi["version/$_"] } | Where-Object { $null -ne $_ }) }
            Mock Get-MpArtifact { return $script:FixtureArtifacts[$Hash] }
            Mock Invoke-Packwiz { return @('refreshed') }
            Mock Resolve-MpCommandProject { return $script:FixtureProject }
        }
        It 'recovers a pending committed-file write from its journal before the next operation' {
            $root = $script:FixtureProject.Root
            $path = Join-Path $root 'original.txt'; [IO.File]::WriteAllText($path,'before')
            $old = (Get-FileHash $path).Hash
            $homePath = Join-Path (Get-ModpackToolsConfigDirectory) ('transactions/' + (Get-MpHash $root.ToLowerInvariant()))
            $pending = Join-Path $homePath 'interrupted'
            [void][IO.Directory]::CreateDirectory((Join-Path $pending 'backup'))
            [IO.File]::Copy($path,(Join-Path $pending 'backup/original.txt'))
            [IO.File]::WriteAllText($path,'after')
            Write-Utf8TextFileAtomic (Join-Path $pending 'journal.json') (@{ Root=$root; Status='pending'; Changes=@(@{ Path='original.txt'; Before=$old; After=(Get-FileHash $path).Hash }) } | ConvertTo-Json -Depth 8)
            Invoke-MpProjectTransaction $script:FixtureProject -Prepare { param($p) } | Out-Null
            (Get-Content $path) | Should Be before
            (Test-Path $pending) | Should Be $false
        }
        It 'preserves external edits when pending recovery cannot be completed' {
            $root = $script:FixtureProject.Root; $path = Join-Path $root 'original.txt'
            [IO.File]::WriteAllText($path,'external')
            $pending = Join-Path $TestDrive 'journal-conflict'; [void][IO.Directory]::CreateDirectory($pending)
            Write-Utf8TextFileAtomic (Join-Path $pending 'journal.json') (@{ Root=$root; Status='pending'; Changes=@(@{ Path='original.txt'; Before='old'; After='new' }) } | ConvertTo-Json -Depth 8)
            { Restore-MpJournal $pending $root } | Should Throw 'external edit'
            (Get-Content $path) | Should Be external
            (Test-Path (Join-Path $pending 'journal.json')) | Should Be $true
        }
        It 'refuses to apply a repair different from the reviewed preview' {
            $preview = Invoke-MpProjectTransaction $script:FixtureProject -DryRun -Prepare { param($p); [IO.File]::WriteAllText((Join-Path $p.Root 'a.txt'),'one') }
            { Invoke-MpProjectTransaction $script:FixtureProject -ExpectedChanges $preview.Changes -Prepare { param($p); [IO.File]::WriteAllText((Join-Path $p.Root 'a.txt'),'two') } } | Should Throw 'plan changed'
            (Test-Path (Join-Path $script:FixtureProject.Root 'a.txt')) | Should Be $false
        }
        It 'classifies an entire batch and does not change it when another selector is invalid' {
            [void](Add-FixtureVersion a '1' -Installed); [void](Add-FixtureVersion b '1' -Installed)
            Invoke-MpClassify @('set','a','b','performance')
            $metadata = Get-ModpackMetadata $script:FixtureProject
            $metadata.Mods['modrinth:a'].Category | Should Be performance
            $metadata.Mods['modrinth:b'].Category | Should Be performance
            $before = Get-MpTreeState $script:FixtureProject.Root
            { Invoke-MpClassify @('set','a','absent','unclassified') } | Should Throw
            @(Get-MpTreeChanges $before (Get-MpTreeState $script:FixtureProject.Root)).Count | Should Be 0
        }
        It 'changes sides as one dry-runnable batch and preserves CRLF' {
            [void](Add-FixtureVersion a '1' -Installed); [void](Add-FixtureVersion b '1' -Installed)
            $path = Join-Path $script:FixtureProject.Root 'mods/a.pw.toml'
            [IO.File]::WriteAllText($path,([IO.File]::ReadAllText($path).Replace("`n","`r`n")))
            Invoke-MpSide @('set','a','b','client','--dry-run')
            (Get-TomlString (Get-Content $path -Raw) side) | Should Be both
            Invoke-MpSide @('set','a','b','client')
            (Get-TomlString (Get-Content $path -Raw) side) | Should Be client
            [IO.File]::ReadAllText($path).Contains("`r`n") | Should Be $true
        }
        It 'moves a deduplicated resource block in argument order' {
            [void](Add-FixtureVersion defaultoptions '1' -Installed)
            $path = Join-Path $script:FixtureProject.Root 'config/defaultoptions-common.toml'
            [IO.File]::WriteAllText($path,'defaultResourcePacks = ["file/c.zip", "file/b.zip", "file/a.zip", "vanilla"]')
            foreach ($name in @('a','b','c','d')) { [IO.File]::WriteAllText((Join-Path $script:FixtureProject.Root "resourcepacks/$name.zip"),'fixture') }
            Invoke-MpResource @('move','c.zip','a.zip','c.zip','--position','2')
            ((Get-DefaultResourcePackOrder $script:FixtureProject) -join ',') | Should Be 'vanilla,file/c.zip,file/a.zip,file/b.zip'
            Invoke-MpResource @('enable','d.zip','b.zip','--position','1')
            ((Get-DefaultResourcePackOrder $script:FixtureProject) -join ',') | Should Be 'file/d.zip,file/b.zip,vanilla,file/c.zip,file/a.zip'
            $before = [IO.File]::ReadAllText($path)
            { Invoke-MpResource @('move','a.zip','absent','--position','1') } | Should Throw
            [IO.File]::ReadAllText($path) | Should Be $before
        }
        It 'pins and unpins managed files without duplicating editorial state' {
            [void](Add-FixtureVersion a '1' -Installed)
            Invoke-MpPin @('a')
            (Get-ModpackInventory $script:FixtureProject).Mods[0].Pinned | Should Be $true
            Invoke-MpUnpin @('a')
            (Get-ModpackInventory $script:FixtureProject).Mods[0].Pinned | Should Be $false
            (Get-ModpackMetadata $script:FixtureProject).Mods.Count | Should Be 0
        }
        It 'records transitives and promotes a dependency when explicitly added later' {
            [void](Add-FixtureVersion lib '1')
            [void](Add-FixtureVersion root '1' -Depends @{ lib='*' } -ProviderDependencies @(@{ project_id='lib'; version_id=$null; dependency_type='required' }))
            Invoke-MpContentOperation $script:FixtureProject -Operation add -Selectors @('root') | Out-Null
            (Get-ModpackMetadata $script:FixtureProject).Content['modrinth:lib'].Intent | Should Be transitive
            Invoke-MpContentOperation $script:FixtureProject -Operation add -Selectors @('lib') | Out-Null
            (Get-ModpackMetadata $script:FixtureProject).Content['modrinth:lib'].Intent | Should Be explicit
        }
        It 'uses the saved version number instead of renumbering a freshly fetched list' {
            [void](Add-FixtureVersion a '1')
            [void](Add-FixtureVersion a '2' -Installed -Published '2026-02-01T00:00:00Z')
            $cache = @{ CreatedUtc=[datetime]::UtcNow.ToString('o'); ProjectId='fixture'; ItemId='modrinth:a'; ItemName='a'; Versions=@(@{Index=1;Id='a-1';VersionNumber='1'}) }
            [void][IO.Directory]::CreateDirectory((Get-ModpackToolsConfigDirectory))
            Write-Utf8TextFileAtomic (Get-ModrinthVersionCachePath) ($cache | ConvertTo-Json -Depth 8)
            $plan = New-MpContentPlan $script:FixtureProject -Operation update -Selectors @('a') -To '1'
            $plan.Nodes['modrinth:a'].VersionId | Should Be 'a-1'
            (Read-ModrinthVersionCache).Versions[0].Id | Should Be 'a-1'
        }
        It 'skips pinned content in update all' {
            [void](Add-FixtureVersion a '1' -Installed -Pinned)
            [void](Add-FixtureVersion a '2' -Published '2026-02-01T00:00:00Z')
            $result = Invoke-MpContentOperation $script:FixtureProject -Operation update -All
            $result.Changes.Count | Should Be 0
            $result.Result.Nodes['modrinth:a'].VersionId | Should Be 'a-1'
        }
        It 'adds and activates a resource pack atomically' {
            [void](Add-FixtureVersion defaultoptions '1' -Installed)
            [void](Add-FixtureVersion texture '1')
            $script:FixtureApi['project/texture'].project_type = 'resourcepack'
            $script:FixtureApi['version/texture-1'].files[0].filename = 'texture.zip'
            $config = Join-Path $script:FixtureProject.Root 'config/defaultoptions-common.toml'
            [IO.File]::WriteAllText($config,'defaultResourcePacks = ["vanilla"]')
            Invoke-MpAdd @('texture','--enable','--position','1')
            (Get-DefaultResourcePackOrder $script:FixtureProject)[0] | Should Be 'file/texture.zip'
            (Get-ModpackInventory $script:FixtureProject).ResourcePacks.Count | Should Be 1
        }
        It 'rejects an affected pre-existing requirement' {
            [void](Add-FixtureVersion broken '1' -Installed -Depends @{ lib='>=4' })
            [void](Add-FixtureVersion lib '2' -Installed)
            [void](Add-FixtureVersion lib '3' -Published '2026-02-01T00:00:00Z')
            { New-MpContentPlan $script:FixtureProject -Operation update -Selectors @('lib') } | Should Throw 'No verified dependency solution'
        }
        It 'resolves required dependency cycles without repeated installation' {
            [void](Add-FixtureVersion a '1' -Depends @{ b='*' } -ProviderDependencies @(@{ project_id='b'; version_id=$null; dependency_type='required' }))
            [void](Add-FixtureVersion b '1' -Depends @{ a='*' } -ProviderDependencies @(@{ project_id='a'; version_id=$null; dependency_type='required' }))
            (New-MpContentPlan $script:FixtureProject -Operation add -Selectors @('a')).Changes.Count | Should Be 2
        }
        It 'blocks build conflicts before refresh even with no-refresh' {
            [void](Add-FixtureVersion broken '1' -Installed -Depends @{ absent='*' })
            { Build-ModpackProject $script:FixtureProject -NoRefresh } | Should Throw 'dependency validation'
            Assert-MockCalled Invoke-Packwiz -Times 0 -Scope It
        }
        It 'uses the same unavailable-provider policy for doctor and build' {
            [void](Add-FixtureVersion owner '1' -Installed -ProviderDependencies @(@{ project_id='ghost'; version_id=$null; dependency_type='required' }))
            $script:FixtureApi['project/ghost'] = [pscustomobject]@{ id='ghost'; title='Ghost'; project_type='mod'; server_side='required'; client_side='required' }
            $script:FixtureApi['project/ghost/version'] = @()
            Mock Test-MpProjectIndex { @() }
            Mock Invoke-MpStagedBuild {
                $path = Join-Path $Project.Root 'dist/fixture.mrpack'
                [void][IO.Directory]::CreateDirectory((Split-Path -Parent $path)); [IO.File]::WriteAllText($path,'artifact')
                $inventory = Get-ModpackInventory $Project
                [pscustomobject]@{ Path=$path; Size=8; Duration=[timespan]::Zero; Log=@(); RawLog=@(); Inventory=$inventory }
            }
            Mock Compare-MpBuildArtifactToProject { [pscustomobject]@{ Added=@(); Changed=@(); Removed=@(); Total=0 } }

            $build = Build-ModpackProject $script:FixtureProject -DryRun

            $build.Health.Errors.Count | Should Be 0
            $build.Health.Unknown.Count | Should BeGreaterThan 0
            Assert-MockCalled Invoke-MpStagedBuild -Times 1 -Scope It
        }
        It 'rejects an exported artifact that differs from the prepared project' {
            [void](Add-FixtureVersion example '1' -Installed)
            Mock Test-MpProjectIndex { @() }
            Mock Invoke-MpStagedBuild {
                $path = Join-Path $Project.Root 'dist/fixture.mrpack'
                [void][IO.Directory]::CreateDirectory((Split-Path -Parent $path)); [IO.File]::WriteAllText($path,'artifact')
                $inventory = Get-ModpackInventory $Project
                [pscustomobject]@{ Path=$path; Size=8; Duration=[timespan]::Zero; Log=@(); RawLog=@(); Inventory=$inventory }
            }
            Mock Compare-MpBuildArtifactToProject { [pscustomobject]@{ Added=@([pscustomobject]@{Path='mods/missing.jar'}); Changed=@(); Removed=@(); Total=1 } }

            { Build-ModpackProject $script:FixtureProject -DryRun } | Should Throw 'does not match the prepared project'
        }
        It 'preserves the previous artifact on export failure' {
            $path = Join-Path $script:FixtureProject.Root ('dist/' + $script:FixtureProject.OutputName)
            [void][IO.Directory]::CreateDirectory((Split-Path -Parent $path)); [IO.File]::WriteAllText($path,'original')
            Mock Invoke-MpStagedBuild { throw 'export failed' }
            { Build-ModpackProject $script:FixtureProject } | Should Throw 'export failed'
            [IO.File]::ReadAllText($path) | Should Be original
        }
        It 'uses health cache without network and invalidates it for changed content' {
            [void](Add-FixtureVersion a '1' -Installed)
            $checked = Get-MpProjectHealth $script:FixtureProject -Check
            $checked.Complete | Should Be $true
            Mock Invoke-ModrinthApiRequest { throw 'network must not be used' }
            (Get-MpProjectHealth $script:FixtureProject).Complete | Should Be $true
            $jar = New-FixtureJar 'local.jar' @{ 'fabric.mod.json'='{"schemaVersion":1,"id":"local","version":"1","depends":{"missing":"*"}}' }
            [IO.File]::Copy($jar,(Join-Path $script:FixtureProject.Root 'mods/local.jar'))
            (Get-MpProjectHealth $script:FixtureProject).Errors.Count | Should BeGreaterThan 0
        }
        It 'reports a stale installable artifact separately from project health' {
            $artifact = Join-Path $script:FixtureProject.Root 'dist/fixture.mrpack'
            [void][IO.Directory]::CreateDirectory((Split-Path -Parent $artifact)); [IO.File]::WriteAllText($artifact,'old')
            Mock Compare-MpBuildArtifactToProject {
                [pscustomobject]@{
                    Added=@([pscustomobject]@{Kind='MOD';Path='mods/new.jar'})
                    Changed=@(); Removed=@([pscustomobject]@{Kind='MOD';Path='mods/old.jar'}); Total=2
                }
            }

            $check = Get-MpBuildArtifactDoctorCheck $script:FixtureProject

            $check.Section | Should Be 'BUILD ARTIFACT'
            $check.Status | Should Be warn
            $check.Value | Should Be Stale
            ($check.Items.Text -join '; ') | Should Match 'Do not install this artifact'
        }
        It 'rejects duplicate provider identities instead of hiding one artifact' {
            [void](Add-FixtureVersion a '1' -Installed)
            $metadata = (Get-ModpackInventory $script:FixtureProject).Mods[0].MetadataPath
            [IO.File]::Copy($metadata, (Join-Path (Split-Path -Parent $metadata) 'duplicate.pw.toml'))
            { Get-MpProjectState $script:FixtureProject -Check } | Should Throw 'Duplicate content ID'
        }
        It 'keeps unavailable metadata distinct from known conflicts' {
            [void](Add-FixtureVersion a '1' -Installed)
            Mock Get-MpArtifact { return $null }
            $report = Get-MpProjectHealth $script:FixtureProject -Check
            $report.Errors.Count | Should Be 0
            $report.Unknown.Count | Should BeGreaterThan 0
            { Assert-MpGraphPolicy $report -Build } | Should Not Throw
            { Assert-MpGraphPolicy $report -Build -Strict } | Should Throw
        }
        It 'validates client-only dependencies separately from the server' {
            [void](Add-FixtureVersion a '1' -Installed -Depends @{ b='*' }); [void](Add-FixtureVersion b '1' -Installed)
            Set-ModpackModSide $script:FixtureProject a client | Out-Null
            Set-ModpackModSide $script:FixtureProject b client | Out-Null
            (Get-MpProjectHealth $script:FixtureProject -Check).Errors.Count | Should Be 0
            Set-ModpackModSide $script:FixtureProject a both | Out-Null
            $report = Get-MpProjectHealth $script:FixtureProject -Check
            $report.Errors.Count | Should Be 1
            $report.Errors[0].Side | Should Be server
        }
    }

}
