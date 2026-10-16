param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string]$LegacyRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-forge-manifest-'+[guid]::NewGuid().ToString('N'))
$probes=@('0','1','2','17','opaque')
$base="[[mods]]`nmodId='m'`nversion='1'`n"
$cases=@(
 @{f=$null},@{f=$base},@{f=$base;l='FORGE'},@{f=$base;n='broken=';l='neoforge'},@{f=$base;n='';l='neoforge'},
 @{f=$base;n=' ';l='neoforge'},@{f='invalid';l='other'},
 @{f=$base+"[[dependencies.m]]`nmodId='d'`nversionRange='[1,)'`nmandatory=false`nside='CLIENT'`nordering='AFTER'"},
 @{f=$base+"[[dependencies.m]]`nmodId='d'`nversionRange=['1','2']`nmandatory=[false]"},
 @{f=$base+"[[dependencies.m]]`nmodId='d'`nversionRange={any=['1','2']}`nmandatory=[0,0]`ntype=''"},
 @{f=$base+"features={javaVersion='[17,)'}"},@{f=$base+"features=[{javaVersion='[17,)'}]"},
 @{f=$base+"features=[{javaVersion='[17,)'},{java_version='[18,)'}]"},
 @{f=$base+"features=[]"},@{f=$base+"features={}`n[features.m]`njavaVersion='[17,)'"},
 @{f=$base+"[[mods]]`nmodId='second'`nversion='2'`nfeatures=1"},
 @{f="loaderVersion='[1,)'`nmodLoader='javafml'`n"+$base},
 @{f="[[mods]]`nmodId='m'`nversion='"+'${file.jarVersion}'+"'";m="Implementation-Version:`nOther: next`n"},
 @{f="[[mods]]`nmodId='m'`nversion='"+'${other}'+"'"},
 @{f=$base;j='{jars:[{path:"one.jar"},{path:"two.jar"}]}'},@{f=$base;j='{jars:[{}]}'},@{j='{jars:[]}'},
 @{f="[[mods]]`nmodId=2000-01-01T12:34:56.123Z`nversion=1.5"},
 @{f=$base+"[[dependencies.m]]`nmodId='d'`nordering=true"},
 @{f=$base+"[[dependencies.m]]`nmodId='d'`nordering=[true,false]"},
 @{f=$base+"[[dependencies.m]]`nmodId='d'`nordering=[false,false]"}
)
function Project-Requirement($r){
 [ordered]@{Target=$r.Target;RangeDisplay=($r.Range -join ' OR ');Kind=$r.Kind;Side=$r.Side;Scope=$r.Scope;Maven=$r.Maven;Source=$r.Source;SuggestedVersionId=$r.SuggestedVersionId;RangeChecks=@(foreach($v in $probes){[ordered]@{Version=$v;Result=(&$module {param($v,$range)Test-MpVersionRange $v $range -Maven} $v $r.Range);Error=$null}});Any=@();All=@();Unless=$null}
}
function Same($a,$b){
 if($null-eq$a-or$null-eq$b){return $null-eq$a-and$null-eq$b}
 if($a-is[Collections.IDictionary]-and$b-is[Collections.IDictionary]){
  if($a.Count-ne$b.Count){return $false};foreach($entry in $a.GetEnumerator()){if(-not$b.Contains($entry.Key)-or-not(Same $entry.Value $b[$entry.Key])){return $false}};return $true
 }
 if($a-is[array]-and$b-is[array]){if($a.Count-ne$b.Count){return $false};for($i=0;$i-lt$a.Count;$i++){if(-not(Same $a[$i] $b[$i])){return $false}};return $true}
 return $a.GetType()-eq$b.GetType()-and$a-ceq$b
}
try{
 [void][IO.Directory]::CreateDirectory($fixture)
 Import-Module (Join-Path $LegacyRoot 'ModpackTools.psd1') -Force
 $module=Get-Module ModpackTools
 $count=0
 foreach($case in $cases){
  $loader=if($case.l){$case.l}else{'forge'}
  $files=@(foreach($key in @('f','n','m','j')){if($null-ne$case[$key]){$path=Join-Path $fixture ("$count-$key.txt");[IO.File]::WriteAllText($path,$case[$key]);$path}else{'null'}})
  $old=&$module {param($c,$l)
   $memory=[IO.MemoryStream]::new();$archive=$null
   try{
    $create=[IO.Compression.ZipArchive]::new($memory,[IO.Compression.ZipArchiveMode]::Create,$true)
    try{foreach($entry in @(@('META-INF/mods.toml','f'),@('META-INF/neoforge.mods.toml','n'),@('META-INF/MANIFEST.MF','m'),@('META-INF/jarjar/metadata.json','j'))){if($null-ne$c[$entry[1]]){$writer=[IO.StreamWriter]::new($create.CreateEntry($entry[0]).Open());try{$writer.Write([string]$c[$entry[1]])}finally{$writer.Dispose()}}}}finally{$create.Dispose()}
    $memory.Position=0;$archive=[IO.Compression.ZipArchive]::new($memory,[IO.Compression.ZipArchiveMode]::Read,$true)
    Read-MpLoaderArchive $archive $l
   }finally{if($archive){$archive.Dispose()};$memory.Dispose()}
  } $case $loader
  $expected=@(foreach($mod in $old.Mods){[ordered]@{Id=$mod.Id;Version=$mod.Version;Side=$mod.Side;Nested=$false;Aliases=@();Requirements=@($mod.Requirements|ForEach-Object {Project-Requirement $_})}})
  $native=Invoke-NativeTestProcess $Executable (@('--inspect-forge-manifest')+$files+@($loader,'--range-probes',(ConvertTo-Json -InputObject $probes -Compress)))
  if($native.Exit-ne0-or$native.Stderr){throw "Forge transport failed: $($native.Stdout)"}
  $actual=ConvertFrom-Json -InputObject $native.Stdout -AsHashtable -DateKind String
  if(-not(Same $expected @($actual.mods))){throw "Forge case ${count}: mod mismatch. Expected $(ConvertTo-Json -InputObject $expected -Depth 50 -Compress); got $(ConvertTo-Json -InputObject $actual.mods -Depth 50 -Compress)"}
  $warnings=@($old.Warnings|Where-Object {$_-notlike"Nested JAR '*' is unavailable or too large"})
  if($warnings.Count-ne$actual.warnings.Count){throw "Forge case ${count}: warning count mismatch"}
  for($i=0;$i-lt$warnings.Count;$i++){
   if($warnings[$i]-like'Manifest could not be fully read:*'){if($actual.warnings[$i]-notlike'Manifest could not be fully read:*'-or$actual.can_traverse_nested){throw "Forge case ${count}: incomplete stage mismatch"}}
   elseif($warnings[$i]-cne$actual.warnings[$i]){throw "Forge case ${count}: warning mismatch"}
  }
  $count++
 }
 "Native Forge root declarations passed: $count full-root and live Maven-range cases"
}finally{
 if(Test-Path -LiteralPath $fixture){$resolved=[IO.Path]::GetFullPath($fixture);$root=[IO.Path]::GetFullPath([IO.Path]::GetTempPath());if(-not$resolved.StartsWith($root,[StringComparison]::OrdinalIgnoreCase)-or[IO.Path]::GetFileName($resolved)-notlike'modpacktools-forge-manifest-*'){throw 'Unsafe fixture cleanup'};Remove-Item -LiteralPath $resolved -Recurse -Force}
}
