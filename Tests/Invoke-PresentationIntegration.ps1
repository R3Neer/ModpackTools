[CmdletBinding()]
param([string]$WorkRoot = [IO.Path]::GetTempPath())
$ErrorActionPreference = 'Stop'
$source = Split-Path -Parent $PSScriptRoot
$run = Join-Path ([IO.Path]::GetFullPath($WorkRoot)) ('presentation-' + [guid]::NewGuid().ToString('N'))
$modules = Join-Path $run 'Modules'
$installed = Join-Path $modules 'ModpackTools'
[void][IO.Directory]::CreateDirectory($run)
$pwsh = (Get-Process -Id $PID).Path

function Invoke-Child {
    param([string[]]$ChildArguments)
    $start = [Diagnostics.ProcessStartInfo]::new($pwsh)
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $start.Environment['MP_PRESENTATION_MODULES'] = $modules
    foreach ($argument in $ChildArguments) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    try { return [pscustomobject]@{ ExitCode=$process.ExitCode; Output=$stdout.Result; Error=$stderr.Result } }
    finally { $process.Dispose() }
}

$installerProbe = Join-Path $run 'install.ps1'
[IO.File]::WriteAllText($installerProbe, @'
param([string]$InstallerPath, [switch]$Force)
$ErrorActionPreference = 'Stop'
$env:PSModulePath = $env:MP_PRESENTATION_MODULES + [IO.Path]::PathSeparator + (Join-Path $PSHOME 'Modules')
& $InstallerPath -Force:$Force -NonInteractive -SkipDoctor
'@)
$first = Invoke-Child @('-NoProfile','-File',$installerProbe,'-InstallerPath',(Join-Path $source 'Install-ModpackTools.ps1'))
if ($first.ExitCode) { throw "Isolated installation failed: $($first.Error)" }

# Simulate an existing, customised legacy complete theme and preserve its bytes.
$themePath = Join-Path $installed 'theme.toml'
$custom = "[colors]`r`nclient = `"#748FFC`"`r`nhost = `"#BE70FF`"`r`nlocal = `"#FF91CD`"`r`nheading = `"#123456`"`r`n"
[IO.File]::WriteAllText($themePath,$custom)
$themeHash = (Get-FileHash $themePath).Hash
$upgrade = Invoke-Child @('-NoProfile','-File',$installerProbe,'-InstallerPath',(Join-Path $source 'Install-ModpackTools.ps1'),'-Force')
if ($upgrade.ExitCode -or (Get-FileHash $themePath).Hash -ne $themeHash) { throw "Upgrade lost the custom theme: $($upgrade.Error)" }

$probe = Join-Path $run 'probe.ps1'
[IO.File]::WriteAllText($probe, @'
$ErrorActionPreference = 'Stop'
$env:PSModulePath = $env:MP_PRESENTATION_MODULES + [IO.Path]::PathSeparator + (Join-Path $PSHOME 'Modules')
Import-Module ModpackTools -Force
$module = Get-Module ModpackTools
if (@($module.ExportedFunctions.Keys) -join ',' -ne 'modpack') { throw 'Unexpected public exports' }
& $module {
    [void](Test-MpR3Package $script:ModuleRoot)
    if ((Get-MpConsole).Theme.heading -ne '#123456') { throw 'Custom theme override was not loaded' }
    $version = @(modpack --version 6>&1)
    if ($version.Count -ne 1) { throw 'Version output is not compact' }
    foreach ($name in (Get-MpCommandCatalog).Keys) { modpack $name --help --ascii --colour never 6>$null }
    $data = @(modpack --help 6>$null)
    if ($data.Count) { throw 'Presentation polluted the pipeline' }
    $captured = @(& { try { modpack wrong-command } catch { $_ } } 6>&1)
    if ($captured.Count -ne 1 -or $captured[0].FullyQualifiedErrorId -notlike 'ModpackTools.Command.Unknown*') { throw 'Error contract changed' }
    $plain = @(modpack --help --colour never 6>&1 | ForEach-Object { [string]$_ }) -join "`n"
    if ($plain.Contains([string][char]27)) { throw 'Plain rendering contains ANSI' }
}
'@)
$smoke = Invoke-Child @('-NoProfile','-File',$probe)
if ($smoke.ExitCode) { throw "Installed-module checks failed: $($smoke.Error)" }

$before = @{}
Get-ChildItem $installed -File -Recurse | ForEach-Object { $before[[IO.Path]::GetRelativePath($installed,$_.FullName)] = (Get-FileHash $_.FullName).Hash }
foreach ($file in Get-ChildItem (Join-Path $source 'Private') -File -Recurse) {
    $relative = [IO.Path]::GetRelativePath($source,$file.FullName)
    if ($before[$relative] -ne (Get-FileHash $file.FullName).Hash) { throw "Installed code differs from source: $relative" }
}
$brokenSource = Join-Path $run 'broken-source'
[void][IO.Directory]::CreateDirectory($brokenSource)
foreach ($name in @('docs','Private','Public','ModpackTools.psd1','ModpackTools.psm1','README.md','LICENSE','theme.toml','dependencies.psd1','Install-ModpackTools.ps1')) {
    Copy-Item -LiteralPath (Join-Path $source $name) -Destination $brokenSource -Recurse
}
[IO.File]::AppendAllText((Join-Path $brokenSource 'Private/vendor/R3CLI/R3CLI.psm1'), 'tamper')
$failed = Invoke-Child @('-NoProfile','-File',$installerProbe,'-InstallerPath',(Join-Path $brokenSource 'Install-ModpackTools.ps1'),'-Force')
if (-not $failed.ExitCode -or $failed.Error -notmatch 'verification failed') { throw 'Corrupt package was not rejected' }
$after = @(Get-ChildItem $installed -File -Recurse)
if ($after.Count -ne $before.Count) { throw 'Failed upgrade changed installed files' }
foreach ($file in $after) {
    if ($before[[IO.Path]::GetRelativePath($installed,$file.FullName)] -ne (Get-FileHash $file.FullName).Hash) { throw 'Failed upgrade changed installed bytes' }
}
$redirectProbe = Join-Path $run 'redirect.ps1'
[IO.File]::WriteAllText($redirectProbe, @'
$env:PSModulePath = $env:MP_PRESENTATION_MODULES + [IO.Path]::PathSeparator + (Join-Path $PSHOME 'Modules')
Import-Module ModpackTools -Force
modpack --help
'@)
$redirected = Invoke-Child @('-NoProfile','-File',$redirectProbe)
if ($redirected.ExitCode -or $redirected.Output.Contains([string][char]27)) { throw 'Redirected auto output is not plain' }
$result = [pscustomobject]@{ InstalledModule=(Join-Path $installed 'ModpackTools.psd1'); SourceHashesMatch=$true; CustomThemePreserved=$true; FailedUpgradePreserved=$true; FreshProcessChecks=$true; RedirectedAutoPlain=$true }
$result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'result.json')
$result
