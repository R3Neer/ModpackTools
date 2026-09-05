Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'ModpackTools.psd1') -Force
InModuleScope ModpackTools {
    function New-SelfRelease {
        param([string]$Version = '99.0.0')
        [pscustomobject]@{tag_name="v$Version"; draft=$false; prerelease=$false; html_url="https://github.com/R3Neer/ModpackTools/releases/tag/v$Version"; assets=@([pscustomobject]@{name="ModpackTools-$Version.zip"; browser_download_url="https://github.com/R3Neer/ModpackTools/releases/download/v$Version/ModpackTools-$Version.zip"; digest=('sha256:' + ('a' * 64))})}
    }
    Describe 'Self-update checks and presentation' {
        BeforeEach {
            $script:ConfigHomeOverride = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
            $script:SelfRelease = New-SelfRelease
            Mock Invoke-RestMethod { $script:SelfRelease }
            Mock Resolve-MpCommandProject { throw 'No project access allowed' }
        }
        It 'keeps offline version local and exactly one line without reading cache' {
            @(modpack --version --offline 6>&1).Count | Should Be 1
            (modpack --version --offline 6>&1 | Out-String).Trim() | Should Be "ModpackTools $script:ModuleVersion"
            Assert-MockCalled Invoke-RestMethod -Times 0 -Scope It
            Test-Path (Join-Path $script:ConfigHomeOverride 'self-update.json') | Should Be $false
        }
        It 'announces a newer stable version and caches the response for 24 hours' {
            $text = modpack --version --ascii --colour never 6>&1 | Out-String
            $text | Should Match 'Update available: 99.0.0'
            $text | Should Not Match '\x1b'
            modpack --version 6>$null
            Assert-MockCalled Invoke-RestMethod -Times 1 -Scope It -ParameterFilter { $TimeoutSec -eq 3 }
            Assert-MockCalled Resolve-MpCommandProject -Times 0 -Scope It
        }
        It 'refreshes stale cache and forced checks' {
            [void](Get-MpLatestRelease)
            $path = Join-Path $script:ConfigHomeOverride 'self-update.json'
            $cache = Get-Content $path -Raw | ConvertFrom-Json
            $cache.CheckedUtc = [datetime]::UtcNow.AddHours(-25).ToString('o')
            Write-Utf8TextFileAtomic $path ($cache | ConvertTo-Json -Depth 20)
            [void](Get-MpLatestRelease)
            [void](Get-MpLatestRelease -Refresh)
            Assert-MockCalled Invoke-RestMethod -Times 3 -Scope It
        }
        It 'recovers from corrupt cache' {
            [void][IO.Directory]::CreateDirectory($script:ConfigHomeOverride)
            [IO.File]::WriteAllText((Join-Path $script:ConfigHomeOverride 'self-update.json'), '{')
            (Get-MpLatestRelease).Version | Should Be '99.0.0'
        }
        It 'does not break version or claim current when GitHub is unavailable' {
            Mock Invoke-RestMethod { throw 'offline' }
            $text = modpack --version 6>&1 | Out-String
            $text.Trim() | Should Be "ModpackTools $script:ModuleVersion"
            { modpack --update --check 6>$null } | Should Throw 'Could not check'
        }
        It 'does not announce equal or older releases' {
            $script:SelfRelease = New-SelfRelease $script:ModuleVersion
            @(modpack --version 6>&1).Count | Should Be 1
            $script:SelfRelease = New-SelfRelease '1.0.0'
            (modpack --update --check 6>&1 | Out-String) | Should Match 'No newer stable release'
        }
        It 'rejects prereleases draft releases foreign URLs and invalid tags' {
            $r = New-SelfRelease; $r.prerelease = $true
            { ConvertFrom-MpRelease $r } | Should Throw 'stable'
            $r = New-SelfRelease; $r.draft = $true
            { ConvertFrom-MpRelease $r } | Should Throw 'stable'
            $r = New-SelfRelease; $r.html_url = 'https://example.invalid/release'
            { ConvertFrom-MpRelease $r } | Should Throw 'location'
            $r = New-SelfRelease; $r.tag_name = 'v9.0.0-preview'
            { ConvertFrom-MpRelease $r } | Should Throw 'stable'
        }
        It 'checks without installation and renders help without network' {
            Mock Install-MpSelfUpdate { throw 'No installation allowed' }
            Mock Get-MpSelfUpdateTarget { throw 'No install lookup allowed' }
            @(modpack --update --check 6>$null).Count | Should Be 0
            $text = modpack --update --help --ascii --colour never 6>&1 | Out-String
            $text | Should Match 'ARGUMENTS AND OPTIONS'
            $text | Should Match '--check'
            $text | Should Match '--yes'
            Assert-MockCalled Invoke-RestMethod -Times 1 -Scope It
        }
        It 'rejects unsupported options and combinations' {
            { modpack --version --yes } | Should Throw 'not recognized'
            { modpack --version foo } | Should Throw 'positional'
            { modpack --update --offline } | Should Throw 'not recognized'
            { modpack --update --check --yes } | Should Throw 'Invalid'
            { modpack --update sodium } | Should Throw 'Invalid'
        }
    }
    Describe 'Self-update application policy' {
        BeforeEach {
            $script:ConfigHomeOverride = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
            $script:SelfRelease = ConvertFrom-MpRelease (New-SelfRelease)
            $installed = Join-Path $TestDrive 'installed'
            [void][IO.Directory]::CreateDirectory($installed)
            [IO.File]::WriteAllText((Join-Path $installed 'ModpackTools.psd1'), "@{ModuleVersion='1.0.0'}")
            $script:SelfTarget = [pscustomobject]@{Path=$installed; Version=[version]'1.0.0'; Others=@()}
            Mock Get-MpLatestRelease { $script:SelfRelease }
            Mock Get-MpSelfUpdateTarget { $script:SelfTarget }
            Mock Install-MpSelfUpdate { }
        }
        It 'cancels without downloading when confirmation is declined' {
            Mock Confirm-MpDoctorAction { $false }
            (modpack --update 6>&1 | Out-String) | Should Match 'cancelled'
            Assert-MockCalled Install-MpSelfUpdate -Times 0 -Scope It
            Assert-MockCalled Confirm-MpDoctorAction -Times 1 -Scope It -ParameterFilter { -not $Default }
        }
        It 'applies yes to the installed target even when running from a newer checkout' {
            Mock Confirm-MpDoctorAction { throw 'No prompt allowed' }
            @(modpack --update --yes 6>$null).Count | Should Be 0
            Assert-MockCalled Install-MpSelfUpdate -Times 1 -Scope It -ParameterFilter { $Target.Path -eq $script:SelfTarget.Path -and $Target.ManifestHash }
        }
        It 'does not reinstall or downgrade an up-to-date target' {
            $script:SelfTarget.Version = [version]'100.0.0'
            modpack --update --yes 6>$null
            Assert-MockCalled Install-MpSelfUpdate -Times 0 -Scope It
        }
    }
    Describe 'Update package boundary' {
        BeforeEach {
            $script:ConfigHomeOverride = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
            Mock Invoke-WebRequest { [IO.File]::WriteAllText($OutFile, 'corrupt download') }
            Mock Invoke-MpInstallProcess { throw 'Must not execute an installer' }
        }
        It 'rejects a missing asset hash foreign download URL and duplicate assets' {
            $raw = New-SelfRelease
            $raw.assets[0].digest = ''
            { Install-MpSelfUpdate (ConvertFrom-MpRelease $raw) $null } | Should Throw 'invalid'
            $raw = New-SelfRelease; $raw.assets[0].browser_download_url = 'https://example.invalid/malicious.zip'
            { Install-MpSelfUpdate (ConvertFrom-MpRelease $raw) $null } | Should Throw 'invalid'
            $raw = New-SelfRelease; $raw.assets += $raw.assets[0]
            { Install-MpSelfUpdate (ConvertFrom-MpRelease $raw) $null } | Should Throw 'unique'
            Assert-MockCalled Invoke-WebRequest -Times 0 -Scope It
        }
        It 'rejects a bad download hash before executing anything' {
            { Install-MpSelfUpdate (ConvertFrom-MpRelease (New-SelfRelease)) $null } | Should Throw 'SHA256'
            Assert-MockCalled Invoke-MpInstallProcess -Times 0 -Scope It
        }
        It 'rejects traversal and duplicate ZIP paths before extraction' {
            foreach ($entries in @(@('../outside.ps1'), @('x.ps1','X.ps1'))) {
                $path = Join-Path $TestDrive ([guid]::NewGuid().ToString('N') + '.zip')
                $zip = [IO.Compression.ZipFile]::Open($path, 'Create')
                try { foreach ($entry in $entries) { [void]$zip.CreateEntry($entry) } } finally { $zip.Dispose() }
                { Expand-MpUpdatePackage $path (Join-Path $TestDrive 'expanded') } | Should Throw
            }
            Test-Path (Join-Path $TestDrive 'outside.ps1') | Should Be $false
        }
    }
    Describe 'Installation target selection' {
        BeforeEach {
            $script:RootA = Join-Path $TestDrive 'modules-a'; $script:RootB = Join-Path $TestDrive 'modules-b'
            foreach ($root in @($script:RootA, $script:RootB)) {
                [void][IO.Directory]::CreateDirectory((Join-Path $root 'ModpackTools'))
                [IO.File]::WriteAllText((Join-Path $root 'ModpackTools/ModpackTools.psd1'), "@{ModuleVersion='1.0.0'}")
            }
            Mock Get-MpUserModuleRoots { @($script:RootA, $script:RootB) }
            Mock Get-Module { @([pscustomobject]@{ModuleBase=(Join-Path $script:RootA 'ModpackTools')}) } -ParameterFilter { $ListAvailable }
        }
        It 'selects the first installed user path and reports other copies' {
            $target = Get-MpSelfUpdateTarget
            $target.Path | Should Be (Join-Path $script:RootA 'ModpackTools')
            $target.Others | Should Be @((Join-Path $script:RootB 'ModpackTools'))
        }
        It 'refuses a shadowing machine or versioned installation' {
            Mock Get-Module { @([pscustomobject]@{ModuleBase='C:\machine\ModpackTools'}) } -ParameterFilter { $ListAvailable }
            { Get-MpSelfUpdateTarget } | Should Throw 'precedence'
        }
        It 'rejects installation outside allowed roots' {
            { Resolve-MpInstallDestination (Join-Path $TestDrive 'unrelated') } | Should Throw 'user PSModulePath'
        }
    }
}
