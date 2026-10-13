param([Parameter(Mandatory)][string]$Executable,[string]$LegacyRoot='D:\Tools\ModpackTools')
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
Import-Module (Join-Path $LegacyRoot 'ModpackTools.psd1') -Force
$module=Get-Module ModpackTools
$cases=@(
 @{v='1';r='null'},@{v='1';r='[]'},@{v='1';r='[null]'},@{v='1';r='false'},
 @{v='1';r='">opaque <0"'},@{v='1';r='[">opaque","2"]'},@{v='1';r='{"all":[">opaque","2"]}'},
 @{v='1';r='{"any":[["1","2"]]}'},@{v='1';r='{"all":[["1","2"]]}'},
 @{v='1';r='{"any":[[]],"all":"*"}'},@{v='1';r='{"any":[[null]],"all":"*"}'},
 @{v='1';r='{"any":[false,false],"all":"*"}'},@{v='1';r='{"all":[null,null]}'},
 @{v='1';r='{"ANY":[["1","2"],"1"]}'},@{v='1';r='{"all":[["1","2"],"1"]}'},
 @{v='1';r='"1 || ~2147483648"'},@{v='1';r='"~2147483648"'},@{v='2147483647';r='"^2147483647"'},
 @{v='1.2147483647';r='"~1.2147483647"'},@{v='1.2';r='"1.x"'},@{v='1.2';r='"1.x.x"'},
 @{v='1.2';r='">1.x"'},@{v='0.2.1';r='"^0.2.0"'},@{v='1.2.9';r='"~1.2"'},
 @{v='1.2.9';r='"~1.2+build"'},@{v='1';r='"[1],(bad)"';m=$true},
 @{v='1';r='"[1],(1,2,3]"';m=$true},@{v='1';r='"[ 1 ]"';m=$true},
 @{v='1';r='"[1,2) , [3,)"';m=$true},@{v='2';r='"[1,2) , [3,)"';m=$true},
 @{v='opaque';r='">x"';m=$true},@{v='1.5';r='"[１,2]"';m=$true}
)
$count=0
try{
 foreach($case in $cases){
  $range=ConvertFrom-Json -InputObject $case.r -NoEnumerate -AsHashtable
  $maven=[bool]$case.m
  $failure=$null
  try{$expected=&$module {param($v,$r,$m) Test-MpVersionRange -Version $v -Range $r -Maven:$m} $case.v $range $maven}catch{$failure=$_}
  $native=Invoke-NativeTestProcess $Executable @('--inspect-version-range',(ConvertTo-Json -InputObject $case.v -Compress),$case.r,'--maven',$maven.ToString())
  if($native.Stderr){throw 'Unexpected range stderr'}
  $actual=ConvertFrom-Json -InputObject $native.Stdout -NoEnumerate
  if($failure){
   $id=if($failure.FullyQualifiedErrorId-like'InvalidCastFromStringToInteger*'){'ModpackTools.Version.InvalidRangeBound'}else{'ModpackTools.Version.InvalidNumericComparison'}
   if($native.Exit-ne1-or$actual.error.id-cne$id-or!$actual.error.details-or!$actual.error.hint){throw "Range consumed numeric error mismatch: $($native.Stdout)"}
  }elseif($native.Exit-ne0-or$native.Stdout-cne(ConvertTo-Json -InputObject $expected -Compress)){throw "Range result mismatch $($case.r): $expected / $($native.Stdout)"}
  $count++
 }
 $controls=@(
  @{r='"*"';flags=@('--max-range-chars','1','--max-range-nodes','1','--max-range-depth','1');expected='true'},
  @{r='" * "';flags=@('--max-range-chars','1');id='RangeLimitExceeded';target='MaximumTextCharacters'},
  @{r='["*"]';flags=@('--max-range-nodes','1');id='RangeLimitExceeded';target='MaximumNodes'},
  @{r='["*"]';flags=@('--max-range-depth','1');id='RangeLimitExceeded';target='MaximumDepth'},
  @{r='"1 || 12"';flags=@('--max-range-comparisons','1');expected='true'},
  @{r='"2 || 1"';flags=@('--max-range-comparisons','1');id='RangeLimitExceeded';target='MaximumComparisons'},
  @{r='["1",null]';flags=@('--max-declaration-nodes','2');id='RangeDeclarationLimitExceeded';target='MaximumNodes'},
  @{r='["1",null]';flags=@('--max-declaration-nodes','3','--max-declaration-depth','2');expected='true'},
  @{r='"*"';flags=@('--max-range-depth','65');id='InvalidRangePolicy';target='MaximumDepth'},
  @{r='"*"';flags=@('--max-declaration-depth','65');id='InvalidRangeDeclarationPolicy';target='MaximumDepth'},
  @{r='"12.x"';flags=@('--max-version-digits','1');id='RangeLimitExceeded';target='MaximumNumericDigits'},
  @{r='"~12"';flags=@('--max-version-digits','1');id='RangeLimitExceeded';target='MaximumNumericDigits'}
 )
 foreach($case in $controls){
  $native=Invoke-NativeTestProcess $Executable (@('--inspect-version-range','"1"',$case.r)+$case.flags)
  $actual=ConvertFrom-Json -InputObject $native.Stdout
  if($native.Stderr){throw 'Unexpected range control stderr'}
  if($case.expected){if($native.Exit-ne0-or$native.Stdout-cne$case.expected){throw "Range boundary/shortcircuit failed $($native.Stdout)"}}
  elseif($native.Exit-ne1-or$actual.error.id-cne('ModpackTools.Version.'+$case.id)-or$actual.error.target-cne$case.target){throw "Range policy/quota failed $($native.Stdout)"}
  $count++
 }
 "Native version ranges: $count differential/lowering/resource/shortcircuit checks passed"
}finally{Remove-Module ModpackTools -ErrorAction SilentlyContinue}
