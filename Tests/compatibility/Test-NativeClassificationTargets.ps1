param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string]$LegacyRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-classification-targets-'+[guid]::NewGuid().ToString('N'))
function Sentence([string]$value){$value=$value.Trim();if($value-notmatch'[.!?]$'){$value+='.'};$value}
try {
 foreach($name in @('.modpack','mods','resourcepacks','shaderpacks')){[void][IO.Directory]::CreateDirectory((Join-Path $fixture $name))}
 [IO.File]::WriteAllText((Join-Path $fixture '.modpack/project.psd1'),"@{SchemaVersion=1;Id='fixture'}")
 [IO.File]::WriteAllText((Join-Path $fixture 'pack.toml'),'[index]')
 [IO.File]::WriteAllText((Join-Path $fixture 'mods/Alpha.PW.TOML'),"name = `"Original Alpha`"`nfilename = `"one.jar`"`nside = `"client`"`n[update.modrinth]`nmod-id = `"alpha`"`nversion = `"v1`"`n")
 [IO.File]::WriteAllText((Join-Path $fixture 'mods/shared.pw.toml'),"name = `"Shared`"`nfilename = `"two.jar`"`n[update.modrinth]`nmod-id = `"beta`"`n")
 [IO.File]::WriteAllText((Join-Path $fixture 'resourcepacks/resource.pw.toml'),"name = `"Shared`"`nfilename = `"R.zip`"`n[update.modrinth]`nmod-id = `"alpha`"`n")
 [IO.File]::WriteAllText((Join-Path $fixture 'shaderpacks/shader.pw.toml'),"name = `"1`"`nfilename = `"S.zip`"`n[update.modrinth]`nmod-id = `"shader`"`n")
 [IO.File]::WriteAllText((Join-Path $fixture 'mods/local.jar'),'optional invalid display archive')
 $metadataPath=Join-Path $fixture '.modpack/metadata.psd1'
 [IO.File]::WriteAllText($metadataPath,"@{Categories=@{qol=@{Order='unused invalid order'}};Mods=@{'modrinth:alpha'=@{Name='Edited';Category='qol'}}}")
 Import-Module (Join-Path $LegacyRoot 'ModpackTools.psd1') -Force
 $module=Get-Module ModpackTools
 $project=&$module {param($root)Read-ModpackProject $root} $fixture
 $cases=@(
  @('Edited'),@('ALPHA'),@('one.jar'),@('modrinth:alpha'),@('one.jar','Edited','Alpha'),
  @('Shared','local.jar','Edited','Shared'),@('local.jar'),@('local:local.jar'),
  @('1'),@('01'),@('R.zip'),@('Original Alpha'),@('Alph'),@(' Edited '),
  @('absent'),@('Edited','absent'),@('absent','Edited')
 )
 $before=@(Get-ChildItem -LiteralPath $fixture -File -Recurse -Force|Sort-Object FullName|ForEach-Object { $_.FullName+':'+(Get-FileHash -LiteralPath $_.FullName).Hash+':'+[int]$_.Attributes }) -join "`n"
 $count=0
 foreach($selectors in $cases){
  $failure=$null
  try{
   $expected=&$module {param($project,$selectors)
    $rows=@(foreach($selector in $selectors){Resolve-ModpackModForClassification $project $selector})
    $projected=@(foreach($item in $rows){
     $row=[ordered]@{id=$item.Id;kind=$item.Kind;name=$item.Name;filename=$item.Filename;side=$item.Side;source=$item.Source;metadata_path=$item.MetadataPath;category=$item.Category;invalid_category=$item.InvalidCategory}
     if($item.Source-eq'packwiz'){$row.version_id=$item.VersionId;$row.pinned=$item.Pinned};$row
    })
    ConvertTo-Json -InputObject $projected -Depth 30 -Compress
   } $project $selectors
  }catch{$failure=$_}
  $native=Invoke-NativeTestProcess $Executable @('--inspect-classification-targets',$fixture,(ConvertTo-Json -InputObject $selectors -Compress))
  if($native.Stderr){throw 'Unexpected classification selector stderr'}
  if($failure){
   $actual=$native.Stdout|ConvertFrom-Json -DateKind String
   $lines=@((Sentence $actual.error.message));if($actual.error.details){$lines+='Details: '+(Sentence $actual.error.details)};if($actual.error.hint){$lines+='Try: '+$actual.error.hint.Trim()}
   if($native.Exit-ne1-or$actual.error.id-cne$failure.Exception.Data['ModpackTools.ErrorId']-or$actual.error.target-cne$failure.Exception.Data['ModpackTools.TargetObject']-or$actual.error.category-cne([Management.Automation.ErrorCategory][int]$failure.Exception.Data['ModpackTools.ErrorCategory']).ToString()-or($lines-join[Environment]::NewLine)-cne$failure.Exception.Message){throw "Classification failure mismatch: $($native.Stdout)"}
  }else{
   $canonical=ConvertTo-Json -InputObject ($native.Stdout|ConvertFrom-Json -AsHashtable -DateKind String -NoEnumerate) -Depth 30 -Compress
   if($native.Exit-ne0-or$canonical-cne$expected){throw "Classification full row mismatch: $expected / $canonical"}
  }
  $count++
 }
 foreach($json in @('[]','null','[null]','[""]','[1]','{}',(ConvertTo-Json -InputObject (@('Edited')*1025) -Compress))){
  $native=Invoke-NativeTestProcess $Executable @('--inspect-classification-targets',(Join-Path $fixture 'missing'),$json)
  if($native.Exit-ne2-or$native.Stderr-or($native.Stdout|ConvertFrom-Json).error.id-cne'Core.InputReadFailed'){throw 'Classification grammar did not precede IO'}
  $count++
 }
 $native=Invoke-NativeTestProcess $Executable @('--inspect-classification-targets',$fixture,(ConvertTo-Json -InputObject (@('Edited')*1024) -Compress))
 $rows=$native.Stdout|ConvertFrom-Json -NoEnumerate
 if($native.Exit-ne0-or$native.Stderr-or$rows.Count-ne1024-or@($rows|Where-Object id -cne 'modrinth:alpha').Count){throw 'Classification exact count or repeated output failed'}
 $count++
 $after=@(Get-ChildItem -LiteralPath $fixture -File -Recurse -Force|Sort-Object FullName|ForEach-Object { $_.FullName+':'+(Get-FileHash -LiteralPath $_.FullName).Hash+':'+[int]$_.Attributes }) -join "`n"
 if($before-cne$after){throw 'Classification selector modified fixture'}
 [IO.File]::WriteAllText($metadataPath,'@{ResourcePacks=1}')
 $native=Invoke-NativeTestProcess $Executable @('--inspect-classification-targets',$fixture,'["absent"]')
 if($native.Exit-ne1-or($native.Stdout|ConvertFrom-Json).error.id-cne'ModpackTools.Metadata.InvalidSection'){throw 'Full inventory failure did not precede mod lookup'}
 "Native classification targets: $count differential/grammar/boundary controls, complete repeated physical rows/errors, full-read priority and unchanged fixtures passed"
}finally{
 Remove-Module ModpackTools -ErrorAction SilentlyContinue
 $resolved=[IO.Path]::GetFullPath($fixture)
 $temporaryRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
 if(!$resolved.StartsWith($temporaryRoot,[StringComparison]::OrdinalIgnoreCase)-or[IO.Path]::GetFileName($resolved)-notlike'modpacktools-classification-targets-*'){throw 'Unsafe fixture cleanup'}
 Remove-Item -LiteralPath $resolved -Recurse -Force
}
