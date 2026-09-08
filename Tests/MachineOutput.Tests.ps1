Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'ModpackTools.psd1') -Force

InModuleScope ModpackTools {
    Describe 'Machine-readable output' {
        BeforeEach {
            $script:MpConsole = $null
            $script:MpMachineContext = $null
        }

        It 'extracts JSON presentation flags without changing command argument order' {
            $parsed = ConvertFrom-MpPresentationOptions @('--json','inventory','one','--no-human','--colour','never','two')
            $parsed.Arguments | Should Be @('inventory','one','two')
            $parsed.Json | Should Be $true
            $parsed.NoHuman | Should Be $true
            $parsed.Colour | Should Be never
        }

        It 'requires JSON when human presentation is disabled' {
            { ConvertFrom-MpPresentationOptions @('inventory','--no-human') } | Should Throw "requires '--json'"
        }

        It 'returns one clean JSON envelope for version in no-human mode' {
            $output = @(modpack --version --offline --json --no-human)
            $output.Count | Should Be 1
            $envelope = $output[0] | ConvertFrom-Json
            $envelope.schema_version | Should Be 1
            $envelope.ok | Should Be $true
            $envelope.command | Should Be '--version'
            $envelope.data.version.version | Should Be $script:ModuleVersion
        }

        It 'returns a structured error envelope before preserving the terminating error' {
            $output = [Collections.Generic.List[object]]::new()
            $caught = $null
            try {
                modpack definitely-not-a-command --json --no-human | ForEach-Object { $output.Add($_) }
            }
            catch { $caught = $_ }
            $caught | Should Not BeNullOrEmpty
            $output.Count | Should Be 1
            $envelope = [string]$output[0] | ConvertFrom-Json
            $envelope.ok | Should Be $false
            $envelope.error.id | Should Match '^ModpackTools.Command.Unknown'
        }

        It 'snapshots list renderer data without changing the human renderer contract' {
            [void](Initialize-MpMachineContext -Enabled -Command list)
            $script:MpConsole = New-R3Console -Colour never -Sink { param($Text,$Stream) }
            $projects = @(
                [pscustomobject]@{ Id='one'; DisplayName='One'; DisplayVersion='1.0'; MinecraftVersion='1.21.1'; Loader='fabric'; LoaderVersion='0.16'; Root='C:\One'; OutputName='One.mrpack' },
                [pscustomobject]@{ Id='two'; DisplayName='Two'; DisplayVersion='2.0'; MinecraftVersion='1.21.1'; Loader='fabric'; LoaderVersion='0.16'; Root='C:\Two'; OutputName='Two.mrpack' }
            )
            { Write-ModpackList -Projects $projects -Root 'C:\Packs' } | Should Not Throw
            $script:MpMachineContext.Data.projects.root | Should Be 'C:\Packs'
            $script:MpMachineContext.Data.projects.items.Count | Should Be 2
            $script:MpMachineContext.Data.projects.items[0].id | Should Be one
        }

        It 'snapshots transaction changes independently from human output' {
            [void](Initialize-MpMachineContext -Enabled -Command add)
            $script:MpConsole = New-R3Console -Colour never -Sink { param($Text,$Stream) }
            $transaction = [pscustomobject]@{
                Applied = $true
                Changes = @([pscustomobject]@{ Path='mods/example.pw.toml'; Before=$null; After=[pscustomobject]@{}; Reason='requested' })
            }
            Write-MpTransactionSummary -Transaction $transaction
            $data = $script:MpMachineContext.Data.transaction
            $data.applied | Should Be $true
            $data.change_count | Should Be 1
            $data.changes[0].action | Should Be add
            $data.changes[0].path | Should Be 'mods/example.pw.toml'
        }
    }
}
