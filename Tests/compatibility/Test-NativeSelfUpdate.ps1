param([Parameter(Mandatory)][string]$Setup,[Parameter(Mandatory)][string]$Launcher,[Parameter(Mandatory)][string]$NativeExecutable,[Parameter(Mandatory)][string]$NuExecutable,[switch]$Online)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$setupPath=[IO.Path]::GetFullPath($Setup);$launcherPath=[IO.Path]::GetFullPath($Launcher);$corePath=[IO.Path]::GetFullPath($NativeExecutable);$nuPath=[IO.Path]::GetFullPath($NuExecutable)
$source=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'));$temporary=[IO.Path]::GetFullPath([IO.Path]::GetTempPath());$run=Join-Path $temporary ('modpacktools-native self-update-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($run)
$names=@('PATH','APPDATA','LOCALAPPDATA','MODPACKTOOLS_CONFIG_HOME','MODPACKTOOLS_STATE_HOME','MODPACKTOOLS_PROJECT','TEMP','TMP')
$previous=@{};foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name)}
function Invoke-Setup([string[]]$Tokens) {
    $capture=Invoke-NativeTestProcess $setupPath ($Tokens+@('--json')) -TimeoutMilliseconds 240000
    if($capture.Exit -ne 0 -or $capture.Stderr){throw "Setup failed: $($capture.Stdout) $($capture.Stderr)"}
    $capture.Stdout|ConvertFrom-Json
}
function Snapshot([string]$Root) {
    $files=[ordered]@{};foreach($file in Get-ChildItem -LiteralPath $Root -File -Recurse -Force|Sort-Object FullName){$files[[IO.Path]::GetRelativePath($Root,$file.FullName)]=(Get-FileHash -LiteralPath $file.FullName).Hash}
    $files|ConvertTo-Json -Depth 6 -Compress
}
function Invoke-Human([string]$Name,[switch]$Yes) {
    $script=Join-Path $run ($Name+'.nu');$bootstrap=($installed.nu_module -replace '\\','/');$package=($secondZip -replace '\\','/')
    $yesToken=if($Yes){' --yes'}else{''}
    $tokens="modpack self-update --package r###'$package'### --sha256 '$($second.sha256)' --ascii --color never"+$yesToken
    [IO.File]::WriteAllText($script,"use r###'$bootstrap'### *`n$tokens`n")
    Invoke-NativeTestProcess $nuPath @('--no-config-file','--config-home',$nuConfig,$script) -CloseInput -TimeoutMilliseconds 240000
}
function Has-EmptyData([string]$Json) {
    $result=$Json|ConvertFrom-Json
    $result.schema_version -eq 1 -and $result.ok -and $result.command -ceq 'self-update' -and @($result.data.PSObject.Properties).Count -eq 0 -and $null -eq $result.presentation
}
try {
    foreach($name in @('APPDATA','LOCALAPPDATA','MODPACKTOOLS_CONFIG_HOME','MODPACKTOOLS_STATE_HOME')){[Environment]::SetEnvironmentVariable($name,(Join-Path $run $name));[void][IO.Directory]::CreateDirectory([Environment]::GetEnvironmentVariable($name))}
    $env:PATH='';$env:MODPACKTOOLS_PROJECT='must-not-read-real-project'
    $privateTemp=Join-Path $run 'private temp';[void][IO.Directory]::CreateDirectory($privateTemp);$env:TEMP=$privateTemp;$env:TMP=$privateTemp
    $nuConfig=Join-Path $run 'private nu';[void][IO.Directory]::CreateDirectory($nuConfig)
    $sentinel=Join-Path $env:MODPACKTOOLS_CONFIG_HOME 'sentinel.txt';[IO.File]::WriteAllText($sentinel,'personal configuration preserved')
    $configBefore=Snapshot $env:MODPACKTOOLS_CONFIG_HOME
    $assets=Join-Path $run 'assets';[void][IO.Directory]::CreateDirectory($assets)
    $paths=@('cli.nu','Nushell/modpack.nu','Nushell/Native-Presentation.nu','Nushell/Native-Views.nu','Nushell/Render-Native.nu','presentation/r3cli/mod.nu','presentation/r3cli/resources.json','presentation/r3cli/LICENSE','presentation/r3cli/hashes.json','presentation/r3cli/manifest.json','LICENSE','README.md','Private/lib/LICENSE.Tomlyn.txt','Private/lib/Newtonsoft.Json.LICENSE.txt')
    foreach($relative in $paths){$destination=Join-Path $assets $relative;[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination));[IO.File]::Copy((Join-Path $source $relative),$destination)}
    $firstZip=Join-Path $run 'first.zip';$first=Invoke-Setup @('pack','--core',$corePath,'--launcher',$launcherPath,'--assets',$assets,'--output',$firstZip)
    [IO.File]::AppendAllText((Join-Path $assets 'README.md'),"`nPublic self-update second build.`n")
    $secondZip=Join-Path $run 'second.zip';$second=Invoke-Setup @('pack','--core',$corePath,'--launcher',$launcherPath,'--assets',$assets,'--output',$secondZip)
    $app=Join-Path $run "application it's # spaces";$state=Join-Path $run 'recovery state';$env:MODPACKTOOLS_STATE_HOME=$state
    $installed=Invoke-Setup @('install','--root',$app,'--nu',$nuPath,'--package',$firstZip,'--sha256',$first.sha256,'--state',$state)
    $version=Invoke-NativeTestProcess $installed.launcher @('--version','--no-human');if($version.Exit -ne 0 -or $version.Stderr){throw 'Installed launcher cannot create its initial admission pin'}
    $before=Snapshot $app;$firstRoot=Join-Path $app ('versions/'+$first.build_id);$original=Snapshot $firstRoot;$stable=Snapshot (Join-Path $app 'bin')
    $theme=Join-Path $env:MODPACKTOOLS_CONFIG_HOME 'theme.toml';[IO.File]::WriteAllText($theme,'broken=[')
    $preview=Invoke-NativeTestProcess $installed.launcher @('self-update','--package',$secondZip,'--sha256',$second.sha256,'--no-human') -TimeoutMilliseconds 240000
    if($preview.Exit -ne 0 -or $preview.Stderr -or !(Has-EmptyData $preview.Stdout) -or (Snapshot $app) -cne $before -or [IO.File]::ReadAllText($theme) -cne 'broken=['){throw "Machine self-update preview changed installation or loaded theme: $($preview.Stdout) $($preview.Stderr)"}
    [IO.File]::Delete($theme)
    $cancelled=Invoke-Human cancel
    if($cancelled.Exit -ne 0 -or $cancelled.Stdout -ne '' -or $cancelled.Stderr -notmatch 'Update cancelled\.' -or $cancelled.Stderr.Contains([char]27) -or (Snapshot $app) -cne $before){throw "Noninteractive human update did not cancel cleanly: $($cancelled.Stdout) $($cancelled.Stderr)"}
    $bad=Invoke-NativeTestProcess $installed.launcher @('self-update','--package',$secondZip,'--sha256',('0'*64),'--yes','--no-human') -TimeoutMilliseconds 240000
    if($bad.Exit -ne 1 -or $bad.Stderr -or ($bad.Stdout|ConvertFrom-Json).error.id -cne 'ModpackTools.Package.ArchiveHashMismatch' -or (Snapshot $app) -cne $before){throw 'Untrusted local self-update changed the installation or lost its typed error'}
    $applied=Invoke-Human apply -Yes
    if($applied.Exit -ne 0 -or $applied.Stdout -ne '' -or $applied.Stderr -notmatch 'installed and verified' -or $applied.Stderr -notmatch 'new Nushell session' -or $applied.Stderr.Contains([char]27)){throw "Human reviewed native self-update failed: $($applied.Stdout) $($applied.Stderr)"}
    $control=Get-Content -LiteralPath (Join-Path $app 'control/current.json') -Raw|ConvertFrom-Json
    if($control.current.build_id -cne $second.build_id -or $control.previous_build_id -cne $first.build_id -or (Snapshot $firstRoot) -cne $original -or (Snapshot (Join-Path $app 'bin')) -cne $stable){throw 'Reviewed self-update did not preserve originals or select packageB'}
    $baseline=Invoke-NativeTestProcess $installed.launcher @('--version','--no-human');if($baseline.Exit -ne 0 -or $baseline.Stderr){throw 'Updated launcher cannot create its admission pin'}
    $current=Snapshot $app
    $noop=Invoke-NativeTestProcess $installed.launcher @('self-update','--package',$secondZip,'--sha256',$second.sha256,'--yes','--no-human') -TimeoutMilliseconds 240000
    if($noop.Exit -ne 0 -or $noop.Stderr -or !(Has-EmptyData $noop.Stdout) -or (Snapshot $app) -cne $current){throw 'Same-package machine self-update is not an empty-data no-op'}
    if($Online){$check=Invoke-NativeTestProcess $installed.launcher @('self-update','--check','--no-human') -TimeoutMilliseconds 30000;if($check.Exit -ne 0 -or $check.Stderr -or !(Has-EmptyData $check.Stdout) -or (Snapshot $app) -cne $current){throw "Official stable self-update check failed: $($check.Stdout) $($check.Stderr)"}}
    if((Snapshot $env:MODPACKTOOLS_CONFIG_HOME) -cne $configBefore -or @((Get-ChildItem -LiteralPath $privateTemp -Force)).Count -ne 0 -or $env:MODPACKTOOLS_PROJECT -cne 'must-not-read-real-project'){throw 'Self-update leaked temporary state or changed caller configuration/project'}
    'PASS: public native self-update machine empty-data contract, theme independence, non-TTY cancellation, reviewed Nu update, hash refusal, immutable originals and same-package no-op'
} finally {
    foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name])}
    $resolved=[IO.Path]::GetFullPath($run)
    if(!$resolved.StartsWith($temporary,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notmatch '^modpacktools-native self-update-[0-9a-f]{32}$'){throw 'Unsafe temporary cleanup path'}
    if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
