param([Parameter(Mandatory)][string]$PackageDirectory,[Parameter(Mandatory)][string]$NuExecutable,[Parameter(Mandatory)][string]$FixtureRoot,[int]$Samples=31)
$ErrorActionPreference='Stop'
if($Samples -lt 31){throw 'Public acceptance requires 31 measured samples'}
$fixedPackage=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../../../assessment/native-final/package'))
if(-not [StringComparer]::OrdinalIgnoreCase.Equals([IO.Path]::GetFullPath($PackageDirectory),$fixedPackage)){throw 'This environment-specific Nu import is bound to assessment/native-final/package; choose that same package'}
$base=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../../../assessment/benchmarks'))
$run=Join-Path $base ('public-native-'+[guid]::NewGuid().ToString('N'));[void][IO.Directory]::CreateDirectory($run)
function Manifest([string]$Root) {@(Get-ChildItem -LiteralPath $Root -File -Recurse -Force|Sort-Object FullName|ForEach-Object{[ordered]@{path=[IO.Path]::GetRelativePath($Root,$_.FullName);length=$_.Length;attributes=[int]$_.Attributes;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}})}
$packageBefore=Manifest $fixedPackage
$before=@{};$sourceBefore=@{}
foreach($case in @(@{Name='local';Source='20-local'},@{Name='managed';Source='100-managed-verified'})){
    $source=Join-Path $FixtureRoot $case.Source;$destination=Join-Path $run $case.Name
    $sourceBefore[$case.Name]=Manifest (Join-Path $source 'pack')
    [void][IO.Directory]::CreateDirectory((Join-Path $destination 'projects'))
    Copy-Item -LiteralPath (Join-Path $source 'pack') -Destination (Join-Path $destination 'projects/benchmark') -Recurse
    # The earlier internal health fixture omitted this required public-project file.
    [IO.File]::WriteAllText((Join-Path $destination 'projects/benchmark/.modpack/metadata.psd1'),'@{SchemaVersion=1}',[Text.UTF8Encoding]::new($false))
    foreach($engine in @('legacy','native')){
        Copy-Item -LiteralPath (Join-Path $source 'config') -Destination (Join-Path $destination $engine) -Recurse
        # Renew only copied metadata mtimes: the immutable source fixture is older
        # than the provider cache's 24h TTL. Artifact bytes and SHA512 stay intact.
        Get-ChildItem -LiteralPath (Join-Path $destination "$engine/metadata") -File | ForEach-Object {$_.LastWriteTimeUtc=[DateTime]::UtcNow}
        $root=(Join-Path $destination 'projects').Replace("'","''")
        [IO.File]::WriteAllText((Join-Path $destination "$engine/config.psd1"),"@{Root='$root'}",[Text.UTF8Encoding]::new($false))
    }
    $before[$case.Name]=Manifest (Join-Path $destination 'projects')
}
$core=Join-Path ([IO.Path]::GetFullPath($PackageDirectory)) 'modpacktools.exe'
[IO.File]::WriteAllText((Join-Path $run 'native-executable.txt'),$core)
foreach($name in @('Roaming','Local','temporary')){[void][IO.Directory]::CreateDirectory((Join-Path $run $name))}
$legacy='D:\Juegos\Minecraft\Herramientas\ModpackTools'
$anchors=@(foreach($name in @('ModpackTools.psd1','Nushell/modpack.nu','Nushell/Invoke-ModpackBridge.ps1','Private/Inventory.ps1')){[ordered]@{path=$name;sha256=(Get-FileHash -LiteralPath (Join-Path $legacy $name)).Hash}})
function Capture([string]$Executable,[string[]]$Arguments,[string]$Config) {
    $info=[Diagnostics.ProcessStartInfo]::new($Executable);$info.UseShellExecute=$false;$info.RedirectStandardOutput=$true;$info.RedirectStandardError=$true
    $info.Environment['MODPACKTOOLS_CONFIG_HOME']=$Config;$info.Environment['MODPACKTOOLS_PROJECT']='benchmark';$info.Environment['MODPACKTOOLS_STATE_HOME']=Join-Path $run 'preflight-state'
    $info.Environment['APPDATA']=Join-Path $run 'Roaming';$info.Environment['LOCALAPPDATA']=Join-Path $run 'Local'
    foreach($argument in $Arguments){$info.ArgumentList.Add($argument)}
    $child=[Diagnostics.Process]::Start($info)
    try{$stdout=$child.StandardOutput.ReadToEndAsync();$stderr=$child.StandardError.ReadToEndAsync();if(-not $child.WaitForExit(60000)){$child.Kill($true);$child.WaitForExit();throw 'Preflight timeout'};$text=$stdout.GetAwaiter().GetResult();$err=$stderr.GetAwaiter().GetResult();if($child.ExitCode -ne 0 -or $err){throw "Preflight failed: $text $err"};$text}finally{$child.Dispose()}
}
$preflight=@()
foreach($case in @('local','managed')){foreach($engine in @('legacy','native')){
    $configuration=Join-Path $run "$case/$engine"
    $text=if($engine -ceq 'native'){Capture $core @('content','list','--json','--no-human') $configuration}else{
        $literal=(Join-Path $legacy 'ModpackTools.psd1').Replace("'","''")
        $code="Import-Module '$literal' -Force; modpack content list --json --no-human"
        Capture 'D:\Program Files\PowerShell\7\pwsh.exe' @('-NoLogo','-NoProfile','-NonInteractive','-EncodedCommand',[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($code))) $configuration
    }
    [IO.File]::WriteAllText((Join-Path $run "preflight-$case-$engine.json"),$text,[Text.UTF8Encoding]::new($false))
    $wire=$text|ConvertFrom-Json
    if(-not $wire.ok -or @($wire.data.health.errors).Count -or @($wire.data.health.unknown).Count -or @($wire.data.health.warnings).Count -or $wire.data.summary.mods -ne $(if($case -ceq 'local'){20}else{100})){throw "Fixture health/coverage unexpected before timing: $case / $engine"}
    $preflight+=[ordered]@{workload=$case;engine=$engine;mods=$wire.data.summary.mods;errors=0;unknown=0;warnings=0}
}}
$start=[Diagnostics.ProcessStartInfo]::new([IO.Path]::GetFullPath($NuExecutable));$start.UseShellExecute=$false;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
foreach($argument in @('--no-config-file',(Join-Path $PSScriptRoot 'Measure-NativePublic.nu'),$run,'--samples',[string]$Samples)){$start.ArgumentList.Add($argument)}
$start.Environment['APPDATA']=Join-Path $run 'Roaming';$start.Environment['LOCALAPPDATA']=Join-Path $run 'Local';$start.Environment['TEMP']=Join-Path $run 'temporary';$start.Environment['TMP']=$start.Environment['TEMP']
$process=[Diagnostics.Process]::Start($start)
try {
    $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
    if(-not $process.WaitForExit(900000)){$process.Kill($true);$process.WaitForExit();throw 'Benchmark exceeded 15 minutes'}
    $out=$stdout.GetAwaiter().GetResult();$err=$stderr.GetAwaiter().GetResult()
    [IO.File]::WriteAllText((Join-Path $run 'stdout.json'),$out,[Text.UTF8Encoding]::new($false));[IO.File]::WriteAllText((Join-Path $run 'human-stderr.txt'),$err,[Text.UTF8Encoding]::new($false))
    if($process.ExitCode){throw "Benchmark failed; preserve $run. $err"}
}finally{$process.Dispose()}
$rows=@($out|ConvertFrom-Json)
$fresh=@()
$application=Join-Path $run 'installed benchmark app'
$archive=Join-Path (Split-Path $fixedPackage -Parent) 'ModpackTools-Native-win-x64-4.0.2.zip'
$install=Capture (Join-Path $fixedPackage 'modpacktools-setup.exe') @('install','--root',$application,'--nu',[IO.Path]::GetFullPath($NuExecutable),'--package',$archive,'--sha256',(Get-FileHash -LiteralPath $archive).Hash,'--json') (Join-Path $run 'local/native')
[IO.File]::WriteAllText((Join-Path $run 'benchmark-install.json'),$install,[Text.UTF8Encoding]::new($false))
for($sample=0;$sample -le $Samples;$sample++){
    foreach($entry in $(if($sample%2){@('launcher','core')}else{@('core','launcher')})){
        $executable=if($entry -ceq 'core'){$core}else{Join-Path $application 'bin/modpack.exe'}
        $timer=[Diagnostics.Stopwatch]::StartNew();$text=Capture $executable @('--version','--json','--no-human') (Join-Path $run 'local/native');$timer.Stop()
        if(($text|ConvertFrom-Json).data.version.version -cne '4.0.2'){throw 'Fresh-process version mismatch'}
        $fresh+=[ordered]@{entry=$entry;sample=$sample;milliseconds=$timer.Elapsed.TotalMilliseconds}
    }
}
[IO.File]::WriteAllText((Join-Path $run 'fresh-process-samples.json'),($fresh|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
$summary=@(foreach($workload in @($rows.workload|Sort-Object -Unique)){foreach($engine in @('legacy','native')){
    $values=@($rows|Where-Object{$_.workload -ceq $workload -and $_.engine -ceq $engine -and $_.sample -gt 0}|ForEach-Object milliseconds|Sort-Object)
    if($values.Count -ne $Samples){throw 'Timing sample count mismatch'}
    [ordered]@{workload=$workload;engine=$engine;n=$values.Count;median_ms=$values[[int][Math]::Floor($values.Count/2)];p95_ms=$values[[int][Math]::Ceiling($values.Count*0.95)-1]}
}})
foreach($case in @(@{Name='local';Source='20-local'},@{Name='managed';Source='100-managed-verified'})){
    if(($before[$case.Name]|ConvertTo-Json -Depth 8 -Compress) -cne ((Manifest (Join-Path $run "$($case.Name)/projects"))|ConvertTo-Json -Depth 8 -Compress)){throw 'Benchmark project was changed'}
    if(($sourceBefore[$case.Name]|ConvertTo-Json -Depth 8 -Compress) -cne ((Manifest (Join-Path $FixtureRoot "$($case.Source)/pack"))|ConvertTo-Json -Depth 8 -Compress)){throw 'Original fixture was changed'}
}
foreach($anchor in $anchors){if((Get-FileHash -LiteralPath (Join-Path $legacy $anchor.path)).Hash -cne $anchor.sha256){throw 'Legacy source changed'}}
if(($packageBefore|ConvertTo-Json -Depth 8 -Compress) -cne ((Manifest $fixedPackage)|ConvertTo-Json -Depth 8 -Compress)){throw 'Native package changed'}
$report=[ordered]@{timestamp=[DateTimeOffset]::Now.ToString('O');run=$run;samples=$rows;summary=$summary;fresh_process_samples=$fresh;project_manifests=$before;package_manifest=$packageBefore;preflight=$preflight;legacy_anchors=$anchors;legacy_revision=(& git -C $legacy rev-parse HEAD);native_sha256=(Get-FileHash -LiteralPath $core).Hash;nu_version=(& $NuExecutable --version);ps_version=[string]$PSVersionTable.PSVersion;os=[Environment]::OSVersion.VersionString;scope='Alternating engines within one already-running Nu process; full public command including child startup, request/result validation, cache IO and human rendering where named. Separate fresh core/stable installed launcher includes PS harness setup and full admission/hashing/delegation; no startup medians subtracted.';conditions='31 measured samples plus retained first observation. OS caches warmed by fixture copy/hash; no cache eviction or disk-cold claim. Copied metadata mtimes renewed before health preflight. Private per-engine caches; no network verification requested. Shared host, reviewers idle during timing; no SDK builds. Human null result comparison is not text/render equivalence; functional presentation acceptance is separate. Process-tree memory was not measured.';unchanged_projects=$true}
[IO.File]::WriteAllText((Join-Path $run 'results.json'),($report|ConvertTo-Json -Depth 100),[Text.UTF8Encoding]::new($false))
$summary|ConvertTo-Json -Depth 5
"Evidence: $run"
