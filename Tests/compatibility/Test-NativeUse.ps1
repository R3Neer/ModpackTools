param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)][string]$LegacyRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$run=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-use-'+[guid]::NewGuid().ToString('N'))
$previousProject=$env:MODPACKTOOLS_PROJECT
$previousConfig=$env:MODPACKTOOLS_CONFIG_HOME
[void][IO.Directory]::CreateDirectory($run)
$request=Join-Path $run 'request.json'
$runner=Join-Path $run 'legacy.ps1'
try {
    $config=Join-Path $run 'config'
    $projects=Join-Path $run 'packs'
    [void][IO.Directory]::CreateDirectory($config)
    $env:MODPACKTOOLS_CONFIG_HOME=$config; $env:MODPACKTOOLS_PROJECT='wrong-environment'
    foreach($id in @('ALPHA','beta')) {
        $pack=Join-Path $projects $id
        [void][IO.Directory]::CreateDirectory((Join-Path $pack '.modpack'))
        [IO.File]::WriteAllText((Join-Path $pack '.modpack/project.psd1'),"@{SchemaVersion=1;Id='$id'}")
        [IO.File]::WriteAllText((Join-Path $pack 'pack.toml'),'[index]')
    }
    $configuration=Join-Path $config 'config.psd1'
    [IO.File]::WriteAllText($configuration,"@{Root='$($projects.Replace("'","''"))'}")
    [IO.File]::WriteAllText($runner,@'
param($ModulePath,$RequestPath)
Import-Module $ModulePath -Force
$request=Get-Content -Raw -LiteralPath $RequestPath | ConvertFrom-Json
& (Get-Module ModpackTools) {
    param($active,$tokens)
    $script:ActiveProjectId=$active
    try { modpack @tokens } catch {}
} $request.context.active_project $request.arguments
'@)
    $script:count=0
    function Assert-UseCase([string[]]$tokens,[AllowNull()][object]$active) {
        $spec=@{schema_version=1;arguments=@($tokens)+@('--json','--no-human');context=@{active_project=$active}}
        [IO.File]::WriteAllText($request,($spec|ConvertTo-Json -Depth 5 -Compress))
        $legacy=Invoke-NativeTestProcess (Get-Command pwsh).Source @('-NoProfile','-File',$runner,'-ModulePath',(Join-Path $LegacyRoot 'ModpackTools.psd1'),'-RequestPath',$request)
        $native=Invoke-NativeTestProcess $Executable @('--request-path',$request)
        $expected=$legacy.Stdout|ConvertFrom-Json; $actual=$native.Stdout|ConvertFrom-Json
        if(-not $expected.ok){$expected.arguments=@($expected.arguments|Where-Object {$null -ne $_})}
        if(($expected|ConvertTo-Json -Depth 20 -Compress) -cne ($actual|ConvertTo-Json -Depth 20 -Compress) -or $native.Stderr -cne '' -or $native.Exit -ne $(if($expected.ok){0}else{1})){throw "Use mismatch: $($tokens -join ' ') active=<$active>`n$($legacy.Stdout)`n$($native.Stdout)"}
        $script:count++
    }
    foreach($tokens in @(,@('project','use','alpha'); ,@('PROJECT','USE','beta'); ,@('project','use','missing'); ,@('project','use',' alpha '); ,@('project','use','--wat'); ,@('project','use','--'); ,@('project','use'); ,@('project','use','alpha','beta'); ,@('project','use','alpha','--project','forbidden'); ,@('project','use','alpha','extra','--project','forbidden'))) {Assert-UseCase $tokens 'beta'}
    foreach($active in @($null,'',' ','alpha','missing')) {Assert-UseCase @('project','use',' ') $active}
    [IO.File]::WriteAllText($request,'{"schema_version":1,"arguments":["project","use",""],"context":{"active_project":"alpha"}}')
    $empty=Invoke-NativeTestProcess $Executable @('--request-path',$request)
    $failure=$empty.Stdout|ConvertFrom-Json
    if($empty.Exit -ne 1 -or $empty.Stderr -cne '' -or $failure.error.id -cne 'ModpackTools.Project.InvalidSelection' -or $failure.error.category -cne 'InvalidArgument' -or $failure.error.target -cne '' -or $failure.error.message -notmatch 'Try: modpack project use <id>'){throw 'E04 empty selector mismatch'}
    [IO.File]::Delete($configuration)
    Assert-UseCase @('project','use',' ') $null
    Assert-UseCase @('project','use','alpha') $null
    [IO.File]::WriteAllText($configuration,"@{Root='$($projects.Replace("'","''"))'}")
    $duplicate=Join-Path $projects 'duplicate'
    [void][IO.Directory]::CreateDirectory((Join-Path $duplicate '.modpack'))
    [IO.File]::WriteAllText((Join-Path $duplicate '.modpack/project.psd1'),"@{SchemaVersion=1;Id='alpha'}")
    [IO.File]::WriteAllText((Join-Path $duplicate 'pack.toml'),'[index]')
    Assert-UseCase @('project','use','missing') $null
    "Native use: $script:count differential cases and explicit E04 passed"
} finally {
    $env:MODPACKTOOLS_PROJECT=$previousProject; $env:MODPACKTOOLS_CONFIG_HOME=$previousConfig
    $resolved=[IO.Path]::GetFullPath($run)
    if(-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-use-*'){throw 'Unsafe fixture cleanup target'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
