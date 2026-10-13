param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)][string]$StubExecutable)
$ErrorActionPreference='Stop'
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-ps-use-'+[guid]::NewGuid().ToString('N'))
$previousCore=$env:MODPACKTOOLS_CORE_PATH; $previousConfig=$env:MODPACKTOOLS_CONFIG_HOME; $previousProject=$env:MODPACKTOOLS_PROJECT
$previousResponse=$env:MODPACKTOOLS_TEST_RESPONSE_PATH; $previousExit=$env:MODPACKTOOLS_TEST_RESPONSE_EXIT
try {
    [void][IO.Directory]::CreateDirectory($fixture)
    $env:MODPACKTOOLS_CORE_PATH=$Executable; $env:MODPACKTOOLS_CONFIG_HOME=$fixture; $env:MODPACKTOOLS_PROJECT='wrong-environment'
    [IO.File]::WriteAllText((Join-Path $fixture 'config.psd1'),"@{Root='$($fixture.Replace("'","''"))'}")
    foreach($id in @('ALPHA','beta')) {
        $pack=Join-Path $fixture $id
        [void][IO.Directory]::CreateDirectory((Join-Path $pack '.modpack'))
        [IO.File]::WriteAllText((Join-Path $pack '.modpack/project.psd1'),"@{SchemaVersion=1;Id='$id'}")
        [IO.File]::WriteAllText((Join-Path $pack 'pack.toml'),'[index]')
    }
    Import-Module (Join-Path $PSScriptRoot '../../ModpackTools.psd1') -Force
    $module=Get-Module ModpackTools
    & $module {
        function script:Initialize-MpConsole { throw 'Renderer must not be initialized' }
        function script:Invoke-MpUse { throw 'Legacy use must not run' }
    }
    foreach($tokens in @(,@('project','use','alpha'); ,@('PROJECT','USE','ALPHA'); ,@('project','use','--ascii','alpha'); ,@('--ascii','project','use','alpha'))) {
        & $module {$script:ActiveProjectId='beta'}
        $result=modpack @tokens --json --no-human | ConvertFrom-Json
        if(-not $result.ok -or $result.data.active_project -cne 'ALPHA' -or (& $module {$script:ActiveProjectId}) -cne 'ALPHA'){throw 'Native use did not update module session after success'}
        if($env:MODPACKTOOLS_PROJECT -cne 'wrong-environment'){throw 'PS use changed environment instead of module state'}
        $current=modpack project current --json --no-human | ConvertFrom-Json
        if($current.data.active_project -cne 'ALPHA'){throw 'Native use/current session roundtrip failed'}
    }
    & $module {$script:ActiveProjectId='beta'}
    $result=modpack project use ' ' --json --no-human | ConvertFrom-Json
    if($result.data.active_project -cne 'beta'){throw 'Use whitespace ignored explicit module snapshot'}
    foreach($selector in @('missing','')) {
        $output=[Collections.Generic.List[string]]::new()
        try {modpack project use $selector --json --no-human | ForEach-Object {$output.Add($_)}; throw 'Failure was accepted'}
        catch {if($_.FullyQualifiedErrorId -notlike 'ModpackTools.Project.*'){throw}}
        if($output.Count -ne 1 -or ($output[0]|ConvertFrom-Json).ok -or (& $module {$script:ActiveProjectId}) -cne 'beta'){throw 'Use failure changed state or envelope count'}
    }
    $output=[Collections.Generic.List[string]]::new()
    try {modpack project use --json --no-human | ForEach-Object {$output.Add($_)}; throw 'Missing selector accepted'}
    catch {if($_.FullyQualifiedErrorId -notlike 'ModpackTools.Command.InvalidArguments*' -or $_.TargetObject -isnot [array] -or $_.TargetObject.Count -ne 0){throw}}
    if($output.Count -ne 1 -or $null -ne ($output[0]|ConvertFrom-Json).error.target){throw 'Missing selector JSON target changed'}
    $env:MODPACKTOOLS_CORE_PATH=$StubExecutable
    $env:MODPACKTOOLS_TEST_RESPONSE_PATH=Join-Path $fixture 'reply.json'
    $env:MODPACKTOOLS_TEST_RESPONSE_EXIT='0'
    for($case=0;$case -lt 11;$case++) {
        $env:MODPACKTOOLS_TEST_RESPONSE_EXIT='0'
        $reply=@{schema_version=1;ok=$true;command='project';arguments=@('use','alpha');data=@{active_project='ALPHA'}}
        switch($case) {
            0 {$reply.data.active_project=$null}
            1 {$reply.data.active_project=''}
            2 {$reply.data.active_project=42}
            3 {$reply.command='content'}
            4 {$reply.arguments=@('current','alpha')}
            5 {$reply.arguments=@('use')}
            6 {$reply.data.Remove('active_project')}
            7 {$env:MODPACKTOOLS_TEST_RESPONSE_EXIT='1'}
            8 {$reply.data.active_project=' '}
            9 {$reply.command='PROJECT'}
            10 {$reply.arguments=@('USE','alpha')}
        }
        [IO.File]::WriteAllText($env:MODPACKTOOLS_TEST_RESPONSE_PATH,($reply|ConvertTo-Json -Depth 5 -Compress))
        $output=[Collections.Generic.List[string]]::new()
        try {modpack project use alpha --json --no-human | ForEach-Object {$output.Add($_)}; throw 'Malformed success was accepted'}
        catch {if($_.FullyQualifiedErrorId -notlike 'ModpackTools.Core.*'){throw}}
        if($output.Count -ne 1 -or ($output[0]|ConvertFrom-Json).ok -or (& $module {$script:ActiveProjectId}) -cne 'beta'){throw 'Invalid response changed session'}
    }
    'Native PS use/current state transitions, target representations and 11 malformed-response protections passed'
} finally {
    $env:MODPACKTOOLS_CORE_PATH=$previousCore; $env:MODPACKTOOLS_CONFIG_HOME=$previousConfig; $env:MODPACKTOOLS_PROJECT=$previousProject
    $env:MODPACKTOOLS_TEST_RESPONSE_PATH=$previousResponse; $env:MODPACKTOOLS_TEST_RESPONSE_EXIT=$previousExit
    Remove-Module ModpackTools -ErrorAction SilentlyContinue
    $resolved=[IO.Path]::GetFullPath($fixture)
    if(-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-ps-use-*'){throw 'Unsafe fixture cleanup target'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
