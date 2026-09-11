Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'ModpackTools.psd1') -Force

InModuleScope ModpackTools {
    function New-ProviderSideFixture {
        $script:ConfigHomeOverride = Join-Path $TestDrive ('cache-' + [guid]::NewGuid().ToString('N'))
        $root = Join-Path $TestDrive ('provider-side-pack-' + [guid]::NewGuid().ToString('N'))
        foreach ($dir in @('.modpack','mods','config','resourcepacks')) {
            [void][IO.Directory]::CreateDirectory((Join-Path $root $dir))
        }
        [IO.File]::WriteAllText((Join-Path $root 'pack.toml'), "name = `"Fixture`"`nversion = `"1`"`npack-format = `"packwiz:1.1.0`"`n[index]`nfile = `"index.toml`"`nhash-format = `"sha256`"`nhash = `"test`"`n[versions]`nminecraft = `"1.21.1`"`nfabric = `"0.16.0`"`n")
        [IO.File]::WriteAllText((Join-Path $root 'index.toml'), 'hash-format = "sha256"')
        Write-PowerShellDataFileAtomic @{ SchemaVersion = 1; Id = 'fixture'; JavaVersion = '21' } (Join-Path $root '.modpack/project.psd1')
        Write-PowerShellDataFileAtomic @{ Categories = @{}; Mods = @{}; ResourcePacks = @{} } (Join-Path $root '.modpack/metadata.psd1')
        $script:ProviderSideProject = Read-ModpackProject $root
        $script:ProviderSideApi = @{}
        $script:ProviderSideArtifacts = @{}
    }

    function New-ProviderSideJar {
        param([string]$Id, [string]$Version, [string]$Environment = '*')
        $path = Join-Path $TestDrive ([guid]::NewGuid().ToString('N') + ".jar")
        $manifest = @{
            schemaVersion = 1
            id = $Id
            name = $Id
            version = $Version
            environment = $Environment
        } | ConvertTo-Json -Depth 8
        $zip = [IO.Compression.ZipFile]::Open($path, 'Create')
        try {
            $writer = [IO.StreamWriter]::new($zip.CreateEntry('fabric.mod.json').Open())
            try { $writer.Write($manifest) } finally { $writer.Dispose() }
        }
        finally { $zip.Dispose() }
        return $path
    }

    function Add-ProviderSideVersion {
        param(
            [string]$Id,
            [string]$Version,
            [array]$ProviderDependencies = @(),
            [string]$ClientSide = 'required',
            [string]$ServerSide = 'required'
        )
        $environment = if ($ServerSide -eq 'unsupported') { 'client' } elseif ($ClientSide -eq 'unsupported') { 'server' } else { '*' }
        $jar = New-ProviderSideJar $Id $Version $environment
        $hash = (Get-FileHash $jar -Algorithm SHA512).Hash
        $script:ProviderSideArtifacts[$hash] = $jar
        $versionId = "$Id-$Version"
        $raw = [pscustomobject]@{
            id = $versionId
            project_id = $Id
            name = $versionId
            version_number = $Version
            date_published = '2026-01-01T00:00:00Z'
            version_type = 'release'
            game_versions = @('1.21.1')
            loaders = @('fabric')
            dependencies = $ProviderDependencies
            files = @([pscustomobject]@{
                filename = "$versionId.jar"
                primary = $true
                url = "https://example.invalid/$versionId.jar"
                hashes = @{ sha512 = $hash; sha1 = 'unused' }
            })
        }
        $script:ProviderSideApi["version/$versionId"] = $raw
        $script:ProviderSideApi["project/$Id"] = [pscustomobject]@{
            id = $Id
            slug = $Id
            title = $Id
            project_type = 'mod'
            client_side = $ClientSide
            server_side = $ServerSide
        }
        if (-not $script:ProviderSideApi.ContainsKey("project/$Id/version")) {
            $script:ProviderSideApi["project/$Id/version"] = @()
        }
        $script:ProviderSideApi["project/$Id/version"] += $raw
        return $raw
    }

    function New-GraphMod {
        param([string]$Id, [string]$Side = 'both', [array]$Requirements = @())
        [pscustomobject]@{
            Id = $Id
            Version = '1'
            Side = $Side
            Provides = @{}
            Requirements = @($Requirements)
            Nested = $false
        }
    }

    function New-GraphNode {
        param([string]$Id, [string]$Side = 'both', [array]$Requirements = @(), [array]$Mods = @())
        [pscustomobject]@{
            Id = $Id
            Item = [pscustomobject]@{ Id = $Id; Name = $Id; Kind = 'mod'; Side = $Side; Source = 'packwiz' }
            VersionId = "$Id-1"
            RawVersion = $null
            File = $null
            Mods = @($Mods)
            Requirements = @($Requirements)
            Warnings = @()
            Pinned = $false
            Intent = 'explicit'
            Enabled = $true
            Published = ''
        }
    }

    Describe 'Provider dependency side resolution' {
        BeforeEach {
            New-ProviderSideFixture
            Mock Invoke-ModrinthApiRequest {
                $key = ($PathAndQuery -split '\?')[0]
                if (-not $script:ProviderSideApi.ContainsKey($key)) { throw "Missing fixture $key" }
                return $script:ProviderSideApi[$key]
            }
            Mock Get-ModrinthVersionsByIds {
                @($VersionIds | ForEach-Object { $script:ProviderSideApi["version/$_"] } | Where-Object { $null -ne $_ })
            }
            Mock Get-MpArtifact { return $script:ProviderSideArtifacts[$Hash] }
        }

        It 'resolves a both-side project with a client-only provider dependency' {
            [void](Add-ProviderSideVersion 'modmenu' '1' -ClientSide required -ServerSide unsupported)
            [void](Add-ProviderSideVersion 'gravity' '1' -ProviderDependencies @(@{ project_id = 'modmenu'; version_id = $null; dependency_type = 'required' }))

            $plan = New-MpContentPlan $script:ProviderSideProject -Operation add -Selectors @('gravity')

            $plan.Nodes.ContainsKey('modrinth:modmenu') | Should Be $true
            $plan.Nodes['modrinth:modmenu'].Item.Side | Should Be 'client'
            @($plan.Report.Errors).Count | Should Be 0
        }

        It 'resolves a both-side project with a server-only provider dependency' {
            [void](Add-ProviderSideVersion 'serverlib' '1' -ClientSide unsupported -ServerSide required)
            [void](Add-ProviderSideVersion 'owner' '1' -ProviderDependencies @(@{ project_id = 'serverlib'; version_id = $null; dependency_type = 'required' }))

            $plan = New-MpContentPlan $script:ProviderSideProject -Operation add -Selectors @('owner')

            $plan.Nodes.ContainsKey('modrinth:serverlib') | Should Be $true
            $plan.Nodes['modrinth:serverlib'].Item.Side | Should Be 'server'
            @($plan.Report.Errors).Count | Should Be 0
        }

        It 'keeps a both-side provider dependency active on both sides' {
            $requirement = New-MpRequirement -Target 'modrinth:sharedlib' -Kind required -Scope project -Source provider -SuggestedVersionId 'sharedlib-2'
            $owner = New-GraphNode 'modrinth:owner' -Requirements @($requirement)
            $shared = New-GraphNode 'modrinth:sharedlib' -Side both
            $nodes = @{ 'modrinth:owner' = $owner; 'modrinth:sharedlib' = $shared }

            $report = Get-MpGraphReport $script:ProviderSideProject $nodes

            @($report.Unknown | Where-Object Owner -eq 'modrinth:owner').Count | Should Be 2
        }

        It 'keeps an unresolved provider dependency required until a target exists' {
            $requirement = New-MpRequirement -Target 'modrinth:missing' -Kind required -Scope project -Source provider
            $owner = New-GraphNode 'modrinth:owner' -Requirements @($requirement)
            $nodes = @{ 'modrinth:owner' = $owner }

            $report = Get-MpGraphReport $script:ProviderSideProject $nodes

            @($report.Errors | Where-Object Owner -eq 'modrinth:owner').Count | Should Be 2
        }

        It 'does not weaken loader-manifest dependencies on the unsupported side of another project' {
            $manifestRequirement = New-MpRequirement -Target 'clientlib' -Kind required -Scope mod -Source manifest
            $ownerMod = New-GraphMod 'owner' -Requirements @($manifestRequirement)
            $clientMod = New-GraphMod 'clientlib' -Side client
            $owner = New-GraphNode 'modrinth:owner' -Mods @($ownerMod)
            $client = New-GraphNode 'modrinth:clientlib' -Side client -Mods @($clientMod)
            $nodes = @{ 'modrinth:owner' = $owner; 'modrinth:clientlib' = $client }

            $report = Get-MpGraphReport $script:ProviderSideProject $nodes

            @($report.Errors | Where-Object { $_.Owner -eq 'modrinth:owner' -and $_.Side -eq 'server' }).Count | Should Be 1
        }
    }
}
