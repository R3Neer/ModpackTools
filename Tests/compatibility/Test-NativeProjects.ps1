param([Parameter(Mandatory)][string]$Executable, [string]$LegacyRoot = (Join-Path $PSScriptRoot '../..'))
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$run=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-projects-'+[guid]::NewGuid().ToString('N'))
$packs=Join-Path $run "packs á O'Brien"
$config=Join-Path $run 'config'
[void][IO.Directory]::CreateDirectory($config)
$previous=$env:MODPACKTOOLS_CONFIG_HOME
try {
    $env:MODPACKTOOLS_CONFIG_HOME=$config
    foreach($id in @('beta','ALPHA')) {
        $root=Join-Path $packs $id
        [void][IO.Directory]::CreateDirectory((Join-Path $root '.modpack'))
        [IO.File]::WriteAllText((Join-Path $root '.modpack/project.psd1'), "@{SchemaVersion='1';Id='$id'}")
        [IO.File]::WriteAllText((Join-Path $root 'pack.toml'), "name = `"Pack $id`"`r`nversion = `"1.0`"`r`n[index]`r`nfile = `"index.toml`"`r`n[versions]`r`nminecraft = `"1.21.1`"`r`nfabric = `"0.16`"`r`nforge = `"1.0`"`r`n")
    }
    $configPath=Join-Path $config 'config.psd1'
    [IO.File]::WriteAllText($configPath, "@{ Root='$($packs.Replace("'","''"))' }")
    $cases=@(,@('project','list'); ,@('PROJECT','LIST'); ,@('project','list','--project','absent'); ,@('project','list','--wat'))
    function Assert-ProjectCase([string[]]$tokens) {
        $tokens=@($tokens)+@('--json','--no-human')
        $request=Join-Path $run 'request.json'
        [IO.File]::WriteAllText($request,(@{arguments=$tokens}|ConvertTo-Json -Compress))
        $legacy=Invoke-NativeTestProcess (Get-Command pwsh).Source @('-NoLogo','-NoProfile','-File',(Join-Path $LegacyRoot 'Nushell/Invoke-ModpackBridge.ps1'),'-RequestPath',$request)
        $native=Invoke-NativeTestProcess $Executable $tokens
        $expected=$legacy.Stdout|ConvertFrom-Json
        $actual=$native.Stdout|ConvertFrom-Json
        if(-not $expected.ok){$expected.arguments=@($expected.arguments|Where-Object {$null -ne $_})}
        if(($expected|ConvertTo-Json -Depth 30 -Compress) -cne ($actual|ConvertTo-Json -Depth 30 -Compress) -or $native.Exit -ne $(if($expected.ok){0}else{1}) -or $native.Stderr -cne ''){throw "Project differential mismatch: $($tokens -join ' ')`n$($legacy.Stdout)`n$($native.Stdout)"}
    }
    foreach($tokens in $cases) { Assert-ProjectCase $tokens }
    $edgeRoot=Join-Path $packs 'edge'
    [void][IO.Directory]::CreateDirectory((Join-Path $edgeRoot '.modpack'))
    $edgeDescriptor=Join-Path $edgeRoot '.modpack/project.psd1'
    [IO.File]::WriteAllText((Join-Path $edgeRoot 'pack.toml'), '[index]')
    [IO.File]::WriteAllText($edgeDescriptor, "@{SchemaVersion=1;Id='edge'}")
    Assert-ProjectCase @('project','list')
    foreach($number in @('1.234567891234567','1.0000000000000002','1234567891234567.0','99999999999999999999999999999999','-99999999999999999999999999999999')) {
        [IO.File]::WriteAllText($edgeDescriptor, "@{SchemaVersion=1;Id='edge';DisplayName=$number;DisplayVersion=$number;OutputName=$number}")
        Assert-ProjectCase @('project','list')
        [IO.File]::WriteAllText($edgeDescriptor, "@{SchemaVersion=1;Id=$number}")
        Assert-ProjectCase @('project','list')
    }
    foreach($number in @(('9'*309),('9'*310),('-'+('9'*309)))) {
        [IO.File]::WriteAllText($edgeDescriptor, "@{SchemaVersion=1;Id='edge';DisplayName=$number}")
        $overflowLiteral=Invoke-NativeTestProcess $Executable @('project','list','--json','--no-human')
        if($overflowLiteral.Exit -ne 1 -or ($overflowLiteral.Stdout|ConvertFrom-Json).error.id -cne 'ModpackTools.Project.InvalidDescriptor' -or $overflowLiteral.Stderr -cne ''){throw 'Non-importable integer became project success'}
    }
    [IO.File]::WriteAllText($edgeDescriptor, "@{SchemaVersion=1;Id='edge';DisplayName=`$null;DisplayVersion=`$null;OutputName=`$null}")
    Assert-ProjectCase @('project','list')
    [IO.File]::WriteAllText($edgeDescriptor, "@{SchemaVersion=1;Id='edge';DisplayName=1.5;DisplayVersion=1e3}")
    Assert-ProjectCase @('project','list')
    [IO.File]::WriteAllText($edgeDescriptor, "@{SchemaVersion=2.1;Id='edge'}")
    Assert-ProjectCase @('project','list')
    [IO.File]::WriteAllText($edgeDescriptor, "@{SchemaVersion=9223372036854775808;Id='edge'}")
    # This literal exceeds the legacy Int32 schema coercion: native reports a structured read failure.
    $overflow=Invoke-NativeTestProcess $Executable @('project','list','--json','--no-human')
    if($overflow.Exit -ne 1 -or ($overflow.Stdout|ConvertFrom-Json).error.id -ne 'Core.InputReadFailed' -or $overflow.Stderr -cne ''){throw 'Schema overflow was not a structured failure'}
    foreach($schema in @('1.0','1e0','0x1','0b1','')) {
        [IO.File]::WriteAllText($edgeDescriptor, "@{SchemaVersion='$schema';Id='edge'}")
        Assert-ProjectCase @('project','list')
    }
    foreach($id in @('1e309','99999999999999999999999999999')) {
        [IO.File]::WriteAllText($edgeDescriptor, "@{SchemaVersion=1;Id=$id}")
        Assert-ProjectCase @('project','list')
    }
    [IO.File]::WriteAllText($edgeDescriptor, "@{SchemaVersion=1;Id='edge'}")
    foreach($manifest in @("name = 'literal'`n", "`"name`" = `"quoted key`"`n", 'name = """multiline"""', "[versions] #ignored header`nfabric = `"0.16`"`n", "[ versions ]`nfabric = `"0.16`"`n")) {
        [IO.File]::WriteAllText((Join-Path $edgeRoot 'pack.toml'), $manifest)
        Assert-ProjectCase @('project','list')
    }
    [IO.File]::WriteAllText((Join-Path $edgeRoot 'pack.toml'), '')
    $emptyManifest=Invoke-NativeTestProcess $Executable @('project','list','--json','--no-human')
    if($emptyManifest.Exit -ne 1 -or ($emptyManifest.Stdout|ConvertFrom-Json).error.id -ne 'ModpackTools.Project.InvalidManifest' -or $emptyManifest.Stderr -cne ''){throw 'Empty manifest was not a structured failure'}
    [IO.File]::WriteAllText((Join-Path $edgeRoot 'pack.toml'), ('a='+('['*5000)+'0'+(']'*5000)))
    $deepManifest=Invoke-NativeTestProcess $Executable @('project','list','--json','--no-human')
    if($deepManifest.Exit -ne 1 -or ($deepManifest.Stdout|ConvertFrom-Json).error.id -ne 'ModpackTools.Project.InvalidManifest' -or $deepManifest.Stderr -cne ''){throw 'Deep TOML crashed or escaped structured failure'}
    [IO.File]::WriteAllText((Join-Path $edgeRoot 'pack.toml'), ('a=['+('0,'*100001)+'0]'))
    $largeManifest=Invoke-NativeTestProcess $Executable @('project','list','--json','--no-human')
    if($largeManifest.Exit -ne 1 -or ($largeManifest.Stdout|ConvertFrom-Json).error.id -ne 'ModpackTools.Project.InvalidManifest' -or $largeManifest.Stderr -cne ''){throw 'Large TOML escaped structured failure'}
    foreach($malformed in @(('name="unterminated'+"`n"+'x='+('['*5000)+'0'+(']'*5000)), ('x='+('[}'*5000)))) {
        [IO.File]::WriteAllText((Join-Path $edgeRoot 'pack.toml'), $malformed)
        $recovery=Invoke-NativeTestProcess $Executable @('project','list','--json','--no-human')
        if($recovery.Exit -ne 1 -or ($recovery.Stdout|ConvertFrom-Json).error.id -ne 'ModpackTools.Project.InvalidManifest' -or $recovery.Stderr -cne ''){throw 'Malformed TOML evaded the preflight bound'}
    }
    [IO.File]::WriteAllText((Join-Path $edgeRoot 'pack.toml'), ('name="'+('['*5000)+'"'))
    Assert-ProjectCase @('project','list')
    $emptyRoot=Join-Path $run 'empty-root'
    [void][IO.Directory]::CreateDirectory($emptyRoot)
    [IO.File]::WriteAllText($configPath, "@{Root='$($emptyRoot.Replace("'","''"))'}")
    $emptyCatalog=Invoke-NativeTestProcess $Executable @('project','list','--json','--no-human')
    $emptyResult=$emptyCatalog.Stdout|ConvertFrom-Json
    if($emptyCatalog.Exit -ne 0 -or -not $emptyResult.ok -or $emptyResult.data.projects.items -isnot [array] -or $emptyResult.data.projects.items.Count -ne 0 -or $emptyCatalog.Stderr -cne ''){throw 'Empty catalog was not a valid typed result'}
    'Native project catalog: 31 differential cases and 10 explicit error/empty/limit cases passed'
} finally {
    $env:MODPACKTOOLS_CONFIG_HOME=$previous
    # Resolved unique fixture root, created by this harness only.
    $resolved=[IO.Path]::GetFullPath($run)
    if(-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-projects-*'){throw 'Unsafe fixture cleanup target'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
