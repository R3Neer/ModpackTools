Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'ModpackTools.psd1') -Force

InModuleScope ModpackTools {
    Describe 'CLI usability regressions' {
        BeforeEach {
            $script:ActiveProjectId = $null
            $script:MpConsole = $null
            $script:MpMachineContext = $null
            $global:MpCliCapturedLines = [Collections.Generic.List[string]]::new()
        }

        AfterEach {
            Remove-Variable -Name MpCliCapturedLines -Scope Global -ErrorAction SilentlyContinue
        }

        It 'defines the documented short aliases on the public command' {
            $command = Get-Command modpack
            $command.Parameters.Help.Aliases | Should Contain 'h'
            $command.Parameters.SelfUpdate.Aliases | Should Contain 'u'
            $command.Parameters.Version.Aliases | Should Contain 'v'
        }

        It 'routes -h to global and command help' {
            Mock Initialize-MpConsole {}
            Mock Show-MpHelp {}

            modpack -h
            Assert-MockCalled Show-MpHelp -Times 1 -ParameterFilter { [string]::IsNullOrEmpty($Command) }

            modpack search -h
            Assert-MockCalled Show-MpHelp -Times 1 -ParameterFilter { $Command -eq 'search' }
        }

        It 'routes -v and forwarded literal -v to the global version action' {
            Mock Initialize-MpConsole {}
            Mock Invoke-MpVersion {}

            modpack -v --offline
            Assert-MockCalled Invoke-MpVersion -Times 1 -ParameterFilter { $Arguments -contains '--offline' }

            $forwarded = @('-v','--offline')
            & (Get-Command modpack) @forwarded
            Assert-MockCalled Invoke-MpVersion -Times 2 -ParameterFilter { $Arguments -contains '--offline' }
        }

        It 'routes -u to self-update rather than content update' {
            Mock Initialize-MpConsole {}
            Mock Invoke-MpSelfUpdate {}
            Mock Invoke-MpUpdate { throw 'content update must not run' }
            Mock Write-R3Line {}
            Mock Get-MpConsole { [pscustomobject]@{} }

            modpack -u --check
            Assert-MockCalled Invoke-MpSelfUpdate -Times 1 -ParameterFilter { $Arguments -contains '--check' }
            Assert-MockCalled Invoke-MpUpdate -Times 0
        }

        It 'allows search with no active or explicit project' {
            Mock Search-ModrinthContent {
                [pscustomobject]@{ Query='sodium'; Type='all'; TotalHits=0; Results=@(); ProjectId=$null; MinecraftVersion=$null; Loader=$null }
            }
            Mock Write-ModrinthSearchResults {}
            Mock Resolve-ModpackProject { throw 'search must not require a project' }
            Mock Get-MpConsole { $null }
            Mock Write-R3Status {}

            { Invoke-MpSearch -Arguments @('sodium') } | Should Not Throw
            Assert-MockCalled Search-ModrinthContent -Times 1 -ParameterFilter { $null -eq $Project -and $Query -eq 'sodium' }
            Assert-MockCalled Write-ModrinthSearchResults -Times 1 -ParameterFilter { $null -eq $Project }
        }

        It 'omits Minecraft and loader facets from a global Modrinth search request' {
            Mock Invoke-RestMethod { [pscustomobject]@{ total_hits=0; hits=@() } }

            [void](Invoke-ModrinthSearchRequest -Query 'sodium' -Project $null -Type mod -Limit 10)

            Assert-MockCalled Invoke-RestMethod -Times 1 -ParameterFilter {
                $Uri -match 'project_type' -and $Uri -notmatch 'versions' -and $Uri -notmatch 'categories'
            }
        }

        It 'stores null compatibility context for a global search' {
            Mock Invoke-ModrinthSearchRequest { [pscustomobject]@{ total_hits=0; hits=@() } }
            Mock Write-ModrinthSearchCache {}

            $search = Search-ModrinthContent -Project $null -Query 'global' -Type mod
            $search.ProjectId | Should BeNullOrEmpty
            $search.MinecraftVersion | Should BeNullOrEmpty
            $search.Loader | Should BeNullOrEmpty
            Assert-MockCalled Invoke-ModrinthSearchRequest -Times 1 -ParameterFilter { $null -eq $Project -and $Query -eq 'global' }
        }

        It 'lets a global search number resolve inside a later project' {
            Mock Read-ModrinthSearchCache {
                [pscustomobject]@{
                    CreatedUtc=[datetime]::UtcNow.ToString('o'); ProjectId=$null; MinecraftVersion=$null; Loader=$null
                    Results=@([pscustomobject]@{ Index=1; ProjectId='global-id'; Slug='global'; Type='mod'; Title='Global' })
                }
            }
            Mock Test-MpCacheTimestamp { $true }
            $project = [pscustomobject]@{ Id='target'; MinecraftVersion='1.21.1'; Loader='fabric' }

            $resolved = Resolve-ModrinthSearchNumber -Selector '1' -Project $project
            $resolved.ProjectId | Should Be 'global-id'
        }

        It 'keeps project-filtered search numbers bound to their search project' {
            Mock Read-ModrinthSearchCache {
                [pscustomobject]@{
                    CreatedUtc=[datetime]::UtcNow.ToString('o'); ProjectId='source'; MinecraftVersion='1.21.1'; Loader='fabric'
                    Results=@([pscustomobject]@{ Index=1; ProjectId='filtered-id'; Slug='filtered'; Type='mod'; Title='Filtered' })
                }
            }
            Mock Test-MpCacheTimestamp { $true }
            $project = [pscustomobject]@{ Id='target'; MinecraftVersion='1.21.1'; Loader='fabric' }

            { Resolve-ModrinthSearchNumber -Selector '1' -Project $project } | Should Throw "belongs to project 'source'"
        }

        It 'converts a global search number to its Modrinth identity before add planning' {
            $project = [pscustomobject]@{ Id='target'; MinecraftVersion='1.21.1'; Loader='fabric' }
            Mock Resolve-MpCommandProject { $project }
            Mock Resolve-ModrinthSearchNumber { [pscustomobject]@{ ProjectId='global-id' } }
            Mock Invoke-MpContentOperation {
                [pscustomobject]@{
                    Applied=$true
                    Changes=@()
                    Result=[pscustomobject]@{ Changes=@(); Report=$null }
                }
            }
            Mock Write-MpContentPlan {}
            Mock Write-MpTransactionSummary {}

            Invoke-MpAdd -Arguments @('1')
            Assert-MockCalled Invoke-MpContentOperation -Times 1 -ParameterFilter {
                $Project.Id -eq 'target' -and @($Selectors).Count -eq 1 -and $Selectors[0] -eq 'modrinth:global-id'
            }
        }

        It 'renders a project-free search and exposes null project machine context' {
            [void](Initialize-MpMachineContext -Enabled -Command search)
            $script:MpConsole = New-R3Console -Colour never -ThemeExtension (Read-MpThemeExtension) -Sink {
                param($Text,$Stream)
                $global:MpCliCapturedLines.Add([string]$Text)
            }
            $search = [pscustomobject]@{ Query='nothing'; Type='all'; TotalHits=0; Results=@() }

            Write-ModrinthSearchResults -Search $search -Project $null
            $text = $global:MpCliCapturedLines -join "`n"
            $text | Should Match 'Any Minecraft version / loader'
            $text | Should Match 'No results were found'
            $script:MpMachineContext.Data.Contains('project') | Should Be $true
            $script:MpMachineContext.Data.project | Should BeNullOrEmpty
        }

        It 'uses completed wording for applied transaction file changes' {
            $script:MpConsole = New-R3Console -Colour never -ThemeExtension (Read-MpThemeExtension) -Sink {
                param($Text,$Stream)
                $global:MpCliCapturedLines.Add([string]$Text)
            }
            $transaction = [pscustomobject]@{
                Applied=$true
                Changes=@(
                    [pscustomobject]@{ Path='new.txt'; Before=$null; After='hash'; Reason='fixture' },
                    [pscustomobject]@{ Path='pack.toml'; Before='old'; After='new'; Reason='fixture' },
                    [pscustomobject]@{ Path='old.txt'; Before='hash'; After=$null; Reason='fixture' }
                )
            }

            Write-MpTransactionSummary -Transaction $transaction
            $text = $global:MpCliCapturedLines -join "`n"
            $text | Should Match 'Added new\.txt'
            $text | Should Match 'Changed pack\.toml'
            $text | Should Match 'Removed old\.txt'
            $text | Should Match '3 file change\(s\) applied'
        }

        It 'uses hypothetical wording for dry-run transaction file changes' {
            $script:MpConsole = New-R3Console -Colour never -ThemeExtension (Read-MpThemeExtension) -Sink {
                param($Text,$Stream)
                $global:MpCliCapturedLines.Add([string]$Text)
            }
            $transaction = [pscustomobject]@{
                Applied=$false
                Changes=@([pscustomobject]@{ Path='pack.toml'; Before='old'; After='new'; Reason='fixture' })
            }

            Write-MpTransactionSummary -Transaction $transaction -DryRun
            $text = $global:MpCliCapturedLines -join "`n"
            $text | Should Match 'Would change pack\.toml'
            $text | Should Match 'nothing was changed'
            $text | Should Not Match 'Changed pack\.toml'
        }

        It 'describes planned removals as state transitions rather than imperatives' {
            $script:MpConsole = New-R3Console -Colour never -ThemeExtension (Read-MpThemeExtension) -Sink {
                param($Text,$Stream)
                $global:MpCliCapturedLines.Add([string]$Text)
            }
            $plan = [pscustomobject]@{
                Changes=@([pscustomobject]@{
                    Before=[pscustomobject]@{ VersionId='old-version'; Item=[pscustomobject]@{ Name='Library X' } }
                    After=$null
                    Reason='unused dependency'
                })
                Report=$null
            }

            Write-MpContentPlan -Plan $plan -SkipHealth
            $text = $global:MpCliCapturedLines -join "`n"
            $text | Should Match 'Library X: old-version -> removed \(unused dependency\)'
            $text | Should Not Match 'Library X: remove'
        }
    }
}
