param([Parameter(Mandatory)][string]$Inspector,[Parameter(Mandatory)][string]$Launcher,[Parameter(Mandatory)][string]$NativeExecutable,[Parameter(Mandatory)][string]$NuExecutable)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$inspectorPath=[IO.Path]::GetFullPath($Inspector);$launcherPath=[IO.Path]::GetFullPath($Launcher)
$payload=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$temporary=[IO.Path]::GetFullPath([IO.Path]::GetTempPath());$run=Join-Path $temporary ('modpacktools-native launcher-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($run)
$names=@('PATH','APPDATA','LOCALAPPDATA','MODPACKTOOLS_CONFIG_HOME','MODPACKTOOLS_STATE_HOME','MODPACKTOOLS_TEST_READY_PATH','MODPACKTOOLS_TEST_RELEASE_PATH')
$previous=@{};foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name)}
$waitingProcess=$null
function Invoke-InstallationFixture($Request) {
    $path=Join-Path $run ('request-'+[guid]::NewGuid().ToString('N')+'.json');[IO.File]::WriteAllText($path,($Request|ConvertTo-Json -Depth 12 -Compress))
    $capture=Invoke-NativeTestProcess $inspectorPath @('--inspect-native-package',$path) -TimeoutMilliseconds 120000
    if($capture.Stderr){throw "Unexpected inspector stderr: $($capture.Stderr)"}
    [pscustomobject]@{Exit=$capture.Exit;Result=($capture.Stdout|ConvertFrom-Json)}
}
function Create-Fixture([string]$Name,[string]$Assets) {
    $archive=Join-Path $run ($Name+'.zip');$fixture=Invoke-InstallationFixture @{CreateFixture=$true;ArchivePath=$archive;Executable=[IO.Path]::GetFullPath($NativeExecutable);LauncherExecutable=$launcherPath;PayloadDirectory=$Assets}
    if($fixture.Exit -ne 0){throw 'Fixture package creation failed'}
    return $fixture.Result
}
function Prepare-Root($Fixture,[string]$App) {
    $request=@{ArchivePath=$Fixture.ArchivePath;ArchiveSha256=$Fixture.ArchiveSha256;StagingParent=(Join-Path $run 'prepared');InitializeInstallation=$App;NuExecutable=[IO.Path]::GetFullPath($NuExecutable);PublishParent=(Join-Path $App 'versions');ControlRoot=(Join-Path $App 'control');StateDirectory=(Join-Path $run 'state');VerifyAdmissionPin=$App}
    $result=Invoke-InstallationFixture $request
    if($result.Exit -ne 0 -or !$result.Result.ControlApplied){throw "Owned fixture root failed: $($result.Result|ConvertTo-Json -Compress)"}
    $stable=Join-Path $App 'bin/modpack.exe';[IO.File]::Copy($launcherPath,$stable)
    if((Get-FileHash -LiteralPath $stable).Hash -cne (Get-FileHash -LiteralPath $launcherPath).Hash){throw 'Fixture stable launcher differs'}
    return $result.Result
}
try {
    foreach($name in @('APPDATA','LOCALAPPDATA','MODPACKTOOLS_CONFIG_HOME','MODPACKTOOLS_STATE_HOME')){[Environment]::SetEnvironmentVariable($name,(Join-Path $run $name));[void][IO.Directory]::CreateDirectory([Environment]::GetEnvironmentVariable($name))}
    $env:PATH=''
    $fixture=Create-Fixture real $payload;$app=Join-Path $run 'application with spaces';$installed=Prepare-Root $fixture $app;$stable=Join-Path $app 'bin/modpack.exe'
    $protocol=Invoke-NativeTestProcess $launcherPath @('--launcher-protocol')
    if($protocol.Exit -ne 0 -or $protocol.Stdout.Trim() -cne '1' -or $protocol.Stderr){throw 'Launcher protocol mismatch'}
    $machine=Invoke-NativeTestProcess $stable @('--version','--no-human')
    if($machine.Exit -ne 0 -or $machine.Stderr -or ($machine.Stdout|ConvertFrom-Json).data.version.version -cne '4.0.2'){throw "Stable machine launcher failed without PATH: $($machine|ConvertTo-Json -Compress)"}
    $human=Invoke-NativeTestProcess $stable @('--version') -TimeoutMilliseconds 60000 -CloseInput
    if($human.Exit -ne 0 -or ($human.Stderr+$human.Stdout) -notmatch '4\.0\.2' -or ($human.Stderr+$human.Stdout).Contains([string][char]27)){throw "Stable human launcher failed without PowerShell: $($human.Stdout) $($human.Stderr)"}
    $occupied=Invoke-InstallationFixture @{ArchivePath=$fixture.ArchivePath;ArchiveSha256=$fixture.ArchiveSha256;StagingParent=(Join-Path $run 'prepared');InitializeInstallation=$app;NuExecutable=[IO.Path]::GetFullPath($NuExecutable)}
    if($occupied.Exit -ne 1 -or $occupied.Result.Id -ne 'ModpackTools.Installation.RootOccupied' -or (Get-Content -LiteralPath (Join-Path $app 'installation.json') -Raw|ConvertFrom-Json).root.file_identity -notmatch '^[0-9a-f]{48}$'){throw 'Existing managed root was adopted or damaged'}
    [IO.File]::AppendAllText((Join-Path $installed.Directory 'cli.nu'),'foreign edit')
    $altered=Invoke-NativeTestProcess $stable @('--version','--no-human')
    if($altered.Exit -ne 1 -or $altered.Stderr -notmatch 'ModpackTools.Package.StageChanged'){throw 'Launcher executed an altered immutable bundle'}
    $waitAssets=Join-Path $run 'wait assets';[void][IO.Directory]::CreateDirectory($waitAssets)
    [IO.File]::WriteAllText((Join-Path $waitAssets 'cli.nu'),'def --wrapped main [...args: string] { "ready" | save $env.MODPACKTOOLS_TEST_READY_PATH; while not ($env.MODPACKTOOLS_TEST_RELEASE_PATH | path exists) { sleep 20ms }; print "done" }')
    $waitFixture=Create-Fixture wait $waitAssets;$waitApp=Join-Path $run 'waiting app';$waitInstalled=Prepare-Root $waitFixture $waitApp
    $env:MODPACKTOOLS_TEST_READY_PATH=Join-Path $run 'child ready';$env:MODPACKTOOLS_TEST_RELEASE_PATH=Join-Path $run 'child release'
    $start=[Diagnostics.ProcessStartInfo]::new((Join-Path $waitApp 'bin/modpack.exe'));$start.UseShellExecute=$false;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true;$start.ArgumentList.Add('fixture-wait')
    $waitingProcess=[Diagnostics.Process]::Start($start);$waitOut=$waitingProcess.StandardOutput.ReadToEndAsync();$waitErr=$waitingProcess.StandardError.ReadToEndAsync()
    $deadline=[DateTime]::UtcNow.AddSeconds(15);while(!(Test-Path -LiteralPath $env:MODPACKTOOLS_TEST_READY_PATH)){if($waitingProcess.HasExited -or [DateTime]::UtcNow -gt $deadline){throw 'Owned waiting launcher child did not become ready'};Start-Sleep -Milliseconds 20}
    $pin=Join-Path $waitApp ('pins/'+$waitInstalled.BuildId+'.lease');$blocked=$false
    try{$exclusive=[IO.File]::Open($pin,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);$exclusive.Dispose()}catch [IO.IOException]{$blocked=$true}
    if(!$blocked){throw 'Live launcher child lost its admission pin'}
    [IO.File]::WriteAllText($env:MODPACKTOOLS_TEST_RELEASE_PATH,'continue');if(!$waitingProcess.WaitForExit(15000)){throw 'Owned launcher did not exit after child release'}
    if($waitingProcess.ExitCode -ne 0 -or $waitErr.GetAwaiter().GetResult() -or $waitOut.GetAwaiter().GetResult().Trim() -cne 'done'){throw 'Launcher did not preserve child output or exit'}
    $released=[IO.File]::Open($pin,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);$released.Dispose()
    'PASS: owned root, native launcher protocol, PATH-free machine/human version, occupied-root preservation, altered-bundle rejection and child-lifetime admission pin'
} finally {
    if($waitingProcess){if(!$waitingProcess.HasExited){$waitingProcess.Kill($true);$waitingProcess.WaitForExit()};$waitingProcess.Dispose()}
    foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name])}
    $resolved=[IO.Path]::GetFullPath($run)
    if(!$resolved.StartsWith($temporary,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^modpacktools-native launcher-[0-9a-f]{32}$'){throw 'Unsafe temporary cleanup path'}
    if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
