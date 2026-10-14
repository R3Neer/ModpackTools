param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string]$LegacyRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-batch-selectors-'+[guid]::NewGuid().ToString('N'))
$clock='2026-10-13T12:00:00.0000000Z'
$oldConfig=[Environment]::GetEnvironmentVariable('MODPACKTOOLS_CONFIG_HOME')
function Sentence([string]$value){$value=$value.Trim();if($value-notmatch'[.!?]$'){$value+='.'};$value}
try {
 [void][IO.Directory]::CreateDirectory($fixture)
 $env:MODPACKTOOLS_CONFIG_HOME=$fixture
 Import-Module (Join-Path $LegacyRoot 'ModpackTools.psd1') -Force
 $module=Get-Module ModpackTools
 &$module {param($clock)$body=(Get-Command Test-MpCacheTimestamp).Definition;Set-Item Function:script:Test-MpCacheTimestamp ([scriptblock]::Create($body.Replace('[datetimeoffset]::UtcNow',"([datetimeoffset]::Parse('$clock'))")))} $clock
 $path=Join-Path $fixture 'last-inventory.json'
 $row='{"Index":1,"Kind":"mod","Source":"packwiz","Selector":"Alpha"}'
 $normal='{"SchemaVersion":1,"CreatedUtc":"2026-10-13T12:00:00Z","ProjectId":"fixture","Results":['+$row+',{"Index":2,"Kind":"mod","Source":"local","Selector":"Beta"}]}'
 $cases=@(
  @{selectors=@('2','1','2')},@{selectors=@('ALPHA','1','Beta','2')},@{selectors=@('1',"1`n")},
  @{selectors=@('é','é','É','ﬃ','ffi','soft­hyphen','softhyphen');text='{bad'},
  @{selectors=@('01','0','-1',' 1','1 ',"1`r`n","1`n`n",' ');text='{bad'},
  @{selectors=@();text='{bad'},@{selectors=@('literal','3')},@{selectors=@('1','3')},
  @{selectors=@('literal','1');text='{bad'},@{selectors=@('1');kinds=@()},
  @{selectors=@('1');kinds=$null},@{selectors=@('1');kinds=@('MOD')},
  @{selectors=@('2');require=$true},@{selectors=@('1','2');require=$true},
  @{selectors=@('1');text=$normal.Replace('"Index":2','"Index":"bad"')},
  @{selectors=@('1');text=$normal.Replace('"Kind":"mod"','"Kind":"resourcepack"')},
  @{selectors=@('1');text=$normal.Replace('"Kind":"mod"','"Kind":"resourcepack"');kinds=@('resourcepack')},
  @{selectors=@('1');text=$normal.Replace(',"Selector":"Alpha"','')},
  @{selectors=@('literal','1');text=$normal.Replace(',"Selector":"Alpha"','')}
 )
 foreach($value in @('null','""','true','false','0','1.25','["a",null,"b"]','[["a","b"]]','{"A":1,"B":true,"C":{"x":1}}','"2026-10-13T12:00:00Z"')){
  $cases+=@{selectors=@('1','1','2');text=$normal.Replace('"Selector":"Alpha"','"Selector":'+$value)}
 }
 $count=0
 foreach($case in $cases){
  $text=if($case.ContainsKey('text')){$case.text}else{$normal}
  [IO.File]::WriteAllText($path,$text,[Text.UTF8Encoding]::new($false))
  $before=(Get-FileHash -LiteralPath $path).Hash
  $parameters=@{Project=[pscustomobject]@{Id='fixture'};Selectors=$case.selectors}
  $tokens=@('--inspect-batch-selectors',$fixture,'fixture',(ConvertTo-Json -InputObject $case.selectors -Compress),'--now-utc',$clock)
  if($case.ContainsKey('kinds')){$parameters.Kinds=$case.kinds;$tokens+=@('--allowed-kinds',(ConvertTo-Json -InputObject $case.kinds -Compress))}
  if($case.require){$parameters.RequirePackwiz=$true;$tokens+=@('--require-packwiz','true')}
  $failure=$null
  try{$expected=@(&$module {param($parameters)Resolve-MpBatchSelectors @parameters} $parameters)}catch{$failure=$_}
  $native=Invoke-NativeTestProcess $Executable $tokens
  if($native.Stderr){throw 'Unexpected batch stderr'}
  $actual=$native.Stdout|ConvertFrom-Json -AsHashtable -DateKind String -NoEnumerate
  if($failure){
   $id=$failure.Exception.Data['ModpackTools.ErrorId']
   if(!$id){
    if($native.Exit-ne1-or$actual.error.id-cne'ModpackTools.Inventory.InvalidReferenceCache'-or$actual.error.target-cne$path-or!$actual.error.details-or$actual.error.hint-cne'modpack content list'){throw 'Batch E15 failure mismatch'}
   }else{
    $lines=@((Sentence $actual.error.message));if($actual.error.details){$lines+='Details: '+(Sentence $actual.error.details)};if($actual.error.hint){$lines+='Try: '+$actual.error.hint.Trim()}
    if($native.Exit-ne1-or$actual.error.id-cne$id-or$actual.error.target-cne$failure.Exception.Data['ModpackTools.TargetObject']-or$actual.error.category-cne([Management.Automation.ErrorCategory][int]$failure.Exception.Data['ModpackTools.ErrorCategory']).ToString()-or($lines-join[Environment]::NewLine)-cne$failure.Exception.Message){throw "Batch domain failure mismatch: $($native.Stdout)"}
   }
  }elseif($native.Exit-ne0-or(ConvertTo-Json -InputObject $actual -Compress)-cne(ConvertTo-Json -InputObject $expected -Compress)){throw "Batch full string result mismatch: $($native.Stdout)"}
  if((Get-FileHash -LiteralPath $path).Hash-cne$before){throw 'Batch changed its cache'}
  $exclusive=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);$exclusive.Dispose()
  $count++
 }
 foreach($json in @('null','{}','[null]','[""]','[1]','["1",null]',(ConvertTo-Json -InputObject (@('literal')*1025) -Compress))){
  $native=Invoke-NativeTestProcess $Executable @('--inspect-batch-selectors',(Join-Path $fixture 'missing'),'fixture',$json)
  if($native.Exit-ne2-or$native.Stderr-or($native.Stdout|ConvertFrom-Json).error.id-cne'Core.InputReadFailed'){throw 'Batch grammar did not precede IO'}
  $count++
 }
 foreach($json in @('[]','["literal","LITERAL"]',(ConvertTo-Json -InputObject (@('literal')*1024) -Compress))){
  $exclusive=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
  try{$native=Invoke-NativeTestProcess $Executable @('--inspect-batch-selectors',$fixture,'fixture',$json,'--max-cache-bytes','0')}
  finally{$exclusive.Dispose()}
  if($native.Exit-ne0-or$native.Stderr){throw 'Literal/empty batch accessed cache or validated unused limits'}
  $count++
 }
 foreach($selector in @('Alpha','Changed')){
  [IO.File]::WriteAllText($path,$normal.Replace('"Selector":"Alpha"','"Selector":"'+$selector+'"'))
  $native=Invoke-NativeTestProcess $Executable @('--inspect-batch-selectors',$fixture,'fixture','["1","1"]','--now-utc',$clock)
  if($native.Exit-ne0-or$native.Stderr-or$native.Stdout-cne('["'+$selector+'"]')){throw 'Next batch failed to see a replaced cache'}
  $count++
 }
 "Native batch selectors: $count comparisons/grammar/noIO/replacement controls, complete strings/errors, buffered failures and intact cache passed"
}finally{
 [Environment]::SetEnvironmentVariable('MODPACKTOOLS_CONFIG_HOME',$oldConfig)
 Remove-Module ModpackTools -ErrorAction SilentlyContinue
 $resolved=[IO.Path]::GetFullPath($fixture)
 $temporaryRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
 if(!$resolved.StartsWith($temporaryRoot,[StringComparison]::OrdinalIgnoreCase)-or[IO.Path]::GetFileName($resolved)-notlike'modpacktools-batch-selectors-*'){throw 'Unsafe fixture cleanup'}
 Remove-Item -LiteralPath $resolved -Recurse -Force
}
