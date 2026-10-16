param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)][string]$LegacyRoot,
    [string]$NuExecutable = 'D:\Desarrollo\Toolchains\Nushell\bin\nu.exe')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$run=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-search-'+[guid]::NewGuid().ToString('N'))
$config=Join-Path $run 'config with spaces'; [void][IO.Directory]::CreateDirectory($config)
$previous=@{}; foreach($name in @('MODPACKTOOLS_CONFIG_HOME','MODPACKTOOLS_PROJECT','MODPACKTOOLS_CORE_PATH')){$previous[$name]=[Environment]::GetEnvironmentVariable($name)}
try {
    $env:MODPACKTOOLS_CONFIG_HOME=$config; $env:MODPACKTOOLS_PROJECT=$null; $env:MODPACKTOOLS_CORE_PATH=$Executable
    # Malformed config must be irrelevant to global search validation.
    [IO.File]::WriteAllText((Join-Path $config 'config.psd1'),'not a data file')
    foreach($tail in @(,@(); ,@('--wat'); ,@('query','--limit'); ,@('query','--limit','0'); ,@('query','--limit','51','--type','bad'); ,@(' ','--type','bad'); ,@('query','--type','bad'))) {
        $tokens=@('content','search')+$tail+@('--json','--no-human')
        $request=Join-Path $run 'request.json'; [IO.File]::WriteAllText($request,(@{arguments=$tokens}|ConvertTo-Json -Compress))
        $old=Invoke-NativeTestProcess (Get-Command pwsh).Source @('-NoProfile','-File',(Join-Path $LegacyRoot 'Nushell/Invoke-ModpackBridge.ps1'),'-RequestPath',$request) -TimeoutMilliseconds 30000
        $new=Invoke-NativeTestProcess $Executable $tokens
        $expected=$old.Stdout|ConvertFrom-Json -AsHashtable; $actual=$new.Stdout|ConvertFrom-Json -AsHashtable
        $expected.arguments=@($expected.arguments|Where-Object{$null -ne $_})
        if($new.Exit -ne 1 -or $new.Stderr -or ($expected|ConvertTo-Json -Depth 100 -Compress) -cne ($actual|ConvertTo-Json -Depth 100 -Compress)){throw "Search differential failed:`n$($old.Stdout)`n$($new.Stdout)"}
    }
    if([IO.File]::Exists((Join-Path $config 'last-search.json'))){throw 'Failed search published references'}
    $module=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../Nushell/modpack.nu')).Replace('\','/')
    $script=Join-Path $run 'native only.nu'
    [IO.File]::WriteAllText($script,"use '$module' main; `$env.PATH = []; let failure = (try { modpack content search query --limit 0 --no-human; null } catch {|err| `$err.msg }); if `$failure == null or `$failure !~ 'ModpackTools.Option.InvalidLimit' { error make {msg: 'search native failure'} }; print 'PASS: native-only Nu search validation'")
    $nu=Invoke-NativeTestProcess $NuExecutable @('--no-config-file',$script)
    if($nu.Exit -or $nu.Stderr){throw "Nu search failure transport: $($nu.Stderr)"}
    'PASS: seven public search validation differentials and fresh Nu without PowerShell'
} finally {
    foreach($name in $previous.Keys){[Environment]::SetEnvironmentVariable($name,$previous[$name])}
    if([IO.Directory]::Exists($run)){[IO.Directory]::Delete($run,$true)}
}
