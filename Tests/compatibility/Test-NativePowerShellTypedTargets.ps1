param([Parameter(Mandatory)][string]$Executable, [string]$LegacyRoot='D:\Tools\ModpackTools')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-typed-targets-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($fixture)
$previousConfig=$env:MODPACKTOOLS_CONFIG_HOME
try {
    $env:MODPACKTOOLS_CONFIG_HOME=$fixture
    [IO.File]::WriteAllText((Join-Path $fixture 'config.psd1'),"@{Root='$($fixture.Replace("'","''"))'}")
    $pack=Join-Path $fixture 'False'
    [void][IO.Directory]::CreateDirectory((Join-Path $pack '.modpack'))
    [IO.File]::WriteAllText((Join-Path $pack '.modpack/project.psd1'),"@{SchemaVersion=1;Id='False'}")
    [IO.File]::WriteAllText((Join-Path $pack 'pack.toml'),'[index]')
    $runner=Join-Path $fixture 'run.ps1'; $request=Join-Path $fixture 'request.json'; $caught=Join-Path $fixture 'caught.json'
    [IO.File]::WriteAllText($runner,@'
param($ModulePath,$RequestPath,$CaughtPath,$CorePath)
$env:MODPACKTOOLS_CORE_PATH=$CorePath
Import-Module $ModulePath -Force
$tokens=@(Get-Content -Raw -LiteralPath $RequestPath|ConvertFrom-Json)
try {modpack @tokens}
catch {
    [IO.File]::WriteAllText($CaughtPath,([ordered]@{id=$_.FullyQualifiedErrorId;category=[string]$_.CategoryInfo.Category;target=$_.TargetObject}|ConvertTo-Json -Depth 10 -Compress))
}
'@)
    $cases=@(,@('project','current',42); ,@('project','current',$false); ,@('project','list',1.5); ,@('project','list',42,$false); ,@('project','use',42,43); ,@('project','use',42); ,@('project','use',$false); ,@('project','use',$true))
    foreach($tokens in $cases) {
        $tokens=@($tokens)+@('--json','--no-human')
        [IO.File]::WriteAllText($request,(ConvertTo-Json -InputObject $tokens -Depth 10 -Compress))
        $results=@()
        foreach($native in @($false,$true)) {
            [IO.File]::Delete($caught)
            $modulePath=if($native){Join-Path $PSScriptRoot '../../ModpackTools.psd1'}else{Join-Path $LegacyRoot 'ModpackTools.psd1'}
            $corePath=if($native){$Executable}else{''}
            $capture=Invoke-NativeTestProcess (Get-Command pwsh).Source @('-NoProfile','-File',$runner,'-ModulePath',$modulePath,'-RequestPath',$request,'-CaughtPath',$caught,'-CorePath',$corePath)
            $value=$capture.Stdout|ConvertFrom-Json
            if(-not $native -and -not $value.ok){$value.arguments=@($value.arguments|Where-Object {$null -ne $_})}
            $caughtValue=if([IO.File]::Exists($caught)){Get-Content -Raw -LiteralPath $caught|ConvertFrom-Json}else{$null}
            $results+=@{data=($value|ConvertTo-Json -Depth 20 -Compress);caught=($caughtValue|ConvertTo-Json -Depth 20 -Compress);stderr=$capture.Stderr;exit=$capture.Exit}
        }
        if($results[0].data -cne $results[1].data -or $results[0].caught -cne $results[1].caught -or $results[0].stderr -cne $results[1].stderr -or $results[0].exit -ne $results[1].exit){throw "Typed PS mismatch: $($tokens -join ' ')`n$($results|ConvertTo-Json -Depth 10)"}
    }
    'Typed PS targets: 8 native/legacy pairs preserve public and terminating-error types'
} finally {
    $env:MODPACKTOOLS_CONFIG_HOME=$previousConfig
    $resolved=[IO.Path]::GetFullPath($fixture)
    if(-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-typed-targets-*'){throw 'Unsafe fixture cleanup target'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
