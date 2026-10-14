param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string]$LegacyRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-default-options-'+[guid]::NewGuid().ToString('N'))
function Sentence([string]$value){$value=$value.Trim();if($value-notmatch'[.!?]$'){$value+='.'};$value}
try {
 [void][IO.Directory]::CreateDirectory($fixture)
 Import-Module (Join-Path $LegacyRoot 'ModpackTools.psd1') -Force
 $module=Get-Module ModpackTools
 $cases=@(
  @{name='Other';id='other';filename='ordinary.jar'},
  @{name='Default Options';id='other';filename='ordinary.jar'},
  @{name='Other';id='WEg59z5b';filename='ordinary.jar'},
  @{name='Other';id='other';filename='defaultoptions-1.jar'},
  @{name='Other';id='other';filename='defaultoptions.jar'},
  @{name=' Default Options ';id='other';filename='ordinary.jar'},
  @{name='DEFAULT OPTIONS';id='other';filename='ordinary.jar';config='file'},
  @{name='Default Options';id='other';filename='ordinary.jar';config='directory'},
  @{name='Other';id='other';filename='ordinary.jar';config='file'},
  @{name='Other';id='other';filename='defaultoptions_.jar';config='file'},
  @{name='Other';id='other';filename='defaultoptions..jar'},
  @{name='Other';id='other';filename='defaultoptions-.jar'},
  @{name='Other';id='other';filename='prefix-defaultoptions-1.jar'},
  @{name='Other';id='other';filename="defaultoptions-1.jar`n"},
  @{name='Other';id='other';filename="defaultoptions-1.jar`r`n"}
 )
 $count=0
 foreach($case in $cases){
  $root=Join-Path $fixture ('case-'+$count)
  foreach($folder in @('.modpack','mods','config')){[void][IO.Directory]::CreateDirectory((Join-Path $root $folder))}
  [IO.File]::WriteAllText((Join-Path $root '.modpack/project.psd1'),"@{SchemaVersion=1;Id='fixture'}")
  [IO.File]::WriteAllText((Join-Path $root 'pack.toml'),'[index]')
  $filename=$case.filename.Replace("`r",'\r').Replace("`n",'\n')
  [IO.File]::WriteAllText((Join-Path $root 'mods/entry.pw.toml'),"name = `"$($case.name)`"`nfilename = `"$filename`"`n[update.modrinth]`nmod-id = `"$($case.id)`"`nversion = `"v1`"`n")
  $configPath=Join-Path $root 'config/defaultoptions-common.toml'
  if($case.config-eq'file'){[IO.File]::WriteAllText($configPath,'defaultResourcePacks=[]')}
  elseif($case.config-eq'directory'){[void][IO.Directory]::CreateDirectory($configPath)}
  $before=@(Get-ChildItem -LiteralPath $root -File -Recurse -Force|Sort-Object FullName|ForEach-Object { $_.FullName+':'+(Get-FileHash -LiteralPath $_.FullName).Hash+':'+[int]$_.Attributes }) -join "`n"
  $project=&$module {param($root)Read-ModpackProject $root} $root
  foreach($assert in @($false,$true)){
   $failure=$null
   try{$expected=&$module {param($project,$assert)
    $inventory=Get-ModpackInventory $project
    $status=if($assert){Assert-ModpackDefaultOptionsInstalled $project $inventory}else{Get-ModpackDefaultOptionsStatus $project $inventory}
    $mod=$null
    if($status.Mod){$item=$status.Mod;$mod=[ordered]@{id=$item.Id;kind=$item.Kind;name=$item.Name;filename=$item.Filename;side=$item.Side;source=$item.Source;metadata_path=$item.MetadataPath;category=$item.Category;invalid_category=$item.InvalidCategory};if($item.Source-eq'packwiz'){$mod.version_id=$item.VersionId;$mod.pinned=$item.Pinned}}
    ConvertTo-Json -InputObject ([ordered]@{Installed=$status.Installed;ConfigPresent=$status.ConfigPresent;Ready=$status.Ready;Mod=$mod;ConfigPath=$status.ConfigPath}) -Depth 20 -Compress
   } $project $assert}catch{$failure=$_}
   $native=Invoke-NativeTestProcess $Executable @('--inspect-default-options',$root,'--assert-installed',$assert.ToString())
   if($native.Stderr){throw 'Unexpected Default Options stderr'}
   if($failure){
    $actual=$native.Stdout|ConvertFrom-Json -DateKind String
    $lines=@((Sentence $actual.error.message));if($actual.error.details){$lines+='Details: '+(Sentence $actual.error.details)};if($actual.error.hint){$lines+='Try: '+$actual.error.hint.Trim()}
    if($native.Exit-ne1-or$actual.error.id-cne$failure.Exception.Data['ModpackTools.ErrorId']-or$actual.error.target-cne$failure.Exception.Data['ModpackTools.TargetObject']-or$actual.error.category-cne([Management.Automation.ErrorCategory][int]$failure.Exception.Data['ModpackTools.ErrorCategory']).ToString()-or($lines-join[Environment]::NewLine)-cne$failure.Exception.Message){throw "Default Options failure mismatch: $($native.Stdout)"}
   }else{
    $canonical=ConvertTo-Json -InputObject ($native.Stdout|ConvertFrom-Json -AsHashtable -DateKind String) -Depth 20 -Compress
    if($native.Exit-ne0-or$canonical-cne$expected){throw "Default Options full status/mod mismatch: $expected / $canonical"}
   }
   $count++
  }
  $after=@(Get-ChildItem -LiteralPath $root -File -Recurse -Force|Sort-Object FullName|ForEach-Object { $_.FullName+':'+(Get-FileHash -LiteralPath $_.FullName).Hash+':'+[int]$_.Attributes }) -join "`n"
  if($before-cne$after){throw 'Default Options status changed fixture'}
 }
 foreach($tokens in @(@('--assert-installed','yes'),@('--unknown','true'),@('--assert-installed','true','--assert-installed','false'),@('--culture','not_a_valid_culture_!'))){
  $native=Invoke-NativeTestProcess $Executable (@('--inspect-default-options',(Join-Path $fixture 'missing'))+$tokens)
  if($native.Exit-ne2-or$native.Stderr-or($native.Stdout|ConvertFrom-Json).error.id-cne'Core.InputReadFailed'){throw 'Default Options grammar did not precede IO'}
  $count++
 }
 "Native Default Options: $count differential/grammar controls, complete status/mod fields, installed-only prerequisite and unchanged fixtures passed"
}finally{
 Remove-Module ModpackTools -ErrorAction SilentlyContinue
 $resolved=[IO.Path]::GetFullPath($fixture)
 $temporaryRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
 if(!$resolved.StartsWith($temporaryRoot,[StringComparison]::OrdinalIgnoreCase)-or[IO.Path]::GetFileName($resolved)-notlike'modpacktools-default-options-*'){throw 'Unsafe fixture cleanup'}
 Remove-Item -LiteralPath $resolved -Recurse -Force
}
