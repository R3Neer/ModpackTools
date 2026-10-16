param([Parameter(Mandatory)][string]$Inspector,[Parameter(Mandatory)][string]$NativeExecutable,[Parameter(Mandatory)][string]$NuExecutable)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$inspectorPath=[IO.Path]::GetFullPath($Inspector)
$temporary=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$run=Join-Path $temporary ('modpacktools-install control-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($run)
$nuProcess=$null
function Invoke-Control($Request) {
    $path=Join-Path $run ('request-'+[guid]::NewGuid().ToString('N')+'.json');[IO.File]::WriteAllText($path,($Request|ConvertTo-Json -Depth 12 -Compress))
    $capture=Invoke-NativeTestProcess $inspectorPath @('--inspect-native-package',$path) -TimeoutMilliseconds 120000
    if($capture.Stderr){throw "Unexpected inspector stderr: $($capture.Stderr)"}
    [pscustomobject]@{Exit=$capture.Exit;Result=($capture.Stdout|ConvertFrom-Json)}
}
function Snapshot([string]$Root) {
    $files=[ordered]@{};foreach($file in Get-ChildItem -LiteralPath $Root -File -Recurse -Force|Sort-Object FullName){$files[[IO.Path]::GetRelativePath($Root,$file.FullName)]=(Get-FileHash -LiteralPath $file.FullName).Hash}
    $files|ConvertTo-Json -Depth 6 -Compress
}
function Nu-Literal([string]$Path) {
    $value=$Path.Replace('\','/');$hashes='#';while($value.Contains("'"+$hashes)){$hashes+='#'}
    'r'+$hashes+"'"+$value+"'"+$hashes
}
try {
    $fixtures=@{}
    foreach($marker in @('first','second')){
        $archive=Join-Path $run ($marker+'.zip');$created=Invoke-Control @{CreateFixture=$true;ArchivePath=$archive;Executable=[IO.Path]::GetFullPath($NativeExecutable);FixtureMarker=$marker}
        if($created.Exit -ne 0){throw 'Control fixture creation failed'};$fixtures[$marker]=$created.Result
    }
    $app=Join-Path $run "application it's # spaces";$control=Join-Path $app 'control';$state=Join-Path $run 'state';$stages=Join-Path $run 'prepared';$store=Join-Path $app 'versions'
    $request=@{ArchivePath=$fixtures.first.ArchivePath;ArchiveSha256=$fixtures.first.ArchiveSha256;StagingParent=$stages;PublishParent=$store;ControlRoot=$control;StateDirectory=$state}
    $first=Invoke-Control $request
    if($first.Exit -ne 0 -or !$first.Result.ControlApplied -or $first.Result.RetiredCount -ne 0 -or $first.Result.PreviousBuildId){throw "Initial control switch failed: $($first.Result|ConvertTo-Json -Compress)"}
    $bootstrap=Join-Path $control 'Nushell/modpack.nu';$ready=Join-Path $run 'nu ready';$release=Join-Path $run 'nu release'
    $nuScript='use '+(Nu-Literal $bootstrap)+' *; "ready" | save '+(Nu-Literal $ready)+'; while not ('+(Nu-Literal $release)+' | path exists) { sleep 20ms }; {marker: (package-marker), core: $env.MODPACKTOOLS_CORE_PATH} | to json --raw'
    $start=[Diagnostics.ProcessStartInfo]::new([IO.Path]::GetFullPath($NuExecutable));$start.UseShellExecute=$false;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    foreach($argument in @('--no-config-file','-c',$nuScript)){[void]$start.ArgumentList.Add($argument)}
    $nuProcess=[Diagnostics.Process]::Start($start);$nuOut=$nuProcess.StandardOutput.ReadToEndAsync();$nuErr=$nuProcess.StandardError.ReadToEndAsync()
    $deadline=[DateTime]::UtcNow.AddSeconds(15);while(!(Test-Path -LiteralPath $ready)) {if($nuProcess.HasExited -or [DateTime]::UtcNow -gt $deadline){throw "Owned Nu did not load bootstrap within the bounded wait"};Start-Sleep -Milliseconds 20}
    $before=Snapshot $control;$request.ExpectedGeneration=$first.Result.Generation
    $repeat=Invoke-Control $request
    if($repeat.Exit -ne 0 -or $repeat.Result.ControlApplied -or $repeat.Result.Generation -cne $first.Result.Generation -or (Snapshot $control) -cne $before){throw 'Same-version control was rewritten'}
    $request.ArchivePath=$fixtures.second.ArchivePath;$request.ArchiveSha256=$fixtures.second.ArchiveSha256
    $fault=$request.Clone();$fault.FailAfterControlFile=$true;$failed=Invoke-Control $fault
    if($failed.Exit -ne 1 -or $failed.Result.Id -ne 'ModpackTools.Transaction.IoFailed' -or (Snapshot $control) -cne $before){throw "Partial switch did not restore previous control: $($failed.Result|ConvertTo-Json -Compress)"}
    $second=Invoke-Control $request
    if($second.Exit -ne 0 -or !$second.Result.ControlApplied -or $second.Result.PreviousBuildId -cne $fixtures.first.BuildId -or $second.Result.RetiredCount -ne 1){throw 'Second control switch did not preserve previous version metadata'}
    [IO.File]::WriteAllText($release,'continue');if(!$nuProcess.WaitForExit(15000)){throw 'Owned pinned Nu did not finish'}
    $old=$nuOut.GetAwaiter().GetResult()|ConvertFrom-Json
    if($nuProcess.ExitCode -ne 0 -or $nuErr.GetAwaiter().GetResult() -or $old.marker -cne 'first' -or $old.core -cne (Join-Path (Join-Path $store $fixtures.first.BuildId) 'modpacktools.exe').Replace('\','/')){throw 'Existing Nu did not pin its old module and matching native core'}
    $fresh=Invoke-NativeTestProcess ([IO.Path]::GetFullPath($NuExecutable)) @('--no-config-file','-c',('use '+(Nu-Literal $bootstrap)+' *; {marker: (package-marker), core: $env.MODPACKTOOLS_CORE_PATH} | to json --raw'))
    $new=$fresh.Stdout|ConvertFrom-Json
    if($fresh.Exit -ne 0 -or $fresh.Stderr -or $new.marker -cne 'second' -or $new.core -cne (Join-Path (Join-Path $store $fixtures.second.BuildId) 'modpacktools.exe').Replace('\','/')){throw 'Fresh Nu did not load matching new module/core'}
    $after=Snapshot $control;$stale=Invoke-Control $request
    if($stale.Exit -ne 1 -or $stale.Result.Id -ne 'ModpackTools.Installation.GenerationChanged' -or (Snapshot $control) -cne $after){throw 'Stale generation changed active control'}
    $request.ArchivePath=$fixtures.first.ArchivePath;$request.ArchiveSha256=$fixtures.first.ArchiveSha256;$request.ExpectedGeneration=$second.Result.Generation
    $rollback=Invoke-Control $request
    if($rollback.Exit -ne 0 -or !$rollback.Result.ControlApplied -or $rollback.Result.PreviousBuildId -cne $fixtures.second.BuildId -or $rollback.Result.RetiredCount -ne 1){throw 'Selecting the retained previous version did not roll back coherently'}
    'PASS: initial/control reuse, journal rollback after first file, statically pinned open Nu, coherent fresh Nu, stale CAS rejection and retained-version rollback'
} finally {
    if($nuProcess){if(!$nuProcess.HasExited){$nuProcess.Kill($true);$nuProcess.WaitForExit()};$nuProcess.Dispose()}
    $resolved=[IO.Path]::GetFullPath($run)
    if(!$resolved.StartsWith($temporary,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^modpacktools-install control-[0-9a-f]{32}$'){throw 'Unsafe temporary cleanup path'}
    if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
