param([Parameter(Mandatory)][string]$Setup,[Parameter(Mandatory)][string]$Launcher,[Parameter(Mandatory)][string]$NativeExecutable,[Parameter(Mandatory)][string]$NuExecutable,[Parameter(Mandatory)][string]$Inspector)
$ErrorActionPreference='Stop'
$expectedVersion=[string](Import-PowerShellDataFile (Join-Path $PSScriptRoot '../../ModpackTools.psd1')).ModuleVersion
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$setupPath=[IO.Path]::GetFullPath($Setup);$launcherPath=[IO.Path]::GetFullPath($Launcher);$corePath=[IO.Path]::GetFullPath($NativeExecutable);$inspectorPath=[IO.Path]::GetFullPath($Inspector);$nuPath=[IO.Path]::GetFullPath($NuExecutable)
$source=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'));$temporary=[IO.Path]::GetFullPath([IO.Path]::GetTempPath());$run=Join-Path $temporary ('modpacktools-native setup-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($run)
$names=@('PATH','APPDATA','LOCALAPPDATA','MODPACKTOOLS_CONFIG_HOME','MODPACKTOOLS_STATE_HOME','MODPACKTOOLS_PROJECT')
$previous=@{};foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name)}
function Invoke-Setup([string[]]$Tokens,[string]$HostPath=$setupPath) {
    $capture=Invoke-NativeTestProcess $HostPath ($Tokens+@('--json')) -TimeoutMilliseconds 240000
    if($capture.Stderr){throw "Unexpected setup stderr: $($capture.Stderr)"}
    [pscustomobject]@{Exit=$capture.Exit;Result=($capture.Stdout|ConvertFrom-Json)}
}
function Snapshot([string]$Root) {
    $files=[ordered]@{};foreach($file in Get-ChildItem -LiteralPath $Root -File -Recurse -Force|Sort-Object FullName){$files[[IO.Path]::GetRelativePath($Root,$file.FullName)]=(Get-FileHash -LiteralPath $file.FullName).Hash}
    $files|ConvertTo-Json -Depth 6 -Compress
}
try {
    foreach($name in @('APPDATA','LOCALAPPDATA','MODPACKTOOLS_CONFIG_HOME','MODPACKTOOLS_STATE_HOME')){[Environment]::SetEnvironmentVariable($name,(Join-Path $run $name));[void][IO.Directory]::CreateDirectory([Environment]::GetEnvironmentVariable($name))}
    $env:PATH='';$env:MODPACKTOOLS_PROJECT='must-not-select-real-project'
    $sentinel=Join-Path $env:MODPACKTOOLS_CONFIG_HOME 'sentinel.txt';[IO.File]::WriteAllText($sentinel,'personal configuration preserved')
    $assets=Join-Path $run 'assets';[void][IO.Directory]::CreateDirectory($assets)
    $paths=@('cli.nu','Nushell/modpack.nu','Nushell/Native-Presentation.nu','Nushell/Native-Views.nu','Nushell/Render-Native.nu','presentation/r3cli/mod.nu','presentation/r3cli/resources.json','presentation/r3cli/LICENSE','presentation/r3cli/hashes.json','presentation/r3cli/manifest.json','LICENSE','README.md','Private/lib/LICENSE.Tomlyn.txt','Private/lib/Newtonsoft.Json.LICENSE.txt')
    foreach($relative in $paths){$destination=Join-Path $assets $relative;[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination));[IO.File]::Copy((Join-Path $source $relative),$destination)}
    $firstZip=Join-Path $run 'first package.zip';$first=Invoke-Setup @('pack','--core',$corePath,'--launcher',$launcherPath,'--assets',$assets,'--output',$firstZip)
    if($first.Exit -ne 0 -or $first.Result.sha256 -cne (Get-FileHash -LiteralPath $firstZip).Hash){throw "Native package build failed: $($first.Result|ConvertTo-Json -Compress)"}
    $repeatZip=Join-Path $run 'repeat package.zip';$repeat=Invoke-Setup @('pack','--core',$corePath,'--launcher',$launcherPath,'--assets',$assets,'--output',$repeatZip)
    if($repeat.Exit -ne 0 -or $repeat.Result.sha256 -cne $first.Result.sha256){throw 'Native package build is not reproducible for the same inputs'}
    $app=Join-Path $run "application it's # spaces";$state=Join-Path $run 'recovery state'
    $bad=Invoke-Setup @('install','--root',$app,'--nu',$nuPath,'--package',$firstZip,'--sha256',('0'*64),'--state',$state)
    if($bad.Exit -ne 1 -or $bad.Result.id -ne 'ModpackTools.Package.ArchiveHashMismatch' -or (Test-Path -LiteralPath $app)){throw 'Untrusted archive reached installation root'}
    $invalidZip=Join-Path $run 'wrong roles.zip';$request=Join-Path $run 'wrong roles request.json'
    [IO.File]::WriteAllText($request,(@{CreateFixture=$true;ArchivePath=$invalidZip;Executable=$corePath}|ConvertTo-Json -Compress))
    $fixture=Invoke-NativeTestProcess $inspectorPath @('--inspect-native-package',$request);$invalid=$fixture.Stdout|ConvertFrom-Json
    $roles=Invoke-Setup @('install','--root',$app,'--nu',$nuPath,'--package',$invalidZip,'--sha256',$invalid.ArchiveSha256,'--state',$state)
    if($roles.Exit -ne 1 -or (Test-Path -LiteralPath $app) -or $roles.Result.root_created -or $roles.Result.control_switched){throw 'Format-only role fixture was activated or reserved the final root'}
    $installed=Invoke-Setup @('install','--root',$app,'--nu',$nuPath,'--package',$firstZip,'--sha256',$first.Result.sha256,'--state',$state)
    if($installed.Exit -ne 0 -or !$installed.Result.root_created -or !$installed.Result.version_published -or !$installed.Result.bin_ready -or !$installed.Result.control_switched -or !$installed.Result.active_ready -or $installed.Result.build_id -cne $first.Result.build_id){throw "Native install failed: $($installed.Result|ConvertTo-Json -Depth 8 -Compress)"}
    $native=Invoke-NativeTestProcess $installed.Result.launcher @('--version','--no-human')
    if($native.Exit -ne 0 -or $native.Stderr -or ($native.Stdout|ConvertFrom-Json).data.version.version -cne $expectedVersion){throw 'Installed native launcher cannot execute machine version'}
    $versionRoot=Join-Path $app ('versions/'+$first.Result.build_id);$original=Snapshot $versionRoot
    [IO.File]::AppendAllText((Join-Path $assets 'README.md'),"`nIsolated native update fixture.`n")
    $secondZip=Join-Path $run 'second package.zip';$second=Invoke-Setup @('pack','--core',$corePath,'--launcher',$launcherPath,'--assets',$assets,'--output',$secondZip)
    if($second.Exit -ne 0 -or $second.Result.build_id -ceq $first.Result.build_id){throw 'Changed content did not produce a new native build identity'}
    $stableSetup=Join-Path $app 'bin/modpacktools-setup.exe';$stableBefore=Snapshot (Join-Path $app 'bin')
    $updated=Invoke-Setup @('update','--root',$app,'--package',$secondZip,'--sha256',$second.Result.sha256,'--state',$state) $stableSetup
    if($updated.Exit -ne 0 -or $updated.Result.root_created -or !$updated.Result.control_switched -or $updated.Result.build_id -cne $second.Result.build_id -or (Snapshot $versionRoot) -cne $original -or (Snapshot (Join-Path $app 'bin')) -cne $stableBefore){throw "Native update altered immutable/stable originals or did not switch: $($updated.Result|ConvertTo-Json -Compress)"}
    $noop=Invoke-Setup @('update','--root',$app,'--package',$secondZip,'--sha256',$second.Result.sha256,'--state',$state) $stableSetup
    if($noop.Exit -ne 0 -or $noop.Result.control_switched -or $noop.Result.version_published -or !$noop.Result.active_ready){throw 'Same-build update rewrote the active generation'}
    $rollback=Invoke-Setup @('rollback','--root',$app,'--state',$state) $stableSetup
    if($rollback.Exit -ne 0 -or !$rollback.Result.control_switched -or $rollback.Result.build_id -cne $first.Result.build_id -or (Snapshot $versionRoot) -cne $original){throw 'Native retained-version rollback failed'}
    if([IO.File]::ReadAllText($sentinel) -cne 'personal configuration preserved' -or $env:PATH -cne '' -or $env:MODPACKTOOLS_PROJECT -cne 'must-not-select-real-project'){throw 'Native installation changed caller preferences or selection'}
    'PASS: deterministic native packages, pre-root hash/role rejection, full native install, stable/versioned update, no-op and rollback with private human R3/Nu smoke and no PowerShell PATH'
} finally {
    foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name])}
    $resolved=[IO.Path]::GetFullPath($run)
    if(!$resolved.StartsWith($temporary,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^modpacktools-native setup-[0-9a-f]{32}$'){throw 'Unsafe temporary cleanup path'}
    if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
