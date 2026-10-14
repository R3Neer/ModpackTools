param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string]$LegacyRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
Import-Module (Join-Path $LegacyRoot 'ModpackTools.psd1') -Force
$module=Get-Module ModpackTools
$cases=@(@($null,''),@('','0'),@('opaque','opaque'),@('opaque','different'),@('é',"e`u{0301}"),@('١','1'),@('１','1'),@('1.١','2.0'),@('1.١','1.0'),@('2.0','1.١'),@('1.0','1'),@('1-','1'),@('1-.','1-'),@('1-a','1-z'),@('1-a','1-A'),@("1`n",'1'),@("1`r`n",'1'),@('1+ANY_meta.😀','1+other'),@('1-alpha','1-rc'),@('1-a1','1-alpha-1'),@('1-final','1'),@('1--a','1-a'),@(('1.'+('9'*4096)),('1.'+('8'*4096))))
$count=0
try {
 foreach($culture in @('es-ES','tr-TR')){
  foreach($maven in @($false,$true)){
   foreach($pair in $cases){
    $failure=$null
    try{$expected=&$module {param($l,$r,$m,$c) $saved=[Globalization.CultureInfo]::CurrentCulture; try{[Globalization.CultureInfo]::CurrentCulture=[Globalization.CultureInfo]::GetCultureInfo($c); Compare-MpVersion -Left $l -Right $r -Maven:$m}finally{[Globalization.CultureInfo]::CurrentCulture=$saved}} $pair[0] $pair[1] $maven $culture}catch{$failure=$_}
    $left=ConvertTo-Json -InputObject $pair[0] -Compress
    $right=ConvertTo-Json -InputObject $pair[1] -Compress
    $native=Invoke-NativeTestProcess $Executable @('--inspect-compare-version',$left,$right,'--maven',$maven.ToString(),'--culture',$culture)
    if($native.Stderr){throw 'Unexpected version comparison stderr'}
    $actual=$native.Stdout|ConvertFrom-Json
    if($failure){
     if($native.Exit-ne1-or$actual.error.id-cne'ModpackTools.Version.InvalidNumericComparison'-or$actual.error.category-cne'InvalidData'-or!$actual.error.details-or!$actual.error.hint){throw "Version numeric error mismatch: $($native.Stdout)"}
    }elseif($native.Exit-ne0-or$native.Stdout-cne(ConvertTo-Json -InputObject $expected -Compress)){throw "Version raw comparison mismatch: $culture/$maven/$left/$right => $expected / $($native.Stdout)"}
    $count++
   }
  }
 }
 $controls=@(
  @{l='same';r='same';flags=@('--max-version-chars','3');id='ComparisonLimitExceeded';target='Left'},
  @{l='a';r='same';flags=@('--max-version-chars','3');id='ComparisonLimitExceeded';target='Right'},
  @{l='12';r='1';flags=@('--max-version-digits','1');id='ComparisonLimitExceeded';target='Left'},
  @{l='1';r='12';flags=@('--max-version-digits','1');id='ComparisonLimitExceeded';target='Right'},
  @{l='1-a1';r='1.0';flags=@('--maven','true','--max-version-items','4');id='ComparisonLimitExceeded';target='Left'},
  @{l='1-a1';r='1.0';flags=@('--maven','true','--max-version-depth','1');id='ComparisonLimitExceeded';target='Left'},
  @{l='same';r='same';flags=@('--max-version-depth','65');id='InvalidComparisonPolicy';target='MaximumDepth'},
  @{l='same';r='same';flags=@('--max-version-items','0');id='InvalidComparisonPolicy';target='MaximumItems'},
  @{l='same';r='same';flags=@('--max-version-chars','0');id='InvalidComparisonPolicy';target='MaximumTextCharacters'},
  @{l='same';r='same';flags=@('--max-version-digits','0');id='InvalidComparisonPolicy';target='MaximumNumericDigits'}
 )
 foreach($case in $controls){
  $native=Invoke-NativeTestProcess $Executable (@('--inspect-compare-version',(ConvertTo-Json -InputObject $case.l -Compress),(ConvertTo-Json -InputObject $case.r -Compress))+$case.flags)
  $actual=$native.Stdout|ConvertFrom-Json
  if($native.Exit-ne1-or$native.Stderr-or$actual.error.id-cne('ModpackTools.Version.'+$case.id)-or$actual.error.target-cne$case.target){throw "Version limit control failed: $($native.Stdout)"}
  $count++
 }
 foreach($flags in @(@('--maven','yes'),@('--unknown','true'),@('--maven','true','--maven','false'),@('--culture','invalid_culture_!'))){
  $native=Invoke-NativeTestProcess $Executable (@('--inspect-compare-version','"a"','"a"')+$flags)
  if($native.Exit-ne2-or$native.Stderr-or($native.Stdout|ConvertFrom-Json).error.id-cne'Core.InputReadFailed'){throw 'Version grammar control failed'}
  $count++
 }
 "Native version ordering: $count raw differential/limit/grammar checks passed"
}finally{Remove-Module ModpackTools -ErrorAction SilentlyContinue}
