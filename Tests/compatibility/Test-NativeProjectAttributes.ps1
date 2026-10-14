param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string]$LegacyRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-attributes-'+[guid]::NewGuid().ToString('N'))
$previousConfig=$env:MODPACKTOOLS_CONFIG_HOME
try {
    [void][IO.Directory]::CreateDirectory($fixture)
    $configuration=Join-Path $fixture 'configuration'; $projects=Join-Path $fixture 'projects'
    [void][IO.Directory]::CreateDirectory($configuration)
    $env:MODPACKTOOLS_CONFIG_HOME=$configuration
    [IO.File]::WriteAllText((Join-Path $configuration 'config.psd1'),"@{Root='$($projects.Replace("'","''"))'}")
    foreach($id in @('normal','system','hidden','system-hidden')) {
        $pack=Join-Path $projects $id
        [void][IO.Directory]::CreateDirectory((Join-Path $pack '.modpack'))
        $schema=if($id -like '*hidden'){2}else{1}
        [IO.File]::WriteAllText((Join-Path $pack '.modpack/project.psd1'),"@{SchemaVersion=$schema;Id='$id'}")
        [IO.File]::WriteAllText((Join-Path $pack 'pack.toml'),'[index]')
        $attributes=[IO.File]::GetAttributes($pack)
        if($id -like '*system*'){$attributes=$attributes -bor [IO.FileAttributes]::System}
        if($id -like '*hidden'){$attributes=$attributes -bor [IO.FileAttributes]::Hidden}
        [IO.File]::SetAttributes($pack,$attributes)
    }
    $request=Join-Path $fixture 'request.json'
    foreach($tokens in @(,@('project','list'); ,@('project','use','system'); ,@('project','use','hidden'); ,@('project','use','system-hidden'))) {
        $tokens=@($tokens)+@('--json','--no-human')
        [IO.File]::WriteAllText($request,(@{arguments=$tokens}|ConvertTo-Json -Compress))
        $legacy=Invoke-NativeTestProcess (Get-Command pwsh).Source @('-NoProfile','-File',(Join-Path $LegacyRoot 'Nushell/Invoke-ModpackBridge.ps1'),'-RequestPath',$request)
        $native=Invoke-NativeTestProcess $Executable $tokens
        $expected=$legacy.Stdout|ConvertFrom-Json; $actual=$native.Stdout|ConvertFrom-Json
        if(-not $expected.ok){$expected.arguments=@($expected.arguments|Where-Object {$null -ne $_})}
        if(($expected|ConvertTo-Json -Depth 20 -Compress) -cne ($actual|ConvertTo-Json -Depth 20 -Compress) -or $native.Stderr -cne '' -or $native.Exit -ne $(if($expected.ok){0}else{1})){throw "Attribute discovery mismatch: $($tokens -join ' ')"}
    }
    'Native attributes: 4 differential cases include System-only projects and exclude hidden invalid projects'
} finally {
    $env:MODPACKTOOLS_CONFIG_HOME=$previousConfig
    $resolved=[IO.Path]::GetFullPath($fixture)
    if(-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-attributes-*'){throw 'Unsafe fixture cleanup target'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
