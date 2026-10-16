param([Parameter(Mandatory)][string]$Executable)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-imported-lookup-'+[guid]::NewGuid().ToString('N'))
$originalCulture=[Globalization.CultureInfo]::CurrentCulture
try {
 [void][IO.Directory]::CreateDirectory($fixture)
 $path=Join-Path $fixture 'data.psd1'
 $queries=@(@('Name'),@('Nested','Name'),@('Array',0,'Name'),@('I'),@('ı'),@('i'),@('Missing'))
 $count=0
 foreach($culture in @('es-ES','en-US','tr-TR')){
  [Globalization.CultureInfo]::CurrentCulture=[Globalization.CultureInfo]::GetCultureInfo($culture)
  [IO.File]::WriteAllText($path,"@{'N­ame'='root';Nested=@{'N­ame'='nested'};Array=@(@{'N­ame'='array'});I=7}",[Text.UTF8Encoding]::new($false))
  $imported=Import-PowerShellDataFile -LiteralPath $path
  $expected=@(foreach($query in $queries){
   $current=$imported;$found=$true
   foreach($key in $query){
    if($current-is[Collections.IDictionary]-and$current.Contains($key)){$current=$current[$key]}
    elseif($current-is[Array]-and$key-is[int]-and$key-ge0-and$key-lt$current.Length){$current=$current[$key]}
    else{$found=$false;break}
   }
   [ordered]@{found=$found;value=$(if($found){$current}else{$null})}
  })
  $before=(Get-FileHash -LiteralPath $path).Hash
  $native=Invoke-NativeTestProcess $Executable @('--inspect-psd-lookup',$path,(ConvertTo-Json -InputObject $queries -Depth 10 -Compress),'--culture',$culture)
  $actual=ConvertTo-Json -InputObject ($native.Stdout|ConvertFrom-Json -AsHashtable -DateKind String -NoEnumerate) -Depth 10 -Compress
  if($native.Exit-ne0-or$native.Stderr-or$actual-cne(ConvertTo-Json -InputObject $expected -Depth 10 -Compress)){throw "Imported lookup mismatch $culture : $($native.Stdout)"}
  if((Get-FileHash -LiteralPath $path).Hash-cne$before){throw 'Lookup changed imported file'}
  $count++
  foreach($text in @('@{I=1;i=2}','@{I=1;ı=2}',"@{'é'=1;'é'=2}")){
   [IO.File]::WriteAllText($path,$text,[Text.UTF8Encoding]::new($false))
   $failed=$false;try{[void](Import-PowerShellDataFile -LiteralPath $path)}catch{$failed=$true}
   $native=Invoke-NativeTestProcess $Executable @('--inspect-psd-lookup',$path,'[["I"]]','--culture',$culture)
   if($native.Stderr-or($failed-and$native.Exit-ne2)-or(!$failed-and$native.Exit-ne0)){throw "Import duplicate boundary mismatch $culture / $text"}
   $count++
  }
 }
 "Native imported lookup: $count culture/scoped lookup and duplicate comparisons passed; programmatic/captured-culture assertions run separately"
}finally{
 [Globalization.CultureInfo]::CurrentCulture=$originalCulture
 $resolved=[IO.Path]::GetFullPath($fixture)
 $temporaryRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
 if(!$resolved.StartsWith($temporaryRoot,[StringComparison]::OrdinalIgnoreCase)-or[IO.Path]::GetFileName($resolved)-notlike'modpacktools-imported-lookup-*'){throw 'Unsafe fixture cleanup'}
 Remove-Item -LiteralPath $resolved -Recurse -Force
}
