param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)][string]$LegacyRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$run=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-current-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($run)
$request=Join-Path $run 'request.json'
$runner=Join-Path $run 'legacy.ps1'
$previousProject=$env:MODPACKTOOLS_PROJECT
$previousConfig=$env:MODPACKTOOLS_CONFIG_HOME
try {
    $env:MODPACKTOOLS_CONFIG_HOME=Join-Path $run 'missing-config'
    $env:MODPACKTOOLS_PROJECT='environment-stale'
    [IO.File]::WriteAllText($runner, @'
param($ModulePath,$RequestPath)
Import-Module $ModulePath -Force
$request=Get-Content -Raw -LiteralPath $RequestPath | ConvertFrom-Json
& (Get-Module ModpackTools) {
    param($active,$tokens)
    $script:ActiveProjectId=$active
    try { modpack @tokens } catch {}
} $request.context.active_project $request.arguments
'@)
    $count=0
    foreach($active in @($null,'',' ','stale','á O''Brien')) {
        foreach($tokens in @(,@('project','current'); ,@('PROJECT','CURRENT'); ,@('project','current','extra'); ,@('project','current','extra','second'); ,@('project','current','extra','--project','absent'))) {
            $spec=@{schema_version=1;arguments=@($tokens)+@('--json','--no-human');context=@{active_project=$active}}
            [IO.File]::WriteAllText($request,($spec|ConvertTo-Json -Depth 5 -Compress))
            $legacy=Invoke-NativeTestProcess (Get-Command pwsh).Source @('-NoProfile','-File',$runner,'-ModulePath',(Join-Path $LegacyRoot 'ModpackTools.psd1'),'-RequestPath',$request)
            $native=Invoke-NativeTestProcess $Executable @('--request-path',$request)
            $expected=$legacy.Stdout|ConvertFrom-Json
            $actual=$native.Stdout|ConvertFrom-Json
            if(-not $expected.ok){$expected.arguments=@($expected.arguments|Where-Object {$null -ne $_})}
            if(($expected|ConvertTo-Json -Depth 20 -Compress) -cne ($actual|ConvertTo-Json -Depth 20 -Compress) -or $native.Stderr -cne '' -or $native.Exit -ne $(if($expected.ok){0}else{1})){throw "Current mismatch: $($tokens -join ' ') active=<$active>`n$($legacy.Stdout)`n$($native.Stdout)"}
            $count++
        }
    }
    $fallback=Invoke-NativeTestProcess $Executable @('project','current')
    if($fallback.Exit -ne 0 -or $fallback.Stderr -cne '' -or ($fallback.Stdout|ConvertFrom-Json).data.active_project -cne 'environment-stale'){throw 'Direct current environment fallback failed'}
    [IO.File]::WriteAllText($request,'{"schema_version":1,"arguments":["project","current"]}')
    $fallback=Invoke-NativeTestProcess $Executable @('--request-path',$request)
    if($fallback.Exit -ne 0 -or $fallback.Stderr -cne '' -or ($fallback.Stdout|ConvertFrom-Json).data.active_project -cne 'environment-stale'){throw 'Request current environment fallback failed'}
    foreach($context in @('null','{}','{"active_project":1}','{"active_project":true}','{"active_project":[]}','{"active_project":null,"extra":1}','{"active_project":null,"active_project":"second"}','{"active_project":"\u0000"}','{"active_project":"\uD800"}','{"\uD800":null}','{"other":null}')) {
        [IO.File]::WriteAllText($request,('{"schema_version":1,"arguments":["project","current"],"context":'+$context+'}'))
        $native=Invoke-NativeTestProcess $Executable @('--request-path',$request)
        if($native.Exit -ne 2 -or $native.Stderr -cne '' -or ($native.Stdout|ConvertFrom-Json).error.id -cne 'Core.InvalidRequest'){throw "Invalid context accepted: $context"}
    }
    "Native current: $count differential cases, 2 environment fallbacks and 11 invalid contexts passed"
} finally {
    $env:MODPACKTOOLS_PROJECT=$previousProject; $env:MODPACKTOOLS_CONFIG_HOME=$previousConfig
    [IO.File]::Delete($request); [IO.File]::Delete($runner); [IO.Directory]::Delete($run)
}
