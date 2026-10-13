param([Parameter(Mandatory)][string]$Executable,[string]$LegacyRoot='D:\Tools\ModpackTools')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-resource-targets-'+[guid]::NewGuid().ToString('N'))
function Sentence([string]$value){$value=$value.Trim();if($value-notmatch'[.!?]$'){$value+='.'};$value}
try {
 foreach($name in @('.modpack','mods','resourcepacks','shaderpacks','config')){[void][IO.Directory]::CreateDirectory((Join-Path $fixture $name))}
 [IO.File]::WriteAllText((Join-Path $fixture '.modpack/project.psd1'),"@{SchemaVersion=1;Id='fixture'}")
 [IO.File]::WriteAllText((Join-Path $fixture 'pack.toml'),'[index]')
 [IO.File]::WriteAllText((Join-Path $fixture 'resourcepacks/resource.pw.toml'),"name = `"Physical`"`nfilename = `"R.zip`"`n[update.modrinth]`nmod-id = `"resource`"`n")
 [IO.File]::WriteAllText((Join-Path $fixture 'resourcepacks/local.zip'),'local resource bytes')
 $orderPath=Join-Path $fixture 'config/defaultoptions-common.toml'
 [IO.File]::WriteAllText($orderPath,'defaultResourcePacks=["file/R.zip","vanilla","file/missing.zip"]')
 $metadataPath=Join-Path $fixture '.modpack/metadata.psd1'
 $normal="@{ResourcePacks=@{'file/R.zip'=@{Name='Active name'};extra=@{Name='Extra'}}}"
 Import-Module (Join-Path $LegacyRoot 'ModpackTools.psd1') -Force
 $module=Get-Module ModpackTools
 $project=&$module {param($root)Read-ModpackProject $root} $fixture
 $cases=@(
  @{selectors=@('Active name','R.zip','file/R.zip','vanilla','missing.zip','local.zip','extra','Extra')},
  @{selectors=@('Physical')},@{selectors=@('modrinth:resource')},@{selectors=@('resource')},
  @{selectors=@('1')},@{selectors=@('extra','absent')},@{selectors=@('absent','extra')},
  @{selectors=@('Grouped');metadata="@{ResourcePacks=@{a=@{Name='Grouped'};z=@{Name='Grouped'}}}";e19=$true},
  @{selectors=@('vanilla');order='defaultResourcePacks=["vanilla","vanilla"]'},
  @{selectors=@('Empty key');metadata="@{ResourcePacks=@{''=@{Name='Empty key'}}}"}
 )
 foreach($entry in @('@{}','@{Name=$null}','@{Name=@()}','@{Name=@("a",$null,"b")}','@{Name=@{x=1}}','@(@{Name="Wrapped"})','@(@{Name="a"},@{Name="b"})',
  '$null','$false','@()','@(@{Name=@()})','@(@{},@{})','@(@{Name="a"},@{})')){
  $cases+=@{selectors=@('extra','vanilla');metadata='@{ResourcePacks=@{extra='+$entry+'}}'}
 }
 $count=0
 foreach($case in $cases){
  [IO.File]::WriteAllText($metadataPath, $(if($case.ContainsKey('metadata')){$case.metadata}else{$normal}))
  [IO.File]::WriteAllText($orderPath, $(if($case.ContainsKey('order')){$case.order}else{'defaultResourcePacks=["file/R.zip","vanilla","file/missing.zip"]'}))
  $before=@(Get-ChildItem -LiteralPath $fixture -File -Recurse -Force|Sort-Object FullName|ForEach-Object { $_.FullName+':'+(Get-FileHash -LiteralPath $_.FullName).Hash+':'+[int]$_.Attributes }) -join "`n"
  $failure=$null
  try{$expected=&$module {param($project,$selectors)
   $inventory=Get-ModpackInventory $project
   $targets=@(foreach($selector in $selectors){Resolve-ModpackResourcePack $inventory $selector})
   ConvertTo-Json -InputObject $targets -Depth 30 -Compress
  } $project $case.selectors}catch{$failure=$_}
  $native=Invoke-NativeTestProcess $Executable @('--inspect-resource-targets',$fixture,(ConvertTo-Json -InputObject $case.selectors -Compress))
  if($native.Stderr){throw 'Unexpected resource selector stderr'}
  if($failure){
   $actual=$native.Stdout|ConvertFrom-Json -DateKind String
   $id=$failure.Exception.Data['ModpackTools.ErrorId']
   if(!$id){
    if($native.Exit-ne1-or$actual.error.id-cne'ModpackTools.ResourcePack.InvalidSelectorMetadata'-or$actual.error.category-cne'InvalidData'-or$actual.error.target-cne'extra'-or!$actual.error.details-or$actual.error.hint-cne'repair the matching ResourcePacks entry in .modpack/metadata.psd1'){throw 'Resource E18 failure mismatch'}
   }else{
    $lines=@((Sentence $actual.error.message));if($actual.error.details){$lines+='Details: '+(Sentence $actual.error.details)};if($actual.error.hint){$lines+='Try: '+$actual.error.hint.Trim()}
    $expectedMessage=$failure.Exception.Message
    if($case.e19){$expectedMessage=$expectedMessage -replace '(?m)^Details:[^\r\n]*',"Details: Matches: 'Grouped' [a], 'Grouped' [z]."}
    if($native.Exit-ne1-or$actual.error.id-cne$id-or$actual.error.target-cne$failure.Exception.Data['ModpackTools.TargetObject']-or$actual.error.category-cne([Management.Automation.ErrorCategory][int]$failure.Exception.Data['ModpackTools.ErrorCategory']).ToString()-or($lines-join[Environment]::NewLine)-cne$expectedMessage){throw "Resource domain failure mismatch: $($native.Stdout)"}
   }
  }else{
   $canonical=ConvertTo-Json -InputObject ($native.Stdout|ConvertFrom-Json -AsHashtable -DateKind String -NoEnumerate) -Depth 30 -Compress
   if($native.Exit-ne0-or$canonical-cne$expected){throw "Resource full five-field mismatch: $expected / $canonical"}
  }
  $after=@(Get-ChildItem -LiteralPath $fixture -File -Recurse -Force|Sort-Object FullName|ForEach-Object { $_.FullName+':'+(Get-FileHash -LiteralPath $_.FullName).Hash+':'+[int]$_.Attributes }) -join "`n"
  if($before-cne$after){throw 'Resource selector changed fixtures'}
  $count++
 }
 foreach($json in @('[]','null','[null]','[""]','[1]','{}',(ConvertTo-Json -InputObject (@('vanilla')*1025) -Compress))){
  $native=Invoke-NativeTestProcess $Executable @('--inspect-resource-targets',(Join-Path $fixture 'missing'),$json)
  if($native.Exit-ne2-or$native.Stderr-or($native.Stdout|ConvertFrom-Json).error.id-cne'Core.InputReadFailed'){throw 'Resource grammar did not precede IO'}
  $count++
 }
 [IO.File]::WriteAllText($metadataPath,'@{ResourcePacks=@{}}')
 $native=Invoke-NativeTestProcess $Executable @('--inspect-resource-targets',$fixture,(ConvertTo-Json -InputObject (@('vanilla')*1024) -Compress))
 if($native.Exit-ne0-or$native.Stderr-or($native.Stdout|ConvertFrom-Json -NoEnumerate).Count-ne1024){throw 'Resource exact count or repeated output failed'}
 "Native resource targets: $count differential/grammar controls, exact1024 repeated targets, full five fields/errors, E18/E19 and intact fixtures passed"
}finally{
 Remove-Module ModpackTools -ErrorAction SilentlyContinue
 $resolved=[IO.Path]::GetFullPath($fixture)
 $temporaryRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
 if(!$resolved.StartsWith($temporaryRoot,[StringComparison]::OrdinalIgnoreCase)-or[IO.Path]::GetFileName($resolved)-notlike'modpacktools-resource-targets-*'){throw 'Unsafe fixture cleanup'}
 Remove-Item -LiteralPath $resolved -Recurse -Force
}
