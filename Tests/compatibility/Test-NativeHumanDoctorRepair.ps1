param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string]$NuExecutable,[Parameter(Mandatory)][string]$PackwizExecutable,[ValidateSet('None','Confirm','Eof')][string]$Interactive='None')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$sourceRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$temporary=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$root=Join-Path $temporary ('modpacktools-human doctor repair-'+[guid]::NewGuid().ToString('N'))
$names=@('MODPACKTOOLS_CORE_PATH','MODPACKTOOLS_CONFIG_HOME','MODPACKTOOLS_STATE_HOME','MODPACKTOOLS_PROJECT','APPDATA','LOCALAPPDATA')
$previous=@{};foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name)}
try {
    $app=Join-Path $root 'installed app';$catalog=Join-Path $root 'catalog';$configuration=Join-Path $root 'configuration';$project=Join-Path $catalog 'doctor project spaces'
    foreach($directory in @((Join-Path $app 'Nushell'),(Join-Path $project '.modpack'),$configuration,(Join-Path $root 'roaming'),(Join-Path $root 'local'))){[void][IO.Directory]::CreateDirectory($directory)}
    Copy-Item -LiteralPath $Executable -Destination (Join-Path $app 'modpacktools.exe')
    Copy-Item -LiteralPath (Join-Path $sourceRoot 'presentation') -Destination $app -Recurse
    foreach($name in @('modpack.nu','Native-Presentation.nu','Render-Native.nu','Native-Views.nu')){Copy-Item -LiteralPath (Join-Path $sourceRoot "Nushell/$name") -Destination (Join-Path $app "Nushell/$name")}
    [IO.File]::WriteAllText((Join-Path $project '.modpack/project.psd1'),"@{SchemaVersion=1;Id='doctor-ui'}")
    [IO.File]::WriteAllText((Join-Path $project '.modpack/metadata.psd1'),"@{Categories=@{};Mods=@{};ResourcePacks=@{}}")
    [IO.File]::WriteAllText((Join-Path $project 'pack.toml'),"name=`"Doctor UI fixture`"`nversion=`"1.0`"`n[index]`nfile=`"index.toml`"`nhash-format=`"sha256`"`n[versions]`nminecraft=`"1.21.1`"`nfabric=`"0.16.10`"`n")
    [IO.File]::WriteAllText((Join-Path $project 'index.toml'),"hash-format=`"sha256`"`nfiles=[]`n")
    [IO.File]::WriteAllText((Join-Path $project 'fixture.txt'),'stale repair fixture')
    $config=Join-Path $configuration 'config.psd1'
    [IO.File]::WriteAllText($config,"@{Root='$($catalog.Replace("'","''"))';PackwizPath='$($PackwizExecutable.Replace("'","''"))';Sentinel='preserved'}")
    $configHash=(Get-FileHash -LiteralPath $config).Hash
    $env:MODPACKTOOLS_CORE_PATH=Join-Path $app 'modpacktools.exe';$env:MODPACKTOOLS_CONFIG_HOME=$configuration;$env:MODPACKTOOLS_STATE_HOME=Join-Path $root 'state';$env:MODPACKTOOLS_PROJECT='doctor-ui'
    $env:APPDATA=Join-Path $root 'roaming';$env:LOCALAPPDATA=Join-Path $root 'local'
    $driver=Join-Path $app 'driver.nu'
    [IO.File]::WriteAllText($driver,@'
use ./Nushell/modpack.nu
def main [project: string, mode: string] {
    if (is-terminal --stdin) != ($mode != 'None') {error make {msg: 'Doctor fixture has wrong terminal mode'}}
    $env.PATH = []
    let pwd = $env.PWD
    let paths = ['pack.toml' 'index.toml' 'fixture.txt' '.modpack/project.psd1' '.modpack/metadata.psd1']
    let before = ($paths | each {|path| open --raw ($project | path join $path) | hash sha256})
    if $mode == 'None' {
        if (modpack doctor --fix --yes --dry-run --ascii --color never) != null {error make {msg: 'Human doctor dry-run leaked output'}}
        if ($paths | each {|path| open --raw ($project | path join $path) | hash sha256}) != $before {error make {msg: 'Human doctor dry-run changed source'}}
    }
    if (modpack doctor --fix --ascii --color never) != null {error make {msg: 'Human doctor preview leaked output'}}
    if $mode != 'Confirm' {
        if ($paths | each {|path| open --raw ($project | path join $path) | hash sha256}) != $before {error make {msg: 'Cancelled/EOF repair changed source'}}
        if (modpack doctor --fix --yes --ascii --color never) != null {error make {msg: 'Applied human doctor leaked output'}}
    }
    if ($paths | each {|path| open --raw ($project | path join $path) | hash sha256}) == $before {error make {msg: 'Confirmed doctor repair did not apply'}}
    if (modpack doctor --fix --yes --ascii --color never) != null {error make {msg: 'No-op human doctor leaked output'}}
    if $env.MODPACKTOOLS_PROJECT != 'doctor-ui' or $env.PATH != [] or $env.PWD != $pwd {error make {msg: 'Human doctor changed parent session'}}
    print 'Native human doctor repair passed'
}
'@)
    $arguments=@('--no-config-file',$driver,$project,$Interactive)
    if($Interactive -ne 'None'){
        & $NuExecutable @arguments
        if($LASTEXITCODE -ne 0){throw 'Interactive native doctor repair failed'}
    }else{
        $capture=Invoke-NativeTestProcess $NuExecutable $arguments -TimeoutMilliseconds 120000 -CloseInput
        if($capture.Exit -ne 0 -or $capture.Stdout.Trim() -cne 'Native human doctor repair passed' -or $capture.Stderr.Contains([char]27)){throw "Human doctor repair failed: $($capture.Stdout) $($capture.Stderr)"}
        foreach($text in @('Dry run:','Project repair cancelled; no project changes applied.','file change(s) planned; nothing has been changed.','file change(s) applied.','No repairable project changes are required.')){if(!$capture.Stderr.Contains($text)){throw ('Missing doctor repair receipt: '+$text)}}
        'Bridge-free human doctor repair passed with closed stdin, dry-run, explicit approval, no-op and empty PATH'
    }
    if((Get-FileHash -LiteralPath $config).Hash -cne $configHash){throw 'Project repair changed user configuration'}
} finally {
    foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name])}
    $resolved=[IO.Path]::GetFullPath($root)
    if(!$resolved.StartsWith($temporary.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-human doctor repair-*'){throw 'Unsafe temporary cleanup path'}
    if([IO.Directory]::Exists($resolved)){[IO.Directory]::Delete($resolved,$true)}
}
