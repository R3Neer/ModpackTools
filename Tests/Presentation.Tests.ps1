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

        It 'keeps human output out of the object pipeline' {
            @(modpack --help 6>$null).Count | Should Be 0
            @(modpack --version 6>$null).Count | Should Be 0
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

        It 'renders dependency conflicts as separate statuses and summarizes incomplete verification' {
            $health = [pscustomobject]@{
                Errors = @(
                    [pscustomobject]@{ Message = 'First dependency conflict' },
                    [pscustomobject]@{ Message = 'Second dependency conflict' }
                )
                Warnings = @([pscustomobject]@{ Message = 'Optional recommendation detail' })
                Unknown = @(
                    [pscustomobject]@{ Message = 'Unavailable detail one' },
                    [pscustomobject]@{ Message = 'Unavailable detail two' }
                )
            }
            $check = New-MpDoctorCheck PROJECT fail Dependencies '2 conflict(s)' -Items @(Get-MpHealthDisplayItems $health)
            $report = [pscustomobject]@{ Checks = @($check); Failures = 1; Warnings = 0 }
            $text = (Write-MpDoctorReport $report *>&1 | ForEach-Object { [string]$_ }) -join "`n"
            $text | Should Match 'First dependency conflict'
            $text | Should Match 'Second dependency conflict'
            $text | Should Match '1 optional dependency recommendation\(s\)'
            $text | Should Match '2 requirement\(s\) could not be verified'
            $text | Should Not Match 'Optional recommendation detail'
            $text | Should Not Match 'Unavailable detail one'
            $text | Should Not Match 'Unavailable detail two'
            $text | Should Not Match 'First dependency conflict; Second dependency conflict'
        }
    }
}
