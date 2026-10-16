param([Parameter(Mandatory)][string]$PackageDirectory,[Parameter(Mandatory)][string]$NuExecutable,[string]$PackwizExecutable,[switch]$RootSetup)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$package=[IO.Path]::GetFullPath($PackageDirectory)
$temporary=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$run=Join-Path $temporary ('modpacktools-human environment-'+[guid]::NewGuid().ToString('N'))
$names=@('MODPACKTOOLS_CORE_PATH','MODPACKTOOLS_CONFIG_HOME','MODPACKTOOLS_STATE_HOME','MODPACKTOOLS_PROJECT','APPDATA','LOCALAPPDATA')
$previous=@{};foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name)}
try {
    $configuration=Join-Path $run 'configuration';$catalog=Join-Path $run 'catalog spaces'
    foreach($directory in @($configuration,$catalog,(Join-Path $run 'roaming'),(Join-Path $run 'local'))){[void][IO.Directory]::CreateDirectory($directory)}
    $config=Join-Path $configuration 'config.psd1'
    $initial=if($RootSetup){if(!$PackwizExecutable){throw 'RootSetup requires existing Packwiz'};"@{Sentinel='preserved';PackwizPath='$($PackwizExecutable.Replace("'","''"))'}"}else{"@{Sentinel='preserved'}"}
    [IO.File]::WriteAllText($config,$initial)
    $env:MODPACKTOOLS_CORE_PATH=Join-Path $package 'modpacktools.exe';$env:MODPACKTOOLS_CONFIG_HOME=$configuration;$env:MODPACKTOOLS_STATE_HOME=Join-Path $run 'state'
    $env:APPDATA=Join-Path $run 'roaming';$env:LOCALAPPDATA=Join-Path $run 'local';Remove-Item Env:MODPACKTOOLS_PROJECT -ErrorAction SilentlyContinue
    $module=(Join-Path $package 'Nushell/modpack.nu').Replace('\','/')
    $driver=Join-Path $run 'driver.nu'
    $body=@'
use '__MODULE__'
def main [mode: string] {
    $env.PATH = []
    let pwd = $env.PWD
    let result = if $mode == 'root' {modpack doctor --fix --ascii --color never} else {modpack doctor --fix --yes --ascii --color never}
    if $result != null or $env.MODPACKTOOLS_PROJECT? != null or $env.PATH != [] or $env.PWD != $pwd {error make {msg: 'Environment repair leaked output or changed parent session'}}
}
'@
    [IO.File]::WriteAllText($driver,$body.Replace('__MODULE__',$module))
    if($RootSetup){
        Write-Output "TEST ROOT INPUT: $catalog"
        & $NuExecutable --no-config-file $driver root
        if($LASTEXITCODE -ne 0){throw 'Interactive root setup failed'}
        if(![IO.File]::ReadAllText($config).Contains($catalog) -or ![IO.File]::ReadAllText($config).Contains('preserved')){throw 'Root setup lost target or sentinel'}
        'PASS: native human root setup preserved settings and no active project'
    }else{
        $capture=Invoke-NativeTestProcess $NuExecutable @('--no-config-file',$driver,'environment') -TimeoutMilliseconds 120000 -CloseInput
        if($capture.Exit -ne 0 -or $capture.Stdout -ne '' -or !$capture.Stderr.Contains('Verified Packwiz installed:') -or !$capture.Stderr.Contains('without changing PATH.')){throw "Human environment install failed: $($capture.Stdout) $($capture.Stderr)"}
        $installed=Join-Path $configuration 'tools/packwiz/dfd8b68/packwiz.exe'
        if((Get-FileHash -LiteralPath $installed).Hash -cne '7182F0B513A4FF117CA252B7EAEA2CEDEEDE19DD9A0BED77A10403D75A3C9295' -or ![IO.File]::ReadAllText($config).Contains('preserved')){throw 'Human environment install hash/config mismatch'}
        $hash=(Get-FileHash -LiteralPath $config).Hash
        $repeat=Invoke-NativeTestProcess $NuExecutable @('--no-config-file',$driver,'environment') -TimeoutMilliseconds 30000 -CloseInput
        if($repeat.Exit -ne 0 -or $repeat.Stdout -ne '' -or $repeat.Stderr.Contains('Verified Packwiz installed:') -or (Get-FileHash -LiteralPath $config).Hash -cne $hash){throw 'Human environment repeat changed installation/config'}
        'PASS: bridge-free human environment install and repeat verified pinned Packwiz, preserved unknown settings, no project/PATH selection'
    }
} finally {
    foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name])}
    $resolved=[IO.Path]::GetFullPath($run)
    if(!$resolved.StartsWith($temporary.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-human environment-*'){throw 'Unsafe temporary cleanup path'}
    if([IO.Directory]::Exists($resolved)){[IO.Directory]::Delete($resolved,$true)}
}
