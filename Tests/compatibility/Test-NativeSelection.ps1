param([Parameter(Mandatory)][string]$Executable,[string]$LegacyRoot='D:\Tools\ModpackTools')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-selection-'+[guid]::NewGuid().ToString('N'))
try {
    foreach($directory in @('.modpack','mods','resourcepacks','shaderpacks','config')){[void][IO.Directory]::CreateDirectory((Join-Path $fixture $directory))}
    [IO.File]::WriteAllText((Join-Path $fixture '.modpack/project.psd1'),"@{SchemaVersion=1;Id='fixture'}")
    [IO.File]::WriteAllText((Join-Path $fixture 'pack.toml'),'[index]')
    [IO.File]::WriteAllText((Join-Path $fixture 'mods/alpha.pw.toml'),"name = `"Alpha`"`nfilename = `"managed.jar`"`nside = `"client`"`n[update.modrinth]`nmod-id = `"alpha`"`n")
    [IO.File]::WriteAllText((Join-Path $fixture 'resourcepacks/resource.pw.toml'),"name = `"É Resource`"`nfilename = `"Ra.zip`"`n")
    [IO.File]::WriteAllText((Join-Path $fixture 'shaderpacks/shader.pw.toml'),"name = `"Shader`"`n")
    foreach($kind in @('resourcepacks','shaderpacks')){[IO.File]::WriteAllText((Join-Path $fixture "$kind/local.zip"),'local display only')}
    $archive=[IO.Compression.ZipFile]::Open((Join-Path $fixture 'mods/bravo.jar'),[IO.Compression.ZipArchiveMode]::Create)
    try{$writer=[IO.StreamWriter]::new($archive.CreateEntry('fabric.mod.json').Open());try{$writer.Write('{"name":"Bravo","environment":"server"}')}finally{$writer.Dispose()}}finally{$archive.Dispose()}
    [IO.File]::WriteAllText((Join-Path $fixture 'config/defaultoptions-common.toml'),'defaultResourcePacks=["file/Ra.zip","vanilla","file/missing.zip"]')
    [IO.File]::WriteAllText((Join-Path $fixture '.modpack/metadata.psd1'),"@{Categories=@{qol=@{}};Mods=@{'modrinth:alpha'=@{Category='QOL'}}}")
    $runner=Join-Path $fixture 'legacy.ps1';$queryPath=Join-Path $fixture 'query.json'
    [IO.File]::WriteAllText($runner,@'
param($ModulePath,$RootPath,$QueryPath)
Import-Module $ModulePath -Force
$query=Get-Content -LiteralPath $QueryPath -Raw|ConvertFrom-Json -AsHashtable
& (Get-Module ModpackTools) {
 param($root,$query)
 function Physical($items){return ,@(foreach($item in $items){$row=[ordered]@{id=$item.Id;kind=$item.Kind;name=$item.Name;filename=$item.Filename;side=$item.Side;source=$item.Source;metadata_path=$item.MetadataPath;category=$item.Category};if($item.Kind -eq 'mod'){$row.invalid_category=$item.InvalidCategory};if($item.Source -eq 'packwiz'){$row.version_id=$item.VersionId;$row.pinned=$item.Pinned};$row})}
 try {
  $snapshot=Get-ModpackInventory -Project (Read-ModpackProject $root);$parameters=@{Inventory=$snapshot}
  foreach($key in $query.Keys){$parameters[$(if($key -eq 'match'){'Search'}else{$key})]=$query[$key]}
  $view=Select-ModpackInventory @parameters
  $active=@(foreach($item in $view.ActiveResources){[ordered]@{id=$item.Id;kind=$item.Kind;name=$item.Name;filename=$item.Filename;source=$item.Source;enabled=$item.Enabled;priority=$item.Priority}})
  ConvertTo-Json -InputObject ([ordered]@{mods=(Physical $view.Mods);active=$active;inactive=(Physical $view.InactiveResources);shaders=(Physical $view.Shaders);included_types=@($view.IncludedTypes);filters=@($view.Filters);total_matches=$view.TotalMatches}) -Depth 20 -Compress
 }catch{ConvertTo-Json -InputObject @{failure=$true;id=$_.Exception.Data['ModpackTools.ErrorId'];category=[string][Management.Automation.ErrorCategory]$_.Exception.Data['ModpackTools.ErrorCategory'];target=$_.Exception.Data['ModpackTools.TargetObject'];hint=([regex]::Match($_.Exception.Message,'(?m)^Try:\s*(.*)$').Groups[1].Value.TrimEnd())} -Compress}
} $RootPath $query
'@)
    $queries=@(@{},@{type='mods'},@{type='RESOURCES'},@{type='shaders'},@{side='host'},@{category='QOL'},@{source='missing'},@{state='inactive';source='local'},@{match='Alpha modrinth:'},@{match='ALPHA'},@{match="E$([char]769)"},@{type="m$([char]173)od"},@{side="ho$([char]173)st"},@{source="lo$([char]173)cal"},@{state="a$([char]173)ctive"},@{category='absent'},@{type='shader';category='qol'},@{side='client';state='active'},@{type='mod';state='inactive'},@{type='bad';side='bad';source='bad';state='bad'},@{side='bad';source='bad'},@{source='bad';state='bad'},@{state='bad';category='absent'},@{type=''},@{state=''},@{side='';source='';category='';match=''},@{match=' '})
    $success=0;$failure=0
    foreach($query in $queries){
        [IO.File]::WriteAllText($queryPath,($query|ConvertTo-Json -Compress));$options=@(foreach($key in $query.Keys){'--'+$key;[string]$query[$key]})
        $old=Invoke-NativeTestProcess (Get-Command pwsh).Source @('-NoProfile','-File',$runner,'-ModulePath',(Join-Path $LegacyRoot 'ModpackTools.psd1'),'-RootPath',$fixture,'-QueryPath',$queryPath)
        $new=Invoke-NativeTestProcess $Executable (@('--inspect-selected-inventory',$fixture)+$options)
        if($old.Exit -ne 0 -or $new.Stderr -cne ''){throw 'Selection transport failed'}
        $oldValue=$old.Stdout|ConvertFrom-Json;$newValue=$new.Stdout|ConvertFrom-Json
        if($oldValue.failure -eq $true){
            if($new.Exit -ne 1 -or $newValue.error.id -cne $oldValue.id -or $newValue.error.category -cne $oldValue.category -or $newValue.error.target -cne $oldValue.target -or $newValue.error.hint -cne $oldValue.hint){throw "Selection error mismatch: $($query|ConvertTo-Json -Compress) $($old.Stdout) $($new.Stdout)"};$failure++
        }else{
            if($new.Exit -ne 0 -or ($oldValue|ConvertTo-Json -Depth 20 -Compress) -cne ($newValue|ConvertTo-Json -Depth 20 -Compress)){throw "Selection mismatch: $($query|ConvertTo-Json -Compress) $($old.Stdout) $($new.Stdout)"};$success++
        }
    }
    "Native inventory selection: $success differential successes and $failure exact domain errors passed"
} finally {
    $resolved=[IO.Path]::GetFullPath($fixture)
    if(-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-selection-*'){throw 'Unsafe fixture cleanup target'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
