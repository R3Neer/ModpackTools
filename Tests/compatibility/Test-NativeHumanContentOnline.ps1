param([Parameter(Mandatory)][string]$PackageDirectory,[Parameter(Mandatory)][string]$NuExecutable,[Parameter(Mandatory)][string]$PackwizExecutable,[Parameter(Mandatory)][string]$VersionId)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$package=[IO.Path]::GetFullPath($PackageDirectory)
$temporary=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$run=Join-Path $temporary ('modpacktools-live content-'+[guid]::NewGuid().ToString('N'))
$names=@('MODPACKTOOLS_CORE_PATH','MODPACKTOOLS_CONFIG_HOME','MODPACKTOOLS_STATE_HOME','MODPACKTOOLS_PROJECT','APPDATA','LOCALAPPDATA')
$previous=@{};foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name)}
function Snapshot([string]$Root) {
    $result=[ordered]@{}
    foreach($file in Get-ChildItem -LiteralPath $Root -File -Recurse -Force | Sort-Object FullName){$result[[IO.Path]::GetRelativePath($Root,$file.FullName)]=@((Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash,$file.LastWriteTimeUtc.Ticks)}
    return ($result|ConvertTo-Json -Depth 10 -Compress)
}
try {
    $configuration=Join-Path $run 'configuration';$catalog=Join-Path $run 'catalog';$project=Join-Path $catalog 'live fixture spaces'
    [void][IO.Directory]::CreateDirectory((Join-Path $project '.modpack'));[void][IO.Directory]::CreateDirectory($configuration)
    [IO.File]::WriteAllText((Join-Path $project '.modpack/project.psd1'),"@{SchemaVersion=1;Id='live-content'}")
    [IO.File]::WriteAllText((Join-Path $project '.modpack/metadata.psd1'),"@{Categories=@{};Mods=@{};ResourcePacks=@{}}")
    [IO.File]::WriteAllText((Join-Path $project 'pack.toml'),"name=`"Live content fixture`"`nversion=`"1.0`"`n[index]`nfile=`"index.toml`"`nhash-format=`"sha256`"`n[versions]`nminecraft=`"1.21.1`"`nfabric=`"0.16.10`"`n")
    [IO.File]::WriteAllText((Join-Path $project 'index.toml'),"hash-format=`"sha256`"`nfiles=[]`n")
    [IO.File]::WriteAllText((Join-Path $configuration 'config.psd1'),"@{Root='$($catalog.Replace("'","''"))';PackwizPath='$($PackwizExecutable.Replace("'","''"))'}")
    $env:MODPACKTOOLS_CORE_PATH=Join-Path $package 'modpacktools.exe';$env:MODPACKTOOLS_CONFIG_HOME=$configuration;$env:MODPACKTOOLS_STATE_HOME=Join-Path $run 'state';$env:MODPACKTOOLS_PROJECT='live-content'
    $env:APPDATA=Join-Path $run 'Roaming';$env:LOCALAPPDATA=Join-Path $run 'Local'
    [void][IO.Directory]::CreateDirectory($env:APPDATA);[void][IO.Directory]::CreateDirectory($env:LOCALAPPDATA)
    $module=(Join-Path $package 'Nushell/modpack.nu').Replace('\','/')
    $driver=Join-Path $run 'driver.nu'
    $body=@'
use '__MODULE__'
def main [selector: string, mode: string] {
    $env.PATH = []
    let result = if $mode == 'dry' {modpack content add $selector --dry-run --ascii --color never} else {modpack content add $selector --ascii --color never}
    if $result != null or $env.MODPACKTOOLS_PROJECT != 'live-content' or $env.PATH != [] {error make {msg: 'Live add changed parent session or leaked output'}}
}
'@
    [IO.File]::WriteAllText($driver,$body.Replace('__MODULE__',$module))
    $selector='https://modrinth.com/mod/fabric-api/version/'+$VersionId
    $before=Snapshot $project
    $dry=Invoke-NativeTestProcess $NuExecutable @('--no-config-file',$driver,$selector,'dry') -TimeoutMilliseconds 120000
    if($dry.Exit -ne 0 -or $dry.Stdout -ne '' -or $dry.Stderr -notmatch [regex]::Escape("not installed -> $VersionId (requested)") -or $dry.Stderr -notmatch 'Dry run:' -or (Snapshot $project) -cne $before){throw "Real provider dry-run failed: $($dry.Stderr)"}
    $apply=Invoke-NativeTestProcess $NuExecutable @('--no-config-file',$driver,$selector,'apply') -TimeoutMilliseconds 120000
    if($apply.Exit -ne 0 -or $apply.Stdout -ne '' -or $apply.Stderr -notmatch [regex]::Escape("not installed -> $VersionId (requested)") -or $apply.Stderr -notmatch 'file change\(s\) applied'){throw "Real provider add failed: $($apply.Stderr)"}
    $metadata=@(Get-ChildItem -LiteralPath (Join-Path $project 'mods') -File -Filter '*.pw.toml' | Where-Object {(Get-Content -LiteralPath $_.FullName -Raw).Contains('P7dR8mSH')})
    if($metadata.Count -ne 1 -or !(Get-Content -LiteralPath $metadata[0].FullName -Raw).Contains($VersionId)){throw 'Applied add metadata did not retain pinned version'}
    "PASS: real public Modrinth add dry/apply for Fabric API $VersionId, AOT/Nu with empty PATH; private source unchanged in dry-run"
} finally {
    foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name])}
    $resolved=[IO.Path]::GetFullPath($run)
    if(!$resolved.StartsWith($temporary.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-live content-*'){throw 'Unsafe temporary cleanup path'}
    if([IO.Directory]::Exists($resolved)){[IO.Directory]::Delete($resolved,$true)}
}
