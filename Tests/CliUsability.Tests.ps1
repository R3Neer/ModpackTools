Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'ModpackTools.psd1') -Force

InModuleScope ModpackTools {
    Describe 'CLI usability regressions' {
        BeforeEach {
            $script:ActiveProjectId = $null
            $script:CommandProjectId = $null
            $script:MpConsole = $null
            $script:MpMachineContext = $null
        }

        It 'routes -h to global and command help' {
            Mock Initialize-MpConsole {}
            Mock Show-MpHelp {}

            modpack -h
            Assert-MockCalled Show-MpHelp -Times 1 -ParameterFilter { [string]::IsNullOrEmpty($Command) }

            modpack content -h
            Assert-MockCalled Show-MpHelp -Times 1 -ParameterFilter { $Command -eq 'content' }
        }

        It 'routes -V and a forwarded literal -V to the local version action' {
            Mock Initialize-MpConsole {}
            Mock Invoke-MpVersion {}

            modpack -V
            Assert-MockCalled Invoke-MpVersion -Times 1 -ParameterFilter { -not $Arguments.Count }

            $forwarded = @('-V')
            & (Get-Command modpack) @forwarded
            Assert-MockCalled Invoke-MpVersion -Times 2 -ParameterFilter { -not $Arguments.Count }
        }

        It 'does not retain the retired self-update shorthand' {
            { modpack -u --check } | Should Throw "Command '-u' is not recognized"
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

        It 'keeps explicit project-free search presentation wording in the human renderer' {
            $source = $script:MpOriginalWriteModrinthSearchResults.ToString()
            $source | Should Match ([regex]::Escape("'Compatibility' 'Any Minecraft version / loader'"))
            $source | Should Match ([regex]::Escape("'No results were found.'"))
            (ConvertTo-MpMachineProject $null) | Should BeNullOrEmpty
        }

        It 'keeps completed transaction wording distinct from imperative actions' {
            $source = $script:MpOriginalWriteMpTransactionSummary.ToString()
            foreach ($word in @("'Added'","'Changed'","'Removed'")) { $source | Should Match ([regex]::Escape($word)) }
            $source | Should Match ([regex]::Escape('file change(s) applied.'))
        }

        It 'keeps dry-run and preview transaction wording explicitly hypothetical' {
            $source = $script:MpOriginalWriteMpTransactionSummary.ToString()
            foreach ($word in @("'Would add'","'Would change'","'Would remove'")) { $source | Should Match ([regex]::Escape($word)) }
            $source | Should Match ([regex]::Escape('nothing was changed.'))
            $source | Should Match ([regex]::Escape('nothing has been changed.'))
        }

        It 'describes planned removals as a state transition rather than an imperative' {
            $source = (Get-Item Function:\Write-MpContentPlan).ScriptBlock.ToString()
            $source | Should Match ([regex]::Escape('-> removed'))
            $source | Should Not Match ': remove \('
        }
    }
}
