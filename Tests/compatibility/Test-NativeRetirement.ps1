param([Parameter(Mandatory)][string]$Setup,[Parameter(Mandatory)][string]$Launcher,[Parameter(Mandatory)][string]$NativeExecutable,[Parameter(Mandatory)][string]$NuExecutable,[Parameter(Mandatory)][string]$Inspector)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$setupPath=[IO.Path]::GetFullPath($Setup);$launcherPath=[IO.Path]::GetFullPath($Launcher);$corePath=[IO.Path]::GetFullPath($NativeExecutable);$inspectorPath=[IO.Path]::GetFullPath($Inspector);$nuPath=[IO.Path]::GetFullPath($NuExecutable)
$source=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'));$temporary=[IO.Path]::GetFullPath([IO.Path]::GetTempPath());$run=Join-Path $temporary ('modpacktools-native retirement-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($run)
$names=@('PATH','APPDATA','LOCALAPPDATA','MODPACKTOOLS_CONFIG_HOME','MODPACKTOOLS_STATE_HOME','MODPACKTOOLS_PROJECT','MODPACKTOOLS_TEST_READY_PATH','MODPACKTOOLS_TEST_RELEASE_PATH')
$previous=@{};foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name)}
$waitingProcess=$null;$heldPin=$null
function Invoke-Setup([string[]]$Tokens) {
    $capture=Invoke-NativeTestProcess $setupPath ($Tokens+@('--json')) -TimeoutMilliseconds 240000
    if($capture.Exit -ne 0 -or $capture.Stderr){throw "Setup failed: $($capture.Stdout) $($capture.Stderr)"}
    $capture.Stdout|ConvertFrom-Json
}
function Snapshot([string]$Root) {
    $files=[ordered]@{};foreach($file in Get-ChildItem -LiteralPath $Root -File -Recurse -Force|Sort-Object FullName){$files[[IO.Path]::GetRelativePath($Root,$file.FullName)]=(Get-FileHash -LiteralPath $file.FullName).Hash}
    $files|ConvertTo-Json -Depth 6 -Compress
}
try {
    foreach($name in @('APPDATA','LOCALAPPDATA','MODPACKTOOLS_CONFIG_HOME','MODPACKTOOLS_STATE_HOME')){[Environment]::SetEnvironmentVariable($name,(Join-Path $run $name));[void][IO.Directory]::CreateDirectory([Environment]::GetEnvironmentVariable($name))}
    $env:PATH='';Remove-Item Env:MODPACKTOOLS_PROJECT -ErrorAction SilentlyContinue
    $nuConfig=Join-Path $run 'private nu';[void][IO.Directory]::CreateDirectory($nuConfig)
    $assets=Join-Path $run 'assets';[void][IO.Directory]::CreateDirectory($assets)
    $paths=@('cli.nu','Nushell/modpack.nu','Nushell/Native-Presentation.nu','Nushell/Native-Views.nu','Nushell/Render-Native.nu','presentation/r3cli/mod.nu','presentation/r3cli/resources.json','presentation/r3cli/LICENSE','presentation/r3cli/hashes.json','presentation/r3cli/manifest.json','LICENSE','README.md','Private/lib/LICENSE.Tomlyn.txt','Private/lib/Newtonsoft.Json.LICENSE.txt')
    foreach($relative in $paths){$destination=Join-Path $assets $relative;[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination));[IO.File]::Copy((Join-Path $source $relative),$destination)}
    $firstZip=Join-Path $run 'first.zip';$first=Invoke-Setup @('pack','--core',$corePath,'--launcher',$launcherPath,'--assets',$assets,'--output',$firstZip)
    [IO.File]::AppendAllText((Join-Path $assets 'README.md'),"`nRetirement test second build.`n")
    $secondZip=Join-Path $run 'second.zip';$second=Invoke-Setup @('pack','--core',$corePath,'--launcher',$launcherPath,'--assets',$assets,'--output',$secondZip)
    $app=Join-Path $run "application it's # spaces";$state=Join-Path $run 'recovery state'
    $installed=Invoke-Setup @('install','--root',$app,'--nu',$nuPath,'--package',$firstZip,'--sha256',$first.sha256,'--state',$state)
    $firstRoot=Join-Path $app ('versions/'+$first.build_id);$firstBefore=Snapshot $firstRoot
    $env:MODPACKTOOLS_TEST_READY_PATH=Join-Path $run 'nu ready';$env:MODPACKTOOLS_TEST_RELEASE_PATH=Join-Path $run 'nu release'
    $bootstrap=($installed.nu_module -replace '\\','/');$nuScript="use r###'$bootstrap'### *; 'ready' | save `$env.MODPACKTOOLS_TEST_READY_PATH; while not (`$env.MODPACKTOOLS_TEST_RELEASE_PATH | path exists) { sleep 20ms }; print 'done'"
    $start=[Diagnostics.ProcessStartInfo]::new($nuPath);$start.UseShellExecute=$false;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    foreach($token in @('--no-config-file','--config-home',$nuConfig,'-c',$nuScript)){$start.ArgumentList.Add($token)}
    $waitingProcess=[Diagnostics.Process]::Start($start);$waitOut=$waitingProcess.StandardOutput.ReadToEndAsync();$waitErr=$waitingProcess.StandardError.ReadToEndAsync()
    $deadline=[DateTime]::UtcNow.AddSeconds(20);while(!(Test-Path -LiteralPath $env:MODPACKTOOLS_TEST_READY_PATH)){if($waitingProcess.HasExited -or [DateTime]::UtcNow -gt $deadline){throw 'Owned Nu did not import the old bootstrap'};Start-Sleep -Milliseconds 20}
    $updated=Invoke-Setup @('update','--root',$app,'--package',$secondZip,'--sha256',$second.sha256,'--state',$state)
    $deferred=Invoke-Setup @('cleanup','--root',$app,'--state',$state)
    if($deferred.items.Count -ne 1 -or $deferred.items[0].status -cne 'deferred' -or (Snapshot $firstRoot) -cne $firstBefore){throw 'A Nu session holding the old bootstrap did not defer retirement intact'}
    [IO.File]::WriteAllText($env:MODPACKTOOLS_TEST_RELEASE_PATH,'continue');if(!$waitingProcess.WaitForExit(20000)){throw 'Owned Nu did not exit'}
    if($waitingProcess.ExitCode -ne 0 -or $waitErr.GetAwaiter().GetResult() -or $waitOut.GetAwaiter().GetResult().Trim() -cne 'done'){throw 'Owned Nu exited unexpectedly'}
    $waitingProcess.Dispose();$waitingProcess=$null
    $secondRoot=Join-Path $app ('versions/'+$second.build_id);$secondBefore=Snapshot $secondRoot;$stableBefore=Snapshot (Join-Path $app 'bin')
    $heldPin=[IO.FileStream]::new((Join-Path $app ('pins/'+$first.build_id+'.lease')),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    $blocked=Invoke-Setup @('cleanup','--root',$app,'--state',$state)
    if($blocked.items[0].status -eq 'deferred'){throw 'Foreign Nushell sessions prevent deletion/resumption coverage; no foreign process was closed'}
    if($blocked.items[0].status -cne 'retained' -or (Snapshot $firstRoot) -cne $firstBefore){throw 'An admitted child pin did not preserve the retired version'}
    $heldPin.Dispose();$heldPin=$null
    $request=Join-Path $run 'cancel request.json';[IO.File]::WriteAllText($request,(@{Root=$app;StateDirectory=$state;CancelAfterFirstFile=$true;AddForeignFile=$true}|ConvertTo-Json -Compress))
    $cancelled=Invoke-NativeTestProcess $inspectorPath @('--inspect-native-retirement',$request) -TimeoutMilliseconds 120000;$cancelResult=$cancelled.Stdout|ConvertFrom-Json
    if($cancelled.Exit -ne 1 -or !$cancelResult.cancelled -or $cancelled.Stderr){throw "Retirement did not cancel after its first deletion: $($cancelled.Stdout)"}
    $journals=@(Get-ChildItem -LiteralPath (Join-Path $app 'pins') -Filter 'retire-*.json' -File);$tombstones=@(Get-ChildItem -LiteralPath (Join-Path $app 'versions') -Filter '.retiring-*' -Directory)
    if($journals.Count -ne 1 -or $tombstones.Count -ne 1 -or (Test-Path -LiteralPath $firstRoot)){throw 'Interrupted retirement is not durably resumable'}
    $partial=Snapshot $tombstones[0].FullName
    $retained=Invoke-Setup @('cleanup','--root',$app,'--state',$state)
    if($retained.items[0].status -cne 'retained' -or $retained.items[0].reason -cnotmatch 'unknown file' -or (Snapshot $tombstones[0].FullName) -cne $partial){throw 'Resume erased unexpected input or rejected it for an unrelated reason'}
    $foreign=@(Get-ChildItem -LiteralPath $tombstones[0].FullName -Filter 'foreign-retirement.txt' -File -Recurse)
    if($foreign.Count -ne 1){throw 'Foreign fixture file was not retained'}
    [IO.File]::Delete($foreign[0].FullName)
    $removed=Invoke-Setup @('cleanup','--root',$app,'--state',$state)
    if($removed.items[0].status -cne 'removed' -or (Test-Path -LiteralPath $journals[0].FullName) -or (Test-Path -LiteralPath $tombstones[0].FullName) -or (Snapshot $secondRoot) -cne $secondBefore -or (Snapshot (Join-Path $app 'bin')) -cne $stableBefore){throw "Retirement did not safely resume: $($removed|ConvertTo-Json -Compress -Depth 8)"}
    $control=Get-Content -LiteralPath (Join-Path $app 'control/current.json') -Raw|ConvertFrom-Json
    if($control.current.build_id -cne $second.build_id -or $control.retired.Count -ne 0 -or $null -ne $control.previous_build_id){throw 'Retirement did not prune only the removed reference'}
    'PASS: generation-bound Nu census, live-session deferral, active admission pin, interrupted deletion journal, foreign-file preservation and verified resumption/pruning'
} finally {
    if($heldPin){$heldPin.Dispose()}
    if($waitingProcess){if(!$waitingProcess.HasExited){$waitingProcess.Kill($true);$waitingProcess.WaitForExit()};$waitingProcess.Dispose()}
    foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name])}
    $resolved=[IO.Path]::GetFullPath($run)
    if(!$resolved.StartsWith($temporary,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^modpacktools-native retirement-[0-9a-f]{32}$'){throw 'Unsafe temporary cleanup path'}
    if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
