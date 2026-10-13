param([Parameter(Mandatory)][string]$Executable,[string]$LegacyRoot='D:\Tools\ModpackTools')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-numbering-'+[guid]::NewGuid().ToString('N'))
try {
 foreach($directory in @('.modpack','mods','resourcepacks','shaderpacks','config','_config')){[void][IO.Directory]::CreateDirectory((Join-Path $fixture $directory))}
 [IO.File]::WriteAllText((Join-Path $fixture '.modpack/project.psd1'),"@{SchemaVersion=1;Id='fixture'}")
 [IO.File]::WriteAllText((Join-Path $fixture 'pack.toml'),'[index]')
 foreach($filename in @('one','two')){[IO.File]::WriteAllText((Join-Path $fixture "mods/$filename.pw.toml"),"name = `"Same`"`nfilename = `"managed.jar`"`n[update.modrinth]`nmod-id = `"same`"`n")}
 [IO.File]::WriteAllText((Join-Path $fixture 'mods/local.jar'),'unreadable display fallback')
 [IO.File]::WriteAllText((Join-Path $fixture 'resourcepacks/A.zip'),'local resource')
 [IO.File]::WriteAllText((Join-Path $fixture 'resourcepacks/Z.zip'),'inactive resource')
 [IO.File]::WriteAllText((Join-Path $fixture 'shaderpacks/S.zip'),'local shader')
 [IO.File]::WriteAllText((Join-Path $fixture 'config/defaultoptions-common.toml'),'defaultResourcePacks=["file/missing.zip","vanilla","file/A.zip","file/A.zip"]')
 [IO.File]::WriteAllText((Join-Path $fixture '.modpack/metadata.psd1'),"@{Categories=@{unclassified=@{Order=0};qol=@{Order=1}};Mods=@{'modrinth:same'=@{Category='qol'}}}")
 $runner=Join-Path $fixture 'legacy.ps1';$queryPath=Join-Path $fixture 'query.json'
 [IO.File]::WriteAllText($runner,@'
param($ModulePath,$RootPath,$QueryPath)
Import-Module $ModulePath -Force
$query=Get-Content -LiteralPath $QueryPath -Raw|ConvertFrom-Json -AsHashtable
& (Get-Module ModpackTools) {
 param($root,$query)
 # E13 expectation: change only row Name sorts in the in-memory module.
 # Independent review retains the unmodified captures separately.
 foreach($functionName in @('Get-ModpackInventory','Get-ModpackInventoryReferenceItems')){
  $body=(Get-Command $functionName -CommandType Function).Definition
  Set-Item -Path ("Function:script:"+$functionName) -Value ([scriptblock]::Create($body.Replace('Sort-Object Name','Sort-Object Name -Stable')))
 }
 $script:ConfigHomeOverride=Join-Path $root '_config'
 function script:Write-Utf8TextFileAtomic {param($Path,$Text) $script:NumberingCapture=$Text|ConvertFrom-Json}
 function Physical($item){$row=[ordered]@{id=$item.Id;kind=$item.Kind;name=$item.Name;filename=$item.Filename;side=$item.Side;source=$item.Source;metadata_path=$item.MetadataPath;category=$item.Category};if($item.Kind -eq 'mod'){$row.invalid_category=$item.InvalidCategory};if($item.Source -eq 'packwiz'){$row.version_id=$item.VersionId;$row.pinned=$item.Pinned};return $row}
 function Numbered($items){return ,@(foreach($item in $items){[ordered]@{number=(Get-MpPropertyValue $item 'ReferenceNumber');item=(Physical $item)}})}
 try {
  $snapshot=Get-ModpackInventory -Project (Read-ModpackProject $root);$parameters=@{Inventory=$snapshot}
  foreach($key in $query.Keys){$parameters[$(if($key -eq 'match'){'Search'}else{$key})]=$query[$key]}
  $view=Set-ModpackInventoryReferences (Select-ModpackInventory @parameters)
  $categories=@(foreach($key in $view.Metadata.Categories.Keys){$entry=$view.Metadata.Categories[$key];[pscustomobject][ordered]@{key=$key;name=$(if($entry.ContainsKey('Name')){[string]$entry.Name}else{$key.ToUpperInvariant()});order=$(if($entry.ContainsKey('Order')){[int]$entry.Order}else{1000})}})|Sort-Object order,name
  $active=@(foreach($item in $view.ActiveResources){[ordered]@{number=$item.ReferenceNumber;item=[ordered]@{id=$item.Id;kind=$item.Kind;name=$item.Name;filename=$item.Filename;source=$item.Source;enabled=$item.Enabled;priority=$item.Priority}}})
  $refs=@(foreach($item in $script:NumberingCapture.Results){[ordered]@{index=$item.Index;id=$item.Id;kind=$item.Kind;name=$item.Name;filename=$item.Filename;source=$item.Source;selector=$item.Selector}})
  ConvertTo-Json -InputObject ([ordered]@{categories=@($categories);mods=(Numbered $view.Mods);active=$active;inactive=(Numbered $view.InactiveResources);shaders=(Numbered $view.Shaders);references=$refs}) -Depth 30 -Compress
 }catch{ConvertTo-Json -InputObject @{failure=$true;id=$_.Exception.Data['ModpackTools.ErrorId'];target=$_.Exception.Data['ModpackTools.TargetObject']} -Compress}
} $RootPath $query
'@)
 $count=0
 foreach($query in @(@{},@{type='mod'},@{state='active'},@{source='local'},@{category='unclassified'},@{match='missing'},@{match='no matches'})) {
  [IO.File]::WriteAllText($queryPath,($query|ConvertTo-Json -Compress));$options=@(foreach($key in $query.Keys){'--'+$key;[string]$query[$key]})
  $old=Invoke-NativeTestProcess (Get-Command pwsh).Source @('-NoProfile','-File',$runner,'-ModulePath',(Join-Path $LegacyRoot 'ModpackTools.psd1'),'-RootPath',$fixture,'-QueryPath',$queryPath)
  $new=Invoke-NativeTestProcess $Executable (@('--inspect-numbered-inventory',$fixture)+$options)
  if($old.Exit -ne 0 -or $new.Exit -ne 0 -or $new.Stderr -cne ''){throw "Numbering transport failed $($old.Stdout) $($new.Stdout)"}
  $a=$old.Stdout|ConvertFrom-Json;$b=$new.Stdout|ConvertFrom-Json
  if(($a|ConvertTo-Json -Depth 30 -Compress) -cne ($b|ConvertTo-Json -Depth 30 -Compress)){throw "Numbering mismatch $($query|ConvertTo-Json -Compress) $($old.Stdout) $($new.Stdout)"};$count++
 }
 if(Test-Path -LiteralPath (Join-Path $fixture '_config/last-inventory.json')){throw 'Pure numbering unexpectedly persisted a cache'}
 "Native numbering: $count full E13-stable differential views and no-cache-write control passed"
}finally{
 $resolved=[IO.Path]::GetFullPath($fixture)
 if(-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'modpacktools-numbering-*'){throw 'Unsafe fixture cleanup target'}
 Remove-Item -LiteralPath $resolved -Recurse -Force
}
