param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string]$LegacyRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-quoted-integers-'+[guid]::NewGuid().ToString('N'))
try {
 [void][IO.Directory]::CreateDirectory((Join-Path $fixture '.modpack'))
 [IO.File]::WriteAllText((Join-Path $fixture 'pack.toml'),'[index]')
 $descriptor=Join-Path $fixture '.modpack/project.psd1'
 $metadata=Join-Path $fixture '.modpack/metadata.psd1'
 [IO.File]::WriteAllText($descriptor,"@{SchemaVersion=1;Id='fixture'}")
 Import-Module (Join-Path $LegacyRoot 'ModpackTools.psd1') -Force
 $module=Get-Module ModpackTools
 $cases=@('0x1L','-0xFFFFFFFF','+0xFFFFFFFF','0xFFFFFFFFFFFFFFFF','-0xFFFFFFFFFFFFFFFF',
  '-0x80000000','-0x080000000','-0x0000000080000000','-0x00000000FFFFFFFF','0x00000000FFFFFFFF',
  '-0x000000000000000000001','+0x000000000000000000001',
  '0b11111111','0b11111111y','0b1111111111111111s',('-0b0'+('1'*32)),('-0b'+('1'*64)),
  '0xFFFFFFFFL','-0x80000000L','0xFFFFFFFFu','-0xFFFFFFFFu','0x1UL','+0b01u','0x1N','0b1D','0x1D',
  '0x1KB','1.5KB','1yKB','1sKB','2.5L','2.5d','0x1_0','0x--1','0b++1','0x 1','1,234','#ff','&hff',
  '+0x','-0x','0xL','+0xL','-0xL','0xY','0xKB','+0xKB','-0xKB','0bL','+0b','-0b')
 $count=0
 foreach($value in $cases){
  [IO.File]::WriteAllText($metadata,"@{Categories=@{qol=@{Name='Display';Order='$value'}}}")
  $before=(Get-FileHash -LiteralPath $metadata).Hash
  $failure=$null
  try{$old=&$module {param($root)(Get-ModpackCategoryView -Project ([pscustomobject]@{Root=$root;Id='fixture'})).Categories[0]} $fixture}
  catch{$failure=$_}
  $native=Invoke-NativeTestProcess $Executable @('--inspect-categories',$fixture)
  if($native.Stderr){throw "Unexpected stderr for $value"}
  $actual=$native.Stdout|ConvertFrom-Json -DateKind String
  if($failure){
   if($native.Exit-ne1-or$actual.error.id-cne'ModpackTools.Metadata.InvalidCategoryDefinition'-or$actual.error.target-cne'qol'){
    throw "Quoted Order E11 mismatch $value`: $($native.Stdout)"
   }
  }elseif($native.Exit-ne0-or$actual.Count-ne1-or$actual[0].key-cne$old.Id-or$actual[0].name-cne$old.Name-or$actual[0].order-ne$old.Order){
   throw "Quoted Order value mismatch $value`: $($native.Stdout)"
  }
  if((Get-FileHash -LiteralPath $metadata).Hash-cne$before){throw 'Category reader wrote metadata'}
  $count++
 }
 [IO.File]::WriteAllText($metadata,'@{}')
 foreach($value in @('0x1L','+0b1u','-0xFFFFFFFF','-0xFFFFFFFFFFFFFFFF','1KB','2.5L','0x1_0','-0x80000000')){
  [IO.File]::WriteAllText($descriptor,"@{SchemaVersion='$value';Id='fixture'}")
  $before=(Get-FileHash -LiteralPath $descriptor).Hash
  $failure=$null
  try{[void](&$module {param($root)Read-ModpackProject -ProjectRoot $root} $fixture)}catch{$failure=$_}
  $native=Invoke-NativeTestProcess $Executable @('--inspect-categories',$fixture)
  if($native.Stderr){throw 'Unexpected project stderr'}
  if(!$failure){if($native.Exit-ne0-or$native.Stdout.Trim()-cne'[]'){throw "Valid quoted project schema failed $value"}}
  elseif($failure.Exception.Data['ModpackTools.ErrorId']){
   $actual=$native.Stdout|ConvertFrom-Json -DateKind String
   if($native.Exit-ne1-or$actual.error.id-cne$failure.Exception.Data['ModpackTools.ErrorId']-or$actual.error.target-cne$value){
    throw "Unsupported project schema lost original target $value"
   }
  }else{
   $actual=$native.Stdout|ConvertFrom-Json
   if($native.Exit-ne2-or$actual.error.id-cne'Core.InputReadFailed'){throw 'Invalid schema changed inspector failure mapping'}
  }
  if((Get-FileHash -LiteralPath $descriptor).Hash-cne$before){throw 'Project reader wrote descriptor'}
  $count++
 }
 "Quoted Int32 data: $count category/project differential cases, E11 and original schema targets passed"
}finally{
 Remove-Module ModpackTools -ErrorAction SilentlyContinue
 $resolved=[IO.Path]::GetFullPath($fixture)
 $temporaryRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
 if(!$resolved.StartsWith($temporaryRoot,[StringComparison]::OrdinalIgnoreCase)-or[IO.Path]::GetFileName($resolved)-notlike'modpacktools-quoted-integers-*'){throw 'Unsafe fixture cleanup'}
 Remove-Item -LiteralPath $resolved -Recurse -Force
}
