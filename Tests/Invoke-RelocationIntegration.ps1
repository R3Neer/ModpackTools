[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$WorkRoot,
    [Parameter(Mandatory)][string]$InstalledModule,
    [string]$NuPath = 'D:\Desarrollo\Toolchains\Nushell\bin\nu.exe'
)
$ErrorActionPreference = 'Stop'
$source = Split-Path -Parent $PSScriptRoot
$root = [IO.Path]::GetFullPath($WorkRoot)
if ($root -eq [IO.Path]::GetPathRoot($root) -or $root.StartsWith([IO.Path]::GetFullPath($InstalledModule), [StringComparison]::OrdinalIgnoreCase)) { throw 'Use a separate isolated test root.' }
$run = Join-Path $root ('relocation-' + [guid]::NewGuid().ToString('N'))
$package = Join-Path $run 'Source package with spaces/ModpackTools'
$modules = Join-Path $run 'Permanent Modules with spaces'
$target = Join-Path $modules 'ModpackTools'
$nuHome = Join-Path $run 'Private Nu configuration'
$nuConfig = Join-Path $nuHome 'config.nu'
$data = Join-Path $run 'Private tool data'
$packs = Join-Path $run 'Isolated projects'
$pack = Join-Path $packs 'Pack with spaces'
foreach ($directory in @($package,$modules,$nuHome,$data,$pack)) { [void][IO.Directory]::CreateDirectory($directory) }
# Preserve the actual installed image. Do not install the unfinished native branch
# as if it were the released application, or fetch an older release over the network.
foreach ($item in Get-ChildItem -LiteralPath $InstalledModule -Force) { Copy-Item -LiteralPath $item.FullName -Destination $package -Recurse -Force }
foreach ($relative in @('Private/Installation.ps1','Private/SelfUpdate.ps1','Private/NushellInstallation.ps1','Install-ModpackTools.ps1','install-modpack-tools.nu')) {
    Copy-Item -LiteralPath (Join-Path $source $relative) -Destination (Join-Path $package $relative) -Force
}
$version = [string](Import-PowerShellDataFile (Join-Path $package 'ModpackTools.psd1')).ModuleVersion
$prefix = "# retained café`r`n`$env.config.edit_mode = 'vi'`r`ndef retained-script [] { 'preserved' }`r`n`r`n"
$suffix = "`r`n`r`n# retained tail`r`n`$env.RELOCATION_SENTINEL = 'preserved'`r`n"
[IO.File]::WriteAllText($nuConfig, $prefix + "# >>> ModpackTools Nushell >>>`r`nuse 'old/adapter.nu' main`r`n# <<< ModpackTools Nushell <<<`r`n" + $suffix, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $nuHome 'env.nu'), '# isolated environment', [Text.UTF8Encoding]::new($false))
$environment = @{
    MODPACKTOOLS_CONFIG_HOME=$data
    MODPACKTOOLS_PROJECT=''
    MODPACKTOOLS_CORE_PATH=''
    PSModulePath=($modules + [IO.Path]::PathSeparator + (Join-Path $PSHOME 'Modules'))
    PATH=((Split-Path -Parent (Get-Process -Id $PID).Path) + [IO.Path]::PathSeparator + (Split-Path -Parent $NuPath) + [IO.Path]::PathSeparator + (Join-Path $env:SystemRoot 'System32'))
}
$calls = [Collections.Generic.List[object]]::new()
function Invoke-FixtureProcess {
    param([string]$Executable, [string[]]$Arguments)
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute=$false; $start.CreateNoWindow=$true
    $start.RedirectStandardOutput=$true; $start.RedirectStandardError=$true
    $start.StandardOutputEncoding=[Text.UTF8Encoding]::new($false); $start.StandardErrorEncoding=[Text.UTF8Encoding]::new($false)
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    foreach ($entry in $environment.GetEnumerator()) { $start.Environment[$entry.Key] = $entry.Value }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(60000)) { $process.Kill($true); $process.WaitForExit(); throw 'Isolated process timed out.' }
        $result = [pscustomobject]@{Executable=$Executable;Arguments=$Arguments;Exit=$process.ExitCode;Stdout=$stdout.GetAwaiter().GetResult();Stderr=$stderr.GetAwaiter().GetResult()}
        $calls.Add($result)
        $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $run ('process-'+$calls.Count+'.json')) -Encoding utf8NoBOM
        if ($result.Exit) { throw "Isolated process failed: $($result.Stderr) $($result.Stdout)" }
        return $result
    } finally { $process.Dispose() }
}
$pwsh = (Get-Process -Id $PID).Path
function Invoke-FixtureNu {
    param([string]$Command)
    Invoke-FixtureProcess $NuPath @('--config-home',$nuHome,'--config',$nuConfig,'--env-config',(Join-Path $nuHome 'env.nu'),'--plugin-config',(Join-Path $nuHome 'plugin.msgpackz'),'-c',$Command)
}
$install = Invoke-FixtureProcess $pwsh @('-NoProfile','-NonInteractive','-File',(Join-Path $package 'Install-ModpackTools.ps1'),'-InstallPath',$target,'-NushellConfigPath',$nuConfig,'-SkipDoctor','-NonInteractive')
$configured = [IO.File]::ReadAllText($nuConfig)
if (-not $configured.StartsWith($prefix,[StringComparison]::Ordinal) -or -not $configured.EndsWith($suffix,[StringComparison]::Ordinal)) { throw 'Nu configuration outside the managed block changed.' }
if ($configured.Contains('old/adapter.nu')) { throw 'Old Nu import survived.' }
$nuModule = (Join-Path $target 'Nushell/modpack.nu').Replace('\','/')
$humanNu = Invoke-FixtureNu 'modpack --version --ascii --color never'
if (($humanNu.Stdout+$humanNu.Stderr) -notmatch [regex]::Escape("ModpackTools $version")) { throw 'Nu human version failed.' }
$machineNu = Invoke-FixtureNu 'modpack --version --no-human | to json'
if ($machineNu.Stderr) { throw 'Nu machine version emitted stderr.' }
$nuVersion = $machineNu.Stdout | ConvertFrom-Json
if ($nuVersion.version -ne $version) { throw 'Nu structured version changed.' }
$helpNu = Invoke-FixtureNu 'modpack --help --ascii --color never'
if (($helpNu.Stdout+$helpNu.Stderr) -notmatch 'CONTENT') { throw 'Nu help failed.' }
$sentinel = Invoke-FixtureNu "{script: (retained-script), mode: `$env.config.edit_mode, sentinel: `$env.RELOCATION_SENTINEL} | to json"
$retained = $sentinel.Stdout | ConvertFrom-Json
if ($retained.script -ne 'preserved' -or $retained.mode -ne 'vi' -or $retained.sentinel -ne 'preserved') { throw 'Retained Nu configuration behavior changed.' }
foreach ($directory in @('.modpack','mods','config','resourcepacks')) { [void][IO.Directory]::CreateDirectory((Join-Path $pack $directory)) }
[IO.File]::WriteAllText((Join-Path $pack 'pack.toml'), "name = `"Relocation fixture`"`nversion = `"1`"`npack-format = `"packwiz:1.1.0`"`n[index]`nfile = `"index.toml`"`nhash-format = `"sha256`"`nhash = `"test`"`n[versions]`nminecraft = `"1.21.1`"`nfabric = `"0.16.0`"`n")
[IO.File]::WriteAllText((Join-Path $pack 'index.toml'),'hash-format = "sha256"')
[IO.File]::WriteAllText((Join-Path $pack '.modpack/project.psd1'),"@{SchemaVersion=1;Id='relocation';JavaVersion='21'}")
[IO.File]::WriteAllText((Join-Path $pack '.modpack/metadata.psd1'),"@{Categories=@{test=@{Name='Test';Order=1}};Mods=@{};ResourcePacks=@{}}")
$jarPath = Join-Path $pack 'mods/fixture.jar'
$archive = [IO.Compression.ZipFile]::Open($jarPath,'Create')
try { $writer=[IO.StreamWriter]::new($archive.CreateEntry('fabric.mod.json').Open()); try { $writer.Write('{"schemaVersion":1,"id":"fixture","version":"1"}') } finally { $writer.Dispose() } } finally { $archive.Dispose() }
$jarHash = (Get-FileHash -LiteralPath $jarPath).Hash
[IO.File]::WriteAllText((Join-Path $data 'config.psd1'), "@{Root='" + $packs.Replace("'","''") + "'}")
$load = "Import-Module '" + (Join-Path $target 'ModpackTools.psd1').Replace("'","''") + "' -Force; "
$resolved = Invoke-FixtureProcess $pwsh @('-NoProfile','-NonInteractive','-Command',$load+'(Get-Command modpack).Module.ModuleBase')
if ($resolved.Stdout.Trim() -ne $target) { throw 'PowerShell resolves a different module.' }
$humanPs = Invoke-FixtureProcess $pwsh @('-NoProfile','-NonInteractive','-Command',$load+'modpack --version --ascii --color never')
if ($humanPs.Stdout.Trim() -ne "ModpackTools $version") { throw 'PS human version failed.' }
$listPs = Invoke-FixtureProcess $pwsh @('-NoProfile','-NonInteractive','-Command',$load+'modpack -p relocation content list --json --no-human')
if (-not ($listPs.Stdout | ConvertFrom-Json).ok) { throw 'PS structured inventory failed.' }
$listNu = Invoke-FixtureNu 'modpack --project relocation content list --no-human | to json'
if ($listNu.Stderr -or -not $listNu.Stdout.Contains('fixture')) { throw 'Nu structured inventory failed.' }
$beforeMetadata = (Get-FileHash -LiteralPath (Join-Path $pack '.modpack/metadata.psd1')).Hash
$dry = Invoke-FixtureProcess $pwsh @('-NoProfile','-NonInteractive','-Command',$load+'modpack -p relocation mod set-side client fixture.jar --dry-run --json --no-human')
if (-not ($dry.Stdout | ConvertFrom-Json).ok -or (Get-FileHash -LiteralPath (Join-Path $pack '.modpack/metadata.psd1')).Hash -ne $beforeMetadata) { throw 'Dry run changed the fixture or failed.' }
$apply = Invoke-FixtureNu 'modpack --project relocation mod set-side client fixture.jar --no-human | to json'
if ($apply.Stderr) { throw 'Complete Nu fixture operation failed.' }
$metadata = Import-PowerShellDataFile -LiteralPath (Join-Path $pack '.modpack/metadata.psd1')
if ($metadata.Mods['local:mods/fixture.jar'].Side -ne 'client') { throw 'Fixture operation was not applied.' }
if ((Get-FileHash -LiteralPath $jarPath).Hash -ne $jarHash) { throw 'Operation changed unrelated JAR bytes.' }
$afterNu = Invoke-FixtureNu 'modpack --project relocation content list --no-human | to json'
if (-not $afterNu.Stdout.Contains('client')) { throw 'Fresh Nu inventory did not observe the applied side.' }
$state = Get-Content -LiteralPath (Join-Path $target 'installation.json') -Raw | ConvertFrom-Json
if ($state.InstallPath -ne $target -or $state.NushellConfigPath -ne $nuConfig) { throw 'Installation did not retain exact destinations.' }
# A fresh installer re-run through the native Nu entry point uses the same explicit
# destination. It must preserve the marked block and all its surrounding bytes.
$nuInstaller = Join-Path $package 'install-modpack-tools.nu'
$reinstall = Invoke-FixtureProcess $NuPath @('--no-config-file',$nuInstaller,'--force','--non-interactive','--skip-doctor','--install-path',$target,'--nushell-config-path',$nuConfig)
if ([IO.File]::ReadAllText($nuConfig) -cne $configured) { throw 'Repeated installation rewrote unrelated Nu text.' }
$result = [pscustomobject]@{
    Run=$run;InstalledModule=$target;BaseVersion=$version;CurrentInstalledImagePreserved=$true
    NuVersion=$nuVersion.version;FreshShells=$true;HumanAndStructured=$true;ModuleResolution=$true
    ModalScriptsAndSurroundingBytesPreserved=$true;FixtureRead=$true;DryRunUnchanged=$true
    CompleteNuOperation=$true;UnrelatedJarUnchanged=$true;NuInstaller=$true;ExactDestinationsRetained=$true
    OldSourceAbsentFromPath=$true;OldInstalledModuleAbsentFromPSModulePath=$true;ProcessCount=$calls.Count
    RealInstallationChanged=$false;RealConfigurationChanged=$false;NetworkUsed=$false
}
$result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $run 'result.json') -Encoding utf8NoBOM
$result | ConvertTo-Json -Depth 6
