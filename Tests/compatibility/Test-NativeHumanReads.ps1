param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string]$NuExecutable,[Parameter(Mandatory)][string]$PackwizExecutable,[Parameter(Mandatory)][string]$StubExecutable)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$sourceRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$temporaryBase=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$root=Join-Path $temporaryBase ('modpacktools-human reads-'+[guid]::NewGuid().ToString('N'))
$names=@('MODPACKTOOLS_CORE_PATH','MODPACKTOOLS_CONFIG_HOME','MODPACKTOOLS_STATE_HOME','MODPACKTOOLS_PROJECT','APPDATA','LOCALAPPDATA')
$previous=@{};foreach($name in $names){$previous[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
try {
    $app=Join-Path $root 'installed app';$projects=Join-Path $root 'projects';$configuration=Join-Path $root 'configuration'
    foreach($directory in @((Join-Path $app 'Nushell'),$configuration,$projects,(Join-Path $root 'roaming'),(Join-Path $root 'local'))){[void][IO.Directory]::CreateDirectory($directory)}
    Copy-Item -LiteralPath $Executable -Destination (Join-Path $app 'modpacktools.exe')
    Copy-Item -LiteralPath (Join-Path $sourceRoot 'presentation') -Destination $app -Recurse
    foreach($name in @('modpack.nu','Native-Presentation.nu','Render-Native.nu','Native-Views.nu')){Copy-Item -LiteralPath (Join-Path $sourceRoot "Nushell/$name") -Destination (Join-Path $app "Nushell/$name")}
    $jar=Join-Path $root 'local fixture.jar';$archive=[IO.Compression.ZipArchive]::new([IO.File]::Create($jar),[IO.Compression.ZipArchiveMode]::Create)
    try {$writer=[IO.StreamWriter]::new($archive.CreateEntry('fabric.mod.json').Open(),[Text.UTF8Encoding]::new($false));try{$writer.Write('{"schemaVersion":1,"id":"ui_local","version":"1.0","name":"UI Local Mod","environment":"*"}')}finally{$writer.Dispose()}}finally{$archive.Dispose()}
    $env:MODPACKTOOLS_CORE_PATH=Join-Path $app 'modpacktools.exe';$env:MODPACKTOOLS_CONFIG_HOME=$configuration;$env:MODPACKTOOLS_STATE_HOME=Join-Path $root 'state'
    $env:APPDATA=Join-Path $root 'roaming';$env:LOCALAPPDATA=Join-Path $root 'local';Remove-Item Env:MODPACKTOOLS_PROJECT -ErrorAction SilentlyContinue
    $driver=Join-Path $app 'driver.nu'
    [IO.File]::WriteAllText($driver,@'
use ./Nushell/modpack.nu
def main [packwiz: string, projects: string, jar: string, stub: string] {
    $env.PATH = []
    modpack config set root $projects --no-human | ignore
    modpack config set packwiz $packwiz --no-human | ignore
    modpack project create read-ui --name 'Native UI Pack' --minecraft 1.21.1 --loader fabric --loader-version 0.16.10 --no-human | ignore
    modpack project use read-ui --no-human | ignore
    let pack = (modpack project status --no-human).project.root
    mkdir ($pack | path join 'mods')
    cp $jar ($pack | path join 'mods/local.jar')
    modpack build --no-human | ignore
    let paths = ['pack.toml' 'index.toml' '.modpack/project.psd1' '.modpack/metadata.psd1' 'mods/local.jar']
    let before = ($paths | each {|path| open --raw ($pack | path join $path) | hash sha256})
    for tokens in [['--version'] ['project' 'list'] ['project' 'current'] ['project' 'use' 'read-ui'] ['project' 'status'] ['--project' 'read-ui' 'project' 'status'] ['content' 'list'] ['category' 'list'] ['config' 'get' 'root'] ['config' 'get' 'packwiz'] ['diff']] {
        let value = (modpack ...$tokens --ascii --color never)
        if $value != null {error make {msg: $'Human command leaked a pipeline value: ($tokens | str join " ")'}}
    }
    let project = (modpack project status --no-human).project
    let previous_core = $env.MODPACKTOOLS_CORE_PATH
    let response_path = ($nu.temp-dir | path join $'modpacktools-human-provider-((random uuid)).json')
    $env.MODPACKTOOLS_CORE_PATH = $stub
    $env.MODPACKTOOLS_TEST_RESPONSE_PATH = $response_path
    $env.MODPACKTOOLS_TEST_RESPONSE_EXIT = '0'
    {schema_version: 1, ok: true, command: 'content', arguments: ['search' 'fixture'], data: {project: $project, search: {query: 'fixture', type: 'mod', total_hits: 1, results: [{number: 1, id: 'fixture-id', slug: 'fixture-slug', name: 'Search UI Result', type: 'mod', author: 'Fixture Author', downloads: 12345, description: 'A literal UI description.'}]}}} | to json | save --force $response_path
    if (modpack content search fixture --ascii --color never) != null {error make {msg: 'Human search leaked a value'}}
    {schema_version: 1, ok: true, command: 'content', arguments: ['versions' 'fixture'], data: {project: $project, versions: {item_name: 'Version UI Item', item_type: 'mod', items: [{number: 1, id: 'fixture-version', version: '1.2.3', version_type: 'release', filename: 'fixture.jar', published: '2026-10-16T00:00:00Z', installed: false}]}}} | to json | save --force $response_path
    if (modpack content versions fixture --ascii --color never) != null {error make {msg: 'Human versions leaked a value'}}
    rm --force $response_path
    hide-env --ignore-errors MODPACKTOOLS_TEST_RESPONSE_PATH MODPACKTOOLS_TEST_RESPONSE_EXIT
    $env.MODPACKTOOLS_CORE_PATH = $previous_core
    if ($paths | each {|path| open --raw ($pack | path join $path) | hash sha256}) != $before {error make {msg: 'Human read views changed the project'}}
    if $env.MODPACKTOOLS_PROJECT != 'read-ui' or $env.PATH != [] {error make {msg: 'Human read views changed selection or PATH'}}
    print 'Native human read views passed'
}
'@)
    $capture=Invoke-NativeTestProcess $NuExecutable @('--no-config-file',$driver,([IO.Path]::GetFullPath($PackwizExecutable)),$projects,$jar,([IO.Path]::GetFullPath($StubExecutable))) -TimeoutMilliseconds 120000
    if($capture.Exit -ne 0 -or $capture.Stdout.Trim() -cne 'Native human read views passed' -or $capture.Stderr.Contains([char]27)){throw "Native human read views failed: $($capture.Stdout) $($capture.Stderr)"}
    foreach($text in @('Native UI Pack','UI Local Mod','UNCLASSIFIED','CLASSIFICATIONS','No differences from the latest build.','ModpackTools 4.0.2','Search UI Result','Fixture Author','12345','Version UI Item','LATEST','fixture-version')){if(-not $capture.Stderr.Contains($text)){throw ('Missing native human view information: '+$text)}}
    if($capture.Stderr -notmatch 'source\s+configured'){throw 'Human config did not show the executable discovery source'}
    'Bridge-free native human read views passed with empty PATH and preserved project bytes'
} finally {
    foreach($name in $names){[Environment]::SetEnvironmentVariable($name,$previous[$name],'Process')}
    $resolved=[IO.Path]::GetFullPath($root)
    if(-not $resolved.StartsWith($temporaryBase,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-human reads-*'){throw 'Unsafe human-read fixture cleanup'}
    if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
