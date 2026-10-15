param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string]$NuExecutable,[Parameter(Mandatory)][string]$PackwizExecutable,[switch]$IncludeHelp)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$sourceRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$temporaryBase=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$root=Join-Path $temporaryBase ('modpacktools-human doctor-'+[guid]::NewGuid().ToString('N'))
$names=@('PATH','MODPACKTOOLS_CORE_PATH','MODPACKTOOLS_CONFIG_HOME','MODPACKTOOLS_STATE_HOME','MODPACKTOOLS_PROJECT','NO_COLOR')
$previous=@{};foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
try {
    $app=Join-Path $root 'installed app spaces';$configuration=Join-Path $root 'configuration'
    [void][IO.Directory]::CreateDirectory((Join-Path $app 'Nushell'));[void][IO.Directory]::CreateDirectory($configuration)
    Copy-Item -LiteralPath $Executable -Destination (Join-Path $app 'modpacktools.exe')
    Copy-Item -LiteralPath (Join-Path $sourceRoot 'presentation') -Destination $app -Recurse
    foreach($name in @('modpack.nu','Native-Presentation.nu','Render-Native.nu')){Copy-Item -LiteralPath (Join-Path $sourceRoot "Nushell/$name") -Destination (Join-Path $app "Nushell/$name")}
    [IO.File]::WriteAllText((Join-Path $configuration 'config.psd1'),"@{PackwizPath='$($PackwizExecutable.Replace("'","''"))'}")
    $env:MODPACKTOOLS_CORE_PATH=Join-Path $app 'modpacktools.exe';$env:MODPACKTOOLS_CONFIG_HOME=$configuration;$env:MODPACKTOOLS_STATE_HOME=Join-Path $root 'state';$env:PATH=''
    Remove-Item Env:MODPACKTOOLS_PROJECT -ErrorAction SilentlyContinue
    $driver=Join-Path $app 'driver.nu'
    [IO.File]::WriteAllText($driver,"use ./Nushell/modpack.nu`ndef main [] {modpack doctor --ascii --color never}`n")
    $capture=Invoke-NativeTestProcess $NuExecutable @('--no-config-file',$driver) -TimeoutMilliseconds 30000
    if($capture.Exit -ne 0 -or $capture.Stdout -ne '' -or $capture.Stderr -notmatch 'SYSTEM' -or $capture.Stderr -notmatch 'PACKWIZ' -or $capture.Stderr -notmatch '0.5.2' -or $capture.Stderr.Contains([char]27)){throw "Human doctor channel/renderer failed: $($capture.Stdout) $($capture.Stderr)"}
    $env:NO_COLOR='1'
    [IO.File]::WriteAllText($driver,"use ./Nushell/modpack.nu`ndef main [] {modpack doctor --color always}`n")
    $capture=Invoke-NativeTestProcess $NuExecutable @('--no-config-file',$driver) -TimeoutMilliseconds 30000
    if($capture.Exit -ne 0 -or $capture.Stdout -ne '' -or -not $capture.Stderr.Contains([char]27)){throw 'Explicit always did not override NO_COLOR'}
    if($IncludeHelp){
        [IO.File]::WriteAllText($driver,"use ./Nushell/modpack.nu`ndef main [] {modpack --help --ascii --color never}`n")
        $capture=Invoke-NativeTestProcess $NuExecutable @('--no-config-file',$driver) -TimeoutMilliseconds 30000
        if($capture.Exit -ne 0 -or $capture.Stdout -ne '' -or $capture.Stderr -notmatch 'MODPACKTOOLS' -or $capture.Stderr -notmatch 'USAGE' -or $capture.Stderr -notmatch 'self-update' -or $capture.Stderr.Contains([char]27)){throw "Native global human help failed: $($capture.Stderr)"}
        [IO.File]::WriteAllText($driver,"use ./Nushell/modpack.nu`ndef main [] {modpack doctor --help --ascii --color never}`n")
        $capture=Invoke-NativeTestProcess $NuExecutable @('--no-config-file',$driver) -TimeoutMilliseconds 30000
        if($capture.Exit -ne 0 -or $capture.Stdout -ne '' -or $capture.Stderr -notmatch 'DOCTOR' -or $capture.Stderr -notmatch '--details' -or $capture.Stderr.Contains([char]27)){throw "Native topic human help failed: $($capture.Stderr)"}
    }
    Remove-Item -LiteralPath (Join-Path $app 'presentation/r3cli/resources.json')
    [IO.File]::WriteAllText($driver,"use ./Nushell/modpack.nu`ndef main [] {let report = (modpack doctor --no-human); if (`$report.checks | where label == 'R3CLI' | get status) != ['fail'] {error make {msg: 'Missing R3CLI was not diagnosed'}}}`n")
    $capture=Invoke-NativeTestProcess $NuExecutable @('--no-config-file',$driver) -TimeoutMilliseconds 30000
    if($capture.Exit -ne 0 -or $capture.Stdout -ne '' -or $capture.Stderr -ne ''){throw "Machine doctor depends on presentation resources: $($capture.Stderr)"}
    [IO.File]::WriteAllText($driver,"use ./Nushell/modpack.nu`ndef main [] {modpack doctor --ascii --color never}`n")
    $capture=Invoke-NativeTestProcess $NuExecutable @('--no-config-file',$driver) -TimeoutMilliseconds 30000
    if($capture.Exit -eq 0 -or $capture.Stdout -ne '' -or $capture.Stderr -notmatch 'R3CLI package file is missing'){throw 'Human doctor accepted missing resources or invoked legacy fallback'}
    'Native human doctor uses packaged R3CLI on stderr; machine mode survives missing resources without PowerShell'
} finally {
    foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name],'Process')}
    $resolved=[IO.Path]::GetFullPath($root)
    if(-not $resolved.StartsWith($temporaryBase,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-human doctor-*'){throw 'Unsafe human fixture cleanup'}
    if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
