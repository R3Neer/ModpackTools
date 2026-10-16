param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string]$NuExecutable,[Parameter(Mandatory)][string]$PackwizExecutable,[ValidateSet('None','Confirm','Eof')][string]$Interactive='None')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$sourceRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$temporaryBase=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$root=Join-Path $temporaryBase ('modpacktools-human removal-'+[guid]::NewGuid().ToString('N'))
$names=@('MODPACKTOOLS_CORE_PATH','MODPACKTOOLS_CONFIG_HOME','MODPACKTOOLS_STATE_HOME','MODPACKTOOLS_PROJECT','APPDATA','LOCALAPPDATA')
$previous=@{};foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
try {
    $app=Join-Path $root 'installed app';$projects=Join-Path $root 'projects';$configuration=Join-Path $root 'configuration'
    foreach($directory in @((Join-Path $app 'Nushell'),$configuration,$projects,(Join-Path $root 'roaming'),(Join-Path $root 'local'))){[void][IO.Directory]::CreateDirectory($directory)}
    Copy-Item -LiteralPath $Executable -Destination (Join-Path $app 'modpacktools.exe')
    Copy-Item -LiteralPath (Join-Path $sourceRoot 'presentation') -Destination $app -Recurse
    foreach($name in @('modpack.nu','Native-Presentation.nu','Render-Native.nu','Native-Views.nu')){Copy-Item -LiteralPath (Join-Path $sourceRoot "Nushell/$name") -Destination (Join-Path $app "Nushell/$name")}
    $jar=Join-Path $root 'local fixture.jar';$archive=[IO.Compression.ZipArchive]::new([IO.File]::Create($jar),[IO.Compression.ZipArchiveMode]::Create)
    try{$writer=[IO.StreamWriter]::new($archive.CreateEntry('fabric.mod.json').Open(),[Text.UTF8Encoding]::new($false));try{$writer.Write('{"schemaVersion":1,"id":"remove_ui","version":"1.0","name":"Removal UI Local","environment":"*"}')}finally{$writer.Dispose()}}finally{$archive.Dispose()}
    $env:MODPACKTOOLS_CORE_PATH=Join-Path $app 'modpacktools.exe';$env:MODPACKTOOLS_CONFIG_HOME=$configuration;$env:MODPACKTOOLS_STATE_HOME=Join-Path $root 'state'
    $env:APPDATA=Join-Path $root 'roaming';$env:LOCALAPPDATA=Join-Path $root 'local';Remove-Item Env:MODPACKTOOLS_PROJECT -ErrorAction SilentlyContinue
    $driver=Join-Path $app 'driver.nu'
    [IO.File]::WriteAllText($driver,@'
use ./Nushell/modpack.nu
def main [packwiz: string, projects: string, jar: string, mode: string] {
    if (is-terminal --stdin) != ($mode != 'None') {error make {msg: 'Human removal fixture has the wrong terminal mode'}}
    $env.PATH = []
    modpack config set root $projects --no-human | ignore
    modpack config set packwiz $packwiz --no-human | ignore
    modpack project create remove-ui --name 'Removal UI Pack' --minecraft 1.21.1 --loader fabric --loader-version 0.16.10 --no-human | ignore
    modpack project use remove-ui --no-human | ignore
    let pack = (modpack project status --no-human).project.root
    mkdir ($pack | path join 'mods')
    cp $jar ($pack | path join 'mods/local.jar')
    let paths = ['pack.toml' 'index.toml' '.modpack/project.psd1' '.modpack/metadata.psd1' 'mods/local.jar']
    let before = ($paths | each {|path| open --raw ($pack | path join $path) | hash sha256})
    let pwd = $env.PWD
    if (modpack content remove local.jar --ascii --color never) != null {error make {msg: 'Cancelled removal leaked a pipeline value'}}
    if ($paths | each {|path| open --raw ($pack | path join $path) | hash sha256}) != $before {error make {msg: 'Cancelled/default-NO/EOF removal changed the project'}}
    if (modpack content remove local.jar --yes --dry-run --ascii --color never) != null {error make {msg: 'Human removal dry-run leaked a value'}}
    if ($paths | each {|path| open --raw ($pack | path join $path) | hash sha256}) != $before {error make {msg: 'Human removal dry-run changed the project'}}
    let value = if $mode == 'Confirm' {modpack content remove local.jar --ascii --color never} else {modpack content remove local.jar -y --ascii --color never}
    if $value != null {error make {msg: 'Applied human removal leaked a value'}}
    if ($pack | path join 'mods/local.jar' | path exists) or not (modpack content list --type mod --no-human | is-empty) {error make {msg: 'Human removal did not remove the local item'}}
    if $env.MODPACKTOOLS_PROJECT != 'remove-ui' or $env.PATH != [] or $env.PWD != $pwd {error make {msg: 'Human removal changed parent session state'}}
    print 'Native human removal passed'
}
'@)
    $arguments=@('--no-config-file',$driver,([IO.Path]::GetFullPath($PackwizExecutable)),$projects,$jar,$Interactive)
    if($Interactive -ne 'None'){
        & $NuExecutable @arguments
        if($LASTEXITCODE -ne 0){throw 'Interactive native human removal failed'}
    }else{
        $capture=Invoke-NativeTestProcess $NuExecutable $arguments -TimeoutMilliseconds 120000 -CloseInput
        if($capture.Exit -ne 0 -or $capture.Stdout.Trim() -cne 'Native human removal passed' -or $capture.Stderr.Contains([char]27)){throw "Native human removal failed: $($capture.Stdout) $($capture.Stderr)"}
        foreach($text in @('Removal UI Local: installed -> removed (requested)','Would remove mods/local.jar','Removal cancelled; no project changes applied.','Dry run:','file change(s) planned; nothing has been changed.','Removed mods/local.jar','file change(s) applied.')){if(-not $capture.Stderr.Contains($text)){throw ('Missing human removal information: '+$text)}}
        'Bridge-free native human removal passed with closed stdin, dry-run, explicit approval and empty PATH'
    }
} finally {
    foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name],'Process')}
    $resolved=[IO.Path]::GetFullPath($root)
    if(-not $resolved.StartsWith($temporaryBase,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-human removal-*'){throw 'Unsafe human-removal fixture cleanup'}
    if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
