param([Parameter(Mandatory)][string]$Executable,[string]$LegacyRoot='D:\Tools\ModpackTools')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-update-targets-'+[guid]::NewGuid().ToString('N'))
function Sentence([string]$value){$value=$value.Trim();if($value-notmatch'[.!?]$'){$value+='.'};$value}
try {
 foreach($name in @('.modpack','mods','resourcepacks','shaderpacks','config')){[void][IO.Directory]::CreateDirectory((Join-Path $fixture $name))}
 [IO.File]::WriteAllText((Join-Path $fixture '.modpack/project.psd1'),"@{SchemaVersion=1;Id='fixture'}")
 [IO.File]::WriteAllText((Join-Path $fixture 'pack.toml'),'[index]')
 [IO.File]::WriteAllText((Join-Path $fixture 'mods/Alpha.PW.TOML'),"name = `"Original Alpha`"`nfilename = `"one.jar`"`nside = `"client`"`n[update.modrinth]`nmod-id = `"alpha`"`nversion = `"v1`"`n")
 [IO.File]::WriteAllText((Join-Path $fixture 'mods/shared.pw.toml'),"name = `"Shared`"`nfilename = `"two.jar`"`n[update.modrinth]`nmod-id = `"beta`"`n")
 [IO.File]::WriteAllText((Join-Path $fixture 'resourcepacks/resource.pw.toml'),"name = `"Shared`"`nfilename = `"R.zip`"`n[update.modrinth]`nmod-id = `"alpha`"`n")
 [IO.File]::WriteAllText((Join-Path $fixture 'shaderpacks/shader.pw.toml'),"name = `"1`"`nfilename = `"S.zip`"`n[update.modrinth]`nmod-id = `"shader`"`n")
 [IO.File]::WriteAllText((Join-Path $fixture 'mods/local.jar'),'optional invalid display archive')
 [IO.File]::WriteAllText((Join-Path $fixture 'resourcepacks/local.zip'),'local resource')
 [IO.File]::WriteAllText((Join-Path $fixture 'config/defaultoptions-common.toml'),'defaultResourcePacks=["file/R.zip","vanilla","file/missing.zip"]')
 [IO.File]::WriteAllText((Join-Path $fixture '.modpack/metadata.psd1'),"@{Categories=@{qol=@{Order='unused invalid order'}};Mods=@{'modrinth:alpha'=@{Name='Edited';Category='qol'}};ResourcePacks=@{'file/R.zip'=@{Name='Fancy'}}}")
 Import-Module (Join-Path $LegacyRoot 'ModpackTools.psd1') -Force
 $module=Get-Module ModpackTools
 $cases=@(
  @{selectors=@('Edited')},@{selectors=@('ALPHA');type='mods'},@{selectors=@('one.jar')},
  @{selectors=@('modrinth:alpha');type='mod'},@{selectors=@('modrinth:alpha');type='resources'},
  @{selectors=@('one.jar','Edited','Alpha')},@{selectors=@('S.zip','Edited','R.zip','one.jar')},
  @{selectors=@('Shared');type='mods'},@{selectors=@('Shared');type='resource'},
  @{selectors=@('1')},@{selectors=@('1');type='SHADERS'},@{selectors=@('01')},
  @{selectors=@('Original Alpha')},@{selectors=@('Fancy')},@{selectors=@('vanilla')},
  @{selectors=@('file/R.zip')},@{selectors=@('missing.zip')},@{selectors=@('Alph')},
  @{selectors=@(' Edited ')},@{selectors=@('modrinth:alpha')},@{selectors=@('Shared')},
  @{selectors=@('local.jar')},@{selectors=@('local.zip');type='resources'},
  @{selectors=@('Edited','local.jar')},@{selectors=@('local.jar','absent')},
  @{selectors=@('absent','Shared')},@{selectors=@('Edited','absent')},
  @{selectors=@('absent');type='bad'},@{selectors=@('Edited');type=''},
  @{selectors=@('Edited');type=('m'+[char]173+'od')}
 )
 $before=@(Get-ChildItem -LiteralPath $fixture -File -Recurse -Force|Sort-Object FullName|ForEach-Object { $_.FullName+':'+(Get-FileHash -LiteralPath $_.FullName).Hash+':'+[int]$_.Attributes }) -join "`n"
 $count=0
 foreach($case in $cases){
  $parameters=@{Project=(&$module {param($root)Read-ModpackProject $root} $fixture);Selectors=$case.selectors}
  $tokens=@('--inspect-update-targets',$fixture,(ConvertTo-Json -InputObject $case.selectors -Compress))
  if($case.ContainsKey('type')){$parameters.Type=$case.type;$tokens+=@('--type',$case.type)}
  $failure=$null
  try{
   $old=&$module {param($parameters)
    $rows=@(Resolve-ModpackUpdateSelectors @parameters)
    $projected=@(foreach($item in $rows){
     $row=[ordered]@{id=$item.Id;kind=$item.Kind;name=$item.Name;filename=$item.Filename;side=$item.Side;source=$item.Source;metadata_path=$item.MetadataPath;category=$item.Category}
     if($item.Kind-eq'mod'){$row.invalid_category=$item.InvalidCategory}
     if($item.Source-eq'packwiz'){$row.version_id=$item.VersionId;$row.pinned=$item.Pinned}
     $row
    })
    ConvertTo-Json -InputObject $projected -Depth 30 -Compress
   } $parameters
  }catch{$failure=$_}
  $native=Invoke-NativeTestProcess $Executable $tokens
  if($native.Stderr){throw 'Unexpected update selector stderr'}
  if($failure){
   $actual=$native.Stdout|ConvertFrom-Json -DateKind String
   $lines=@((Sentence $actual.error.message))
   if($actual.error.details){$lines+='Details: '+(Sentence $actual.error.details)}
   if($actual.error.hint){$lines+='Try: '+$actual.error.hint.Trim()}
   if($native.Exit-ne1-or$actual.error.id-cne$failure.Exception.Data['ModpackTools.ErrorId']-or
    $actual.error.target-cne$failure.Exception.Data['ModpackTools.TargetObject']-or
    $actual.error.category-cne([Management.Automation.ErrorCategory][int]$failure.Exception.Data['ModpackTools.ErrorCategory']).ToString()-or
    ($lines-join[Environment]::NewLine)-cne$failure.Exception.Message){throw "Selector failure mismatch $($case|ConvertTo-Json -Compress): $($native.Stdout)"}
  }else{
   $expected=ConvertTo-Json -InputObject ($old|ConvertFrom-Json -AsHashtable -DateKind String) -Depth 30 -Compress
   $actual=ConvertTo-Json -InputObject ($native.Stdout|ConvertFrom-Json -AsHashtable -DateKind String) -Depth 30 -Compress
   if($native.Exit-ne0-or$actual-cne$expected){throw "Selector full result mismatch $($case|ConvertTo-Json -Compress): $old / $actual"}
  }
  $count++
 }
 foreach($json in @('[]','null','[null]','[""]','[1]','{}',(ConvertTo-Json -InputObject (@('Edited')*1025) -Compress))){
  $native=Invoke-NativeTestProcess $Executable @('--inspect-update-targets',(Join-Path $fixture 'missing'),$json)
  if($native.Exit-ne2-or$native.Stderr-or($native.Stdout|ConvertFrom-Json).error.id-cne'Core.InputReadFailed'){throw 'Selector grammar did not precede IO'}
  $count++
 }
 $native=Invoke-NativeTestProcess $Executable @('--inspect-update-targets',$fixture,(ConvertTo-Json -InputObject (@('Edited')*1024) -Compress))
 if($native.Exit-ne0-or$native.Stderr-or@($native.Stdout|ConvertFrom-Json).Count-ne1){throw 'Exact selector count boundary failed'}
 $after=@(Get-ChildItem -LiteralPath $fixture -File -Recurse -Force|Sort-Object FullName|ForEach-Object { $_.FullName+':'+(Get-FileHash -LiteralPath $_.FullName).Hash+':'+[int]$_.Attributes }) -join "`n"
 if($before-cne$after){throw 'Selector reader modified fixtures'}
 "Native update targets: $count differential/grammar cases, exact 1024 boundary, full rows/diagnostics and unchanged fixtures passed"
}finally{
 Remove-Module ModpackTools -ErrorAction SilentlyContinue
 $resolved=[IO.Path]::GetFullPath($fixture)
 $temporaryRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
 if(!$resolved.StartsWith($temporaryRoot,[StringComparison]::OrdinalIgnoreCase)-or[IO.Path]::GetFileName($resolved)-notlike'modpacktools-update-targets-*'){throw 'Unsafe fixture cleanup'}
 Remove-Item -LiteralPath $resolved -Recurse -Force
}
