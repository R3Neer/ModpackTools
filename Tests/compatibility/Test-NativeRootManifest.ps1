param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string]$LegacyRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'NativeProcess.ps1')
$fixture=Join-Path ([IO.Path]::GetTempPath()) ('modpacktools-root-manifest-'+[guid]::NewGuid().ToString('N'))
$probes=@('0','1','1.2','2','opaque','NaN','Infinity')
function Project-Requirement($r){
 $checks=@(foreach($v in $probes){$problem=$null;try{$match=&$module {param($v,$range,$m)Test-MpVersionRange $v $range -Maven:$m} $v $r.Range $r.Maven}catch{$problem=if($_.FullyQualifiedErrorId-like'InvalidCastFromStringToInteger*'){'ModpackTools.Version.InvalidRangeBound'}else{'ModpackTools.Version.InvalidNumericComparison'};$match=$null};[ordered]@{Version=$v;Result=$match;Error=$problem}})
 [ordered]@{Target=$r.Target;RangeDisplay=($r.Range -join ' OR ');Kind=$r.Kind;Side=$r.Side;Scope=$r.Scope;Maven=$r.Maven;Source=$r.Source;SuggestedVersionId=$r.SuggestedVersionId;RangeChecks=$checks;Any=@($r.Any|ForEach-Object {Project-Requirement $_});All=@($r.All|ForEach-Object {Project-Requirement $_});Unless=$(if($r.Unless){Project-Requirement $r.Unless}else{$null})}
}
$cases=@(
 @{f=$null;q=$null;l='fabric'},@{f='';q=$null;l='fabric'},@{f='{schemaVersion:1,id:"m",version:"1"}';l='FABRIC'},
 @{f='{schemaVersion:2,id:"m",version:"1",environment:"other"}'},@{f='{id:"m",version:"1"}'},@{f='{schemaVersion:1,ID:"m",version:"1"}'},
 @{f='{schemaVersion:1,id:null,version:false,environment:12.5}'},@{f='{schemaVersion:1,id:{},version:[1,2],environment:[[1],{},null]}'},
 @{f='{schemaVersion:1,id:"2000-01-01T00:00:00Z",version:"1",depends:{date:"2000-01-01T00:00:00Z",nan:NaN,text:"NaN",number:1.25}}'},
 @{f='{schemaVersion:1,id:"m",version:"1",depends:{a:"1",b:"2",a:"*"},breaks:{bad:">2"},recommends:{r:["1","2"]},suggests:{s:{all:[">=1","<2"]}}}'},
 @{f='{schemaVersion:1,id:"m",version:"1",provides:[12,"12",true,"True",[1],[1],{},"a","A"]}'},
 @{f='{schemaVersion:1,id:"m",version:"1",jars:[{file:"child.jar"},{file:"child.jar"},{file:null}]}'},
 @{f='{schemaVersion:1,id:"m",version:"1",jars:[{file:"child.jar"},12]}'},@{f='invalid'},@{f='[ {schemaVersion:1,id:"m",version:"1"} ]'},
 @{f='{schemaVersion:1,id:"m",version:"1"}';l='other'},@{f='invalid';q='{schema_version:1,quilt_loader:{id:"q",version:"1"}}';l='quilt'},
 @{f='{schemaVersion:1,id:"m",version:"1"}';q='invalid';l='quilt'},
 @{q='{quilt_loader:{id:"q",version:"1",jars:["one.jar"]}}';l='quilt'},
 @{q='{schema_version:1,quilt_loader:{id:"q",version:"1",depends:["d",{id:"x",optional:"true",versions:["1","2"],any:["a"],all:["b"],unless:"c"}],breaks:[{id:"bad",any:["child"]}]}}';l='quilt'},
 @{q='{schema_version:1,quilt_loader:{id:"q",version:"1",depends:[{id:"x",optional:[false,true]},{id:"y",optional:[1,2]},{id:"z",optional:"false"}]}}';l='quilt'},
 @{q='{schema_version:1,quilt_loader:{id:"q",version:"1",provides:["",{id:"a",version:0},{id:"A",version:"2"}],jars:["one.jar",{},null]}}';l='quilt'},
 @{q='{schema_version:1,minecraft:null,quilt_loader:{id:"q",version:"1"}}';l='quilt'},
 @{q='{schema_version:1,minecraft:{environment:null},quilt_loader:{id:"q",version:"1"}}';l='quilt'}
)
try{
 [void][IO.Directory]::CreateDirectory($fixture)
 Import-Module (Join-Path $LegacyRoot 'ModpackTools.psd1') -Force
 $module=Get-Module ModpackTools
 $count=0
 foreach($case in $cases){
  $loader=if($case.l){$case.l}else{'fabric'}
  $f='null';$q='null'
  if($null-ne$case.f){$f=Join-Path $fixture ('fabric-'+$count+'.json');[IO.File]::WriteAllText($f,$case.f)}
  if($null-ne$case.q){$q=Join-Path $fixture ('quilt-'+$count+'.json');[IO.File]::WriteAllText($q,$case.q)}
  $old=&$module {param($f,$q,$loader)
   $memory=[IO.MemoryStream]::new();$archive=$null
   try{
    $create=[IO.Compression.ZipArchive]::new($memory,[IO.Compression.ZipArchiveMode]::Create,$true)
    try{foreach($entry in @(@('fabric.mod.json',$f),@('quilt.mod.json',$q))){if($null-ne$entry[1]){$writer=[IO.StreamWriter]::new($create.CreateEntry($entry[0]).Open());try{$writer.Write([string]$entry[1])}finally{$writer.Dispose()}}}}finally{$create.Dispose()}
    $memory.Position=0;$archive=[IO.Compression.ZipArchive]::new($memory,[IO.Compression.ZipArchiveMode]::Read,$true)
    Read-MpLoaderArchive $archive $loader
   }finally{if($archive){$archive.Dispose()};$memory.Dispose()}
  } $case.f $case.q $loader
  $expected=@(foreach($mod in $old.Mods){
   $aliases=@($mod.Provides.GetEnumerator()|ForEach-Object {[ordered]@{key_type=$_.Key.GetType().FullName;key=$_.Key;value=$_.Value}}|Sort-Object {($_|ConvertTo-Json -Depth 50 -Compress)})
   [ordered]@{Id=$mod.Id;Version=$mod.Version;Side=$mod.Side;Nested=$mod.Nested;Aliases=$aliases;Requirements=@($mod.Requirements|ForEach-Object {Project-Requirement $_})}
  })
  $native=Invoke-NativeTestProcess $Executable @('--inspect-root-manifest',$f,$q,$loader,'--range-probes',(ConvertTo-Json -InputObject $probes -Compress))
  if($native.Exit-ne0-or$native.Stderr){throw "Root transport failed $($native.Stdout)"}
  $actual=ConvertFrom-Json -InputObject $native.Stdout -AsHashtable -DateKind String
  foreach($mod in $actual.mods){$mod.Aliases=@($mod.Aliases|ForEach-Object {$_.Remove('key_kind');$_}|Sort-Object {($_|ConvertTo-Json -Depth 50 -Compress)})}
  $actualMods=ConvertTo-Json -InputObject @($actual.mods) -Depth 100 -Compress
  $oldMods=ConvertTo-Json -InputObject $expected -Depth 100 -Compress
  # Compare decoded structures by canonical field order; actual maps preserve writer order.
  function Same($a,$b){
   if($null-eq$a-or$null-eq$b){return $null-eq$a-and$null-eq$b}
   if($a-is[Collections.IDictionary]-and$b-is[Collections.IDictionary]){if($a.Count-ne$b.Count){return $false};foreach($entry in $a.GetEnumerator()){if(!$b.Contains($entry.Key)-or!(Same $entry.Value $b[$entry.Key])){return $false}};return $true}
   if($a-is[array]-and$b-is[array]){if($a.Count-ne$b.Count){return $false};for($i=0;$i-lt$a.Count;$i++){if(!(Same $a[$i] $b[$i])){return $false}};return $true}
   return $a-ceq$b
  }
  if(!(Same ($actualMods|ConvertFrom-Json -AsHashtable -DateKind String -NoEnumerate) ($oldMods|ConvertFrom-Json -AsHashtable -DateKind String -NoEnumerate))){throw "Root complete fields mismatch case$count old=$oldMods native=$actualMods"}
  $rootWarnings=@($old.Warnings|Where-Object {$_-notlike"Nested JAR '*"})
  if($actual.warnings.Count-ne$rootWarnings.Count){throw "Root warning stage/count mismatch case${count}: $($native.Stdout) / $($old.Warnings)"}
  for($i=0;$i-lt$rootWarnings.Count;$i++){if($rootWarnings[$i]-notlike'Manifest could not be fully read:*'-and$actual.warnings[$i]-cne$rootWarnings[$i]){throw 'Stable root warning changed'}}
  $count++
 }
 "Root manifests: $count complete differential declarations with live predicate probes passed"
}finally{
 Remove-Module ModpackTools -ErrorAction SilentlyContinue
 $resolved=[IO.Path]::GetFullPath($fixture);$temporaryRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\'
 if(!$resolved.StartsWith($temporaryRoot,[StringComparison]::OrdinalIgnoreCase)-or[IO.Path]::GetFileName($resolved)-notlike'modpacktools-root-manifest-*'){throw 'Unsafe root fixture cleanup'}
 Remove-Item -LiteralPath $resolved -Recurse -Force
}
