Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'ModpackTools.psd1') -Force

InModuleScope ModpackTools {
    Describe 'R3CLI integration' {
        BeforeEach { $script:MpConsole = $null }

        It 'uses the verified private dependency and exports only modpack' {
            $verified = Test-MpR3Package $script:ModuleRoot
            $script:R3Module.Path | Should Be (Join-Path (Split-Path -Parent $verified.Path) 'R3CLI.psm1')
            @((Get-Module ModpackTools).ExportedFunctions.Keys) | Should Be @('modpack')
        }

        It 'fails package validation before loading a modified dependency' {
            $root = Join-Path $TestDrive 'broken-package'
            [void][IO.Directory]::CreateDirectory((Join-Path $root 'Private/vendor'))
            Copy-Item (Join-Path $script:ModuleRoot 'dependencies.psd1') $root
            Copy-Item (Join-Path $script:ModuleRoot 'Private/vendor/R3CLI') (Join-Path $root 'Private/vendor/R3CLI') -Recurse
            [IO.File]::AppendAllText((Join-Path $root 'Private/vendor/R3CLI/resources.json'),'tampered')
            { Test-MpR3Package $root } | Should Throw 'verification failed'
        }

        It 'inherits common colours and preserves legacy overrides without rewriting the file' {
            $path = Join-Path $TestDrive 'legacy-theme.toml'
            $theme = (Get-MpConsole).Theme
            $text = "[colors]`r`n" + (($theme.Keys | Sort-Object | ForEach-Object { '"' + $_ + '" = "' + $theme[$_] + '"' }) -join "`r`n")
            $text = $text.Replace($theme.heading,'#123456')
            [IO.File]::WriteAllText($path,$text)
            $before = (Get-FileHash $path).Hash
            $extension = Read-MpThemeExtension $path
            $extension.Count | Should Be 4
            $extension.heading | Should Be '#123456'
            $extension.ContainsKey('value') | Should Be $false
            (Get-FileHash $path).Hash | Should Be $before
        }

        It 'extracts presentation options once and preserves selector order' {
            $parsed = ConvertFrom-MpPresentationOptions @('--ascii','add','one','--colour','never','two','--category','performance')
            $parsed.Arguments | Should Be @('add','one','two','--category','performance')
            $parsed.Colour | Should Be never
            $parsed.Ascii | Should Be $true
        }

        It 'rejects duplicate and malformed presentation options' {
            { modpack --ascii --help --ascii } | Should Throw 'repeated'
            { modpack --colour always --help --colour never } | Should Throw 'repeated'
            { modpack --help --colour invalid } | Should Throw 'invalid'
            { modpack --colour } | Should Throw 'requires a value'
            { modpack --ascii=true --help } | Should Throw 'does not accept a value'
        }

        It 'does not turn an explicitly empty command into another executable command' {
            Mock Resolve-MpCommandProject { throw 'Must not resolve a project' }
            { modpack '' add sodium } | Should Throw 'global help option'
            Assert-MockCalled Resolve-MpCommandProject -Times 0 -Scope It
        }

        It 'renders help without accessing project or provider state' {
            Mock Resolve-MpCommandProject { throw 'Project access is forbidden' }
            Mock Invoke-ModrinthApiRequest { throw 'Network access is forbidden' }
            $text = modpack add --help --colour never --ascii 6>&1 | Out-String
            $text | Should Match 'ARGUMENTS AND OPTIONS'
            $text | Should Match '--allow-downgrade'
            $text | Should Not Match '\x1b'
            Assert-MockCalled Resolve-MpCommandProject -Times 0 -Scope It
            Assert-MockCalled Invoke-ModrinthApiRequest -Times 0 -Scope It
        }

        It 'renders complete remove help through the catalogue without project or network access' {
            Mock Resolve-MpCommandProject { throw 'Project access is forbidden' }
            Mock Invoke-ModrinthApiRequest { throw 'Network access is forbidden' }
            $text = modpack remove --help --colour never --ascii 6>&1 | Out-String
            foreach ($option in @('--cascade','--autoremove','--strict','--dry-run','--yes','--project','--type')) { $text | Should Match ([regex]::Escape($option)) }
            $text | Should Match 'ARGUMENTS AND OPTIONS'
            $text | Should Not Match '\x1b'
            Assert-MockCalled Resolve-MpCommandProject -Times 0 -Scope It
            Assert-MockCalled Invoke-ModrinthApiRequest -Times 0 -Scope It
        }

        It 'renders removal reasons literally using the shared renderer at narrow widths' {
            $script:MpConsole = New-R3Console -Colour never -Ascii -Width 32 -ThemeExtension (Read-MpThemeExtension)
            $plan = [pscustomobject]@{ Changes=@([pscustomobject]@{ Before=[pscustomobject]@{Item=[pscustomobject]@{Name='Example [literal]'}}; After=$null; Reason='unused dependency' }) }
            $text = (Write-MpContentPlan $plan -SkipHealth 6>&1 | ForEach-Object { [string]$_ }) -join ''
            $text | Should Match 'Example \[literal\]'
            $text | Should Match 'unused dependency'
            $text | Should Not Match '\x1b'
        }

        It 'keeps human output out of the object pipeline' {
            @(modpack --help 6>$null).Count | Should Be 0
            @(modpack --version --offline 6>$null).Count | Should Be 0
        }

        It 'honours explicit colour and restores the context for the next command' {
            $coloured = @(modpack --help --colour always 6>&1 | ForEach-Object { [string]$_ }) -join "`n"
            $plain = @(modpack --help --colour never 6>&1 | ForEach-Object { [string]$_ }) -join "`n"
            $coloured | Should Match '\x1b\['
            $plain | Should Not Match '\x1b'
            $script:MpConsole | Should BeNullOrEmpty
        }

        It 'preserves catchable error identity without emitting a duplicate diagnostic' {
            $records = @(& { try { modpack invalid-command } catch { $_ } } 6>&1)
            $records.Count | Should Be 1
            $records[0].FullyQualifiedErrorId | Should Match '^ModpackTools.Command.Unknown'
            $records[0].CategoryInfo.Category | Should Be InvalidArgument
            $records[0].TargetObject | Should Be 'invalid-command'
            $records[0].Exception.Message | Should Not Match '\x1b'
            $records[0].Exception.Message | Should Match 'Try: modpack --help'
        }

        It 'blocks mutations with a missing renderer and gives a plain recovery diagnostic' {
            Mock Invoke-MpAdd { throw 'Mutation must not start' }
            $saved = $script:R3Module
            try {
                $script:R3Module = $null
                { modpack add sodium } | Should Throw 'R3CLI presentation dependency is unavailable'
            } finally { $script:R3Module = $saved }
            Assert-MockCalled Invoke-MpAdd -Times 0 -Scope It
        }

        It 'preserves domain references, sides, pins and literal content in narrow output' {
            $script:MpConsole = New-R3Console -Colour never -Ascii -Width 30 -ThemeExtension (Read-MpThemeExtension)
            $item = [pscustomobject]@{ ReferenceNumber=12; Side='both'; Name='Example [literal]'; Source='local'; Filename='example.jar'; Pinned=$true }
            $text = (Write-MpSideEntry $item -ReferenceWidth 2 6>&1 | ForEach-Object { [string]$_ }) -join ''
            $text | Should Match '\[12\]'
            $text | Should Match '\[C\]\[H\]'
            $text | Should Match 'Example \[literal\]'
            $text | Should Match '\[pinned\]'
            $text | Should Match 'LOCAL'
            $text | Should Match 'example.jar'
            $text | Should Not Match '✓'
        }

        It 'reports the renderer revision in doctor' {
            Mock Get-ModpackRoot { return $TestDrive }
            Mock Get-ModpackProjects { return @() }
            $check = (Get-MpDoctorReport).Checks | Where-Object Label -eq R3CLI
            $check.Status | Should Be pass
            $check.Value | Should Match ([regex]::Escape($script:verifiedR3.Revision))
        }

        It 'renders conflicts, consolidates recommendations by side and groups incomplete verification' {
            $recommendation = New-MpRequirement -Target appleskin -Kind recommended
            $java = New-MpRequirement -Target java -Range '>=25'
            $health = [pscustomobject]@{
                Errors = @(
                    [pscustomobject]@{ Message = 'First dependency conflict' },
                    [pscustomobject]@{ Message = 'Second dependency conflict' }
                )
                Warnings = @(
                    [pscustomobject]@{ Code='recommendation.optional'; Owner='combatify'; OwnerName='Combatify'; Requirement=$recommendation; Side='client'; Message='Combatify: recommended appleskin * [client]' },
                    [pscustomobject]@{ Code='recommendation.optional'; Owner='combatify'; OwnerName='Combatify'; Requirement=$recommendation; Side='server'; Message='Combatify: recommended appleskin * [server]' }
                )
                Unknown = @(
                    [pscustomobject]@{ Code='environment.java-undeclared'; Owner='example'; OwnerName='Example'; Requirement=$java; Side='client'; Message='Example: required java >=25 [client]' },
                    [pscustomobject]@{ Code='environment.java-undeclared'; Owner='example'; OwnerName='Example'; Requirement=$java; Side='server'; Message='Example: required java >=25 [server]' },
                    [pscustomobject]@{ Code='provider.metadata-unavailable'; Owner='local'; OwnerName='Local'; Requirement=$null; Side='both'; Message='Local: Provider dependency metadata is unavailable' }
                )
            }
            $check = New-MpDoctorCheck PROJECT fail Dependencies '2 conflict(s)' -Items @(Get-MpHealthDisplayItems $health)
            $report = [pscustomobject]@{ Checks = @($check); Failures = 1; Warnings = 0 }
            $text = (Write-MpDoctorReport $report *>&1 | ForEach-Object { [string]$_ }) -join "`n"
            $text | Should Match 'First dependency conflict'
            $text | Should Match 'Second dependency conflict'
            $text | Should Match '1 optional dependency recommendation\(s\)'
            $text | Should Match 'Combatify: appleskin \[client/server\]'
            $text | Should Match '2 requirement\(s\) could not be verified'
            $text | Should Match '1 Java requirement\(s\) cannot be checked'
            $text | Should Match '1 item\(s\) do not expose provider dependency metadata'
            $text | Should Match 'Run: modpack doctor --details'
            $text | Should Match 'Fabric Loader may still reject this pack during startup'
            $text | Should Not Match 'Example: required java'
            $text | Should Not Match 'First dependency conflict; Second dependency conflict'

            $detailed = (@(Get-MpHealthDisplayItems $health -Details) | ForEach-Object Text) -join "`n"
            $detailed | Should Match 'Example: required java >=25 \[client/server\]'
            $detailed | Should Match 'Local: Provider dependency metadata is unavailable'
            $detailed | Should Not Match 'Run: modpack doctor --details'
        }

        It 'names warning checks instead of reporting an unexplained optional count' {
            $check = New-MpDoctorCheck PROJECT warn Dependencies 'Verification incomplete'
            $report = [pscustomobject]@{ Checks = @($check); Failures = 0; Warnings = 1 }
            $text = (Write-MpDoctorReport $report *>&1 | ForEach-Object { [string]$_ }) -join "`n"
            $text | Should Match 'Warning\(s\) remain in: Dependencies'
            $text | Should Not Match '1 optional warning'
            $text | Should Match 'No known required issues were found'
        }

        It 'does not prompt or apply a doctor repair plan with no file changes' {
            $health = [pscustomobject]@{ Errors=@(); Warnings=@(); Unknown=@() }
            $plan = [pscustomobject]@{ Changes=@(); Baseline=$health; Report=$health }
            Mock Resolve-MpCommandProject { [pscustomobject]@{ Id='fixture' } }
            Mock Repair-MpDoctorEnvironment {}
            Mock Invoke-MpContentOperation { [pscustomobject]@{ Changes=@(); Result=$plan } }
            Mock Confirm-MpDoctorAction { $true }
            Mock Get-MpDoctorReport { [pscustomobject]@{ Checks=@(); Failures=0; Warnings=0 } }
            Mock Get-MpProjectDoctorChecks { @() }
            Mock Write-MpDoctorReport {}

            Invoke-MpDoctor @('--project','fixture','--fix')

            Assert-MockCalled Confirm-MpDoctorAction -Times 0 -Scope It
            Assert-MockCalled Invoke-MpContentOperation -Times 1 -Scope It
        }
    }
}
