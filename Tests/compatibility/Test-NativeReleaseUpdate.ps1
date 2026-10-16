param([Parameter(Mandatory)][string]$Inspector,[Parameter(Mandatory)][string]$Setup,[Parameter(Mandatory)][string]$FirstZip,[Parameter(Mandatory)][string]$SecondZip,[Parameter(Mandatory)][string]$NuExecutable)
$ErrorActionPreference='Stop'
$expectedVersion=[string](Import-PowerShellDataFile (Join-Path $PSScriptRoot '../../ModpackTools.psd1')).ModuleVersion
$parsedVersion=[version]$expectedVersion
$differentVersion='{0}.{1}.{2}' -f $parsedVersion.Major,$parsedVersion.Minor,($parsedVersion.Build+1)
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$inspectorPath=[IO.Path]::GetFullPath($Inspector);$setupPath=[IO.Path]::GetFullPath($Setup);$firstPath=[IO.Path]::GetFullPath($FirstZip);$secondPath=[IO.Path]::GetFullPath($SecondZip)
$temporary=[IO.Path]::GetFullPath([IO.Path]::GetTempPath());$run=Join-Path $temporary ('modpacktools-native release-update-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($run)
$names=@('PATH','APPDATA','LOCALAPPDATA','MODPACKTOOLS_CONFIG_HOME','MODPACKTOOLS_STATE_HOME','MODPACKTOOLS_PROJECT','TEMP','TMP');$previous=@{};foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name)}
function Snapshot([string]$Root) {
    $files=[ordered]@{};foreach($file in Get-ChildItem -LiteralPath $Root -File -Recurse -Force|Sort-Object FullName){$files[[IO.Path]::GetRelativePath($Root,$file.FullName)]=(Get-FileHash -LiteralPath $file.FullName).Hash}
    $files|ConvertTo-Json -Depth 6 -Compress
}
function Invoke-Update([string]$Name,[string]$Version,[string]$Generation) {
    $metadata=Join-Path $run ($Name+'.metadata.json');$asset='ModpackTools-Native-win-x64-'+$Version+'.zip'
    $release=@{tag_name=('v'+$Version);draft=$false;prerelease=$false;html_url=('https://github.com/R3Neer/ModpackTools/releases/tag/v'+$Version);assets=@(@{name=$asset;browser_download_url=('https://github.com/R3Neer/ModpackTools/releases/download/v'+$Version+'/'+$asset);digest=('sha256:'+$secondHash);size=(Get-Item -LiteralPath $secondPath).Length;state='uploaded'})}
    [IO.File]::WriteAllText($metadata,($release|ConvertTo-Json -Depth 6 -Compress))
    $request=Join-Path $run ($Name+'.request.json');[IO.File]::WriteAllText($request,(@{Root=$app;StateDirectory=$state;MetadataPath=$metadata;ArchivePath=$secondPath;ExpectedGeneration=$Generation}|ConvertTo-Json -Compress))
    $capture=Invoke-NativeTestProcess $inspectorPath @('--inspect-native-release-update',$request) -TimeoutMilliseconds 240000
    if($capture.Stderr){throw "Unexpected release-update stderr: $($capture.Stderr)"}
    [pscustomobject]@{Exit=$capture.Exit;Result=($capture.Stdout|ConvertFrom-Json)}
}
try {
    foreach($name in @('APPDATA','LOCALAPPDATA','MODPACKTOOLS_CONFIG_HOME','MODPACKTOOLS_STATE_HOME')){[Environment]::SetEnvironmentVariable($name,(Join-Path $run $name));[void][IO.Directory]::CreateDirectory([Environment]::GetEnvironmentVariable($name))}
    $env:PATH='';Remove-Item Env:MODPACKTOOLS_PROJECT -ErrorAction SilentlyContinue
    $privateTemp=Join-Path $run 'private temp';[void][IO.Directory]::CreateDirectory($privateTemp);$env:TEMP=$privateTemp;$env:TMP=$privateTemp
    $firstHash=(Get-FileHash -LiteralPath $firstPath).Hash;$secondHash=(Get-FileHash -LiteralPath $secondPath).Hash
    $app=Join-Path $run "application it's # spaces";$state=Join-Path $run 'recovery state'
    $capture=Invoke-NativeTestProcess $setupPath @('install','--root',$app,'--nu',$NuExecutable,'--package',$firstPath,'--sha256',$firstHash,'--state',$state,'--json') -TimeoutMilliseconds 240000
    if($capture.Exit -ne 0 -or $capture.Stderr){throw "Release-update prerequisite install failed: $($capture.Stdout) $($capture.Stderr)"}
    $installed=$capture.Stdout|ConvertFrom-Json;$before=Snapshot $app;$stable=Snapshot (Join-Path $app 'bin')
    $mismatch=Invoke-Update mismatch $differentVersion $installed.generation
    if($mismatch.Exit -ne 1 -or $mismatch.Result.id -cne 'ModpackTools.Installation.VersionMismatch' -or $mismatch.Result.download_calls -ne 1 -or $mismatch.Result.retain_stage -or $mismatch.Result.version_published -or (Snapshot $app) -cne $before){throw "Selected release/version mismatch changed installation: $($mismatch.Result|ConvertTo-Json -Compress)"}
    if(@(Get-ChildItem -LiteralPath $privateTemp -Force).Count){throw 'Failed release/version binding leaked owned temporary data'}
    $stale=Invoke-Update stale $expectedVersion ([guid]::NewGuid().ToString('N'))
    if($stale.Exit -ne 1 -or $stale.Result.id -cne 'ModpackTools.SelfUpdate.GenerationChanged' -or $stale.Result.download_calls -ne 0 -or (Snapshot $app) -cne $before){throw 'Stale update generation reached download or changed installation'}
    $applied=Invoke-Update applied $expectedVersion $installed.generation
    if($applied.Exit -ne 0 -or !$applied.Result.ok -or $applied.Result.download_calls -ne 1 -or !$applied.Result.result.control_switched -or !$applied.Result.result.active_ready -or $applied.Result.result.build_id -ceq $installed.build_id -or (Snapshot (Join-Path $app 'bin')) -cne $stable){throw "Selected native release did not apply: $($applied.Result|ConvertTo-Json -Depth 8 -Compress)"}
    if(@(Get-ChildItem -LiteralPath $privateTemp -Force).Count){throw 'Applied release update leaked owned temporary data'}
    'PASS: selected-release download/hash/install composition, package-version binding, pre-download generation CAS and immutable stable binaries; injected HTTP only'
} finally {
    foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name])}
    $resolved=[IO.Path]::GetFullPath($run)
    if(!$resolved.StartsWith($temporary,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^modpacktools-native release-update-[0-9a-f]{32}$'){throw 'Unsafe temporary cleanup path'}
    if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
